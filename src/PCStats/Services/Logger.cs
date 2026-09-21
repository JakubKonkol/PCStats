using System.IO;

namespace PCStats.Services;

/// <summary>Minimal append-only file logger; the widget has no UI surface for diagnostics.</summary>
public static class Logger
{
    private static readonly object Gate = new();

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCStats");

    private static string FilePath => Path.Combine(Directory, "log.txt");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
                if (ex is not null)
                    line += Environment.NewLine + ex;
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
