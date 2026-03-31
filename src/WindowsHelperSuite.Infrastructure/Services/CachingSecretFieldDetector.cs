using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Writer;
using WindowsHelperSuite.Infrastructure.Hooks;

namespace WindowsHelperSuite.Infrastructure.Services;

/// <summary>Reuses <see cref="SecretFieldSnapshot"/> while foreground + focus HWND are unchanged (avoids UIA every keystroke).</summary>
public sealed class CachingSecretFieldDetector : ISecretFieldDetector
{
    private const int TtlMs = 120;

    private readonly ISecretFieldDetector _inner;
    private readonly object _sync = new();
    private SecretFieldSnapshot? _cached;
    private nint _fg;
    private nint _focus;
    private long _tick;

    public CachingSecretFieldDetector(ISecretFieldDetector inner)
    {
        _inner = inner;
    }

    public SecretFieldSnapshot GetSnapshot()
    {
        if (!FocusIdentity.TryGet(out var fg, out var focus))
        {
            return _inner.GetSnapshot();
        }

        var now = Environment.TickCount64;
        lock (_sync)
        {
            var age = unchecked((int)(now - _tick));
            if (_cached != null && _fg == fg && _focus == focus && age >= 0 && age < TtlMs)
            {
                return _cached;
            }
        }

        var snap = _inner.GetSnapshot();
        lock (_sync)
        {
            _cached = snap;
            _fg = fg;
            _focus = focus;
            _tick = now;
        }

        return snap;
    }
}
