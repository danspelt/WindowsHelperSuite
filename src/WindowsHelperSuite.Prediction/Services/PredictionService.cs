using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;

namespace WindowsHelperSuite.Prediction.Services;

public class PredictionService : IPredictionService
{
    public IReadOnlyList<SuggestionItem> GetSuggestions(string context, string currentWord)
    {
        return new List<SuggestionItem>();
    }

    public void LearnWord(string word) { }
    public void LearnPhrase(string phrase) { }
}
