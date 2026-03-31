using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>Personal typing profile persisted to disk — words, phrases, corrections, context weights.</summary>
public interface ITypingModel
{
    void RecordWord(string word, WriterContextSnapshot context);

    void RecordPhrase(string phrase, WriterContextSnapshot context);

    void RecordCorrection(string typed, string corrected);

    IReadOnlyList<TypingWordEntry> GetWords(string prefix);

    IReadOnlyList<TypingPhraseEntry> GetPhrases(string prefix);

    /// <summary>Corrections where <see cref="TypingCorrectionRecord.Typed"/> starts with <paramref name="typedPrefix"/>.</summary>
    IReadOnlyList<TypingCorrectionRecord> GetCorrectionMatches(string typedPrefix);

    /// <summary>Exact typo lookup (normalized lower).</summary>
    string? GetCorrection(string typed);

    /// <summary>Extra score from personal model (frequency, recency, context).</summary>
    double GetRankingBoost(string displayText, SuggestionKind kind, WriterContextSnapshot ctx);

    void Save();

    void Load();
}
