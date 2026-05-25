using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Providers;

public sealed class NextWordProvider : IPredictionProvider
{
    private readonly INextWordLookup _lookup;

    public NextWordProvider(INextWordLookup lookup)
    {
        _lookup = lookup;
    }

    public string Name => "next-word";

    public Task<IReadOnlyList<PredictionCandidate>> GetCandidatesAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = request.CurrentToken?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<IReadOnlyList<PredictionCandidate>>([]);
        }

        var prev = request.PreviousCompletedWord?.Trim() ?? "";
        if (prev.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<PredictionCandidate>>([]);
        }

        var wordBefore = WriterSentenceContext.WordBeforeLast(request.CurrentSentence ?? "");
        var list = _lookup.GetNextWordsAfter(prev, wordBefore)
            .Where(x => !string.IsNullOrWhiteSpace(x.Word) && !x.Word.Contains(' ', StringComparison.Ordinal))
            .Select(x => new PredictionCandidate
            {
                Text = x.Word.Trim(),
                Source = Name,
                BaseScore = Math.Max(3.0, x.Score),
                IsPhrase = false
            })
            .Take(16)
            .ToList();

        return Task.FromResult<IReadOnlyList<PredictionCandidate>>(list);
    }
}
