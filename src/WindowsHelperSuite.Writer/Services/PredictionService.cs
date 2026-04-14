using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Models;
using WindowsHelperSuite.Writer.Ranking;

namespace WindowsHelperSuite.Writer.Services;

public sealed class PredictionService : global::WindowsHelperSuite.Writer.Abstractions.IPredictionService
{
    private readonly IReadOnlyList<IPredictionProvider> _providers;
    private readonly CandidateRanker _ranker;

    public PredictionService(IEnumerable<IPredictionProvider> providers, CandidateRanker ranker)
    {
        _providers = providers.ToList();
        _ranker = ranker;
    }

    public async Task<PredictionResult> PredictAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var activeProviders = _providers.Where(p =>
        {
            if (request.PreferLocalOnly &&
                p.Name.Equals("local-llm", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        });

        var tasks = activeProviders.Select(async provider =>
        {
            try
            {
                return await provider.GetCandidatesAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return Array.Empty<PredictionCandidate>();
            }
        });

        var candidateGroups = await Task.WhenAll(tasks).ConfigureAwait(false);

        var merged = candidateGroups
            .SelectMany(x => x)
            .GroupBy(x => x.Text.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var best = g.OrderByDescending(x => x.BaseScore).First();
                return new PredictionCandidate
                {
                    Text = best.Text,
                    Source = string.Join(",", g.Select(x => x.Source).Distinct()),
                    BaseScore = g.Max(x => x.BaseScore),
                    IsPhrase = g.Any(x => x.IsPhrase),
                    RequiresTrailingSpace = best.RequiresTrailingSpace
                };
            })
            .ToList();

        var ranked = _ranker.Rank(request, merged)
            .Take(Math.Clamp(request.MaxSuggestions, 1, 16))
            .ToList();

        var usedLlm = ranked.Any(x => x.Source.Contains("local-llm", StringComparison.OrdinalIgnoreCase));
        return new PredictionResult
        {
            Suggestions = ranked,
            UsedFallback = !usedLlm
        };
    }
}
