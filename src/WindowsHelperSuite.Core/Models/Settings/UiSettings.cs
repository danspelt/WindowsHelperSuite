namespace WindowsHelperSuite.Core.Models.Settings;

public class UiSettings
{
    public int FontSize { get; set; } = 14;
    public bool HighContrast { get; set; } = false;
    public double Opacity { get; set; } = 1.0;
    public string DockPosition { get; set; } = "BottomCenter";
    public bool LargeTextMode { get; set; } = false;
}
