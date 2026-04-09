namespace WindowsHelperSuite.Core.Models.Settings;

/// <summary>User-managed quick phrase for insert/speech menus.</summary>
public class QuickPhraseItem
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";

    /// <summary>Override for speech; empty means use <see cref="Text"/>.</summary>
    public string? SpeakText { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsFavorite { get; set; }

    public int SortOrder { get; set; }
}
