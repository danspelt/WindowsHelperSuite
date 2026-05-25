namespace WindowsHelperSuite.Core.Models.Settings;

public enum WriterOverlayScreenPreference
{
    CurrentScreen,
    NextScreen
}

public class UiSettings
{
    public int FontSize { get; set; } = 20;
    public string FontFamily { get; set; } = "Segoe UI";
    public string FontWeight { get; set; } = "SemiBold";
    public bool HighContrast { get; set; } = false;
    /// <summary>Whole overlay window opacity (0–1). Lower values show more of the app behind the Writer overlay.</summary>
    public double Opacity { get; set; } = 0.42;
    public string DockPosition { get; set; } = "BottomCenter";
    public bool LargeTextMode { get; set; } = false;
    public OverlayLayout Layout { get; set; } = OverlayLayout.Vertical;

    /// <summary>Where to place the overlay relative to the caret.</summary>
    public WriterOverlayCaretPlacement OverlayCaretPlacement { get; set; } = WriterOverlayCaretPlacement.Above;

    /// <summary>Which screen to place the overlay on in multi-monitor setups.</summary>
    public WriterOverlayScreenPreference OverlayScreenPreference { get; set; } = WriterOverlayScreenPreference.CurrentScreen;

    /// <summary>Show/hide opacity transition length in ms; 0 disables.</summary>
    public int OverlayFadeTransitionMs { get; set; } = 110;

    public string AccentColor { get; set; } = "#4ADE80";
    public string OverlayBackgroundColor { get; set; } = "#0F0F14";
    public string CardColor { get; set; } = "#1E1F2A";
    public string TextColor { get; set; } = "#F0F0F5";
}
