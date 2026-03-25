using System.Linq;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Infrastructure.Hooks;
using WindowsHelperSuite.Infrastructure.Services;
using WindowsHelperSuite.Hotkeys.Services;
using WindowsHelperSuite.Overlay.Services;
using WindowsHelperSuite.Input.Services;
using WindowsHelperSuite.Prediction.Services;
using WindowsHelperSuite.Speech.Services;

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
    private readonly ISpeechService _speechService;
    private readonly Queue<string> _recentWords = new();
    private readonly System.Timers.Timer _focusCheckTimer;
    private const int MaxContextWords = 6;
    private int _focusLostCount = 0; // Require multiple failed checks before hiding

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
        _speechService = new SpeechService();

        // Periodic focus check - hide overlay when no text field is focused
        _focusCheckTimer = new System.Timers.Timer(500);
        _focusCheckTimer.Elapsed += OnFocusCheckTimerElapsed;
        _focusCheckTimer.AutoReset = true;

        // Wire up suggestion selection to text injection
        _overlayService.SuggestionSelected += OnSuggestionSelected;

        WireInputToOverlay();
        RegisterHotkeyActions();
        RegisterDefaultHotkeys();

        _hotkeyService.Start();
        _inputService.Start();

        _loggingService.Information("Application started (v4 - enhanced injection + key suppression)");
    }

    public void Run()
    {
        _loggingService.Information("Application running");
    }

    private void WireInputToOverlay()
    {
        // Show overlay when typing starts
        _inputService.TypingStarted += (s, e) =>
        {
            try { ShowOverlay(); }
            catch (Exception ex) { _loggingService.Warning($"TypingStarted handler error: {ex.Message}"); }
        };

        // Update suggestions as text is captured
        _inputService.TextCaptured += (s, text) =>
        {
            try { UpdateSuggestions(text); }
            catch (Exception ex) { _loggingService.Warning($"TextCaptured handler error: {ex.Message}"); }
        };

        _inputService.WordTyped += (s, word) =>
        {
            try
            {
                OnWordTyped(word);

                // After every space, ensure the word bank opens with next-word suggestions
                _hasValidTextInput = true;
                _inputService.IsOverlayVisible = true;
                _focusCheckTimer.Start();
                UpdateSuggestions(string.Empty);
            }
            catch (Exception ex) { _loggingService.Warning($"WordTyped handler error: {ex.Message}"); }
        };
        _inputService.SentenceTyped += (s, sentence) =>
        {
            try { OnSentenceTyped(sentence); }
            catch (Exception ex) { _loggingService.Warning($"SentenceTyped handler error: {ex.Message}"); }
        };
        _inputService.OverlayDismissRequested += (s, e) =>
        {
            HideOverlay();
            _currentWord = string.Empty;
            _hasValidTextInput = false;
            _previousWord = string.Empty;
            _recentWords.Clear();
            _loggingService.Debug("Overlay hidden - explicit dismissal requested");
        };

        // Keep overlay visible when typing stops so the word bank remains available
        _inputService.TypingStopped += (s, e) =>
        {
            _currentWord = string.Empty;
            _loggingService.Debug("Typing stopped - keeping overlay visible");
        };

        // Hide overlay when typing on desktop (no valid text input)
        _inputService.InvalidTypingDetected += (s, e) =>
        {
            HideOverlay();
            _loggingService.Debug("Overlay hidden - invalid typing detected (desktop)");
        };

        // Handle selection keys 1-9
        // Resolve the suggestion SYNCHRONOUSLY so we capture the correct word,
        // then dispatch only the text injection async to avoid hook timeout.
        _inputService.SelectionKeyPressed += (s, slot) =>
        {
            var suggestion = _currentSuggestions.FirstOrDefault(x => x.Slot == slot);
            if (suggestion == null)
            {
                _loggingService.Debug($"Selection key {slot}: no suggestion found");
                return;
            }

            // Capture state NOW before anything changes
            var wordToInsert = suggestion.DisplayText;
            var charsToDelete = _currentWord?.Length ?? 0;

            // Immediately clear so a rapid second press won't double-insert from stale list
            _currentWord = string.Empty;
            _currentSuggestions = [];
            _inputService.ResetAfterInsertion();

            _loggingService.Debug($"Selection key {slot}: inserting \"{wordToInsert}\", deleting {charsToDelete} chars");

            // Visual feedback — flash the selected button green
            _overlayService.FlashSelection(slot);

            // Inject text on the UI thread (STA + message pump = clipboard works)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (charsToDelete > 0)
                    {
                        Win32TextInjection.SendBackspace(charsToDelete);
                        Thread.Sleep(30); // Let target app process deletions
                    }
                    Win32TextInjection.SendText(wordToInsert + " ");

                    // Update context BEFORE getting new suggestions
                    LearnAcceptedSuggestion(wordToInsert);
                    _loggingService.Information($"Inserted: {wordToInsert}");

                    // Speak the word if headset is connected
                    if (_speechService.IsPreferredDeviceConnected)
                    {
                        _speechService.Speak(wordToInsert);
                    }

                    // Refresh word bank with next-word suggestions so user can chain picks
                    _hasValidTextInput = true;
                    UpdateSuggestions(string.Empty);
                }
                catch (Exception ex)
                {
                    _loggingService.Warning($"Text injection failed for \"{wordToInsert}\": {ex.Message}");
                }
            });
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
        _focusCheckTimer.Start();
        _loggingService.Debug("Overlay shown - typing started");
    }

    private void HideOverlay()
    {
        _focusCheckTimer.Stop();
        _overlayService.HideSuggestions();
        _inputService.IsOverlayVisible = false;
    }

    private void OnFocusCheckTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            var hasCaret = Win32Caret.GetCaretPosition(out var caretX, out var caretY);
            var isValidCaret = hasCaret && (caretX != 0 || caretY != 0);
            if (!isValidCaret)
            {
                _focusLostCount++;
                // Require 3 consecutive failed checks (1.5s) before hiding
                // This prevents flickering when caret briefly becomes unavailable
                if (_focusLostCount >= 3)
                {
                    _loggingService.Debug("Focus check: no text field focused for 1.5s - hiding overlay");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => HideOverlay());
                    _hasValidTextInput = false;
                    _currentWord = string.Empty;
                    _focusLostCount = 0;
                }
            }
            else
            {
                _focusLostCount = 0;
            }
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Focus check error: {ex.Message}");
        }
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
        var context = GetContextText();
        var lastContextWord = context.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

        _currentSuggestions = _predictionService.GetSuggestions(context, text).ToList();

        // Set context mode indicator
        if (!string.IsNullOrWhiteSpace(text))
        {
            var modeText = !string.IsNullOrWhiteSpace(lastContextWord)
                ? $"completing \"{text}\" after \"{lastContextWord}\""
                : $"completing \"{text}\"";
            _overlayService.SetContextMode(modeText);
        }
        else if (!string.IsNullOrWhiteSpace(lastContextWord))
        {
            _overlayService.SetContextMode($"next word after \"{lastContextWord}\"");
        }
        else
        {
            _overlayService.SetContextMode("sentence start");
        }

        _overlayService.ShowSuggestions(_currentSuggestions);
    }

    private void OnSuggestionSelected(object? sender, int slot)
    {
        // Legacy path — kept for overlay click selection if ever added
        var suggestion = _currentSuggestions.FirstOrDefault(s => s.Slot == slot);
        if (suggestion != null)
        {
            var wordToInsert = suggestion.DisplayText;
            var charsToDelete = _currentWord?.Length ?? 0;

            _currentWord = string.Empty;
            _inputService.ClearCurrentWord();

            if (charsToDelete > 0)
            {
                Win32TextInjection.SendBackspace(charsToDelete);
            }
            Win32TextInjection.SendText(wordToInsert + " ");
            LearnAcceptedSuggestion(wordToInsert);
            _loggingService.Information($"Inserted: {wordToInsert}");
            UpdateSuggestions(string.Empty);
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

    private string _previousWord = string.Empty;

    private void OnWordTyped(string word)
    {
        LearnWordOrPhrase(word);

        // Learn bigram: previous word → current word
        if (!string.IsNullOrWhiteSpace(_previousWord) && !string.IsNullOrWhiteSpace(word))
        {
            _predictionService.LearnBigram(_previousWord, word);
        }
        _previousWord = word.Trim().ToLowerInvariant();

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
        _previousWord = string.Empty;
    }

    private void LearnAcceptedSuggestion(string acceptedText)
    {
        LearnWordOrPhrase(acceptedText);

        var parts = acceptedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            // Learn bigrams from accepted suggestion words too
            if (!string.IsNullOrWhiteSpace(_previousWord) && !string.IsNullOrWhiteSpace(part))
            {
                _predictionService.LearnBigram(_previousWord, part);
            }
            _previousWord = part.Trim().ToLowerInvariant();
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
        _focusCheckTimer.Stop();
        _focusCheckTimer.Dispose();
        _inputService.Dispose();
        _overlayService.Dispose();
        _hotkeyService.Dispose();
        _trayIconService.Dispose();
        if (_predictionService is IDisposable disposable)
        {
            disposable.Dispose();
        }
        if (_speechService is IDisposable speechDisposable)
        {
            speechDisposable.Dispose();
        }
        _loggingService.Information("Application shutdown");
    }
}
