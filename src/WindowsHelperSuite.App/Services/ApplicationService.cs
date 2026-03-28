using System.Linq;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Core.Modes;
using WindowsHelperSuite.Core.Modules.Text;
using WindowsHelperSuite.Infrastructure.Audio;
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
    private readonly IModeManager _modeManager;
    private readonly Queue<string> _recentWords = new();
    private readonly System.Timers.Timer _focusCheckTimer;
    /// <summary>Cap for phrase/word-bank context list only; prediction uses the full sentence from InputService.</summary>
    private const int MaxPhraseContextWords = 4096;
    private int _focusLostCount = 0; // Require multiple failed checks before hiding
    private SpeakMode _speakMode = SpeakMode.Both;

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
        _speechService = new SpeechService(() => _settingsService.Settings.Speech, _loggingService);

        // Apply speech settings
        ApplySpeechSettings();

        // Periodic focus check - hide overlay when no text field is focused
        _focusCheckTimer = new System.Timers.Timer(500);
        _focusCheckTimer.Elapsed += OnFocusCheckTimerElapsed;
        _focusCheckTimer.AutoReset = true;

        _modeManager = new ModeManager(_settingsService, _loggingService, ApplyApplicationMode);
        _modeManager.Initialize();
        _trayIconService.ApplyModeIndicator(_modeManager.CurrentMode);
        _modeManager.ModeChanged += (_, mode) => _trayIconService.ApplyModeIndicator(mode);

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

        // Update suggestions as text is captured + notify speech of keystroke
        _inputService.TextCaptured += (s, text) =>
        {
            try
            {
                _speechService.NotifyKeystroke();
                UpdateSuggestions(text);
            }
            catch (Exception ex) { _loggingService.Warning($"TextCaptured handler error: {ex.Message}"); }
        };

        _inputService.WordTyped += (s, e) =>
        {
            try
            {
                OnWordTyped(e);

                // After every space, ensure the word bank opens with next-word suggestions
                _hasValidTextInput = true;
                _inputService.IsOverlayVisible = true;
                _focusCheckTimer.Start();
                UpdateSuggestions(string.Empty);
            }
            catch (Exception ex) { _loggingService.Warning($"WordTyped handler error: {ex.Message}"); }
        };

        _inputService.PasteIntercept += OnPasteIntercept;
        _inputService.SentenceTyped += (s, sentence) =>
        {
            try { OnSentenceTyped(sentence); }
            catch (Exception ex) { _loggingService.Warning($"SentenceTyped handler error: {ex.Message}"); }
        };
        _inputService.OverlayDismissRequested += (s, e) =>
        {
            // Esc → instant silence + hide
            _speechService.Stop();
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
            if (_modeManager.CurrentMode != AppMode.Writer)
            {
                _loggingService.Debug($"Selection key {slot}: ignored (not Writer mode)");
                return;
            }

            // Cooldown: ignore auto-repeat / rapid presses that would pick from refreshed list
            var now = Environment.TickCount64;
            if (now - _lastSelectionTick < 300)
            {
                _loggingService.Debug($"Selection key {slot}: ignored (cooldown)");
                return;
            }

            var suggestion = _currentSuggestions.FirstOrDefault(x => x.Slot == slot);
            if (suggestion == null)
            {
                _loggingService.Debug($"Selection key {slot}: no suggestion found");
                return;
            }

            _lastSelectionTick = now;

            var rawBeforePartial = _inputService.GetRawTextBeforeCurrentPartial();
            var capOpts = WriterCapitalizationOptions.From(_settingsService.Settings.Writer);
            var wordToInsert = CapitalizationService.FixInsertion(rawBeforePartial, suggestion.DisplayText, capOpts);
            var charsToDelete = _currentWord?.Length ?? 0;

            // Immediately clear so a rapid second press won't double-insert from stale list
            _currentWord = string.Empty;
            _currentSuggestions = [];
            _inputService.ResetAfterInsertion();

            _loggingService.Debug($"Selection key {slot}: suggestion='{suggestion.DisplayText}', wordToInsert='{wordToInsert}', charsToDelete={charsToDelete}");

            // Visual feedback — flash the selected button green
            _overlayService.FlashSelection(slot);

            // Inject text on the UI thread (STA + message pump = clipboard works)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                // Suppress hook processing so injected keystrokes don't double-update the sentence buffer
                _inputService.BeginInjection();
                try
                {
                    if (charsToDelete > 0)
                    {
                        Win32TextInjection.SendBackspace(charsToDelete);
                        Thread.Sleep(30); // Let target app process deletions
                    }
                    Win32TextInjection.SendText(wordToInsert + " ");
                    _inputService.ApplySuggestionInsertion(charsToDelete, wordToInsert + " ");

                    // Update context BEFORE getting new suggestions
                    LearnAcceptedSuggestion(wordToInsert);
                    _loggingService.Information($"Inserted: {wordToInsert}");

                    // Speak the word via queue (respects typing-speed gate)
                    if (_settingsService.Settings.Speech.EnableSpeechOnSelection &&
                        (_speakMode == SpeakMode.WordsOnly || _speakMode == SpeakMode.Both))
                    {
                        _speechService.SpeakQueued(wordToInsert);
                        _overlayService.ShowSpeakerIndicator(wordToInsert);
                    }

                    // Refresh word bank with next-word suggestions so user can chain picks
                    _hasValidTextInput = true;
                    UpdateSuggestions(string.Empty);
                }
                catch (Exception ex)
                {
                    _loggingService.Warning($"Text injection failed for \"{wordToInsert}\": {ex.Message}");
                }
                finally
                {
                    _inputService.EndInjection();
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
    private long _lastSelectionTick;

    private void ShowOverlay()
    {
        if (_modeManager.CurrentMode != AppMode.Writer)
        {
            return;
        }

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
        if (_modeManager.CurrentMode != AppMode.Writer)
        {
            return;
        }

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

        if (_modeManager.CurrentMode != AppMode.Writer)
        {
            return;
        }

        _currentWord = text;
        var context = _inputService.GetSuggestionContextPrefix();
        var lastContextWord = context.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        var fullSentence = _inputService.GetFullSentenceForOverlay();

        _currentSuggestions = _predictionService.GetSuggestions(context, text).ToList();

        string modeSummary;
        if (!string.IsNullOrWhiteSpace(text))
        {
            modeSummary = !string.IsNullOrWhiteSpace(lastContextWord)
                ? $"completing \"{text}\" after \"{lastContextWord}\""
                : $"completing \"{text}\"";
        }
        else if (!string.IsNullOrWhiteSpace(lastContextWord))
        {
            modeSummary = $"next word after \"{lastContextWord}\"";
        }
        else
        {
            modeSummary = "sentence start";
        }

        _overlayService.SetContextMode(
            modeSummary,
            string.IsNullOrWhiteSpace(fullSentence) ? null : fullSentence);

        _overlayService.ShowSuggestions(_currentSuggestions);
    }

    private void OnSuggestionSelected(object? sender, int slot)
    {
        // Legacy path — kept for overlay click selection if ever added
        var suggestion = _currentSuggestions.FirstOrDefault(s => s.Slot == slot);
        if (suggestion != null)
        {
            var rawBeforePartial = _inputService.GetRawTextBeforeCurrentPartial();
            var capOpts = WriterCapitalizationOptions.From(_settingsService.Settings.Writer);
            var wordToInsert = CapitalizationService.FixInsertion(rawBeforePartial, suggestion.DisplayText, capOpts);
            var charsToDelete = _currentWord?.Length ?? 0;

            _currentWord = string.Empty;
            _inputService.ClearCurrentWord();

            _inputService.BeginInjection();
            try
            {
                if (charsToDelete > 0)
                {
                    Win32TextInjection.SendBackspace(charsToDelete);
                }
                Win32TextInjection.SendText(wordToInsert + " ");
                _inputService.ApplySuggestionInsertion(charsToDelete, wordToInsert + " ");
                LearnAcceptedSuggestion(wordToInsert);
                _loggingService.Information($"Inserted: {wordToInsert}");
                UpdateSuggestions(string.Empty);
            }
            finally
            {
                _inputService.EndInjection();
            }
        }
    }

    private void RegisterHotkeyActions()
    {
        _hotkeyService.RegisterAction("OpenModeMenu", OpenModeMenu);

        _hotkeyService.RegisterAction("VolumeUp", () =>
        {
            if (_modeManager.CurrentMode != AppMode.Hotkey)
            {
                return;
            }

            Win32Audio.VolumeUp();
            _loggingService.Information("Volume increased");
        });

        _hotkeyService.RegisterAction("VolumeDown", () =>
        {
            if (_modeManager.CurrentMode != AppMode.Hotkey)
            {
                return;
            }

            Win32Audio.VolumeDown();
            _loggingService.Information("Volume decreased");
        });

        _hotkeyService.RegisterAction("VolumeMute", () =>
        {
            if (_modeManager.CurrentMode != AppMode.Hotkey)
            {
                return;
            }

            Win32Audio.VolumeMute();
            _loggingService.Information("Volume muted/unmuted");
        });

        _hotkeyService.RegisterAction("WriterRefresh", () =>
        {
            if (_modeManager.CurrentMode != AppMode.Writer)
            {
                return;
            }

            _loggingService.Information("Writer refresh requested");
        });

        _hotkeyService.RegisterAction("ToggleOverlay", () =>
        {
            if (_modeManager.CurrentMode != AppMode.Writer)
            {
                return;
            }

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
            if (_modeManager.CurrentMode != AppMode.Writer)
            {
                return;
            }

            _inputService.IsEnabled = !_inputService.IsEnabled;
            _loggingService.Information($"Writer {(_inputService.IsEnabled ? "enabled" : "paused")}");
        });

        _hotkeyService.RegisterAction("AddToWordBank", () =>
        {
            if (_modeManager.CurrentMode != AppMode.Writer)
            {
                return;
            }

            AddCurrentTypingToWordBank();
        });

        _hotkeyService.RegisterAction("AddPhraseToWordBank", () =>
        {
            if (_modeManager.CurrentMode != AppMode.Writer)
            {
                return;
            }

            AddCurrentPhraseToWordBank();
        });

        _hotkeyService.RegisterAction("FixClipboardCapitalization", () =>
        {
            if (_modeManager.CurrentMode != AppMode.Writer)
            {
                return;
            }

            FixClipboardSentenceCapitalization();
        });
    }

    private void RegisterDefaultHotkeys()
    {
        var settings = _settingsService.Settings.Hotkeys.Bindings;
        var menuGesture = string.IsNullOrWhiteSpace(_settingsService.Settings.ModeSystem.MenuHotkeyGesture)
            ? "Ctrl+F3"
            : _settingsService.Settings.ModeSystem.MenuHotkeyGesture.Trim();

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
            _hotkeyService.RegisterHotkey("FixClipboardCapitalization", "Ctrl+Shift+C");

            _loggingService.Information("Registered default hotkeys");
        }
        else
        {
            foreach (var binding in settings.Where(b => b.Enabled))
            {
                _hotkeyService.RegisterHotkey(binding.ActionName, binding.Gesture);
            }
        }

        _hotkeyService.RegisterHotkey("OpenModeMenu", menuGesture, consumeMatchingKeys: true);
        _loggingService.Information($"Mode menu hotkey registered: {menuGesture}");
    }

    private string _previousWord = string.Empty;

    private void OnWordTyped(WordTypedEventArgs e)
    {
        var opts = WriterCapitalizationOptions.From(_settingsService.Settings.Writer);
        var word = e.Word;
        if (opts.Enabled)
        {
            var fixedWord = CapitalizationService.FixCompletedTypedWord(e.TextBeforeWord, e.Word, opts);
            if (!string.Equals(fixedWord, e.Word, StringComparison.Ordinal))
            {
                _inputService.BeginInjection();
                try
                {
                    Win32TextInjection.SendBackspace(e.Word.Length + 1);
                    Win32TextInjection.SendText(fixedWord + " ");
                    _inputService.ReplaceLastCompletedWord(e.Word, fixedWord + " ");
                    word = fixedWord;
                }
                catch (Exception ex)
                {
                    _loggingService.Warning($"Sentence capitalization fix failed: {ex.Message}");
                }
                finally
                {
                    _inputService.EndInjection();
                }
            }
        }

        LearnWordOrPhrase(word);

        // Learn bigram: previous word → current word
        if (!string.IsNullOrWhiteSpace(_previousWord) && !string.IsNullOrWhiteSpace(word))
        {
            _predictionService.LearnBigram(_previousWord, word);
        }

        _previousWord = word.Trim().ToLowerInvariant();

        AppendContext(word);
    }

    private void OnPasteIntercept(object? sender, PasteInterceptEventArgs e)
    {
        if (_modeManager.CurrentMode != AppMode.Writer)
        {
            return;
        }

        var w = _settingsService.Settings.Writer;
        if (!w.AutoCapitalizeSentences)
        {
            return;
        }

        string? inject = null;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                var t = System.Windows.Clipboard.GetText() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(t))
                {
                    return;
                }

                inject = CapitalizationService.ApplySentenceCapitalization(t, WriterCapitalizationOptions.From(w));
            }
            catch (Exception ex)
            {
                _loggingService.Warning($"Paste clipboard read failed: {ex.Message}");
            }
        });

        if (string.IsNullOrEmpty(inject))
        {
            return;
        }

        e.SuppressNativePaste = true;
        _inputService.BeginInjection();
        try
        {
            Win32TextInjection.SendText(inject);
            _inputService.AppendPastedPlainText(inject);
            UpdateSuggestions(_inputService.GetCurrentWord());
        }
        catch (Exception ex)
        {
            _loggingService.Warning($"Paste inject failed: {ex.Message}");
        }
        finally
        {
            _inputService.EndInjection();
        }
    }

    private void FixClipboardSentenceCapitalization()
    {
        var w = _settingsService.Settings.Writer;
        if (!w.AutoCapitalizeSentences)
        {
            _loggingService.Debug("FixClipboardCapitalization: AutoCapitalizeSentences is off");
            return;
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            try
            {
                var t = System.Windows.Clipboard.GetText() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(t))
                {
                    return;
                }

                var n = CapitalizationService.ApplySentenceCapitalization(t, WriterCapitalizationOptions.From(w));
                if (!string.Equals(n, t, StringComparison.Ordinal))
                {
                    System.Windows.Clipboard.SetText(n);
                    _loggingService.Information("Clipboard text updated with sentence capitalization");
                }
            }
            catch (Exception ex)
            {
                _loggingService.Warning($"FixClipboardCapitalization failed: {ex.Message}");
            }
        });
    }

    private void OnSentenceTyped(string sentence)
    {
        var normalized = sentence.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        normalized = CapitalizationService.ApplySentenceCapitalization(
            normalized,
            WriterCapitalizationOptions.From(_settingsService.Settings.Writer));

        _predictionService.LearnPhrase(normalized);

        // Sentence readback when mode is SentencesOnly or Both
        if (_speakMode == SpeakMode.SentencesOnly || _speakMode == SpeakMode.Both)
        {
            _speechService.SpeakQueued(normalized);
        }

        _recentWords.Clear();
        _previousWord = string.Empty;
    }

    private void LearnAcceptedSuggestion(string acceptedText)
    {
        var normalized = acceptedText.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return;

        // Stronger learning for explicitly selected suggestions
        if (normalized.Contains(' '))
        {
            _predictionService.AcceptPhrase(normalized);
        }
        else
        {
            _predictionService.AcceptWord(normalized);
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    private void ApplySpeechSettings()
    {
        var settings = _settingsService.Settings.Speech;
        _speakMode = settings.SpeakMode;

        // Map SpeechRate (int, e.g. -2..+2) to double (0.6..1.8)
        var rate = 1.0 + (settings.SpeechRate * 0.2);
        _speechService.SetRate(rate);

        // Map SpeechVolume (0-100) to float (0.0-1.0)
        _speechService.SetVolume(settings.SpeechVolume / 100f);

        _loggingService.Information(
            $"Speech: voice={_speechService.VoiceName}, rate={rate:F1}, vol={settings.SpeechVolume}, speakMode={_speakMode}, voiceMode={settings.VoiceMode}, route={_speechService.VoiceRouteStatus}");
    }

    private void AppendContext(string word)
    {
        var normalized = word.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _recentWords.Enqueue(normalized);
        while (_recentWords.Count > MaxPhraseContextWords)
        {
            _recentWords.Dequeue();
        }
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
        var phrase = _inputService.GetFullSentenceForOverlay().Trim();
        if (string.IsNullOrWhiteSpace(phrase))
        {
            _loggingService.Information("AddPhraseToWordBank requested but there is no current phrase context");
            return;
        }

        _predictionService.LearnPhrase(phrase);
        _loggingService.Information($"Added phrase to word bank: {phrase}");
    }

    private void ApplyApplicationMode(AppMode mode)
    {
        if (mode == AppMode.Writer)
        {
            _inputService.IsEnabled = true;
            return;
        }

        _focusCheckTimer.Stop();
        _speechService.Stop();
        HideOverlay();
        _inputService.IsOverlayVisible = false;
        _hasValidTextInput = false;
        _currentWord = string.Empty;
        _currentSuggestions = [];
        _inputService.IsEnabled = false;
    }

    private void OpenModeMenu()
    {
        _loggingService.Information("Mode menu opened (global shortcut)");
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        // Let digits, Esc, etc. reach the WPF menu instead of overlay / writer handlers.
        _inputService.SuspendWriterKeyHandling = true;
        dispatcher.BeginInvoke(() =>
        {
            ModeMenuWindow? menu = null;
            try
            {
                menu = new ModeMenuWindow(_modeManager, _trayIconService.ShowSettings, _loggingService, ShowModeFeedback);
                _inputService.ModeMenuKeySink = menu;
                menu.ShowDialog();
            }
            catch (Exception ex)
            {
                _loggingService.Warning($"Mode menu error: {ex.Message}");
            }
            finally
            {
                _inputService.ModeMenuKeySink = null;
                _inputService.SuspendWriterKeyHandling = false;
            }
        }, System.Windows.Threading.DispatcherPriority.Send);
    }

    private void ShowModeFeedback(string headline)
    {
        var ms = _settingsService.Settings.ModeSystem;
        if (ms.ShowModeToast)
        {
            ModeToastWindow.ShowBrief(headline);
        }

        if (ms.SpeakModeChange)
        {
            _speechService.Speak(headline);
        }
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
