using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Infrastructure.Hooks;

namespace WindowsHelperSuite.Infrastructure.Services;

/// <summary>
/// Caches <see cref="IWriterOverlayExclusionDetector"/> while foreground + focus HWND are unchanged.
/// </summary>
public sealed class CachingWriterOverlayExclusionDetector : IWriterOverlayExclusionDetector
{
    private const int TtlMs = 120;

    private readonly IWriterOverlayExclusionDetector _inner;
    private readonly object _sync = new();
    private bool? _cachedExclude;
    private string _cachedReason = "";
    private nint _fg;
    private nint _focus;
    private long _tick;

    public CachingWriterOverlayExclusionDetector(IWriterOverlayExclusionDetector inner)
    {
        _inner = inner;
    }

    public bool ShouldExcludeWriterOverlay(out string reason)
    {
        if (!FocusIdentity.TryGet(out var fg, out var focus))
        {
            return _inner.ShouldExcludeWriterOverlay(out reason);
        }

        var now = Environment.TickCount64;
        lock (_sync)
        {
            var age = unchecked((int)(now - _tick));
            if (_cachedExclude.HasValue && _fg == fg && _focus == focus && age >= 0 && age < TtlMs)
            {
                reason = _cachedReason;
                return _cachedExclude.Value;
            }
        }

        var exclude = _inner.ShouldExcludeWriterOverlay(out var r);
        lock (_sync)
        {
            _cachedExclude = exclude;
            _cachedReason = r;
            _fg = fg;
            _focus = focus;
            _tick = now;
        }

        reason = r;
        return exclude;
    }
}
