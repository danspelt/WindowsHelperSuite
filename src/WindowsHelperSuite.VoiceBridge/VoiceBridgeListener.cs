using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using VoiceBridge.Contracts;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.VoiceBridge;

/// <summary>
/// Local Kestrel host for Voice Bridge WebSocket clients (Android app in a separate repo).
/// Binds loopback by default; optional LAN binding for the phone.
/// </summary>
public sealed class VoiceBridgeListener : IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILoggingService _logging;
    private readonly Func<VoiceBridgeConnectionOptions> _getOptions;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly ConcurrentDictionary<string, BridgeConnection> _connections = new(StringComparer.OrdinalIgnoreCase);

    public VoiceBridgeListener(ILoggingService logging, Func<VoiceBridgeConnectionOptions> getOptions)
    {
        _logging = logging;
        _getOptions = getOptions;
    }

    /// <summary>Raised for each inbound text frame after pairing (excluding ping handling).</summary>
    public event Action<VoiceBridgeEnvelope>? MessageReceived;

    /// <summary>Raised when a client connects or disconnects.</summary>
    public event Action<bool, string?, string?>? ConnectionChanged;

    public void Start()
    {
        if (_runTask != null)
        {
            return;
        }

        var opts = _getOptions();
        if (!opts.Enabled)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _runTask = Task.Run(async () => await RunHostAsync(token).ConfigureAwait(false), CancellationToken.None);
    }

    private async Task RunHostAsync(CancellationToken cancellationToken)
    {
        var opts = _getOptions();
        var port = Math.Clamp(opts.Port, 1024, 65535);
        var bindDesc = opts.ListenOnAllInterfaces ? "all interfaces" : "127.0.0.1";

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.WebHost.ConfigureKestrel(k =>
            {
                if (opts.ListenOnAllInterfaces)
                {
                    k.ListenAnyIP(port);
                }
                else
                {
                    k.ListenLocalhost(port);
                }
            });
            var app = builder.Build();
            app.UseWebSockets();

            app.MapGet("/voice-bridge/health", () => Results.Json(new { ok = true, service = "VoiceBridge", version = 1 }));

            app.Map("/ws", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var queryToken = context.Request.Query["token"].FirstOrDefault();
                var liveOpts = _getOptions();
                if (!TokenEquals(queryToken, liveOpts.SharedToken))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                _logging.Information($"Voice Bridge: client connected from {context.Connection.RemoteIpAddress}");
                await HandleSocketAsync(socket, liveOpts.SharedToken, cancellationToken);
            });

            _logging.Information(
                $"Voice Bridge: listening on {bindDesc}:{port} (WebSocket ws://<host>:{port}/ws?token=… )");
            await app.RunAsync().WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            /* shutdown */
        }
        catch (Exception ex)
        {
            _logging.Warning($"Voice Bridge host stopped: {ex.Message}");
        }
    }

    private async Task HandleSocketAsync(WebSocket socket, string sharedToken, CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var conn = new BridgeConnection(sessionId, socket);
        _connections[sessionId] = conn;
        ConnectionChanged?.Invoke(true, sessionId, null);

        // Challenge/response auth (in addition to the ws?token= gate) to discourage accidental LAN connects.
        conn.Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await SendJsonAsync(socket, new VoiceBridgeEnvelope
        {
            Type = VoiceBridgeMessageTypes.AuthChallenge,
            SessionId = sessionId,
            Nonce = conn.Nonce,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        var buffer = new byte[64 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            ValueWebSocketReceiveResult result;
            using var ms = new MemoryStream();
            do
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "bye",
                        CancellationToken.None);
                    return;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(ms.ToArray());
            VoiceBridgeEnvelope? env;
            try
            {
                env = JsonSerializer.Deserialize<VoiceBridgeEnvelope>(json, JsonOpts);
            }
            catch (JsonException)
            {
                _logging.Debug($"Voice Bridge: ignored non-JSON frame ({json.Length} chars)");
                continue;
            }

            if (env == null || string.IsNullOrWhiteSpace(env.Type))
            {
                continue;
            }

            switch (env.Type)
            {
                case VoiceBridgeMessageTypes.Ping:
                    await SendJsonAsync(socket, new VoiceBridgeEnvelope
                    {
                        Type = VoiceBridgeMessageTypes.Pong,
                        Timestamp = DateTime.UtcNow
                    }, cancellationToken);
                    break;

                case VoiceBridgeMessageTypes.PairRequest:
                    if (!TokenEquals(env.Token, sharedToken))
                    {
                        await SendJsonAsync(socket, new VoiceBridgeEnvelope
                        {
                            Type = VoiceBridgeMessageTypes.PairReject,
                            Result = "invalid_token",
                            Timestamp = DateTime.UtcNow
                        }, cancellationToken);
                    }
                    else
                    {
                        await SendJsonAsync(socket, new VoiceBridgeEnvelope
                        {
                            Type = VoiceBridgeMessageTypes.PairAccept,
                            Timestamp = DateTime.UtcNow
                        }, cancellationToken);
                    }

                    break;

                case VoiceBridgeMessageTypes.Hello:
                    conn.DeviceId = env.DeviceId;
                    conn.AppVersion = env.AppVersion;
                    ConnectionChanged?.Invoke(true, sessionId, conn.DeviceId);
                    _logging.Information($"Voice Bridge: hello deviceId={conn.DeviceId} appVersion={conn.AppVersion}");
                    break;

                case VoiceBridgeMessageTypes.AuthResponse:
                    if (conn.Authenticated)
                    {
                        break;
                    }
                    if (!AuthOk(conn.Nonce, env.Hmac, sharedToken))
                    {
                        await SendJsonAsync(socket, new VoiceBridgeEnvelope
                        {
                            Type = VoiceBridgeMessageTypes.PairReject,
                            SessionId = sessionId,
                            Result = "auth_failed",
                            Timestamp = DateTime.UtcNow
                        }, cancellationToken);
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "auth_failed", CancellationToken.None);
                        return;
                    }

                    conn.Authenticated = true;
                    await SendJsonAsync(socket, new VoiceBridgeEnvelope
                    {
                        Type = VoiceBridgeMessageTypes.AuthResponse,
                        SessionId = sessionId,
                        Result = "ok",
                        Timestamp = DateTime.UtcNow
                    }, cancellationToken);
                    _logging.Information($"Voice Bridge: authenticated session={sessionId} deviceId={conn.DeviceId}");
                    break;

                default:
                    if (!conn.Authenticated)
                    {
                        // Ignore anything before auth completes.
                        _logging.Debug($"Voice Bridge: ignored {env.Type} before auth (session={sessionId})");
                        break;
                    }

                    env.SessionId ??= sessionId;
                    MessageReceived?.Invoke(env);
                    _logging.Information(
                        $"Voice Bridge ← {env.Type} session={env.SessionId} textLen={env.Text?.Length ?? 0}");
                    break;
            }
        }

        _connections.TryRemove(sessionId, out _);
        ConnectionChanged?.Invoke(false, sessionId, conn.DeviceId);
    }

    private async Task SendJsonAsync(WebSocket socket, VoiceBridgeEnvelope payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts));
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private static bool TokenEquals(string? a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a.Length != b.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }

    private static bool AuthOk(string? nonceHex, string? hmacHex, string sharedToken)
    {
        if (string.IsNullOrWhiteSpace(nonceHex) || string.IsNullOrWhiteSpace(hmacHex))
        {
            return false;
        }

        byte[] nonce;
        byte[] provided;
        try
        {
            nonce = Convert.FromHexString(nonceHex);
            provided = Convert.FromHexString(hmacHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var key = Encoding.UTF8.GetBytes(sharedToken);
        var expected = HMACSHA256.HashData(key, nonce);
        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    public async Task<int> SendToAllAsync(VoiceBridgeEnvelope payload, CancellationToken cancellationToken)
    {
        var sent = 0;
        foreach (var kvp in _connections)
        {
            var c = kvp.Value;
            if (!c.Authenticated || c.Socket.State != WebSocketState.Open)
            {
                continue;
            }

            try
            {
                payload.SessionId ??= c.SessionId;
                await SendJsonAsync(c.Socket, payload, cancellationToken);
                sent++;
            }
            catch (Exception ex)
            {
                _logging.Debug($"Voice Bridge send failed (session={c.SessionId}): {ex.Message}");
            }
        }

        return sent;
    }

    private sealed class BridgeConnection
    {
        public BridgeConnection(string sessionId, WebSocket socket)
        {
            SessionId = sessionId;
            Socket = socket;
        }

        public string SessionId { get; }
        public WebSocket Socket { get; }
        public bool Authenticated { get; set; }
        public string? Nonce { get; set; }
        public string? DeviceId { get; set; }
        public string? AppVersion { get; set; }
    }

    public void Dispose() =>
        DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
            if (_runTask != null)
            {
                await _runTask.ConfigureAwait(false);
            }
        }
        catch
        {
            /* best-effort */
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _runTask = null;
        }
    }
}
