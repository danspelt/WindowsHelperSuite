namespace WindowsHelperSuite.Core.Models;

public enum OverlayDismissReason
{
    /// <summary>Esc — hide overlay and sleep writer until wake hotkey.</summary>
    Soft,

    /// <summary>Session ended (non-writer field, Enter paragraph end, etc.) — hide overlay, keep awake.</summary>
    SessionEnded,
}

public sealed class OverlayDismissEventArgs : EventArgs
{
    public OverlayDismissReason Reason { get; init; }
}
