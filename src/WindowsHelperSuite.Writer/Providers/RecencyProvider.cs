using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Models;
using WindowsHelperSuite.Writer.Storage;

namespace WindowsHelperSuite.Writer.Providers;

public sealed class RecencyProvider : IPredictionProvider
{
    private readonly UserWordStatsStore _stats;

    public RecencyProvider(UserWordStatsStore stats)
    {
        _stats = stats;
    }

    public string Name => "recency";

    public Task<IReadOnlyList<PredictionCandidate>> GetCandidatesAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = request.CurrentToken?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<IReadOnlyList<PredictionCandidate>>([]);
        }

        var now = DateTime.UtcNow;
        var list = new List<PredictionCandidate>();

        foreach (var row in _stats.Snapshot())
        {
            if (string.IsNullOrWhiteSpace(row.Word) ||
                !row.Word.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var days = Math.Max(0, (now - row.LastUtc).TotalDays);
            var recency = days <= 1 ? 2.0 : days <= 7 ? 1.0 : 0.4;
            var score = 1.5 + recency + Math.Log(1 + Math.Max(1, row.AcceptCount));
            list.Add(new PredictionCandidate
            {
                Text = row.Word,
                Source = Name,
                BaseScore = score,
                IsPhrase = false
            });
        }

        list.Sort((a, b) => b.BaseScore.CompareTo(a.BaseScore));
        if (list.Count > 16)
        {
            list = list.Take(16).ToList();
        }

        return Task.FromResult<IReadOnlyList<PredictionCandidate>>(list);
    }
}
