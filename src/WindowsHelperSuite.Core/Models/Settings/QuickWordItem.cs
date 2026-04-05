namespace WindowsHelperSuite.Core.Models.Settings;

/// <summary>User-managed quick word for insert/speech menus.</summary>
public class QuickWordItem
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";

    /// <summary>SSML/plain override for speech; empty means use <see cref="Text"/>.</summary>
    public string? SpeakText { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsFavorite { get; set; }

    public int SortOrder { get; set; }
}
