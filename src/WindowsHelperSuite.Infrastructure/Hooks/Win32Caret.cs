using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Diagnostics;

namespace WindowsHelperSuite.Infrastructure.Hooks;

public static class Win32Caret
{
    [DllImport("user32.dll")]
    private static extern bool GetCaretPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, out GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Gets the current caret position in screen coordinates.
    /// Returns true if successful, false otherwise.
    /// </summary>
    public static bool GetCaretPosition(out int x, out int y)
    {
        x = 0;
        y = 0;

        try
        {
            // Try to get caret info from GUI thread
            var hwnd = GetForegroundWindow();
            var threadId = GetWindowThreadProcessId(hwnd, out _);

            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };

            if (GetGUIThreadInfo(threadId, out info))
            {
                // Use rcCaret for position
                x = info.rcCaret.Left;
                y = info.rcCaret.Bottom; // Bottom of caret (line below text)

                // Convert to screen coordinates
                if (info.hwndCaret != IntPtr.Zero)
                {
                    var pt = new POINT { X = x, Y = y };
                    if (ClientToScreen(info.hwndCaret, ref pt))
                    {
                        x = pt.X;
                        y = pt.Y;
                        if (x != 0 || y != 0)
                        {
                            return true;
                        }
                    }
                }
            }

            // Fallback: try GetCaretPos
            if (GetCaretPos(out var point))
            {
                x = point.X;
                y = point.Y;
                if (x != 0 || y != 0)
                {
                    return true;
                }
            }

            if (TryGetUiAutomationTextInputBounds(out var bounds))
            {
                x = (int)bounds.Left + 12;
                y = (int)Math.Min(bounds.Bottom, bounds.Top + 36);
                return true;
            }
        }
        catch
        {
            // Ignore errors from apps that don't expose caret
        }

        return false;
    }

    public static bool HasActiveTextInput()
    {
        return GetCaretPosition(out var x, out var y) && (x != 0 || y != 0);
    }

    public static string DescribeFocusedElement()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            var processName = "unknown";

            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out var processId);
                if (processId != 0)
                {
                    try
                    {
                        processName = Process.GetProcessById((int)processId).ProcessName;
                    }
                    catch
                    {
                        processName = $"pid:{processId}";
                    }
                }
            }

            var focusedElement = AutomationElement.FocusedElement;
            if (focusedElement == null)
            {
                return $"process={processName}, focusedElement=null";
            }

            var name = focusedElement.Current.Name;
            var automationId = focusedElement.Current.AutomationId;
            var className = focusedElement.Current.ClassName;
            var controlType = focusedElement.Current.ControlType?.ProgrammaticName ?? "unknown";
            var isKeyboardFocusable = focusedElement.Current.IsKeyboardFocusable;
            var isEditable = IsEditableTextInput(focusedElement);
            var bounds = focusedElement.Current.BoundingRectangle;

            return $"process={processName}, name='{name}', automationId='{automationId}', class='{className}', controlType='{controlType}', focusable={isKeyboardFocusable}, editable={isEditable}, bounds=({bounds.Left},{bounds.Top},{bounds.Width},{bounds.Height})";
        }
        catch (Exception ex)
        {
            return $"focused element unavailable: {ex.GetType().Name}";
        }
    }

    private static bool TryGetUiAutomationTextInputBounds(out System.Windows.Rect bounds)
    {
        bounds = System.Windows.Rect.Empty;

        try
        {
            var focusedElement = AutomationElement.FocusedElement;
            if (focusedElement == null)
            {
                return false;
            }

            if (!IsEditableTextInput(focusedElement))
            {
                return false;
            }

            var boundingRectangle = focusedElement.Current.BoundingRectangle;
            if (boundingRectangle.IsEmpty || boundingRectangle.Width <= 0 || boundingRectangle.Height <= 0)
            {
                return false;
            }

            bounds = boundingRectangle;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEditableTextInput(AutomationElement element)
    {
        try
        {
            var controlType = element.Current.ControlType;
            var isKeyboardFocusable = element.Current.IsKeyboardFocusable;

            var isEditLike = controlType == ControlType.Edit ||
                             controlType == ControlType.Document ||
                             controlType == ControlType.ComboBox;

            var supportsValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) &&
                                valuePatternObject is ValuePattern valuePattern &&
                                !valuePattern.Current.IsReadOnly;

            var supportsText = element.TryGetCurrentPattern(TextPattern.Pattern, out _);

            return isKeyboardFocusable && isEditLike && (supportsValue || supportsText || controlType == ControlType.Document);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads text from the focused editable control (UIA) for overlay context — matches what the user sees,
    /// unlike the keyboard-hook buffer which can drift after paste, autocorrect, or caret moves.
    /// </summary>
    public static bool TryGetTextForOverlayContext(out string text, int maxLength = 720)
    {
        text = string.Empty;
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el == null)
            {
                return false;
            }

            if (TryGetOverlayTextViaTextPattern(el, maxLength, out text))
            {
                return true;
            }

            if (TryGetOverlayTextViaValuePattern(el, maxLength, out text))
            {
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool TryGetOverlayTextViaTextPattern(AutomationElement el, int maxLength, out string text)
    {
        text = string.Empty;
        try
        {
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var tpObj) || tpObj is not TextPattern textPattern)
            {
                return false;
            }

            // Prefer the current line (caret row). TextUnit is not always in compile scope for net8+WPF; Line = 3.
            var selection = textPattern.GetSelection();
            if (selection != null && selection.Length > 0)
            {
                try
                {
                    var range = selection[0].Clone();
                    range.ExpandToEnclosingUnit((dynamic)(object)3);
                    var line = range.GetText(-1);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        text = TruncateOverlayTail(NormalizeOverlayWhitespace(line), maxLength);
                        return true;
                    }
                }
                catch
                {
                    // Line/sentence expansion not supported — fall through to full document text.
                }
            }

            var full = textPattern.DocumentRange.GetText(-1);
            if (!string.IsNullOrWhiteSpace(full))
            {
                text = TruncateOverlayTail(NormalizeOverlayWhitespace(full), maxLength);
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool TryGetOverlayTextViaValuePattern(AutomationElement el, int maxLength, out string text)
    {
        text = string.Empty;
        try
        {
            if (!el.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj) || vpObj is not ValuePattern valuePattern)
            {
                return false;
            }

            if (valuePattern.Current.IsReadOnly)
            {
                return false;
            }

            var v = valuePattern.Current.Value;
            if (string.IsNullOrWhiteSpace(v))
            {
                return false;
            }

            text = TruncateOverlayTail(NormalizeOverlayWhitespace(v), maxLength);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Keep overlay text close to what the control shows — do not collapse spaces or join words.
    /// </summary>
    private static string NormalizeOverlayWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
    }

    /// <summary>Show the end of long text (caret is usually near the end).</summary>
    private static string TruncateOverlayTail(string s, int maxLength)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxLength)
        {
            return s;
        }

        return "…" + s[^maxLength..].TrimStart();
    }
}
