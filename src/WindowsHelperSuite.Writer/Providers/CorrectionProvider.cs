using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Providers;

public sealed class CorrectionProvider : IPredictionProvider
{
    private readonly ITypingModel _typingModel;

    public CorrectionProvider(ITypingModel typingModel)
    {
        _typingModel = typingModel;
    }

    public string Name => "correction";

    public Task<IReadOnlyList<PredictionCandidate>> GetCandidatesAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = request.CurrentToken?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<IReadOnlyList<PredictionCandidate>>([]);
        }

        var list = _typingModel
            .GetCorrectionMatches(token)
            .Select(c => new PredictionCandidate
            {
                Text = c.Corrected,
                Source = Name,
                BaseScore = 6.0 + Math.Log(1 + Math.Max(1, c.Count)),
                IsPhrase = c.Corrected.Contains(' ', StringComparison.Ordinal)
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<PredictionCandidate>>(list);
    }
}
