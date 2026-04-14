using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Abstractions;

public interface IPredictionService
{
    Task<PredictionResult> PredictAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default);
}
