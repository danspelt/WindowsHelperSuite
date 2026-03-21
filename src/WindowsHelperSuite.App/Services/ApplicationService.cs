using System.Linq;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Infrastructure.Hooks;
using WindowsHelperSuite.Infrastructure.Services;
using WindowsHelperSuite.Hotkeys.Services;
using WindowsHelperSuite.Overlay.Services;
using WindowsHelperSuite.Input.Services;
using WindowsHelperSuite.Prediction.Services;

namespace WindowsHelperSuite.App.Services;

public class ApplicationService : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private readonly TrayIconService _trayIconService;
    private readonly HotkeyService _hotkeyService;
    private readonly OverlayService _overlayService;
    private readonly InputService _inputService;
    private readonly IPredictionService _predictionService;
    private readonly Queue<string> _recentWords = new();
    private const int MaxContextWords = 6;

    public ApplicationService()
    {
        _loggingService = new LoggingService();
        _settingsService = new SettingsService();
        _settingsService.Load();

        _trayIconService = new TrayIconService(_loggingService);
        _hotkeyService = new HotkeyService(_loggingService);
        _overlayService = new OverlayService(_loggingService, _settingsService);
        _inputService = new InputService(_loggingService);
        _predictionService = new PredictionService();

        // Wire up suggestion selection to text injection
        _overlayService.SuggestionSelected += OnSuggestionSelected;

        WireInputToOverlay();
        RegisterHotkeyActions();
        RegisterDefaultHotkeys();

        _hotkeyService.Start();
        _inputService.Start();

        _loggingService.Information("Application started (v3 - text input validation fix)");
    }

    public void Run()
    {
        _loggingService.Information("Application running");
    }

    private void WireInputToOverlay()
    {
        // Show overlay when typing starts
        _inputService.TypingStarted += (s, e) => ShowOverlay();

        // Update suggestions as text is captured
        _inputService.TextCaptured += (s, text) => UpdateSuggestions(text);

        _inputService.WordTyped += (s, word) => OnWordTyped(word);
        _inputService.SentenceTyped += (s, sentence) => OnSentenceTyped(sentence);

        // Hide overlay when typing stops (after 10s inactivity)
        _inputService.TypingStopped += (s, e) =>
        {
            HideOverlay();
            _currentWord = string.Empty;
            _hasValidTextInput = false;
            _loggingService.Debug("Overlay hidden - typing stopped");
        };

        // Hide overlay when typing on desktop (no valid text input)
        _inputService.InvalidTypingDetected += (s, e) =>
        {
            HideOverlay();
            _loggingService.Debug("Overlay hidden - invalid typing detected (desktop)");
        };

        // Handle selection keys 1-9
        _inputService.SelectionKeyPressed += (s, slot) =>
        {
            _loggingService.Debug($"Selection key pressed: slot {slot}");
            _overlayService.HandleSelectionKey(slot);
        };

        // Handle paging
        _inputService.NextPageKeyPressed += (s, e) => _overlayService.MoveToNextPage();
        _inputService.PreviousPageKeyPressed += (s, e) => _overlayService.MoveToPreviousPage();
    }

    private string _currentWord = string.Empty;
    private List<SuggestionItem> _currentSuggestions = [];
    private bool _hasValidTextInput = false;

    private void ShowOverlay()
    {
        _hasValidTextInput = true;
        _inputService.IsOverlayVisible = true;
        UpdateSuggestions(_currentWord);
        _loggingService.Debug("Overlay shown - typing started");
    }

    private void HideOverlay()
    {
        _overlayService.HideSuggestions();
        _inputService.IsOverlayVisible = false;
    }

    private void UpdateSuggestions(string text)
    {
        // Only show overlay if we have valid text input
        if (!_hasValidTextInput)
        {
            _loggingService.Debug("UpdateSuggestions called but no valid text input, skipping");
            return;
        }

        _currentWord = text;

        _currentSuggestions = _predictionService.GetSuggestions(GetContextText(), text).ToList();
        _overlayService.ShowSuggestions(_currentSuggestions);
    }

    private void OnSuggestionSelected(object? sender, int slot)
    {
        _loggingService.Debug($"OnSuggestionSelected called with slot {slot}, _currentSuggestions count: {_currentSuggestions.Count}");
        var suggestion = _currentSuggestions.FirstOrDefault(s => s.Slot == slot);
        if (suggestion != null)
        {
            _loggingService.Debug($"Found suggestion in _currentSuggestions: {suggestion.DisplayText}");
            // Clear the typed text by sending backspaces
            if (!string.IsNullOrEmpty(_currentWord))
            {
                Win32TextInjection.SendBackspace(_currentWord.Length);
            }

            // Insert the complete word or phrase
            Win32TextInjection.SendText(suggestion.DisplayText + " ");
            LearnAcceptedSuggestion(suggestion.DisplayText);

            _loggingService.Information($"Inserted: {suggestion.DisplayText}");

            // Clear current word and hide overlay
            _currentWord = string.Empty;
            _inputService.ClearCurrentWord();
            HideOverlay();
        }
        else
        {
            _loggingService.Warning($"No suggestion found in _currentSuggestions for slot {slot}");
        }
    }

    private void RegisterHotkeyActions()
    {
        _hotkeyService.RegisterAction("ToggleOverlay", () =>
        {
            // Only show overlay if there's a text input focused
            if (Win32Caret.GetCaretPosition(out var x, out var y) && (x != 0 || y != 0))
            {
                ShowOverlay();
            }
            else
            {
                _loggingService.Debug("ToggleOverlay hotkey pressed but no text input detected");
            }
        });

        _hotkeyService.RegisterAction("PauseWriter", () =>
        {
            _inputService.IsEnabled = !_inputService.IsEnabled;
            _loggingService.Information($"Writer {(_inputService.IsEnabled ? "enabled" : "paused")}");
        });

        _hotkeyService.RegisterAction("AddToWordBank", () => AddCurrentTypingToWordBank());
        _hotkeyService.RegisterAction("AddPhraseToWordBank", () => AddCurrentPhraseToWordBank());
    }

    private void RegisterDefaultHotkeys()
    {
        var settings = _settingsService.Settings.Hotkeys.Bindings;

        if (settings.Count == 0)
        {
            _hotkeyService.RegisterHotkey("VolumeUp", "Ctrl+Shift+Up");
            _hotkeyService.RegisterHotkey("VolumeDown", "Ctrl+Shift+Down");
            _hotkeyService.RegisterHotkey("VolumeMute", "Ctrl+Shift+M");
            _hotkeyService.RegisterHotkey("WriterRefresh", "`");
            _hotkeyService.RegisterHotkey("ToggleOverlay", "Ctrl+Shift+O");
            _hotkeyService.RegisterHotkey("PauseWriter", "Ctrl+Shift+P");
            _hotkeyService.RegisterHotkey("AddToWordBank", "Ctrl+`");
            _hotkeyService.RegisterHotkey("AddPhraseToWordBank", "Ctrl+Shift+`");

            _loggingService.Information("Registered default hotkeys");
        }
        else
        {
            foreach (var binding in settings.Where(b => b.Enabled))
            {
                _hotkeyService.RegisterHotkey(binding.ActionName, binding.Gesture);
            }
        }
    }

    private void OnWordTyped(string word)
    {
        LearnWordOrPhrase(word);
        AppendContext(word);
    }

    private void OnSentenceTyped(string sentence)
    {
        var normalized = sentence.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _predictionService.LearnPhrase(normalized);
        _recentWords.Clear();
    }

    private void LearnAcceptedSuggestion(string acceptedText)
    {
        LearnWordOrPhrase(acceptedText);

        foreach (var part in acceptedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AppendContext(part);
        }
    }

    private void LearnWordOrPhrase(string text)
    {
        var normalized = text.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (normalized.Contains(' '))
        {
            _predictionService.LearnPhrase(normalized);
        }
        else
        {
            _predictionService.LearnWord(normalized);
        }
    }

    private void AppendContext(string word)
    {
        var normalized = word.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _recentWords.Enqueue(normalized);
        while (_recentWords.Count > MaxContextWords)
        {
            _recentWords.Dequeue();
        }
    }

    private string GetContextText()
    {
        return string.Join(' ', _recentWords);
    }

    private void AddCurrentTypingToWordBank()
    {
        var candidate = !string.IsNullOrWhiteSpace(_currentWord)
            ? _currentWord
            : _inputService.GetCurrentWord();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            _loggingService.Information("AddToWordBank requested but there is no current word");
            return;
        }

        LearnWordOrPhrase(candidate);
        _loggingService.Information($"Added to word bank: {candidate}");
    }

    private void AddCurrentPhraseToWordBank()
    {
        var currentTyping = _inputService.GetCurrentWord();
        var parts = _recentWords.ToList();

        if (!string.IsNullOrWhiteSpace(currentTyping))
        {
            parts.Add(currentTyping);
        }

        var phrase = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        if (string.IsNullOrWhiteSpace(phrase))
        {
            _loggingService.Information("AddPhraseToWordBank requested but there is no current phrase context");
            return;
        }

        _predictionService.LearnPhrase(phrase);
        _loggingService.Information($"Added phrase to word bank: {phrase}");
    }

    public void Dispose()
    {
        _inputService.Dispose();
        _overlayService.Dispose();
        _hotkeyService.Dispose();
        _trayIconService.Dispose();
        _loggingService.Information("Application shutdown");
    }
}
