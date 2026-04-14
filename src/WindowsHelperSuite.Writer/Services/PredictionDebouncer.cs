namespace WindowsHelperSuite.Writer.Services;

/// <summary>Coalesces rapid keystrokes: cancel prior work, delay, then run the latest request only.</summary>
public sealed class PredictionDebouncer : IDisposable
{
    private readonly int _debounceMs;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private int _generation;

    public PredictionDebouncer(int debounceMilliseconds = 60)
    {
        _debounceMs = Math.Clamp(debounceMilliseconds, 0, 500);
    }

    /// <summary>Returns false when this invocation was superseded before <paramref name="work"/> ran.</summary>
    public async Task<bool> TryRunLatestAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource linked;
        int gen;
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
            gen = ++_generation;
        }

        try
        {
            if (_debounceMs > 0)
            {
                await Task.Delay(_debounceMs, linked.Token).ConfigureAwait(false);
            }

            lock (_gate)
            {
                if (gen != _generation)
                {
                    return false;
                }
            }

            await work(linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
