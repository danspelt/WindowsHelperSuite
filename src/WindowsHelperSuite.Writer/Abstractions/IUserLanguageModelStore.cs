using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Abstractions;

public interface IUserLanguageModelStore
{
    Task RecordAcceptedSuggestionAsync(
        string text,
        WriterContextSnapshot context,
        CancellationToken cancellationToken = default);

    Task RecordCommittedTextAsync(
        string text,
        WriterContextSnapshot context,
        CancellationToken cancellationToken = default);
}
