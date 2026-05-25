using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Prediction.Services;

public sealed class WordBankNextWordLookup : INextWordLookup
{
    private readonly PredictionService _wordBank;

    public WordBankNextWordLookup(PredictionService wordBank)
    {
        _wordBank = wordBank;
    }

    public IReadOnlyList<NextWordCandidate> GetNextWordsAfter(string lastWord, string? wordBeforeLast = null) =>
        _wordBank.GetNextWordsAfter(lastWord, wordBeforeLast);
}
