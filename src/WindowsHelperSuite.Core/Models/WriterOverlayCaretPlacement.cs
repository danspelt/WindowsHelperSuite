namespace WindowsHelperSuite.Core.Models;

/// <summary>Vertical placement of the writer suggestion overlay relative to the text caret.</summary>
public enum WriterOverlayCaretPlacement
{
    /// <summary>Prefer above the caret, else below (same as legacy behavior).</summary>
    Auto = 0,

    /// <summary>Place the overlay above the caret when possible.</summary>
    Above = 1,

    /// <summary>Place the overlay below the caret when possible.</summary>
    Below = 2
}
