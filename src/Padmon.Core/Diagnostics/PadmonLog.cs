using System.Diagnostics;

namespace Padmon.Core.Diagnostics;

public static class PadmonLog
{
    private static readonly object Sync = new();
    private static string? _path;

    public static bool IsEnabled => _path is not null;

    public static string? Path => _path;

    public static void Initialize(string path)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _path = path;
            Info($"log started (pid={Environment.ProcessId})");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"padmon log could not be initialized: {exception}");
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    private static void Write(string level, string message)
    {
        Debug.WriteLine($"[padmon {level}] {message}");

        var path = _path;
        if (path is null)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
