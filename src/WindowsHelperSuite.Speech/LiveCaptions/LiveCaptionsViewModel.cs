using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;

namespace WindowsHelperSuite.Speech.LiveCaptions;

/// <summary>
/// Backing ViewModel for <c>LiveCaptionsWindow</c>. Drives start/stop/clear/copy/save
/// against an <see cref="ILiveSpeechService"/>, exposes user toggles (Always on top,
/// Fullscreen, Append mode, font size), and persists those to <see cref="LiveCaptionSettings"/>.
/// </summary>
public partial class LiveCaptionsViewModel : ObservableObject, IDisposable
{
    private readonly ILiveSpeechService _speech;
    private readonly ILoggingService? _log;
    private readonly ISettingsService? _settingsService;
    private readonly Action<bool>? _onFullscreenRequested;

    [ObservableProperty] private string _displayText = "Press Start Listening";
    [ObservableProperty] private string _statusText = "Idle";
    [ObservableProperty] private string _engineText = "";
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private double _captionFontSize = 96;
    [ObservableProperty] private bool _appendMode = true;
    [ObservableProperty] private bool _singleSentenceMode; // true = show only current utterance, replacing in real-time
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _isFullscreen;
    [ObservableProperty] private ObservableCollection<SpeechEngineOption> _availableEngines = new();
    [ObservableProperty] private SpeechEngineOption? _selectedEngine;

    private string _finalTranscript = string.Empty;
    private string _partial = string.Empty;
    private bool _isLoadingSettings;

    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand CopyCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand DismissErrorCommand { get; }
    public IRelayCommand ToggleAlwaysOnTopCommand { get; }
    public IRelayCommand ToggleFullscreenCommand { get; }
    public IRelayCommand ToggleAppendModeCommand { get; }

    public LiveCaptionsViewModel(
        ILiveSpeechService speech,
        ILoggingService? log = null,
        ISettingsService? settingsService = null,
        Action<bool>? onFullscreenRequested = null)
    {
        _speech = speech ?? throw new ArgumentNullException(nameof(speech));
        _log = log;
        _settingsService = settingsService;
        _onFullscreenRequested = onFullscreenRequested;

        // Load persisted preferences (if any) before wiring change notifications.
        LoadFromSettings();

        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsListening);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsListening);
        ClearCommand = new RelayCommand(Clear);
        CopyCommand = new RelayCommand(Copy);
        SaveCommand = new RelayCommand(Save);
        DismissErrorCommand = new RelayCommand(DismissError);
        ToggleAlwaysOnTopCommand = new RelayCommand(() => AlwaysOnTop = !AlwaysOnTop);
        ToggleFullscreenCommand = new RelayCommand(() => IsFullscreen = !IsFullscreen);
        ToggleAppendModeCommand = new RelayCommand(() => AppendMode = !AppendMode);

        _speech.PartialTextReceived += OnPartial;
        _speech.FinalTextReceived += OnFinal;
        _speech.ErrorOccurred += OnError;
        _speech.ListeningStateChanged += OnStateChanged;

        EngineText = _speech.ActiveEngineName;

        // Initialize engine selector
        InitializeEngineSelector();
    }

    partial void OnSelectedEngineChanged(SpeechEngineOption? value)
    {
        if (value == null || _speech is not CompositeLiveSpeechService composite)
            return;

        // Switch engine on the fly
        _ = Task.Run(async () =>
        {
            try
            {
                var lang = _settingsService?.Settings.LiveCaptions.RecognitionLanguage ?? "en-US";
                var success = await composite.SwitchToEngineAsync(value.Id, lang).ConfigureAwait(false);
                if (success)
                {
                    RunOnUi(() => EngineText = _speech.ActiveEngineName);
                }
            }
            catch (Exception ex)
            {
                _log?.Warning($"Engine switch failed: {ex.Message}");
            }
        });
    }

    private void InitializeEngineSelector()
    {
        if (_speech is not CompositeLiveSpeechService composite)
            return;

        var engines = composite.GetAvailableEngines();
        AvailableEngines = new ObservableCollection<SpeechEngineOption>(engines);

        // Select current or default
        var current = engines.FirstOrDefault(e => e.Id == composite.PreferredEngine)
            ?? engines.FirstOrDefault(e => e.IsAvailable)
            ?? engines.Last();
        SelectedEngine = current;
    }

    private async Task StartAsync()
    {
        // Reset any prior error so the new attempt is not obscured by stale text
        // and so OnStateChanged is free to update StatusText normally.
        DismissError();
        StatusText = "Starting…";
        var lang = _settingsService?.Settings.LiveCaptions.RecognitionLanguage ?? "en-US";
        await _speech.StartAsync(lang).ConfigureAwait(false);
        RunOnUi(() => EngineText = _speech.ActiveEngineName);
    }

    private async Task StopAsync()
    {
        StatusText = "Stopping…";
        await _speech.StopAsync().ConfigureAwait(false);
    }

    private void Clear()
    {
        _finalTranscript = string.Empty;
        _partial = string.Empty;
        DismissError();
        DisplayText = IsListening ? "Listening…" : "Press Start Listening";
        StatusText = "Cleared";
    }

    private void DismissError()
    {
        ErrorText = string.Empty;
        HasError = false;
    }

    private void Copy()
    {
        var text = BuildFullText();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "Nothing to copy";
            return;
        }
        try
        {
            Clipboard.SetText(text);
            StatusText = "Copied to clipboard";
        }
        catch (Exception ex)
        {
            StatusText = $"Copy failed: {ex.Message}";
        }
    }

    private void Save()
    {
        var text = BuildFullText();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "Nothing to save";
            return;
        }
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "WindowsHelperSuite", "LiveCaptions");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"captions_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, text);
            StatusText = $"Saved: {path}";
            _log?.Information($"Live Captions transcript saved: {path}");
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    private string BuildFullText() =>
        string.IsNullOrWhiteSpace(_partial) ? _finalTranscript : $"{_finalTranscript} {_partial}".Trim();

    private void OnPartial(object? sender, string text)
    {
        RunOnUi(() =>
        {
            _partial = text;
            // In single sentence mode, clear the accumulated final text so we only show current utterance
            if (SingleSentenceMode)
            {
                _finalTranscript = string.Empty;
            }
            RefreshDisplay();
        });
    }

    private void OnFinal(object? sender, string text)
    {
        RunOnUi(() =>
        {
            if (SingleSentenceMode)
            {
                // Single sentence mode: show this final result, discard previous
                _finalTranscript = text;
                _partial = string.Empty;
            }
            else if (AppendMode)
            {
                _finalTranscript = string.IsNullOrWhiteSpace(_finalTranscript)
                    ? text
                    : $"{_finalTranscript} {text}";
                _partial = string.Empty;
            }
            else
            {
                // Replace-mode: each final result wipes the prior transcript.
                _finalTranscript = text;
                _partial = string.Empty;
            }

            RefreshDisplay();
        });
    }

    private void OnError(object? sender, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        RunOnUi(() =>
        {
            ErrorText = message;
            HasError = true;
            // Keep the status bar terse; the full message lives in the red banner.
            StatusText = "Error";
        });
    }

    private void OnStateChanged(object? sender, bool listening)
    {
        RunOnUi(() =>
        {
            IsListening = listening;
            // When an error has been surfaced, keep it visible in the status bar
            // instead of overwriting it with "Stopped" from the shutdown that
            // follows a failed Start.
            if (!HasError || listening)
            {
                StatusText = listening ? "Listening" : "Stopped";
            }
            EngineText = _speech.ActiveEngineName;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        });
    }

    private void RefreshDisplay()
    {
        var text = BuildFullText();
        DisplayText = string.IsNullOrWhiteSpace(text)
            ? (IsListening ? "Listening…" : "Press Start Listening")
            : text;
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    // ── Persistence ────────────────────────────────────────────────────────────

    private void LoadFromSettings()
    {
        if (_settingsService == null) return;

        _isLoadingSettings = true;
        try
        {
            var lc = _settingsService.Settings.LiveCaptions;
            CaptionFontSize = Math.Clamp(lc.FontSize, 36, 240);
            AppendMode = lc.AppendMode;
            AlwaysOnTop = lc.AlwaysOnTop;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void SaveToSettings()
    {
        if (_settingsService == null || _isLoadingSettings) return;

        var lc = _settingsService.Settings.LiveCaptions;
        lc.FontSize = CaptionFontSize;
        lc.AppendMode = AppendMode;
        lc.AlwaysOnTop = AlwaysOnTop;

        try
        {
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            _log?.Warning($"Live Captions settings save failed: {ex.Message}");
        }
    }

    partial void OnCaptionFontSizeChanged(double value) => SaveToSettings();
    partial void OnAppendModeChanged(bool value) => SaveToSettings();
    partial void OnAlwaysOnTopChanged(bool value) => SaveToSettings();

    partial void OnIsFullscreenChanged(bool value)
    {
        // Fullscreen is not persisted (session-only); delegate to the view.
        _onFullscreenRequested?.Invoke(value);
    }

    public void Dispose()
    {
        _speech.PartialTextReceived -= OnPartial;
        _speech.FinalTextReceived -= OnFinal;
        _speech.ErrorOccurred -= OnError;
        _speech.ListeningStateChanged -= OnStateChanged;

        SaveToSettings();

        if (_speech.IsListening)
        {
            try { _speech.StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
        }
    }
}
