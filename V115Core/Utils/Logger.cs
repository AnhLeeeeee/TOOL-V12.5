using System.Text;
using System.Collections.Concurrent;

namespace ToolTikTokV11.Utils;

public sealed class Logger
{
    static readonly ConcurrentDictionary<string, System.Threading.Timer> CleanupTimers = new(StringComparer.OrdinalIgnoreCase);
    readonly string _dir;
    readonly object _lock = new();
    public event Action<string>? LineWritten;

    public Logger(string baseDir)
    {
        _dir = Path.GetFullPath(Path.Combine(baseDir, "logs"));
        Directory.CreateDirectory(_dir);
        ScheduleLogCleanup();
    }

    public void Info(string text) => Write("INFO", text);
    public void Warn(string text) => Write("WARN", text);
    public void Error(string text) => Write("ERROR", text);

    public void Write(string level, string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {text}";
        lock (_lock)
        {
            File.AppendAllText(Path.Combine(_dir, $"{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine, new UTF8Encoding(true));
        }
        LineWritten?.Invoke(line);
    }

    void ScheduleLogCleanup()
    {
        _ = Task.Run(() => CleanupExpiredLogs(_dir));
        CleanupTimers.GetOrAdd(_dir, root => new System.Threading.Timer(_ =>
        {
            try { CleanupExpiredLogs(root); }
            catch { }
        }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
    }

    // Logs are the only files considered here.  Paths are revalidated below so
    // a malformed/symlinked entry cannot cause config or profile data deletion.
    static void CleanupExpiredLogs(string logRoot)
    {
        var root = Path.GetFullPath(logRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root)) return;
        if ((new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0) return;

        var rootPrefix = root + Path.DirectorySeparatorChar;
        var cutoff = DateTime.UtcNow.AddHours(-24);
        foreach (var path in Directory.EnumerateFiles(root, "*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.GetLastWriteTimeUtc(fullPath) >= cutoff) continue;
                File.Delete(fullPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
