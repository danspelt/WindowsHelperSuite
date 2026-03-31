namespace WindowsHelperSuite.Core.Models.Settings;

public class UiSettings
{
    public int FontSize { get; set; } = 14;
    public bool HighContrast { get; set; } = false;
    public double Opacity { get; set; } = 1.0;
    public string DockPosition { get; set; } = "BottomCenter";
    public bool LargeTextMode { get; set; } = false;
    public OverlayLayout Layout { get; set; } = OverlayLayout.Vertical;

    public string AccentColor { get; set; } = "#4ADE80";
    public string OverlayBackgroundColor { get; set; } = "#0F0F14";
    public string CardColor { get; set; } = "#1E1F2A";
    public string TextColor { get; set; } = "#F0F0F5";
}
