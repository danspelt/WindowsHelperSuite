namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>
/// Detects text fields where Writer (overlay, predictions, buffers) should not run — e.g. browser address bars.
/// </summary>
public interface IWriterOverlayExclusionDetector
{
    /// <summary>
    /// True when the focused control is a URL/search chrome field, not body text.
    /// </summary>
    bool ShouldExcludeWriterOverlay(out string reason);
}
