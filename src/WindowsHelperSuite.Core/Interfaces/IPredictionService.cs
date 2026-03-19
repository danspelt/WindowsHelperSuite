using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Core.Interfaces;

public interface IPredictionService
{
    IReadOnlyList<SuggestionItem> GetSuggestions(string context, string currentWord);
    void LearnWord(string word);
    void LearnPhrase(string phrase);
}
