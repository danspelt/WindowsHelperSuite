using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Models;
using WindowsHelperSuite.Writer.Storage;

namespace WindowsHelperSuite.Writer.Providers;

public sealed class RecencyProvider : IPredictionProvider
{
    private readonly UserWordStatsStore _stats;
    private readonly ITypingModel? _typingModel;

    public RecencyProvider(UserWordStatsStore stats, ITypingModel? typingModel = null)
    {
        _stats = stats;
        _typingModel = typingModel;
    }

    public string Name => "recency";

    public Task<IReadOnlyList<PredictionCandidate>> GetCandidatesAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = request.CurrentToken?.Trim() ?? "";
        var prev = request.PreviousCompletedWord?.Trim() ?? "";
        var now = DateTime.UtcNow;
        var list = new List<PredictionCandidate>();

        if (string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(prev))
        {
            foreach (var row in _stats.Snapshot().OrderByDescending(r => r.LastUtc).Take(32))
            {
                if (string.IsNullOrWhiteSpace(row.Word) || row.Word.Contains(' ', StringComparison.Ordinal))
                {
                    continue;
                }

                var days = Math.Max(0, (now - row.LastUtc).TotalDays);
                var recency = days <= 1 ? 2.0 : days <= 7 ? 1.0 : 0.4;
                var score = 2.2 + recency + Math.Log(1 + Math.Max(1, row.AcceptCount));
                list.Add(new PredictionCandidate
                {
                    Text = row.Word,
                    Source = Name,
                    BaseScore = score,
                    IsPhrase = false
                });
            }

            if (_typingModel is not null)
            {
                foreach (var w in _typingModel.GetWords(prev).Take(8))
                {
                    if (string.IsNullOrWhiteSpace(w.Word) || w.Word.Contains(' ', StringComparison.Ordinal))
                    {
                        continue;
                    }

                    list.Add(new PredictionCandidate
                    {
                        Text = w.Word,
                        Source = Name,
                        BaseScore = 2.5 + Math.Log(1 + Math.Max(1, w.Count)),
                        IsPhrase = false
                    });
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(token))
        {
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
        }

        list = list
            .GroupBy(x => x.Text, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.BaseScore).First())
            .OrderByDescending(x => x.BaseScore)
            .Take(16)
            .ToList();

        return Task.FromResult<IReadOnlyList<PredictionCandidate>>(list);
    }
}
