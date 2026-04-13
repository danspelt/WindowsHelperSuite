using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace StillSpace.Services;

/// <summary>
/// Uses Windows.Media.SpeechRecognition (WinRT) for dictation with
/// <see cref="SpeechRecognizer.HypothesisGenerated"/> so the UI gets live partial text.
/// System.Speech dictation rarely raises SpeechHypothesized, so it feels “dead” until a pause.
/// </summary>
public sealed class WindowsSpeechLiveService
{
    private readonly object _stopLock = new();
    private SpeechRecognizer? _recognizer;
    private Action<string>? _onHypothesis;
    private Action<string>? _onFinal;

    public static bool IsSupported =>
        OperatingSystem.IsWindows() && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    /// <summary>Set when the last <see cref="StartAsync"/> fails (for UI fallback messaging).</summary>
    public string? LastStartFailureMessage { get; private set; }

    public async Task<bool> StartAsync(
        string languageTag,
        Action<string> onHypothesis,
        Action<string> onFinal,
        CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);

        LastStartFailureMessage = null;
        _onHypothesis = onHypothesis;
        _onFinal = onFinal;

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

            rec.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            rec.HypothesisGenerated += OnHypothesisGenerated;
            rec.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
            rec.ContinuousRecognitionSession.Completed += OnCompleted;

            // Keep mic open during pauses while toggle / push-to-talk is active.
            rec.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(10);

            lock (_stopLock)
                _recognizer = rec;

            await rec.CompileConstraintsAsync().AsTask(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await rec.ContinuousRecognitionSession.StartAsync().AsTask(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            await StopAsync().ConfigureAwait(false);
            return false;
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("privacy", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("permission", StringComparison.OrdinalIgnoreCase))
            {
                msg += " Enable online speech / dictation in Windows Settings → Privacy → Speech.";
            }

            LastStartFailureMessage = msg;
            await StopAsync().ConfigureAwait(false);
            return false;
        }
    }

    private void OnHypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        var text = args.Hypothesis?.Text;
        if (string.IsNullOrEmpty(text)) return;
        _onHypothesis?.Invoke(text);
    }

    private void OnResultGenerated(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (args.Result.Status != SpeechRecognitionResultStatus.Success) return;
        var text = args.Result.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        _onFinal?.Invoke(text.Trim());
    }

    private void OnCompleted(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
    {
        // StopAsync() and silence timeouts complete here; avoid surfacing benign statuses as errors.
    }

    public async Task StopAsync()
    {
        SpeechRecognizer? rec;
        lock (_stopLock)
        {
            rec = _recognizer;
            _recognizer = null;
        }
        _onHypothesis = null;
        _onFinal = null;

        if (rec == null) return;

        try
        {
            rec.HypothesisGenerated -= OnHypothesisGenerated;
            rec.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
            rec.ContinuousRecognitionSession.Completed -= OnCompleted;
        }
        catch
        {
            /* ignore */
        }

        try
        {
            if (rec.State != SpeechRecognizerState.Idle)
                await rec.ContinuousRecognitionSession.StopAsync().AsTask().ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }
        finally
        {
            try { rec.Dispose(); } catch { /* ignore */ }
        }
    }
}
