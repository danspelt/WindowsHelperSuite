namespace WindowsHelperSuite.Core.Interfaces;

public interface ILoggingService
{
    void Information(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
    void Debug(string message);
}
