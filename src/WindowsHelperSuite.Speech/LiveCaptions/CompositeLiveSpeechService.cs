using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Speech.LiveCaptions;

/// <summary>
/// Represents an available speech recognition engine option.
/// </summary>
public sealed class SpeechEngineOption
{
    public string Id { get; }
    public string Name { get; }
    public bool IsAvailable { get; }

    public SpeechEngineOption(string id, string name, bool isAvailable)
    {
        Id = id;
        Name = name;
        IsAvailable = isAvailable;
    }

    public override string ToString() => Name;
}

/// <summary>
/// Live captions engine router: supports OpenAI Whisper, Azure, and Windows WinRT.
/// Can auto-select or be explicitly switched via <see cref="SwitchToEngine"/>.
/// Consumers only see a single <see cref="ILiveSpeechService"/> surface.
/// </summary>
public sealed class CompositeLiveSpeechService : ILiveSpeechService, IDisposable
{
    public const string EngineWhisper = "whisper";
    public const string EngineAzure = "azure";
    public const string EngineWindows = "windows";
    public const string EngineAuto = "auto";
    private readonly Func<SpeechSettings> _getSettings;
    private readonly Func<LiveCaptionSettings> _getLiveCaptionSettings;
    private readonly ILoggingService? _log;
    private readonly OpenAiWhisperLiveSpeechEngine _whisper;
    private readonly AzureLiveSpeechEngine _azure;
    private readonly WindowsLiveSpeechEngine _windows;
    private ILiveSpeechService? _active;
    private string _preferredEngine = EngineAuto;

    public event EventHandler<string>? PartialTextReceived;
    public event EventHandler<string>? FinalTextReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<bool>? ListeningStateChanged;

    public string ActiveEngineName => _active?.ActiveEngineName ?? ResolveEnginePreview();
    public bool IsListening => _active?.IsListening ?? false;

    /// <summary>
    /// Gets or sets the preferred engine ID (whisper, azure, windows, or auto).
    /// Changing this takes effect on the next StartAsync or immediately if currently listening.
    /// </summary>
    public string PreferredEngine
    {
        get => _preferredEngine;
        set => _preferredEngine = value?.Trim().ToLowerInvariant() ?? EngineAuto;
    }

    /// <summary>
    /// Returns all available engines with their availability status.
    /// </summary>
    public IReadOnlyList<SpeechEngineOption> GetAvailableEngines()
    {
        return new[]
        {
            new SpeechEngineOption(EngineWhisper, "OpenAI Whisper", _whisper.IsConfigured()),
            new SpeechEngineOption(EngineAzure, "Azure Speech", _azure.IsConfigured()),
            new SpeechEngineOption(EngineWindows, "Windows WinRT", WindowsLiveSpeechEngine.IsSupported),
            new SpeechEngineOption(EngineAuto, "Auto (Best Available)", true)
        };
    }

    /// <summary>
    /// Switches to the specified engine. If currently listening, stops and restarts with the new engine.
    /// </summary>
    public async Task<bool> SwitchToEngineAsync(string engineId, string languageTag = "en-US", CancellationToken cancellationToken = default)
    {
        var target = engineId?.Trim().ToLowerInvariant() ?? EngineAuto;
        if (target == EngineAuto)
        {
            _preferredEngine = EngineAuto;
        }
        else if (target != EngineWhisper && target != EngineAzure && target != EngineWindows)
        {
            _log?.Warning($"Invalid engine ID: {engineId}");
            return false;
        }
        else
        {
            _preferredEngine = target;
        }

        var wasListening = IsListening;
        if (wasListening)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }

        if (wasListening || _active != null)
        {
            try
            {
                await StartAsync(languageTag, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _log?.Warning($"Failed to switch to engine {engineId}: {ex.Message}");
                return false;
            }
        }

        return true;
    }

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
        // If user explicitly selected an engine, try that first
        if (_preferredEngine == EngineWhisper)
        {
            if (_whisper.IsConfigured()) return _whisper;
            _log?.Warning("Whisper requested but not configured; falling back");
        }
        else if (_preferredEngine == EngineAzure)
        {
            if (_azure.IsConfigured()) return _azure;
            _log?.Warning("Azure requested but not configured; falling back");
        }
        else if (_preferredEngine == EngineWindows)
        {
            if (WindowsLiveSpeechEngine.IsSupported) return _windows;
            _log?.Warning("Windows requested but not supported; falling back");
        }

        // Auto mode: Prefer Whisper (best for diverse speech patterns including cerebral palsy)
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
