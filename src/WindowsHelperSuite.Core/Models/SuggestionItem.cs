namespace WindowsHelperSuite.Core.Models;

public class SuggestionItem
{
    public int Slot { get; set; }
    public string DisplayText { get; set; } = string.Empty;
    public string InsertText { get; set; } = string.Empty;
    public SuggestionKind Kind { get; set; }
    public double Score { get; set; }
}
