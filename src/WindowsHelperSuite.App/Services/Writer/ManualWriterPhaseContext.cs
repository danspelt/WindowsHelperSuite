using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.App.Services.Writer;

/// <summary>
/// Wraps another <see cref="IWriterContext"/> and optionally overrides <see cref="WriterTypingMode"/>
/// (ranking / suggestion behavior) until cleared.
/// </summary>
public sealed class ManualWriterPhaseContext : IWriterContext
{
    private readonly IWriterContext _inner;
    private readonly object _sync = new();
    private WriterTypingMode? _override;

    public ManualWriterPhaseContext(IWriterContext inner)
    {
        _inner = inner;
    }

    public WriterTypingMode? PhaseOverride
    {
        get
        {
            lock (_sync)
            {
                return _override;
            }
        }
        set
        {
            lock (_sync)
            {
                _override = value;
            }
        }
    }

    public WriterContextSnapshot GetSnapshot()
    {
        var snap = _inner.GetSnapshot();
        lock (_sync)
        {
            if (_override.HasValue)
            {
                return snap with { Mode = _override.Value };
            }
        }

        return snap;
    }
}
