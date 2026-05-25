using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>Supplies likely next words after a completed word (static map + learned bigrams).</summary>
public interface INextWordLookup
{
    IReadOnlyList<NextWordCandidate> GetNextWordsAfter(string lastWord, string? wordBeforeLast = null);
}
