using System.Threading;

namespace WindowsHelperSuite.App.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly object _disposeLock = new();
    private readonly string _mutexName = "WindowsHelperSuite_SingleInstance_Mutex";
    private Mutex? _mutex;
    private bool _ownsMutex;
    private bool _disposed;

    public bool IsFirstInstance()
    {
        _mutex = new Mutex(true, _mutexName, out bool createdNew);
        _ownsMutex = createdNew;
        return createdNew;
    }

    public void Dispose()
    {
        Mutex? m;
        var owns = false;

        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owns = _ownsMutex;
            m = _mutex;
            _mutex = null;
            _ownsMutex = false;
        }

        if (m == null)
        {
            return;
        }

        if (owns)
        {
            try
            {
                m.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Only the thread that acquired the mutex may release; shutdown ordering can vary.
            }
        }

        m.Dispose();
    }
}
