using System.Text;
using System.Timers;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Writer;
using WindowsHelperSuite.Infrastructure.Hooks;
using WindowsHelperSuite.Infrastructure.Services;

namespace WindowsHelperSuite.Input.Services;

public class InputService : IInputService, IDisposable
{
    private readonly KeyboardHookService _keyboardHook;
    private readonly ILoggingService _loggingService;
    private readonly StringBuilder _currentWord = new();
    private readonly StringBuilder _currentSentence = new();
    private readonly object _bufferLock = new();
    private readonly System.Timers.Timer _inactivityTimer;
    private bool _typingInProgress;
    private bool _hasValidTextInput;
    private volatile bool _suppressHookProcessing;
    private readonly ISecretFieldDetector _secretFieldDetector;
    private readonly IWriterOverlayExclusionDetector _writerOverlayExclusionDetector;
    private bool _inProtectedField;

    public bool IsEnabled { get; set; } = true;

    /// <summary>True while the focused control is treated as password/secret — Writer must not learn or show overlay.</summary>
    public bool IsInProtectedField => _inProtectedField;

    /// <summary>Fires when entering or leaving protected field mode (password/PIN/token).</summary>
    public event EventHandler<bool>? SecretFieldProtectionChanged;

    public bool IsOverlayVisible { get; set; } = false;

    public event EventHandler<string>? TextCaptured;
    public event EventHandler<WordTypedEventArgs>? WordTyped;

    /// <summary>Overlay + Ctrl+V — app can suppress native paste and inject transformed text.</summary>
    public event EventHandler<PasteInterceptEventArgs>? PasteIntercept;
    public event EventHandler<string>? SentenceTyped;
    public event EventHandler<int>? SelectionKeyPressed;
    public event EventHandler? NextPageKeyPressed;
    public event EventHandler? PreviousPageKeyPressed;
    public event EventHandler? ManualRefreshRequested;
    /// <summary>Overlay visible: move suggestion highlight; delta -1 = Up, +1 = Down.</summary>
    public event EventHandler<int>? SuggestionHighlightMoved;

    /// <summary>
    /// Returns the slot (1–9) of the keyboard-highlighted overlay suggestion, or null. Set by the app (UI thread).
    /// </summary>
    public Func<int?>? TryGetHighlightedSuggestionSlot { get; set; }
    public event EventHandler? TypingStarted;
    public event EventHandler? TypingStopped;
    public event EventHandler? OverlayDismissRequested;
    public event EventHandler? InvalidTypingDetected; // New event for typing without valid text input

    public InputService(
        ILoggingService loggingService,
        ISecretFieldDetector secretFieldDetector,
        IWriterOverlayExclusionDetector writerOverlayExclusionDetector)
    {
        _loggingService = loggingService;
        _secretFieldDetector = secretFieldDetector;
        _writerOverlayExclusionDetector = writerOverlayExclusionDetector;
        _keyboardHook = new KeyboardHookService(loggingService);

        // 10 second inactivity timer
        _inactivityTimer = new System.Timers.Timer(10000);
        _inactivityTimer.Elapsed += OnInactivityTimerElapsed;
        _inactivityTimer.AutoReset = false;
    }

    public void Start()
    {
        _keyboardHook.KeyPressed += OnKeyPressed;
        _keyboardHook.StartHook();
        _loggingService.Information("Input service started");
    }

    public void Stop()
    {
        _keyboardHook.KeyPressed -= OnKeyPressed;
        _keyboardHook.StopHook();
        _loggingService.Information("Input service stopped");
    }

    /// <summary>
    /// All completed words in the current sentence (space-separated), excluding the partial word being typed.
    /// Used as prediction context so the word bank sees the full sentence, not just the last few words.
    /// </summary>
    public string GetSuggestionContextPrefix()
    {
        lock (_bufferLock)
        {
            var s = _currentSentence.ToString();
            var w = _currentWord.ToString();
            if (w.Length > 0 && s.Length >= w.Length && s.EndsWith(w, StringComparison.Ordinal))
            {
                return s[..^w.Length].TrimEnd();
            }

            return s.TrimEnd();
        }
    }

    /// <summary>
    /// Full sentence buffer including the current partial word — for overlay display.
    /// </summary>
    public string GetFullSentenceForOverlay()
    {
        lock (_bufferLock)
        {
            return _currentSentence.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Keeps the internal sentence buffer aligned with the focused control after a programmatic
    /// backspace + suggestion insert (the keyboard hook does not see injected keys).
    /// </summary>
    /// <param name="partialCharacterCount">Characters removed from the partial word before insert.</param>
    /// <param name="insertedText">Text that was injected (e.g. word + trailing space).</param>
    public void ApplySuggestionInsertion(int partialCharacterCount, string insertedText)
    {
        lock (_bufferLock)
        {
            if (partialCharacterCount > 0 && _currentSentence.Length >= partialCharacterCount)
            {
                _currentSentence.Length -= partialCharacterCount;
            }

            if (!string.IsNullOrEmpty(insertedText))
            {
                _currentSentence.Append(insertedText);
            }
        }
    }

    /// <summary>Text before the current partial word; not trimmed (so ". " is preserved).</summary>
    public string GetRawTextBeforeCurrentPartial()
    {
        lock (_bufferLock)
        {
            var s = _currentSentence.ToString();
            var w = _currentWord.ToString();
            if (w.Length > 0 && s.Length >= w.Length && s.EndsWith(w, StringComparison.Ordinal))
            {
                return s[..^w.Length];
            }

            return s;
        }
    }

    /// <summary>
    /// After correcting a completed word in the target app, align the internal sentence buffer.
    /// Expects the buffer to end with <paramref name="oldWord"/> plus a trailing space.
    /// </summary>
    public void ReplaceLastCompletedWord(string oldWord, string newWordWithTrailingSpace)
    {
        if (string.IsNullOrEmpty(oldWord))
        {
            return;
        }

        lock (_bufferLock)
        {
            var suffix = oldWord + " ";
            var s = _currentSentence.ToString();
            if (s.Length >= suffix.Length && s.EndsWith(suffix, StringComparison.Ordinal))
            {
                _currentSentence.Length -= suffix.Length;
                _currentSentence.Append(newWordWithTrailingSpace);
            }
        }
    }

    /// <summary>Merges programmatically injected pasted text into the sentence buffer (keyboard hook does not see paste).</summary>
    public void AppendPastedPlainText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_bufferLock)
        {
            foreach (var ch in text)
            {
                if (ch == '\r')
                {
                    continue;
                }

                if (ch is '\n' or '\t')
                {
                    if (_currentWord.Length > 0)
                    {
                        _currentWord.Clear();
                    }

                    AppendSentenceSeparatorLocked(' ');
                    continue;
                }

                if (WriterWordBufferPolicy.IsWordExtendingCharacter(ch))
                {
                    _currentWord.Append(ch);
                    _currentSentence.Append(ch);
                }
                else if (char.IsWhiteSpace(ch))
                {
                    if (_currentWord.Length > 0)
                    {
                        _currentWord.Clear();
                    }

                    AppendSentenceSeparatorLocked(' ');
                }
                else
                {
                    if (_currentWord.Length > 0)
                    {
                        _currentWord.Clear();
                    }

                    _currentSentence.Append(ch);
                }
            }
        }
    }

    private string GetTextBeforeCurrentWordLocked()
    {
        var s = _currentSentence.ToString();
        var w = _currentWord.ToString();
        if (w.Length > 0 && s.Length >= w.Length && s.EndsWith(w, StringComparison.Ordinal))
        {
            return s[..^w.Length];
        }

        return s;
    }

    /// <summary>
    /// Under <c>_bufferLock</c>: full word + text-before for <see cref="WordTyped"/> when a partial is committed.
    /// </summary>
    private void TryResolveWordCommitLocked(out string wordCompleted, out string textBeforeWord)
    {
        var w = _currentWord.ToString();
        var sTrim = _currentSentence.ToString().TrimEnd();
        var fallback = GetTextBeforeCurrentWordLocked();
        WriterWordBufferPolicy.TryResolveCompletedWordForCommit(sTrim, w, fallback, out wordCompleted, out textBeforeWord);
    }

    private void OnKeyPressed(object? sender, KeyEventArgs e)
    {
        if (!IsEnabled || _suppressHookProcessing)
        {
            return;
        }

        var secret = _secretFieldDetector.GetSnapshot();
        if (secret.IsProtected)
        {
            if (!_inProtectedField)
            {
                EnterProtectedField(secret);
                _inProtectedField = true;
            }

            return;
        }

        if (_inProtectedField)
        {
            ExitProtectedField();
            _inProtectedField = false;
        }

        if (_writerOverlayExclusionDetector.ShouldExcludeWriterOverlay(out var excludeReason))
        {
            EndWriterSessionForNonWriterField(excludeReason);
            return;
        }

        var key = e.KeyCode;

        // Only intercept overlay keys when overlay is actually visible
        if (IsOverlayVisible)
        {
            // Handle selection keys 1-9 — suppress FIRST, then fire event
            if (key >= 0x31 && key <= 0x39) // 1-9
            {
                _loggingService.Debug($"Selection key detected: {key - 0x30}, IsOverlayVisible={IsOverlayVisible}");
                e.Handled = true;
                SelectionKeyPressed?.Invoke(this, (int)(key - 0x30));
                return;
            }

            // Handle 0 (next page) and - (prev page)
            if (key == 0x30) // 0
            {
                e.Handled = true;
                NextPageKeyPressed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (key == 0xBD || key == 0x6D) // - key
            {
                e.Handled = true;
                PreviousPageKeyPressed?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Handle grave key (`) for manual refresh
            if (key == 0xC0) // ` key
            {
                e.Handled = true;
                ManualRefreshRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Left arrow: delete previous completed word before partial (Ctrl/Alt still go to host)
            if (key == 0x25 && !e.Ctrl && !e.Alt && TryDeletePreviousWordBeforePartial())
            {
                e.Handled = true;
                _inactivityTimer.Stop();
                _inactivityTimer.Start();
                return;
            }

            // Up/Down: move highlight in suggestion list (Ctrl/Alt still go to host)
            if (!e.Ctrl && !e.Alt && (key == 0x26 || key == 0x28)) // Up / Down
            {
                e.Handled = true;
                _inactivityTimer.Stop();
                _inactivityTimer.Start();
                SuggestionHighlightMoved?.Invoke(this, key == 0x26 ? -1 : 1);
                return;
            }

            // Esc or Tab closes overlay and stops typing session
            if (key == 0x1B || key == 0x09) // Escape or Tab
            {
                lock (_bufferLock)
                {
                    _currentWord.Clear();
                    _currentSentence.Clear();
                }

                _typingInProgress = false;
                _hasValidTextInput = false;
                _inactivityTimer.Stop();
                OverlayDismissRequested?.Invoke(this, EventArgs.Empty);
                TypingStopped?.Invoke(this, EventArgs.Empty);
                if (key == 0x1B)
                {
                    e.Handled = true; // Only suppress Escape, let Tab through
                }

                return;
            }
        }
        // Paste while overlay visible — app may replace with sentence-corrected plain text
        if (IsOverlayVisible && e.Ctrl && key == 0x56 && !e.Alt)
        {
            var pasteArgs = new PasteInterceptEventArgs();
            PasteIntercept?.Invoke(this, pasteArgs);
            if (pasteArgs.SuppressNativePaste)
            {
                e.Handled = true;
                return;
            }

            // Let native paste run when the app did not replace clipboard / inject text
        }

        if (TryGetTypedCharacter(e, out var typedChar) && !e.Ctrl && !e.Alt)
        {
            // Re-validate whenever we don't have a confirmed typing session
            if (!_typingInProgress || !_hasValidTextInput)
            {
                var hasCaret = Win32Caret.GetCaretPosition(out var caretX, out var caretY);
                var focusedElement = Win32Caret.DescribeFocusedElement();
                var isValidCaret = hasCaret && (caretX != 0 || caretY != 0);
                _loggingService.Debug($"Caret check: hasCaret={hasCaret}, pos=({caretX},{caretY}), valid={isValidCaret}, focused={focusedElement}");
                if (!isValidCaret)
                {
                    _hasValidTextInput = false;
                    InvalidTypingDetected?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (!WriterWordBufferPolicy.CanStartWriterSessionFromKeystroke(typedChar))
                {
                    return;
                }

                // Valid text input confirmed — start session
                if (!_typingInProgress)
                {
                    _loggingService.Information($"Text input detected, showing overlay. focused={focusedElement}");
                    _typingInProgress = true;
                    _hasValidTextInput = true;
                    TypingStarted?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    _hasValidTextInput = true;
                }
            }

            // Reset inactivity timer only after we have a valid session
            _inactivityTimer.Stop();
            _inactivityTimer.Start();

            if (WriterWordBufferPolicy.IsWordExtendingCharacter(typedChar))
            {
                lock (_bufferLock)
                {
                    _currentWord.Append(typedChar);
                    _currentSentence.Append(typedChar);
                }

                TextCaptured?.Invoke(this, GetCurrentWord());
            }
            else if (char.IsWhiteSpace(typedChar))
            {
                string? wordCompleted = null;
                string textBeforeWord = string.Empty;
                lock (_bufferLock)
                {
                    if (_currentWord.Length > 0)
                    {
                        TryResolveWordCommitLocked(out var wc, out textBeforeWord);
                        wordCompleted = wc;
                        _currentWord.Clear();
                    }

                    AppendSentenceSeparatorLocked(typedChar);
                }

                if (wordCompleted != null)
                {
                    WordTyped?.Invoke(this, new WordTypedEventArgs
                    {
                        Word = wordCompleted,
                        TextBeforeWord = textBeforeWord
                    });
                }

                TextCaptured?.Invoke(this, GetCurrentWord());
            }
            else
            {
                string? wordCompleted = null;
                string? sentenceCompleted = null;
                string textBeforeWord = string.Empty;
                lock (_bufferLock)
                {
                    if (_currentWord.Length > 0)
                    {
                        TryResolveWordCommitLocked(out var wc, out textBeforeWord);
                        wordCompleted = wc;
                        _currentWord.Clear();
                    }

                    // Build sentence from accumulated words
                    if (_currentSentence.Length > 0)
                    {
                        var sentence = _currentSentence.ToString().Trim();
                        
                        // Add the completed word if we have one
                        if (!string.IsNullOrWhiteSpace(wordCompleted))
                        {
                            // Ensure proper spacing
                            if (sentence.EndsWith(" "))
                            {
                                sentence += wordCompleted;
                            }
                            else
                            {
                                sentence += " " + wordCompleted;
                            }
                        }

                        // Only fire sentence completion if we have meaningful content
                        if (!string.IsNullOrWhiteSpace(sentence.Trim()))
                        {
                            sentenceCompleted = sentence.Trim();
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(wordCompleted))
                    {
                        // Single word without sentence context - treat as sentence for learning
                        sentenceCompleted = wordCompleted;
                    }

                    // Clear buffers for next sentence
                    _currentSentence.Clear();
                    _currentWord.Clear();
                }

                if (wordCompleted != null)
                {
                    WordTyped?.Invoke(this, new WordTypedEventArgs
                    {
                        Word = wordCompleted,
                        TextBeforeWord = textBeforeWord
                    });
                }

                if (sentenceCompleted != null)
                {
                    SentenceTyped?.Invoke(this, sentenceCompleted);
                }

                TextCaptured?.Invoke(this, GetCurrentWord());
            }
        }
        else if (key == 0x08) // Backspace
        {
            if (_hasValidTextInput)
            {
                _inactivityTimer.Stop();
                _inactivityTimer.Start();

                lock (_bufferLock)
                {
                    if (_currentWord.Length > 0)
                    {
                        var s = _currentSentence.ToString();
                        var w = _currentWord.ToString();
                        if (WriterWordBufferPolicy.IsSentenceSuffixAlignedWithCurrentWord(s, w))
                        {
                            _currentWord.Length--;
                            if (_currentSentence.Length > 0)
                            {
                                _currentSentence.Length--;
                            }
                        }
                        else
                        {
                            _currentWord.Length--;
                        }
                    }
                    else if (_currentSentence.Length > 0)
                    {
                        // Also trim sentence buffer so context stays in sync
                        _currentSentence.Length--;
                    }
                }

                TextCaptured?.Invoke(this, GetCurrentWord());
            }
            else if (_typingInProgress)
            {
                // Session active but text input not validated yet — re-validate
                var hasCaret = Win32Caret.GetCaretPosition(out var cx, out var cy);
                if (hasCaret && (cx != 0 || cy != 0))
                {
                    _hasValidTextInput = true;
                    _inactivityTimer.Stop();
                    _inactivityTimer.Start();
                    lock (_bufferLock)
                    {
                        if (_currentWord.Length > 0)
                        {
                            var s = _currentSentence.ToString();
                            var w = _currentWord.ToString();
                            if (WriterWordBufferPolicy.IsSentenceSuffixAlignedWithCurrentWord(s, w))
                            {
                                _currentWord.Length--;
                                if (_currentSentence.Length > 0)
                                {
                                    _currentSentence.Length--;
                                }
                            }
                            else
                            {
                                _currentWord.Length--;
                            }
                        }
                    }

                    TextCaptured?.Invoke(this, GetCurrentWord());
                }
            }
        }
        else if (key == 0x0D) // Enter
        {
            // Plain Enter: accept keyboard-highlighted suggestion if any (Shift/Ctrl/Alt = send Enter to host, e.g. newline)
            if (IsOverlayVisible && !e.Shift && !e.Ctrl && !e.Alt)
            {
                var slot = TryGetHighlightedSuggestionSlot?.Invoke();
                if (slot is >= 1 and <= 9)
                {
                    e.Handled = true;
                    _inactivityTimer.Stop();
                    _inactivityTimer.Start();
                    SelectionKeyPressed?.Invoke(this, slot.Value);
                    return;
                }
            }

            // Shift/Ctrl/Alt+Enter: pass through to host (e.g. newline) while overlay is up
            if (IsOverlayVisible && (e.Shift || e.Ctrl || e.Alt))
            {
                return;
            }

            string? wordCompleted = null;
            string? sentenceCompleted = null;
            string textBeforeWord = string.Empty;
            lock (_bufferLock)
            {
                if (_currentWord.Length > 0)
                {
                    TryResolveWordCommitLocked(out var wc, out textBeforeWord);
                    wordCompleted = wc;
                    _currentWord.Clear();
                }

                // Build sentence from accumulated words with proper spacing
                if (_currentSentence.Length > 0)
                {
                    var sentence = _currentSentence.ToString().Trim();
                    
                    // Add the completed word if we have one
                    if (!string.IsNullOrWhiteSpace(wordCompleted))
                    {
                        // Ensure proper spacing
                        if (sentence.EndsWith(" "))
                        {
                            sentence += wordCompleted;
                        }
                        else
                        {
                            sentence += " " + wordCompleted;
                        }
                    }

                    // Only fire sentence completion if we have meaningful content
                    if (!string.IsNullOrWhiteSpace(sentence.Trim()))
                    {
                        sentenceCompleted = sentence.Trim();
                    }
                }
                else if (!string.IsNullOrWhiteSpace(wordCompleted))
                {
                    // Single word without sentence context - treat as sentence for learning
                    sentenceCompleted = wordCompleted;
                }

                _currentSentence.Clear();
                _currentWord.Clear();
            }

            if (wordCompleted != null)
            {
                WordTyped?.Invoke(this, new WordTypedEventArgs
                {
                    Word = wordCompleted,
                    TextBeforeWord = textBeforeWord
                });
            }

            if (sentenceCompleted != null)
            {
                SentenceTyped?.Invoke(this, sentenceCompleted);
            }

            _typingInProgress = false;
            _hasValidTextInput = false;
            _inactivityTimer.Stop();
            OverlayDismissRequested?.Invoke(this, EventArgs.Empty);
            TypingStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EnterProtectedField(SecretFieldSnapshot secret)
    {
        lock (_bufferLock)
        {
            _currentWord.Clear();
            _currentSentence.Clear();
        }

        _typingInProgress = false;
        _hasValidTextInput = false;
        _inactivityTimer.Stop();
        SecretFieldProtectionChanged?.Invoke(this, true);
        _loggingService.Debug($"Protected field active: Reason={secret.Reason} Process={secret.ProcessName} Control={secret.ControlType}");
    }

    private void ExitProtectedField()
    {
        lock (_bufferLock)
        {
            _currentWord.Clear();
            _currentSentence.Clear();
        }

        _typingInProgress = false;
        _hasValidTextInput = false;
        _inactivityTimer.Stop();
        SecretFieldProtectionChanged?.Invoke(this, false);
        _loggingService.Debug("Protected field: exited — Writer buffers cleared");
    }

    /// <summary>
    /// URL bars and similar: not secret, but Writer overlay and buffers must not attach.
    /// </summary>
    private void EndWriterSessionForNonWriterField(string reason)
    {
        var hadSession = _typingInProgress || _hasValidTextInput || IsOverlayVisible;
        if (!hadSession)
        {
            return;
        }

        lock (_bufferLock)
        {
            _currentWord.Clear();
            _currentSentence.Clear();
        }

        _typingInProgress = false;
        _hasValidTextInput = false;
        _inactivityTimer.Stop();
        OverlayDismissRequested?.Invoke(this, EventArgs.Empty);
        _loggingService.Debug($"Writer session ended — non-writer text field ({reason})");
    }

    private void OnInactivityTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        _typingInProgress = false;
        _hasValidTextInput = false; // Reset on inactivity
        TypingStopped?.Invoke(this, EventArgs.Empty);
        _loggingService.Debug("Typing stopped after 10 seconds of inactivity");
    }

    public string GetCurrentWord()
    {
        lock (_bufferLock)
        {
            return _currentWord.ToString();
        }
    }

    public void ClearCurrentWord()
    {
        lock (_bufferLock)
        {
            _currentWord.Clear();
        }
    }

    public void ResetAfterInsertion()
    {
        lock (_bufferLock)
        {
            _currentWord.Clear();
        }

        // Keep sentence and session alive — user is still typing
        _inactivityTimer.Stop();
        _inactivityTimer.Start();
    }

    /// <summary>
    /// Suppress hook buffer processing while programmatic keystrokes (backspace + paste) are in flight.
    /// Prevents the hook from double-counting characters that ApplySuggestionInsertion will handle.
    /// </summary>
    public void BeginInjection() => _suppressHookProcessing = true;
    public void EndInjection() => _suppressHookProcessing = false;

    /// <summary>
    /// Deletes the last whitespace-delimited token before the partial word in the focused field and realigns buffers.
    /// </summary>
    /// <returns>True when a deletion was applied.</returns>
    private bool TryDeletePreviousWordBeforePartial()
    {
        string before;
        string partialWord;
        lock (_bufferLock)
        {
            before = GetTextBeforeCurrentWordLocked();
            partialWord = _currentWord.ToString();
        }

        if (!WriterWordBufferPolicy.TryGetDeletePreviousWordStart(before, out var startIndex))
        {
            return false;
        }

        var deleteCount = before.Length - startIndex;
        BeginInjection();
        try
        {
            Win32TextInjection.SendBackspace(deleteCount);
            lock (_bufferLock)
            {
                _currentSentence.Clear();
                if (startIndex > 0)
                {
                    _currentSentence.Append(before.AsSpan(0, startIndex));
                }

                _currentSentence.Append(partialWord);
            }
        }
        finally
        {
            EndInjection();
        }

        TextCaptured?.Invoke(this, GetCurrentWord());
        _loggingService.Debug($"Delete previous word: removed {deleteCount} chars before partial (startIndex={startIndex})");
        return true;
    }

    private void AppendSentenceSeparatorLocked(char typedChar)
    {
        if (_currentSentence.Length == 0)
        {
            return;
        }

        // Treat any whitespace (tab, NBSP, etc.) as a word boundary — same as a typed space — so overlay/speech
        // never show words glued together.
        if (char.IsWhiteSpace(typedChar))
        {
            if (typedChar == '\r')
            {
                return;
            }

            if (_currentSentence[^1] != ' ')
            {
                _currentSentence.Append(' ');
            }

            return;
        }

        _currentSentence.Append(typedChar);
    }

    private static bool IsSentenceTerminator(char typedChar)
    {
        return typedChar == '.' || typedChar == '!' || typedChar == '?';
    }

    private static bool TryGetTypedCharacter(KeyEventArgs e, out char typedChar)
    {
        typedChar = '\0';
        var key = e.KeyCode;

        if (key >= 0x41 && key <= 0x5A)
        {
            typedChar = (char)key;
            if (!e.Shift && !e.CapsLock)
            {
                typedChar = char.ToLowerInvariant(typedChar);
            }

            return true;
        }

        if (key >= 0x30 && key <= 0x39)
        {
            typedChar = e.Shift
                ? key switch
                {
                    0x31 => '!',
                    0x32 => '@',
                    0x33 => '#',
                    0x34 => '$',
                    0x35 => '%',
                    0x36 => '^',
                    0x37 => '&',
                    0x38 => '*',
                    0x39 => '(',
                    0x30 => ')',
                    _ => '\0'
                }
                : (char)key;
            return typedChar != '\0';
        }

        if (key >= 0x60 && key <= 0x69)
        {
            typedChar = (char)(key - 0x60 + 0x30);
            return true;
        }

        typedChar = key switch
        {
            0x20 => ' ',
            0xBD => e.Shift ? '_' : '-',
            0xBB => e.Shift ? '+' : '=',
            0xBE => e.Shift ? '>' : '.',
            0xBF => e.Shift ? '?' : '/',
            0xBA => e.Shift ? ':' : ';',
            0xDE => e.Shift ? '"' : '\'',
            0xBC => e.Shift ? '<' : ',',
            0xDB => e.Shift ? '{' : '[',
            0xDD => e.Shift ? '}' : ']',
            0xDC => e.Shift ? '|' : '\\',
            0xC0 => e.Shift ? '~' : '`',
            0x6E => '.',
            0x6A => '*',
            0x6B => '+',
            0x6D => '-',
            0x6F => '/',
            _ => '\0'
        };

        return typedChar != '\0';
    }

    public void Dispose()
    {
        Stop();
        _inactivityTimer.Dispose();
        _keyboardHook.Dispose();
    }
}
