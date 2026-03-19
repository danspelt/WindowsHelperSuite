namespace WindowsHelperSuite.Core.Models;

public class KeyBinding
{
    public string ActionName { get; set; } = string.Empty;
    public string Gesture { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
