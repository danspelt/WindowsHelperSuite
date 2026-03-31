using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.App.Services.Writer;

/// <summary>Placeholder until foreground process / window class detection is implemented.</summary>
public sealed class DefaultWriterContext : IWriterContext
{
    public WriterContextSnapshot GetSnapshot() => WriterContextSnapshot.Neutral;
}
