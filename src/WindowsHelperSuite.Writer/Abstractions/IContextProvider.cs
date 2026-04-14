using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Abstractions;

/// <summary>Optional host hook to supply live foreground context for prompts and ranking.</summary>
public interface IContextProvider
{
    Task<WriterContextSnapshot> GetContextAsync(CancellationToken cancellationToken = default);
}
