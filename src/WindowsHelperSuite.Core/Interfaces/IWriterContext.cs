using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>Foreground app / mode for context-aware suggestions (implementation fills snapshot).</summary>
public interface IWriterContext
{
    WriterContextSnapshot GetSnapshot();
}
