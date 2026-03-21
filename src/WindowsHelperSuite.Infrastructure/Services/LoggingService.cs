using System.IO;
using Serilog;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.Infrastructure.Services;

public class LoggingService : ILoggingService
{
    private readonly ILogger _logger;

    public LoggingService()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite",
            "logs",
            "app-.log");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }

    public void Information(string message) => _logger.Information(message);
    public void Warning(string message) => _logger.Warning(message);
    public void Error(string message, Exception? exception = null) => _logger.Error(exception, message);
    public void Debug(string message) => _logger.Debug(message);
}
