using System.Threading;

namespace WindowsHelperSuite.App.Services;

public class SingleInstanceService : IDisposable
{
    private Mutex? _mutex;
    private readonly string _mutexName = "WindowsHelperSuite_SingleInstance_Mutex";

    public bool IsFirstInstance()
    {
        _mutex = new Mutex(true, _mutexName, out bool createdNew);
        return createdNew;
    }

    public void Dispose()
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
