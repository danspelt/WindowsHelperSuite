namespace WindowsHelperSuite.Core.Models.Settings;

/// <summary>Persisted quick words and phrases for in-app menus (insert + speech).</summary>
public class QuickTextSettings
{
    public List<QuickWordItem> Words { get; set; } = [];

    public List<QuickPhraseItem> Phrases { get; set; } = [];
}
