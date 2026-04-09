using System.Collections.Concurrent;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Speech.Services;

/// <summary>
/// Routes speech: Azure Cognitive Services (online neural) with System.Speech offline fallback.
/// </summary>
public sealed class SpeechService : ISpeechService, IDisposable
{
    private readonly Func<SpeechSettings> _getSettings;
    private readonly ILoggingService? _log;
    private readonly HeadsetDetector _headsetDetector = new();
    private readonly AzureSpeechEngine _azure = new();
    private readonly EdgeTtsEngine _edge = new();
    private readonly WindowsSpeechEngine _windows;
    private readonly System.Timers.Timer _deviceCheckTimer;
    private volatile bool _headsetConnected;
    private bool _disposed;

    private double _rate = 1.0;
    private float _volume = 1.0f;
    private volatile string _voiceRouteStatus = "Idle";

    private readonly ConcurrentQueue<QueuedSpeechItem> _queue = new();
    private volatile bool _isSpeaking;

    private readonly object _speakCtsLock = new();
    private CancellationTokenSource? _speakCts;

    // ── Typing-speed gate (unchanged behavior) ──
    private const int TypingSpeedThreshold = 4;
    private const int TypingWindowMs = 800;
    private const int TypingCooldownMs = 600;
    private readonly Queue<long> _keystrokeTicks = new();
    private readonly object _keystrokeLock = new();
    private long _lastKeystrokeTick;
    private volatile bool _mutedBySpeed;

    public SpeechService(Func<SpeechSettings> getSettings, ILoggingService? log = null)
    {
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _log = log;
        _windows = new WindowsSpeechEngine(log);

        _headsetConnected = _headsetDetector.IsHeadsetConnected();
        _deviceCheckTimer = new System.Timers.Timer(5000) { AutoReset = true };
        _deviceCheckTimer.Elapsed += (_, _) => _headsetConnected = _headsetDetector.IsHeadsetConnected();
        _deviceCheckTimer.Start();
    }

    public bool IsPreferredDeviceConnected => _headsetConnected;

    public string VoiceName => AzureSpeechEngine.ResolveVoiceName(_getSettings());

    public string VoiceRouteStatus => _voiceRouteStatus;

    public bool IsMutedByTypingSpeed => _mutedBySpeed;

    public void SetRate(double rate)
    {
        _rate = Math.Clamp(rate, 0.5, 3.0);
    }

    public void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
    }

    public void NotifyKeystroke()
    {
        var now = Environment.TickCount64;
        lock (_keystrokeLock)
        {
            _lastKeystrokeTick = now;
            _keystrokeTicks.Enqueue(now);

            while (_keystrokeTicks.Count > 0 &&
                   (now - _keystrokeTicks.Peek()) > TypingWindowMs)
            {
                _keystrokeTicks.Dequeue();
            }

            _mutedBySpeed = _keystrokeTicks.Count >= TypingSpeedThreshold;
        }
    }

    private bool IsTypingCooldownActive()
    {
        var elapsed = Environment.TickCount64 - Interlocked.Read(ref _lastKeystrokeTick);
        if (elapsed >= TypingCooldownMs)
        {
            _mutedBySpeed = false;
            return false;
        }

        return _mutedBySpeed;
    }

    public async void Speak(string text)
    {
        var normalizedText = NormalizeSpokenText(text);
        if (string.IsNullOrWhiteSpace(normalizedText) || _disposed)
        {
            return;
        }

        if (!MaySpeak(_getSettings()))
        {
            return;
        }

        ClearQueue();
        CancelActiveSynthesis();
        var token = BeginNewUtterance();

        try
        {
            await RouteSpeakAsync(normalizedText, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when user hits Esc / Stop
        }
        catch (Exception ex)
        {
            _log?.Warning($"Speech routing error: {ex.Message}");
        }
    }

    public void SpeakQueued(string text, bool ignoreTypingCooldown = false)
    {
        var normalizedText = NormalizeSpokenText(text);
        if (string.IsNullOrWhiteSpace(normalizedText) || _disposed)
        {
            return;
        }

        if (!MaySpeak(_getSettings()))
        {
            return;
        }

        if (!ignoreTypingCooldown && IsTypingCooldownActive())
        {
            return;
        }

        while (_queue.TryDequeue(out _)) { }

        _queue.Enqueue(new QueuedSpeechItem(normalizedText, ignoreTypingCooldown));
        ProcessQueue();
    }

    public void Stop()
    {
        CancelActiveSynthesis();
        ClearQueue();
    }

    public void ClearQueue()
    {
        while (_queue.TryDequeue(out _)) { }
    }

    private bool MaySpeak(SpeechSettings settings)
    {
        if (settings.OnlySpeakOnHeadset && !_headsetConnected)
        {
            return false;
        }

        return true;
    }

    private void CancelActiveSynthesis()
    {
        lock (_speakCtsLock)
        {
            _speakCts?.Cancel();
        }

        _azure.StopSpeaking();
        _edge.StopSpeaking();
        _windows.Stop();
    }

    private CancellationToken BeginNewUtterance()
    {
        lock (_speakCtsLock)
        {
            _speakCts?.Cancel();
            _speakCts?.Dispose();
            _speakCts = new CancellationTokenSource();
            return _speakCts.Token;
        }
    }

    private async void ProcessQueue()
    {
        if (_isSpeaking || _disposed)
        {
            return;
        }

        while (_queue.TryDequeue(out var item))
        {
            if (_disposed || !MaySpeak(_getSettings()))
            {
                break;
            }

            if (!item.IgnoreTypingCooldown && IsTypingCooldownActive())
            {
                break;
            }

            _isSpeaking = true;
            try
            {
                CancelActiveSynthesis();
                var token = BeginNewUtterance();
                await RouteSpeakAsync(item.Text, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _log?.Warning($"Queued speech error: {ex.Message}");
            }
            finally
            {
                _isSpeaking = false;
            }
        }
    }

    private async Task RouteSpeakAsync(string text, CancellationToken cancellationToken)
    {
        var settings = _getSettings();
        var rateOffline = OfflineRateFromDouble(_rate);
        var volOffline = (int)Math.Round(_volume * 100);

        switch (settings.VoiceMode)
        {
            case SpeechVoiceMode.OfflineOnly:
                _windows.Speak(text, rateOffline, volOffline, OfflineVoiceOrNull(settings), _log);
                _voiceRouteStatus = "Offline only";
                return;

            case SpeechVoiceMode.OnlineOnly:
                if (!_azure.IsConfigured(settings))
                {
                    _voiceRouteStatus = "Unavailable";
                    _log?.Debug("Voice mode Online only: Azure key/region not configured.");
                    return;
                }

                if (!AzureSpeechEngine.IsNetworkAvailable())
                {
                    _voiceRouteStatus = "Unavailable";
                    _log?.Debug("Voice mode Online only: network unavailable.");
                    return;
                }

                var ssmlOnline = AzureSpeechEngine.BuildSsml(text, settings, _rate);
                var onlineOk = await _azure.TrySpeakSsmlAsync(ssmlOnline, settings, _log, cancellationToken)
                    .ConfigureAwait(false);
                _voiceRouteStatus = onlineOk ? "Online" : "Unavailable";
                return;

            default:
                // 1. Try Azure (highest quality, needs API key)
                if (_azure.IsConfigured(settings) && AzureSpeechEngine.IsNetworkAvailable())
                {
                    var ssml = AzureSpeechEngine.BuildSsml(text, settings, _rate);
                    var ok = await _azure.TrySpeakSsmlAsync(ssml, settings, _log, cancellationToken)
                        .ConfigureAwait(false);
                    if (ok)
                    {
                        _voiceRouteStatus = "Online (Azure)";
                        return;
                    }
                }

                // 2. Try Edge TTS (free neural voices, needs internet, no API key)
                if (AzureSpeechEngine.IsNetworkAvailable())
                {
                    var edgeOk = await _edge.TrySpeakAsync(
                        text, settings, _rate, _volume, _log, cancellationToken)
                        .ConfigureAwait(false);
                    if (edgeOk)
                    {
                        _voiceRouteStatus = "Online (Edge Neural)";
                        return;
                    }
                }

                // 3. Offline fallback (System.Speech — robotic but always available)
                _windows.Speak(text, rateOffline, volOffline, OfflineVoiceOrNull(settings), _log);
                _voiceRouteStatus = "Offline fallback";
                break;
        }
    }

    private static string? OfflineVoiceOrNull(SpeechSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.OfflineVoiceName)
            ? null
            : settings.OfflineVoiceName.Trim();
    }

    private static int OfflineRateFromDouble(double rate)
    {
        var v = (int)Math.Round((rate - 1.0) * 5);
        return Math.Clamp(v, -10, 10);
    }

    private static string NormalizeSpokenText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Null separator: split on all Unicode whitespace so NBSP/thin space/etc. do not merge words for TTS.
        return string.Join(' ', text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _deviceCheckTimer.Stop();
        _deviceCheckTimer.Dispose();
        ClearQueue();
        CancelActiveSynthesis();
        lock (_speakCtsLock)
        {
            _speakCts?.Dispose();
            _speakCts = null;
        }

        _edge.Dispose();
        _windows.Dispose();
    }

    private sealed record QueuedSpeechItem(string Text, bool IgnoreTypingCooldown);
}
