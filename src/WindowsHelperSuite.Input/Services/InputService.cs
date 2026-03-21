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
        _loggingService.Information($"Key pressed: {key} (0x{key:X}), IsEnabled={IsEnabled}");

        // Only intercept overlay keys when overlay is actually visible
        if (IsOverlayVisible)
        {
            // Handle selection keys 1-9 - suppress them so they don't type in document
            if (key >= 0x31 && key <= 0x39) // 1-9
            {
                _loggingService.Information($"Selection key detected: {key - 0x30}, raising event");
                SelectionKeyPressed?.Invoke(this, (int)(key - 0x30));
                e.Handled = true;
                return;
            }

            // Handle 0 (next page) and - (prev page)
            if (key == 0x30) // 0
            {
                NextPageKeyPressed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            if (key == 0xBD || key == 0x6D) // - key
            {
                PreviousPageKeyPressed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            // Handle grave key (`) for manual refresh
            if (key == 0xC0) // ` key
            {
                ManualRefreshRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            // Esc closes overlay and stops typing session
            if (key == 0x1B) // Escape
            {
                _currentWord.Clear();
                _typingInProgress = false;
                _hasValidTextInput = false;
                _inactivityTimer.Stop();
                TypingStopped?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
        }

        if (TryGetTypedCharacter(e, out var typedChar) && !e.Ctrl && !e.Alt)
        {
            // Check if this is first keystroke after idle (timer not running)
            var wasIdle = !_inactivityTimer.Enabled && !_typingInProgress;

            // Reset inactivity timer
            _inactivityTimer.Stop();
            _inactivityTimer.Start();

            // Trigger typing started on first keystroke (only if there's a text input)
            if (wasIdle)
            {
                // Check if there's an active text input (caret available)
                var hasCaret = Win32Caret.GetCaretPosition(out var caretX, out var caretY);
                var focusedElement = Win32Caret.DescribeFocusedElement();
                // Position (0,0) usually means no real caret - reject it
                var isValidCaret = hasCaret && (caretX != 0 || caretY != 0);
                _loggingService.Information($"Caret check: hasCaret={hasCaret}, pos=({caretX},{caretY}), valid={isValidCaret}, focused={focusedElement}");
                if (!isValidCaret)
                {
                    _loggingService.Information($"No text input detected, skipping overlay. focused={focusedElement}");
                    _hasValidTextInput = false;
                    InvalidTypingDetected?.Invoke(this, EventArgs.Empty);  // Hide overlay if visible
                    return; // No text input focused, don't show overlay
                }

                _loggingService.Information($"Text input detected, showing overlay. focused={focusedElement}");
                _typingInProgress = true;
                _hasValidTextInput = true;
                TypingStarted?.Invoke(this, EventArgs.Empty);
            }

            // Only process text if we have valid text input
            if (!_hasValidTextInput)
            {
                return; // Skip processing - no valid text input
            }

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
            if (_currentWord.Length > 0 && _hasValidTextInput)
            {
                _currentWord.Length--;
                TextCaptured?.Invoke(this, _currentWord.ToString());
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
            0x09 => '\t',
            0xBD => e.Shift ? '_' : '-',
            0xBE => e.Shift ? '>' : '.',
            0xBF => e.Shift ? '?' : '/',
            0xBA => e.Shift ? ':' : ';',
            0xDE => e.Shift ? '"' : '\'',
            0xBC => e.Shift ? '<' : ',',
            0x6E => '.',
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

