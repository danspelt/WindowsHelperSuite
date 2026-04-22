using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Speech.LiveCaptions;

/// <summary>
/// Live captions engine router: prefers OpenAI Whisper (when API key is configured),
/// then Azure (when key+region are configured), otherwise falls back to the built-in
/// Windows WinRT recognizer. Consumers only see a single <see cref="ILiveSpeechService"/> surface.
/// </summary>
public sealed class CompositeLiveSpeechService : ILiveSpeechService, IDisposable
{
    private readonly Func<SpeechSettings> _getSettings;
    private readonly Func<LiveCaptionSettings> _getLiveCaptionSettings;
    private readonly ILoggingService? _log;
    private readonly OpenAiWhisperLiveSpeechEngine _whisper;
    private readonly AzureLiveSpeechEngine _azure;
    private readonly WindowsLiveSpeechEngine _windows;
    private ILiveSpeechService? _active;

    public event EventHandler<string>? PartialTextReceived;
    public event EventHandler<string>? FinalTextReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<bool>? ListeningStateChanged;

    public string ActiveEngineName => _active?.ActiveEngineName ?? ResolveEnginePreview();
    public bool IsListening => _active?.IsListening ?? false;

    public CompositeLiveSpeechService(
        Func<SpeechSettings> getSettings,
        Func<LiveCaptionSettings> getLiveCaptionSettings,
        ILoggingService? log = null)
    {
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _getLiveCaptionSettings = getLiveCaptionSettings ?? throw new ArgumentNullException(nameof(getLiveCaptionSettings));
        _log = log;
        _whisper = new OpenAiWhisperLiveSpeechEngine(getLiveCaptionSettings, log);
        _azure = new AzureLiveSpeechEngine(getSettings, log);
        _windows = new WindowsLiveSpeechEngine(log);
    }

    public async Task StartAsync(string languageTag = "en-US", CancellationToken cancellationToken = default)
    {
        if (_active?.IsListening == true)
        {
            return;
        }

        var chosen = ChooseEngine();
        Wire(chosen);
        _active = chosen;

        _log?.Information($"Live Captions using engine: {chosen.ActiveEngineName}");
        await chosen.StartAsync(languageTag, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var current = _active;
        if (current == null)
        {
            return;
        }

        await current.StopAsync(cancellationToken).ConfigureAwait(false);
        Unwire(current);
        _active = null;
    }

    private ILiveSpeechService ChooseEngine()
    {
        // Prefer Whisper (best for diverse speech patterns including cerebral palsy)
        if (_whisper.IsConfigured())
        {
            return _whisper;
        }

        if (_azure.IsConfigured())
        {
            return _azure;
        }

        if (WindowsLiveSpeechEngine.IsSupported)
        {
            return _windows;
        }

        // Fall back to Whisper anyway so it can emit a clear "not configured" error.
        return _whisper;
    }

    private string ResolveEnginePreview()
    {
        return _whisper.IsConfigured()
            ? "OpenAI Whisper (preview)"
            : _azure.IsConfigured()
                ? "Azure Neural (preview)"
                : WindowsLiveSpeechEngine.IsSupported
                    ? "Windows WinRT (preview)"
                    : "Unavailable";
    }

    private void Wire(ILiveSpeechService engine)
    {
        engine.PartialTextReceived += ForwardPartial;
        engine.FinalTextReceived += ForwardFinal;
        engine.ErrorOccurred += ForwardError;
        engine.ListeningStateChanged += ForwardState;
    }

    private void Unwire(ILiveSpeechService engine)
    {
        engine.PartialTextReceived -= ForwardPartial;
        engine.FinalTextReceived -= ForwardFinal;
        engine.ErrorOccurred -= ForwardError;
        engine.ListeningStateChanged -= ForwardState;
    }

    private void ForwardPartial(object? sender, string text) => PartialTextReceived?.Invoke(this, text);
    private void ForwardFinal(object? sender, string text) => FinalTextReceived?.Invoke(this, text);
    private void ForwardError(object? sender, string message) => ErrorOccurred?.Invoke(this, message);
    private void ForwardState(object? sender, bool listening) => ListeningStateChanged?.Invoke(this, listening);

    public void Dispose()
    {
        try { _ = StopAsync(); } catch { /* ignore */ }
        _whisper.Dispose();
        _azure.Dispose();
    }
}
