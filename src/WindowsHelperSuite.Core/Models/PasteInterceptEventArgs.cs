namespace WindowsHelperSuite.Core.Models;

/// <summary>
/// Ctrl+V while the overlay is visible — app can suppress native paste and inject transformed text instead.
/// </summary>
public sealed class PasteInterceptEventArgs : EventArgs
{
    /// <summary>Set true to block the default paste (the app injects text separately).</summary>
    public bool SuppressNativePaste { get; set; }
}
