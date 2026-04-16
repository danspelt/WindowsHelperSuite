using System.Threading;

namespace WindowsHelperSuite.App.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly object _disposeLock = new();
    private readonly string _mutexName = "WindowsHelperSuite_SingleInstance_Mutex";
    private readonly string _activateEventName = "WindowsHelperSuite_SingleInstance_Activate";
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private Thread? _activateListenerThread;
    private bool _ownsMutex;
    private bool _disposed;

    public bool IsFirstInstance()
    {
        _mutex = new Mutex(false, _mutexName);
        var acquired = false;
        try
        {
            acquired = _mutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        _ownsMutex = acquired;

        if (acquired)
        {
            // Auto-reset so each signal triggers exactly one activation.
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _activateEventName);
        }

        return acquired;
    }

    public void StartActivationListener(Action onActivate)
    {
        if (_activateEvent == null)
        {
            return;
        }

        _activateListenerThread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _activateEvent.WaitOne();
                }
                catch
                {
                    return;
                }

                lock (_disposeLock)
                {
                    if (_disposed)
                    {
                        return;
                    }
                }

                try
                {
                    onActivate();
                }
                catch
                {
                    // Never let activation handling kill the listener.
                }
            }
        })
        {
            IsBackground = true,
            Name = "WindowsHelperSuite.SingleInstanceActivationListener"
        };

        _activateListenerThread.Start();
    }

    public bool SignalFirstInstance()
    {
        try
        {
            using var ewh = EventWaitHandle.OpenExisting(_activateEventName);
            return ewh.Set();
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Mutex? m;
        EventWaitHandle? a;
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
            a = _activateEvent;
            _activateEvent = null;
        }

        if (a != null)
        {
            try { a.Set(); } catch { }
            a.Dispose();
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
