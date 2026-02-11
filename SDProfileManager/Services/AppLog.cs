namespace SDProfileManager.Services;

public static class AppLog
{
    private static readonly object _lock = new();
    private static string? _logFilePath;
    private static bool _initialized;

    public static void Bootstrap()
    {
        if (_initialized) return;
        _initialized = true;

        var logDir = LogsDirectoryPath();
        if (logDir is not null)
        {
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, "profilemanager.log");
            WriteLine("INFO", $"Logging initialized. file={_logFilePath}");
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Critical($"Unhandled exception: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Error($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };
    }

    public static void Info(string message) => WriteLine("INFO", message);
    public static void Warn(string message) => WriteLine("WARN", message);
    public static void Error(string message) => WriteLine("ERROR", message);
    public static void Critical(string message) => WriteLine("CRITICAL", message);

    public static string? LogsDirectoryPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData)) return null;
        return Path.Combine(localAppData, "SDProfileManager", "logs");
    }

    public static string? CurrentLogFilePath() => _logFilePath;

    private static void WriteLine(string level, string message)
    {
        var timestamp = DateTime.UtcNow.ToString("O");
        var sanitized = message.Replace("\n", "\\n");
        var line = $"[{timestamp}] [{level}] {sanitized}\n";

        lock (_lock)
        {
            if (_logFilePath is null) return;
            try
            {
                File.AppendAllText(_logFilePath, line);
            }
            catch
            {
                // Silently ignore log write failures
            }
        }
    }
}
