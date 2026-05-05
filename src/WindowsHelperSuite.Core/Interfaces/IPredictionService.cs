using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Core.Interfaces;

public interface IPredictionService
{
    IReadOnlyList<SuggestionItem> GetSuggestions(string context, string currentWord, WriterContextSnapshot writerContext = default);
    bool WordBankContainsWord(string word);
    bool WordBankContainsPhrase(string phrase);
    void LearnWord(string word);
    void LearnPhrase(string phrase);
    void LearnBigram(string previousWord, string currentWord);
    void LearnBigramWithContext(string? wordBefore, string previousWord, string currentWord);
    void AcceptWord(string word);
    void AcceptPhrase(string phrase);
    void RemoveSuggestion(string text);

    /// <summary>
    /// Cleans up nonsensical words and phrases from the prediction service
    /// </summary>
    void CleanupNonsensicalEntries();

    /// <summary>
    /// Clears all words and phrases from the word bank
    /// </summary>
    void ClearAll();
}
