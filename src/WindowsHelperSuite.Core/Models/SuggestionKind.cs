namespace WindowsHelperSuite.Core.Models;

public enum SuggestionKind
{
    WordCompletion,
    NextWord,
    PhraseCompletion,
    UserHistory,
    /// <summary>OpenAI (or compatible) completions merged into the writer overlay.</summary>
    AiSuggestion,
}
