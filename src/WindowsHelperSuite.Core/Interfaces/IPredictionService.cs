using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Core.Interfaces;

public interface IPredictionService
{
    IReadOnlyList<SuggestionItem> GetSuggestions(string context, string currentWord, WriterContextSnapshot writerContext = default);
    void LearnWord(string word);
    void LearnPhrase(string phrase);
    void LearnBigram(string previousWord, string currentWord);
    void AcceptWord(string word);
    void AcceptPhrase(string phrase);
}
