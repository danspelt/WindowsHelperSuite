using System.Text;
using System.Timers;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Infrastructure.Hooks;
using WindowsHelperSuite.Infrastructure.Services;

namespace WindowsHelperSuite.Input.Services;

public class InputService : IInputService, IDisposable
{
    private readonly KeyboardHookService _keyboardHook;
    private readonly ILoggingService _loggingService;
    private readonly StringBuilder _currentWord = new();
    private readonly StringBuilder _currentSentence = new();
    private readonly System.Timers.Timer _inactivityTimer;
    private bool _typingInProgress;
    private bool _hasValidTextInput;

    public bool IsEnabled { get; set; } = true;
    public bool IsOverlayVisible { get; set; } = false;

    public event EventHandler<string>? TextCaptured;
    public event EventHandler<string>? WordTyped;
    public event EventHandler<string>? SentenceTyped;
    public event EventHandler<int>? SelectionKeyPressed;
    public event EventHandler? NextPageKeyPressed;
    public event EventHandler? PreviousPageKeyPressed;
    public event EventHandler? ManualRefreshRequested;
    public event EventHandler? TypingStarted;
    public event EventHandler? TypingStopped;
    public event EventHandler? OverlayDismissRequested;
    public event EventHandler? InvalidTypingDetected;  // New event for typing without valid text input

    public InputService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
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

    private void OnKeyPressed(object? sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

        var key = e.KeyCode;

        // Only intercept overlay keys when overlay is actually visible
        if (IsOverlayVisible)
        {
            // Handle selection keys 1-9 — suppress FIRST, then fire event
            if (key >= 0x31 && key <= 0x39) // 1-9
            {
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

            // Esc or Tab closes overlay and stops typing session
            if (key == 0x1B || key == 0x09) // Escape or Tab
            {
                _currentWord.Clear();
                _typingInProgress = false;
                _hasValidTextInput = false;
                _inactivityTimer.Stop();
                OverlayDismissRequested?.Invoke(this, EventArgs.Empty);
                TypingStopped?.Invoke(this, EventArgs.Empty);
                if (key == 0x1B) e.Handled = true; // Only suppress Escape, let Tab through
                return;
            }
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

            if (char.IsLetterOrDigit(typedChar) || typedChar == '\'' || typedChar == '-')
            {
                _currentWord.Append(typedChar);
                _currentSentence.Append(typedChar);
                var word = _currentWord.ToString();
                TextCaptured?.Invoke(this, word);
            }
            else if (char.IsWhiteSpace(typedChar))
            {
                CompleteCurrentWord();
                AppendSentenceSeparator(typedChar);
                TextCaptured?.Invoke(this, _currentWord.ToString());
            }
            else
            {
                CompleteCurrentWord();
                _currentSentence.Append(typedChar);
                if (IsSentenceTerminator(typedChar))
                {
                    CompleteCurrentSentence();
                }

                TextCaptured?.Invoke(this, _currentWord.ToString());
            }
        }
        else if (key == 0x08) // Backspace
        {
            if (_hasValidTextInput)
            {
                _inactivityTimer.Stop();
                _inactivityTimer.Start();

                if (_currentWord.Length > 0)
                {
                    _currentWord.Length--;
                }
                else if (_currentSentence.Length > 0)
                {
                    // Also trim sentence buffer so context stays in sync
                    _currentSentence.Length--;
                }
                TextCaptured?.Invoke(this, _currentWord.ToString());
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
                    if (_currentWord.Length > 0) _currentWord.Length--;
                    TextCaptured?.Invoke(this, _currentWord.ToString());
                }
            }
        }
        else if (key == 0x0D) // Enter
        {
            CompleteCurrentWord();
            CompleteCurrentSentence();
            _currentWord.Clear();
            _typingInProgress = false;
            _hasValidTextInput = false;
            _inactivityTimer.Stop();
            OverlayDismissRequested?.Invoke(this, EventArgs.Empty);
            TypingStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnInactivityTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        _typingInProgress = false;
        _hasValidTextInput = false;  // Reset on inactivity
        TypingStopped?.Invoke(this, EventArgs.Empty);
        _loggingService.Debug("Typing stopped after 10 seconds of inactivity");
    }

    public string GetCurrentWord() => _currentWord.ToString();

    public void ClearCurrentWord()
    {
        _currentWord.Clear();
    }

    public void ResetAfterInsertion()
    {
        _currentWord.Clear();
        // Keep sentence and session alive — user is still typing
        _inactivityTimer.Stop();
        _inactivityTimer.Start();
    }

    private void CompleteCurrentWord()
    {
        if (_currentWord.Length == 0)
        {
            return;
        }

        WordTyped?.Invoke(this, _currentWord.ToString());
        _currentWord.Clear();
    }

    private void CompleteCurrentSentence()
    {
        var sentence = _currentSentence.ToString().Trim();
        if (string.IsNullOrWhiteSpace(sentence))
        {
            _currentSentence.Clear();
            return;
        }

        SentenceTyped?.Invoke(this, sentence);
        _currentSentence.Clear();
    }

    private void AppendSentenceSeparator(char typedChar)
    {
        if (_currentSentence.Length == 0)
        {
            return;
        }

        if (typedChar == ' ')
        {
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

