using Windows.Globalization;
using Windows.Media.SpeechRecognition;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.Speech.LiveCaptions;

/// <summary>
/// Free, offline-capable live dictation using Windows.Media.SpeechRecognition (WinRT).
/// HypothesisGenerated fires while speaking (partial text); ResultGenerated fires when a phrase is final.
/// </summary>
internal sealed class WindowsLiveSpeechEngine : ILiveSpeechService
{
    private readonly ILoggingService? _log;
    private readonly object _stopLock = new();
    private SpeechRecognizer? _recognizer;

    public event EventHandler<string>? PartialTextReceived;
    public event EventHandler<string>? FinalTextReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<bool>? ListeningStateChanged;

    public string ActiveEngineName => "Windows WinRT";
    public bool IsListening { get; private set; }

    public static bool IsSupported =>
        OperatingSystem.IsWindows() && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    public WindowsLiveSpeechEngine(ILoggingService? log = null)
    {
        _log = log;
    }

    public async Task StartAsync(string languageTag = "en-US", CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            SpeechRecognizer rec;
            try
            {
                var lang = new Language(string.IsNullOrWhiteSpace(languageTag) ? "en-US" : languageTag.Trim());
                rec = new SpeechRecognizer(lang);
            }
            catch
            {
                rec = new SpeechRecognizer();
            }

            rec.Constraints.Add(new SpeechRecognitionTopicConstraint(
                SpeechRecognitionScenario.Dictation, "dictation"));
            rec.HypothesisGenerated += OnHypothesisGenerated;
            rec.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
            rec.ContinuousRecognitionSession.Completed += OnCompleted;

            // Keep mic open during pauses while user leaves captions running.
            rec.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(10);

            lock (_stopLock)
            {
                _recognizer = rec;
            }

            await rec.CompileConstraintsAsync().AsTask(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await rec.ContinuousRecognitionSession.StartAsync().AsTask(cancellationToken).ConfigureAwait(false);

            IsListening = true;
            ListeningStateChanged?.Invoke(this, true);
            _log?.Information("Live Captions (WinRT) started");
        }
        catch (OperationCanceledException)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("privacy", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                msg +=
                    " Enable the microphone for desktop apps: Settings → Privacy & security → Microphone (Microphone access and “Let desktop apps access your microphone”). "
                    + "For WinRT dictation also check Settings → Privacy & security → Speech (online speech recognition).";
            }

            _log?.Warning($"Live Captions (WinRT) failed to start: {msg}");
            ErrorOccurred?.Invoke(this, msg);
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        SpeechRecognizer? rec;
        lock (_stopLock)
        {
            rec = _recognizer;
            _recognizer = null;
        }

        if (rec == null)
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
            rec.HypothesisGenerated -= OnHypothesisGenerated;
            rec.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
            rec.ContinuousRecognitionSession.Completed -= OnCompleted;
        }
        catch { /* ignore */ }

        try
        {
            if (rec.State != SpeechRecognizerState.Idle)
            {
                await rec.ContinuousRecognitionSession.StopAsync().AsTask(cancellationToken).ConfigureAwait(false);
            }
        }
        catch { /* ignore */ }
        finally
        {
            try { rec.Dispose(); } catch { /* ignore */ }
            IsListening = false;
            ListeningStateChanged?.Invoke(this, false);
            _log?.Debug("Live Captions (WinRT) stopped");
        }
    }

    private void OnHypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        var text = args.Hypothesis?.Text;
        if (string.IsNullOrEmpty(text)) return;
        PartialTextReceived?.Invoke(this, text);
    }

    private void OnResultGenerated(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (args.Result.Status != SpeechRecognitionResultStatus.Success) return;
        var text = args.Result.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        FinalTextReceived?.Invoke(this, text.Trim());
    }

    private void OnCompleted(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionCompletedEventArgs args)
    {
        // Benign: StopAsync() and silence timeouts complete here.
    }
}
