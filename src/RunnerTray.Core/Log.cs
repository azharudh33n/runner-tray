using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RunnerTray;

/// <summary>
/// Small file logger: %LOCALAPPDATA%\RunnerTray\logs\runnertray-YYYYMMDD.log, kept 7 days.
/// Tokens are always masked. Recent warnings and errors are kept in memory for the Diagnostics window.
/// </summary>
public static partial class Log
{
    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<string> RecentProblems = new();
    private static DateOnly _cleanedOn;

    /// <summary>Folder for log files. Tests point this at a temporary folder.</summary>
    public static string Dir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolder.Name, "logs");

    public static bool Enabled { get; set; } = true;

    public static IReadOnlyList<string> Recent => RecentProblems.ToArray();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    [GeneratedRegex(@"\b(gh[opsur]_[A-Za-z0-9]{8,}|github_pat_[A-Za-z0-9_]{8,}|ghr_[A-Za-z0-9]{8,})")]
    private static partial Regex TokenPattern();

    /// <summary>Replaces anything that looks like a GitHub token with ***.</summary>
    public static string Mask(string text) => TokenPattern().Replace(text, "***");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {Mask(message)}";
        if (level != "INFO")
        {
            RecentProblems.Enqueue(line);
            // Keep only the newest 50 entries.
            while (RecentProblems.Count > 50)
            {
                if (!RecentProblems.TryDequeue(out _))
                {
                    break;
                }
            }
        }

        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, $"runnertray-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
                CleanOldFiles();
            }
        }
        catch (IOException)
        {
            // Logging must never break the app (disk full, file locked).
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }

    private static void CleanOldFiles()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_cleanedOn == today)
        {
            return;
        }

        _cleanedOn = today;
        foreach (var file in Directory.EnumerateFiles(Dir, "runnertray-*.log"))
        {
            if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-7))
            {
                File.Delete(file);
            }
        }
    }
}
