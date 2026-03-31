using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.App.Services.Writer;

/// <summary>Test / no-persistence substitute for the persisted typing model service.</summary>
public sealed class NullTypingModel : ITypingModel
{
    public void RecordWord(string word, WriterContextSnapshot context)
    {
    }

    public void RecordPhrase(string phrase, WriterContextSnapshot context)
    {
    }

    public void RecordCorrection(string typed, string corrected)
    {
    }

    public IReadOnlyList<TypingWordEntry> GetWords(string prefix) => [];

    public IReadOnlyList<TypingPhraseEntry> GetPhrases(string prefix) => [];

    public IReadOnlyList<TypingCorrectionRecord> GetCorrectionMatches(string typedPrefix) => [];

    public string? GetCorrection(string typed) => null;

    public double GetRankingBoost(string displayText, SuggestionKind kind, WriterContextSnapshot ctx) => 0;

    public void Save()
    {
    }

    public void Load()
    {
    }
}
