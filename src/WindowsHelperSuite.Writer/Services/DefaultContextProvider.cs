using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Services;

public sealed class DefaultContextProvider : IContextProvider
{
    public Task<WriterContextSnapshot> GetContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new WriterContextSnapshot());
}
