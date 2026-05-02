using System;
using System.IO;

namespace dlp_agent;

public static class LogManager
{
    private static readonly string LogFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AerologueDLP");
    private static readonly string LogFilePath = Path.Combine(LogFolder, "agent.log");
    private static readonly object LockObj = new();

    public static void EnsureLogDirectory()
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
        }
        catch
        {
            // Ignore directory creation failure. Caller may already handle.
        }
    }

    public static void LogInfo(string message) => Write("INFO", message);
    public static void LogWarning(string message) => Write("WARN", message);
    public static void LogError(string message, Exception? ex = null)
    {
        var formatted = ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", formatted);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (LockObj)
            {
                EnsureLogDirectory();
                File.AppendAllText(LogFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // If logging fails, do not crash the service.
        }
    }

    public static string GetLogFilePath() => LogFilePath;
}
