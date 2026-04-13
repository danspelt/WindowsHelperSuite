using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using StillSpace.Counseling;

namespace StillSpace.Services;

/// <summary>
/// OpenAI Realtime API over WebSocket: speech-to-speech with Server VAD, optional input transcription for display.
/// </summary>
public sealed class OpenAiRealtimeVoiceSession : IDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _runCts;
    private Task? _receiveTask;
    private Task? _micTask;

    private WasapiCapture? _capture;
    private BufferedWaveProvider? _micBuffer;
    private IWaveProvider? _micResampledPcm16;
    private WasapiOut? _playbackOut;
    private BufferedWaveProvider? _playbackBuffer;

    private volatile bool _sessionConfigured;
    private TaskCompletionSource? _sessionConfigureTcs;
    private volatile bool _disposed;
    private bool _assistantAudioStreaming;
    private bool _firstOutputAudioLifecycleSent;
    private Action<RealtimeLifecycleEvent>? _lifecycleCallback;

    public bool IsRunning => _ws is { State: WebSocketState.Open };

    public static string ResolveRealtimeModel(StillSpaceSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.OpenAiRealtimeModel)
            ? settings.OpenAiRealtimeModel.Trim()
            : (Environment.GetEnvironmentVariable("OPENAI_REALTIME_MODEL")?.Trim() ?? "gpt-realtime");

    public static string ResolveRealtimeVoice(StillSpaceSettings settings)
    {
        var v = settings.OpenAiRealtimeVoice?.Trim();
        if (!string.IsNullOrEmpty(v)) return v;
        return "marin";
    }

    /// <summary>
    /// Starts WebSocket, waits for session handshake, then captures mic and plays PCM replies on <paramref name="playbackDevice"/> (or default if null and not requiring device).
    /// <paramref name="captureDevice"/> selects the recording endpoint; null uses Windows default capture.
    /// </summary>
    public async Task StartAsync(
        string apiKey,
        StillSpaceSettings settings,
        CounselingMode mode,
        MMDevice? captureDevice,
        MMDevice? playbackDevice,
        bool requirePlaybackDevice,
        Action onSessionReady,
        Action<string> onInputTranscriptDelta,
        Action onInputTranscriptReset,
        Action onOutputTranscriptReset,
        Action<string> onOutputTranscriptDelta,
        Action onSpeechStarted,
        Action onAssistantAudioStarted,
        Action onAssistantAudioDone,
        Action<string> onError,
        Action<RealtimeLifecycleEvent>? onLifecycle,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key required.", nameof(apiKey));

        if (requirePlaybackDevice && playbackDevice == null)
            throw new InvalidOperationException("Headset playback device required for private mode.");

        await StopAsync().ConfigureAwait(false);

        _lifecycleCallback = onLifecycle;
        _firstOutputAudioLifecycleSent = false;
        _sessionConfigureTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var model = ResolveRealtimeModel(settings);
        var uri = new Uri($"wss://api.openai.com/v1/realtime?model={Uri.EscapeDataString(model)}");

        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        await _ws.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _runCts.Token;

        _receiveTask = Task.Run(() => ReceiveLoopAsync(
            settings,
            mode,
            onSessionReady,
            onInputTranscriptDelta,
            onInputTranscriptReset,
            onOutputTranscriptReset,
            onOutputTranscriptDelta,
            onSpeechStarted,
            onAssistantAudioStarted,
            onAssistantAudioDone,
            onError,
            playbackDevice,
            requirePlaybackDevice,
            token), token);

        await WaitForSessionConfiguredAsync(token).ConfigureAwait(false);
        StartMicrophonePump(settings, captureDevice, onError, token);
    }

    private async Task WaitForSessionConfiguredAsync(CancellationToken cancellationToken)
    {
        var gate = _sessionConfigureTcs
                   ?? throw new InvalidOperationException("Session configure gate not initialized.");
        var timeout = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        var completed = await Task.WhenAny(gate.Task, timeout).ConfigureAwait(false);
        if (completed != gate.Task)
        {
            if (!_sessionConfigured)
                throw new TimeoutException("Realtime session did not finish configuration in time.");
            return;
        }

        await gate.Task.ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(
        StillSpaceSettings settings,
        CounselingMode mode,
        Action onSessionReady,
        Action<string> onInputTranscriptDelta,
        Action onInputTranscriptReset,
        Action onOutputTranscriptReset,
        Action<string> onOutputTranscriptDelta,
        Action onSpeechStarted,
        Action onAssistantAudioStarted,
        Action onAssistantAudioDone,
        Action<string> onError,
        MMDevice? playbackDevice,
        bool requirePlaybackDevice,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            while (_ws is { State: WebSocketState.Open } && !cancellationToken.IsCancellationRequested)
            {
                var seg = new ArraySegment<byte>(buffer);
                var result = await _ws.ReceiveAsync(seg, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var n = result.Count;
                if (!result.EndOfMessage)
                {
                    using var ms = new MemoryStream();
                    ms.Write(buffer.AsSpan(0, n));
                    while (!result.EndOfMessage)
                    {
                        result = await _ws.ReceiveAsync(seg, cancellationToken).ConfigureAwait(false);
                        ms.Write(buffer.AsSpan(0, result.Count));
                    }

                    await HandleJsonAsync(ms.ToArray(), settings, mode, onSessionReady,
                        onInputTranscriptDelta, onInputTranscriptReset, onOutputTranscriptReset,
                        onOutputTranscriptDelta, onSpeechStarted, onAssistantAudioStarted, onAssistantAudioDone,
                        onError, playbackDevice, requirePlaybackDevice,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await HandleJsonAsync(buffer.AsSpan(0, n).ToArray(), settings, mode, onSessionReady,
                        onInputTranscriptDelta, onInputTranscriptReset, onOutputTranscriptReset,
                        onOutputTranscriptDelta, onSpeechStarted, onAssistantAudioStarted, onAssistantAudioDone,
                        onError, playbackDevice, requirePlaybackDevice,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* normal */
        }
        catch (Exception ex)
        {
            onError(ex.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task HandleJsonAsync(
        byte[] utf8Json,
        StillSpaceSettings settings,
        CounselingMode mode,
        Action onSessionReady,
        Action<string> onInputTranscriptDelta,
        Action onInputTranscriptReset,
        Action onOutputTranscriptReset,
        Action<string> onOutputTranscriptDelta,
        Action onSpeechStarted,
        Action onAssistantAudioStarted,
        Action onAssistantAudioDone,
        Action<string> onError,
        MMDevice? playbackDevice,
        bool requirePlaybackDevice,
        CancellationToken cancellationToken)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(utf8Json);
        }
        catch
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl))
                return;
            var type = typeEl.GetString();
            switch (type)
            {
                case "session.created":
                    await SendSessionUpdateAsync(settings, mode, cancellationToken).ConfigureAwait(false);
                    break;
                case "session.updated":
                    if (!_sessionConfigured)
                    {
                        _sessionConfigured = true;
                        EnsurePlayback(playbackDevice, requirePlaybackDevice);
                        onSessionReady();
                        _sessionConfigureTcs?.TrySetResult();
                        EmitLifecycle(RealtimeLifecyclePhase.Listening);
                    }

                    break;
                case "input_audio_buffer.speech_started":
                    onSpeechStarted();
                    onInputTranscriptReset();
                    ClearPlaybackBuffer();
                    if (_assistantAudioStreaming)
                    {
                        _assistantAudioStreaming = false;
                        onAssistantAudioDone();
                    }

                    EmitLifecycle(RealtimeLifecyclePhase.SpeechStarted);
                    break;
                case "input_audio_buffer.speech_stopped":
                    EmitLifecycle(RealtimeLifecyclePhase.SpeechStopped);
                    break;
                case "input_audio_buffer.committed":
                    EmitLifecycle(RealtimeLifecyclePhase.BufferCommitted);
                    break;
                case "conversation.item.input_audio_transcription.delta":
                    if (root.TryGetProperty("delta", out var inDelta) && inDelta.ValueKind == JsonValueKind.String)
                        onInputTranscriptDelta(inDelta.GetString() ?? "");
                    break;
                case "conversation.item.input_audio_transcription.completed":
                    if (root.TryGetProperty("transcript", out var tr) && tr.ValueKind == JsonValueKind.String)
                    {
                        var full = tr.GetString();
                        if (!string.IsNullOrEmpty(full))
                        {
                            onInputTranscriptReset();
                            onInputTranscriptDelta(full);
                        }
                    }

                    break;
                case "conversation.item.input_audio_transcription.failed":
                    var fail = "Input transcription failed.";
                    if (root.TryGetProperty("error", out var failErr) && failErr.ValueKind == JsonValueKind.Object
                        && failErr.TryGetProperty("message", out var fm) && fm.ValueKind == JsonValueKind.String)
                        fail = fm.GetString() ?? fail;
                    onError(fail);
                    break;
                case "response.created":
                    onOutputTranscriptReset();
                    _assistantAudioStreaming = false;
                    _firstOutputAudioLifecycleSent = false;
                    EmitLifecycle(RealtimeLifecyclePhase.ResponseCreated);
                    break;
                case "response.output_audio_transcript.delta":
                    if (root.TryGetProperty("delta", out var outTd) && outTd.ValueKind == JsonValueKind.String)
                        onOutputTranscriptDelta(outTd.GetString() ?? "");
                    break;
                case "response.output_audio.delta":
                    if (root.TryGetProperty("delta", out var audioB64) && audioB64.ValueKind == JsonValueKind.String)
                    {
                        var b64 = audioB64.GetString();
                        if (!string.IsNullOrEmpty(b64))
                        {
                            try
                            {
                                if (!_assistantAudioStreaming)
                                {
                                    _assistantAudioStreaming = true;
                                    onAssistantAudioStarted();
                                }

                                if (!_firstOutputAudioLifecycleSent)
                                {
                                    _firstOutputAudioLifecycleSent = true;
                                    EmitLifecycle(RealtimeLifecyclePhase.FirstOutputAudio);
                                }

                                var pcm = Convert.FromBase64String(b64);
                                EnqueuePlaybackPcm(pcm);
                            }
                            catch
                            {
                                /* ignore bad chunk */
                            }
                        }
                    }

                    break;
                case "response.output_audio.done":
                    if (_assistantAudioStreaming)
                    {
                        _assistantAudioStreaming = false;
                        onAssistantAudioDone();
                    }

                    EmitLifecycle(RealtimeLifecyclePhase.OutputAudioDone);
                    EmitLifecycle(RealtimeLifecyclePhase.Listening);
                    break;
                case "error":
                    var msg = "Realtime error";
                    if (root.TryGetProperty("error", out var errObj) && errObj.ValueKind == JsonValueKind.Object)
                    {
                        if (errObj.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                            msg = m.GetString() ?? msg;
                    }

                    if (!_sessionConfigured)
                        _sessionConfigureTcs?.TrySetException(new InvalidOperationException(msg));
                    onError(msg);
                    break;
            }
        }
    }

    private void EmitLifecycle(string phase) =>
        _lifecycleCallback?.Invoke(new RealtimeLifecycleEvent(phase, DateTimeOffset.UtcNow));

    private void EnsurePlayback(MMDevice? playbackDevice, bool requirePlaybackDevice)
    {
        if (_playbackBuffer != null) return;

        _playbackBuffer = new BufferedWaveProvider(new WaveFormat(24000, 16, 1))
        {
            BufferLength = 16 * 1024 * 1024,
            DiscardOnBufferOverflow = true
        };

        _playbackOut = playbackDevice != null
            ? new WasapiOut(playbackDevice, AudioClientShareMode.Shared, false, 200)
            : !requirePlaybackDevice
                ? new WasapiOut(AudioClientShareMode.Shared, 200)
                : null;

        if (_playbackOut == null) return;
        _playbackOut.Init(_playbackBuffer);
        _playbackOut.Play();
    }

    private void EnqueuePlaybackPcm(byte[] pcm)
    {
        var buf = _playbackBuffer;
        if (buf == null || pcm.Length == 0) return;
        buf.AddSamples(pcm, 0, pcm.Length);
    }

    private void ClearPlaybackBuffer()
    {
        try
        {
            _playbackBuffer?.ClearBuffer();
        }
        catch
        {
            /* ignore */
        }
    }

    private async Task SendSessionUpdateAsync(StillSpaceSettings settings, CounselingMode mode, CancellationToken ct)
    {
        var (silenceMs, prefixMs, threshold) = RealtimeVadParameters.For(settings.RealtimeResponsiveness);

        var lang = settings.SttLang.Trim();
        var langShort = lang.Length >= 2 ? lang[..2].ToLowerInvariant() : "en";

        var instructions = CounselorPrompts.BuildRealtimeInstructions(mode);
        if (!string.IsNullOrWhiteSpace(settings.PreferredName))
            instructions =
                $"The user’s preferred name is “{settings.PreferredName.Trim()}”.\n\n{instructions}";

        // output_modalities must be either ["audio"] or ["text"], not both — audio mode still streams output transcripts.
        var inputObj = new JsonObject
        {
            ["format"] = new JsonObject { ["type"] = "audio/pcm", ["rate"] = 24000 },
            ["turn_detection"] = new JsonObject
            {
                ["type"] = "server_vad",
                ["create_response"] = true,
                ["interrupt_response"] = true,
                ["silence_duration_ms"] = silenceMs,
                ["prefix_padding_ms"] = prefixMs,
                ["threshold"] = threshold
            },
            ["transcription"] = new JsonObject
            {
                ["model"] = "whisper-1",
                ["language"] = langShort
            }
        };
        if (settings.HeadsetOnlyMode)
            inputObj["noise_reduction"] = new JsonObject { ["type"] = "near_field" };

        var session = new JsonObject
        {
            ["type"] = "realtime",
            ["model"] = ResolveRealtimeModel(settings),
            ["instructions"] = instructions,
            ["output_modalities"] = new JsonArray("audio"),
            ["audio"] = new JsonObject
            {
                ["input"] = inputObj,
                ["output"] = new JsonObject
                {
                    ["format"] = new JsonObject { ["type"] = "audio/pcm", ["rate"] = 24000 },
                    ["voice"] = ResolveRealtimeVoice(settings)
                }
            }
        };

        var envelope = new JsonObject { ["type"] = "session.update", ["session"] = session };
        var json = envelope.ToJsonString();
        await SendRawAsync(json, ct).ConfigureAwait(false);
    }

    private async Task SendRawAsync(string json, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void StartMicrophonePump(
        StillSpaceSettings settings,
        MMDevice? captureDevice,
        Action<string> onError,
        CancellationToken ct)
    {
        _micTask = Task.Run(() => MicrophonePumpAsync(settings, captureDevice, onError, ct), ct);
    }

    private async Task MicrophonePumpAsync(
        StillSpaceSettings settings,
        MMDevice? captureDevice,
        Action<string> onError,
        CancellationToken ct)
    {
        try
        {
            await Task.Yield();
            MMDevice resolved;
            try
            {
                resolved = captureDevice ?? AudioDeviceProbe.GetDefaultLiveVoiceCapture();
            }
            catch (Exception ex)
            {
                onError($"Microphone: {ex.Message}");
                return;
            }

            try
            {
                _capture = new WasapiCapture(resolved);
            }
            catch (Exception ex) when (captureDevice != null)
            {
                onError($"Selected microphone failed ({ex.Message}). Trying Windows default voice input.");
                try
                {
                    _capture = new WasapiCapture(AudioDeviceProbe.GetDefaultLiveVoiceCapture());
                }
                catch (Exception ex2)
                {
                    onError($"Microphone failed: {ex2.Message}");
                    return;
                }
            }
            catch (Exception ex)
            {
                onError($"Microphone failed: {ex.Message}");
                return;
            }

            _micBuffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                BufferLength = 256 * 1024,
                DiscardOnBufferOverflow = true
            };
            _capture.DataAvailable += (_, e) =>
            {
                if (e.BytesRecorded > 0)
                    _micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };

            ISampleProvider samples = _micBuffer.ToSampleProvider();
            if (_micBuffer.WaveFormat.Channels > 1)
                samples = new StereoToMonoSampleProvider(samples)
                {
                    LeftVolume = 0.5f,
                    RightVolume = 0.5f
                };

            var resampler = new WdlResamplingSampleProvider(samples, 24000);
            _micResampledPcm16 = resampler.ToWaveProvider16();

            _capture.StartRecording();

            // ~20 ms at 24 kHz mono PCM16 — smaller uplink frames reduce pipeline delay for VAD.
            var pcmChunk = new byte[960];
            while (!ct.IsCancellationRequested && _ws is { State: WebSocketState.Open })
            {
                if (_micResampledPcm16 == null) break;
                var read = _micResampledPcm16.Read(pcmChunk, 0, pcmChunk.Length);
                if (read <= 0)
                {
                    await Task.Delay(2, ct).ConfigureAwait(false);
                    continue;
                }

                var b64 = Convert.ToBase64String(pcmChunk.AsSpan(0, read));
                var appendObj = new JsonObject
                {
                    ["type"] = "input_audio_buffer.append",
                    ["audio"] = b64
                };
                await SendRawAsync(appendObj.ToJsonString(), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            /* normal */
        }
        catch (Exception ex)
        {
            onError(ex.Message);
        }
    }

    public async Task StopAsync()
    {
        _assistantAudioStreaming = false;
        _sessionConfigured = false;
        try
        {
            _sessionConfigureTcs?.TrySetCanceled();
        }
        catch
        {
            /* ignore */
        }

        try
        {
            _runCts?.Cancel();
        }
        catch
        {
            /* ignore */
        }

        try
        {
            _capture?.StopRecording();
        }
        catch
        {
            /* ignore */
        }

        _capture?.Dispose();
        _capture = null;
        _micBuffer = null;
        _micResampledPcm16 = null;

        try
        {
            _playbackOut?.Stop();
        }
        catch
        {
            /* ignore */
        }

        _playbackOut?.Dispose();
        _playbackOut = null;
        _playbackBuffer = null;

        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                        .ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }

            _ws.Dispose();
            _ws = null;
        }

        try
        {
            if (_receiveTask != null) await _receiveTask.ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }

        try
        {
            if (_micTask != null) await _micTask.ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }

        _receiveTask = null;
        _micTask = null;

        _lifecycleCallback = null;
        _sessionConfigureTcs = null;

        try
        {
            _runCts?.Dispose();
        }
        catch
        {
            /* ignore */
        }

        _runCts = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _sendLock.Dispose();
    }
}
