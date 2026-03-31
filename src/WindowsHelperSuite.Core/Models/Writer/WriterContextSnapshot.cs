namespace WindowsHelperSuite.Core.Models.Writer;

/// <summary>Immutable snapshot from <see cref="IWriterContext"/> for learning and suggestion ranking.</summary>
public readonly record struct WriterContextSnapshot(
    WriterTypingMode Mode,
    string? ForegroundProcessName,
    string? ForegroundWindowTitle)
{
    public static WriterContextSnapshot Neutral { get; } = new(WriterTypingMode.Neutral, null, null);
}
