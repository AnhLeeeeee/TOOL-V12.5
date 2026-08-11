using System.Text;
using System.Collections.Concurrent;

namespace ToolTikTokV12.Utils;

public sealed class Logger
{
    const long MaxActiveLogBytes = 64L * 1024 * 1024;
    static readonly ConcurrentDictionary<string, System.Threading.Timer> CleanupTimers = new(StringComparer.OrdinalIgnoreCase);
    readonly string _dir;
    readonly string _logRoot;
    readonly string? _fixedFileName;
    readonly object _lock = new();
    public event Action<string>? LineWritten;

    public Logger(string baseDir, string? scope = null, string? fixedFileName = null)
    {
        _logRoot = Path.GetFullPath(Path.Combine(baseDir, "logs"));
        _dir = string.IsNullOrWhiteSpace(scope) ? _logRoot : Path.Combine(_logRoot, scope);
        _fixedFileName = fixedFileName;
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
            var fileName = string.IsNullOrWhiteSpace(_fixedFileName) ? $"{DateTime.Now:yyyy-MM-dd}.log" : _fixedFileName;
            var path = Path.Combine(_dir, fileName);
            RotateIfNeeded(path);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            writer.WriteLine(line);
            writer.Flush();
        }
        LineWritten?.Invoke(line);
    }

    void RotateIfNeeded(string activePath)
    {
        try
        {
            var info = new FileInfo(activePath);
            if (!info.Exists || info.Length < MaxActiveLogBytes) return;
            var directory = Path.GetDirectoryName(activePath)!;
            var baseName = Path.GetFileNameWithoutExtension(activePath);
            var archive = Path.Combine(directory, $"{baseName}-{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.Move(activePath, archive);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    void ScheduleLogCleanup()
    {
        _ = Task.Run(() =>
        {
            var result = CleanupExpiredLogs(_logRoot);
            if (result.Deleted > 0)
                Info($"[LOG_CLEANUP] deleted={result.Deleted} freedBytes={result.FreedBytes}");
        });
        CleanupTimers.GetOrAdd(_logRoot, root => new System.Threading.Timer(_ =>
        {
            try { CleanupExpiredLogs(root); }
            catch { }
        }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1)));
    }

    internal static (int Deleted, long FreedBytes) CleanupExpiredLogs(string logRoot)
    {
        var root = Path.GetFullPath(logRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(root)) return (0, 0);
        var cutoff = DateTime.Now.AddHours(-24);
        var deleted = 0;
        long freedBytes = 0;
        foreach (var path in EnumerateToolLogFiles(root))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(fullPath);
                if (info.LastWriteTime >= cutoff) continue;
                var length = info.Length;
                File.Delete(fullPath);
                deleted++;
                freedBytes += length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return (deleted, freedBytes);
    }

    static IEnumerable<string> EnumerateToolLogFiles(string root)
    {
        // Do not follow junctions/symlinks below logs.  A log cleanup must never
        // reach a profile, config, or any other directory outside the Tool log root.
        var rootInfo = new DirectoryInfo(root);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0) yield break;

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            DirectoryInfo directory;
            try { directory = new DirectoryInfo(current); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            FileInfo[] files;
            DirectoryInfo[] subdirectories;
            try
            {
                files = directory.GetFiles("*.log", SearchOption.TopDirectoryOnly);
                subdirectories = directory.GetDirectories("*", SearchOption.TopDirectoryOnly);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0)
                    yield return file.FullName;

            foreach (var subdirectory in subdirectories)
                if ((subdirectory.Attributes & FileAttributes.ReparsePoint) == 0)
                    pending.Push(subdirectory.FullName);
        }
    }
}
