using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Writer;
using WindowsHelperSuite.Infrastructure.Hooks;

namespace WindowsHelperSuite.App.Services.Writer;

/// <summary>Maps the foreground window to <see cref="WriterTypingMode"/> for suggestion ranking.</summary>
public sealed class ForegroundWriterContext : IWriterContext
{
    public WriterContextSnapshot GetSnapshot() => ForegroundContext.GetWriterSnapshot();
}
