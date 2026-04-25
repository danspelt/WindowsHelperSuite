using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Speech.Services;

namespace WindowsHelperSuite.Speech.LiveCaptions;

/// <summary>
/// Azure Cognitive Services continuous speech recognition. Preferred when
/// <see cref="SpeechSettings.AzureSpeechKey"/> and <see cref="SpeechSettings.AzureSpeechRegion"/>
/// are configured — generally higher accuracy than the built-in Windows recognizer.
/// </summary>
internal sealed class AzureLiveSpeechEngine : ILiveSpeechService, IDisposable
{
    private readonly Func<SpeechSettings> _getSettings;
    private readonly ILoggingService? _log;
    private SpeechRecognizer? _recognizer;
    private AudioConfig? _audioConfig;

    public event EventHandler<string>? PartialTextReceived;
    public event EventHandler<string>? FinalTextReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<bool>? ListeningStateChanged;

    public string ActiveEngineName => "Azure Neural";
    public bool IsListening { get; private set; }

    public AzureLiveSpeechEngine(Func<SpeechSettings> getSettings, ILoggingService? log = null)
    {
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _log = log;
    }

    public bool IsConfigured()
    {
        var (key, region) = AzureSpeechEngine.ResolveCredentials(_getSettings());
        return key != null && region != null;
    }

    public async Task StartAsync(string languageTag = "en-US", CancellationToken cancellationToken = default)
    {
        if (IsListening)
        {
            return;
        }

        var settings = _getSettings();
        var (key, region) = AzureSpeechEngine.ResolveCredentials(settings);
        if (key == null || region == null)
        {
            ErrorOccurred?.Invoke(this, "Azure Speech is not configured (missing key or region).");
            return;
        }

        try
        {
            var config = SpeechConfig.FromSubscription(key, region);
            config.SpeechRecognitionLanguage = string.IsNullOrWhiteSpace(languageTag) ? "en-US" : languageTag.Trim();

            _audioConfig = LiveCaptionsMicrophoneHelper.CreateAudioConfigForAzure(_log);
            _recognizer = new SpeechRecognizer(config, _audioConfig);

            _recognizer.Recognizing += OnRecognizing;
            _recognizer.Recognized += OnRecognized;
            _recognizer.Canceled += OnCanceled;
            _recognizer.SessionStarted += OnSessionStarted;
            _recognizer.SessionStopped += OnSessionStopped;

            await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
            _log?.Information("Live Captions (Azure) started");
        }
        catch (Exception ex)
        {
            _log?.Warning($"Live Captions (Azure) failed to start: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Failed to start Azure speech: {ex.Message}");
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_recognizer == null)
        {
            if (IsListening)
            {
                IsListening = false;
                ListeningStateChanged?.Invoke(this, false);
            }
            return;
        }

        try
        {
            await _recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Warning($"Live Captions (Azure) stop error: {ex.Message}");
        }
        finally
        {
            Cleanup();
            IsListening = false;
            ListeningStateChanged?.Invoke(this, false);
            _log?.Debug("Live Captions (Azure) stopped");
        }
    }

    private void OnRecognizing(object? sender, SpeechRecognitionEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Result.Text))
        {
            PartialTextReceived?.Invoke(this, e.Result.Text);
        }
    }

    private void OnRecognized(object? sender, SpeechRecognitionEventArgs e)
    {
        if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
        {
            FinalTextReceived?.Invoke(this, e.Result.Text);
        }
    }

    private void OnCanceled(object? sender, SpeechRecognitionCanceledEventArgs e)
    {
        var reason = e.Reason.ToString();
        if (e.Reason == CancellationReason.Error)
        {
            reason = $"{reason}: {e.ErrorDetails}";
        }

        var msg = $"Azure recognition canceled ({reason}).";
        if (e.Reason == CancellationReason.Error &&
            !string.IsNullOrWhiteSpace(e.ErrorDetails) &&
            (e.ErrorDetails.Contains("microphone", StringComparison.OrdinalIgnoreCase) ||
             e.ErrorDetails.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
             e.ErrorDetails.Contains("0x", StringComparison.OrdinalIgnoreCase)))
        {
            msg +=
                " Check Windows: Settings → System → Sound (pick the correct microphone under Input). "
                + "Privacy → Microphone: enable access and “Let desktop apps access your microphone”.";
        }

        ErrorOccurred?.Invoke(this, msg);
    }

    private void OnSessionStarted(object? sender, SessionEventArgs e)
    {
        IsListening = true;
        ListeningStateChanged?.Invoke(this, true);
    }

    private void OnSessionStopped(object? sender, SessionEventArgs e)
    {
        IsListening = false;
        ListeningStateChanged?.Invoke(this, false);
    }

    private void Cleanup()
    {
        if (_recognizer != null)
        {
            _recognizer.Recognizing -= OnRecognizing;
            _recognizer.Recognized -= OnRecognized;
            _recognizer.Canceled -= OnCanceled;
            _recognizer.SessionStarted -= OnSessionStarted;
            _recognizer.SessionStopped -= OnSessionStopped;
            try { _recognizer.Dispose(); } catch { /* ignore */ }
            _recognizer = null;
        }

        if (_audioConfig != null)
        {
            try { _audioConfig.Dispose(); } catch { /* ignore */ }
            _audioConfig = null;
        }
    }

    public void Dispose() => Cleanup();
}
