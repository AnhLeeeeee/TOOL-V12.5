using System.Text;
using System.Collections.Concurrent;

namespace ToolTikTokV11.Utils;

public sealed class Logger : IDisposable
{
    static readonly ConcurrentDictionary<string, System.Threading.Timer> CleanupTimers = new(StringComparer.OrdinalIgnoreCase);
    static readonly TimeSpan BufferedFlushInterval = TimeSpan.FromMilliseconds(200);
    readonly string _dir;
    readonly object _lock = new();
    readonly System.Threading.Timer _flushTimer;
    StreamWriter? _writer;
    string _activePath = "";
    bool _disposed;
    public event Action<string>? LineWritten;

    public Logger(string baseDir)
    {
        _dir = Path.GetFullPath(Path.Combine(baseDir, "logs"));
        Directory.CreateDirectory(_dir);
        ScheduleLogCleanup();

        // Keep one append stream open and flush it in small batches.  The old
        // implementation opened/closed the log file for every PERF/scan line,
        // putting synchronous disk I/O directly on the automation hot path.
        // Ordering and log contents stay identical; only the persistence I/O is
        // amortized across a very small window.
        _flushTimer = new System.Threading.Timer(_ => FlushBuffered(), null, BufferedFlushInterval, BufferedFlushInterval);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
    }

    public void Info(string text) => Write("INFO", text);
    public void Warn(string text) => Write("WARN", text);
    public void Error(string text) => Write("ERROR", text);

    public void Write(string level, string text)
    {
        var now = DateTime.Now;
        var line = $"[{now:HH:mm:ss}] [{level}] {text}";
        lock (_lock)
        {
            ThrowIfDisposed();
            var path = Path.Combine(_dir, $"{now:yyyy-MM-dd}.log");
            EnsureWriter(path);
            _writer!.WriteLine(line);
            if (level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
                _writer.Flush();
        }
        LineWritten?.Invoke(line);
    }

    void EnsureWriter(string path)
    {
        if (_writer is not null && path.Equals(_activePath, StringComparison.OrdinalIgnoreCase)) return;
        CloseWriterNoThrow();
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.SequentialScan);
        _writer = new StreamWriter(stream, new UTF8Encoding(true), 16 * 1024, leaveOpen: false) { AutoFlush = false };
        _activePath = path;
    }

    void FlushBuffered()
    {
        lock (_lock)
        {
            if (_disposed) return;
            try { _writer?.Flush(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    void CloseWriterNoThrow()
    {
        try { _writer?.Flush(); } catch { }
        try { _writer?.Dispose(); } catch { }
        _writer = null;
        _activePath = "";
    }

    void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Logger));
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

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _flushTimer.Dispose(); } catch { }
            CloseWriterNoThrow();
        }
        GC.SuppressFinalize(this);
    }
}
