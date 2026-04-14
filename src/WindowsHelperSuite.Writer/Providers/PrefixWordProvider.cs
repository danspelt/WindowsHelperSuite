using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Providers;

public sealed class PrefixWordProvider : IPredictionProvider
{
    private readonly IReadOnlyDictionary<string, int> _wordFrequencies;

    public PrefixWordProvider(IReadOnlyDictionary<string, int> wordFrequencies)
    {
        _wordFrequencies = wordFrequencies;
    }

    public string Name => "prefix-word";

    public Task<IReadOnlyList<PredictionCandidate>> GetCandidatesAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = request.CurrentToken?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<IReadOnlyList<PredictionCandidate>>([]);
        }

        var matches = _wordFrequencies
            .Where(x => x.Key.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key.Length)
            .Take(20)
            .Select(x => new PredictionCandidate
            {
                Text = x.Key,
                Source = Name,
                BaseScore = Math.Log(1 + Math.Max(1, x.Value))
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<PredictionCandidate>>(matches);
    }
}
