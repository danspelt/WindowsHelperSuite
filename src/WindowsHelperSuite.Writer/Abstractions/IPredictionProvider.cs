using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Abstractions;

public interface IPredictionProvider
{
    string Name { get; }

    Task<IReadOnlyList<PredictionCandidate>> GetCandidatesAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default);
}
