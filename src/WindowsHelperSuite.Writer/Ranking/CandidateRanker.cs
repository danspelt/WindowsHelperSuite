using WindowsHelperSuite.Writer.Config;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Ranking;

public sealed class CandidateRanker
{
    private readonly WriterPredictionOptions _options;

    public CandidateRanker(WriterPredictionOptions? options = null)
    {
        _options = options ?? new WriterPredictionOptions();
    }

    public IReadOnlyList<PredictionCandidate> Rank(
        PredictionRequest request,
        IEnumerable<PredictionCandidate> candidates)
    {
        var token = request.CurrentToken?.Trim() ?? "";
        var prev = request.PreviousCompletedWord?.Trim() ?? "";
        var postSpace = string.IsNullOrWhiteSpace(token) && prev.Length > 0;
        foreach (var candidate in candidates)
        {
            double score = candidate.BaseScore * TrustForSource(candidate.Source);

            if (prev.Length >= 2 && candidate.IsPhrase &&
                candidate.Text.StartsWith(prev + " ", StringComparison.OrdinalIgnoreCase))
            {
                score += postSpace ? 0.2 : 0.35;
            }

            if (!string.IsNullOrWhiteSpace(token) &&
                candidate.Text.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 2.0 + Math.Min(1.0, token.Length / 6.0);
            }

            if (!string.IsNullOrWhiteSpace(token) &&
                candidate.Text.StartsWith(token, StringComparison.Ordinal) &&
                !candidate.Text.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                score -= 0.15;
            }

            if (postSpace && !candidate.IsPhrase && !candidate.Text.Contains(' ', StringComparison.Ordinal))
            {
                score += 2.5;
            }

            if (candidate.IsPhrase)
            {
                score += postSpace ? 0.15 : 0.4;
            }

            if (request.Context.TypingMode == WriterTypingMode.Chat && candidate.IsPhrase)
            {
                score += postSpace ? 0.15 : 0.3;
            }

            if (request.Context.TypingMode == WriterTypingMode.Email && candidate.IsPhrase)
            {
                score += postSpace ? 0.1 : 0.2;
            }

            if (request.Context.TypingMode == WriterTypingMode.Code && candidate.IsPhrase)
            {
                score -= 0.4;
            }

            if (candidate.Source.Contains("local-llm", StringComparison.OrdinalIgnoreCase))
            {
                score += 0.2;
            }

            candidate.FinalScore = score;
        }

        return candidates
            .OrderByDescending(x => x.FinalScore)
            .ThenBy(x => x.Text.Length)
            .ToList();
    }

    private double TrustForSource(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return 1.0;
        }

        var s = source;
        double t = 1.0;
        if (s.Contains("phrase-memory", StringComparison.OrdinalIgnoreCase))
        {
            t *= _options.PhraseMemoryTrust;
        }

        if (s.Contains("prefix-word", StringComparison.OrdinalIgnoreCase))
        {
            t *= _options.PrefixWordTrust;
        }

        if (s.Contains("recency", StringComparison.OrdinalIgnoreCase))
        {
            t *= _options.RecencyTrust;
        }

        if (s.Contains("local-llm", StringComparison.OrdinalIgnoreCase))
        {
            t *= _options.LocalLlmTrust;
        }

        if (s.Contains("correction", StringComparison.OrdinalIgnoreCase))
        {
            t *= _options.CorrectionTrust;
        }

        if (s.Contains("next-word", StringComparison.OrdinalIgnoreCase))
        {
            t *= _options.NextWordTrust;
        }

        return t;
    }
}
