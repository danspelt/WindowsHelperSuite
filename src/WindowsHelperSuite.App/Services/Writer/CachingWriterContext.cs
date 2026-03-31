using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Writer;
using WindowsHelperSuite.Infrastructure.Hooks;

namespace WindowsHelperSuite.App.Services.Writer;

/// <summary>Caches writer context while foreground + focus HWND are unchanged.</summary>
public sealed class CachingWriterContext : IWriterContext
{
    private const int TtlMs = 200;

    private readonly IWriterContext _inner;
    private readonly object _sync = new();
    private WriterContextSnapshot _cached;
    private bool _hasCache;
    private nint _fg;
    private nint _focus;
    private long _tick;

    public CachingWriterContext(IWriterContext inner)
    {
        _inner = inner;
    }

    public WriterContextSnapshot GetSnapshot()
    {
        if (!FocusIdentity.TryGet(out var fg, out var focus))
        {
            return _inner.GetSnapshot();
        }

        var now = Environment.TickCount64;
        lock (_sync)
        {
            var age = unchecked((int)(now - _tick));
            if (_hasCache && _fg == fg && _focus == focus && age >= 0 && age < TtlMs)
            {
                return _cached;
            }
        }

        var snap = _inner.GetSnapshot();
        lock (_sync)
        {
            _cached = snap;
            _hasCache = true;
            _fg = fg;
            _focus = focus;
            _tick = now;
        }

        return snap;
    }
}
