namespace WindowsHelperSuite.Core.Models.Writer;

/// <summary>High-level typing context for ranking and future app-specific behavior (chat vs code).</summary>
public enum WriterTypingMode
{
    Neutral = 0,
    Chat = 1,
    Development = 2,
    Email = 3
}
