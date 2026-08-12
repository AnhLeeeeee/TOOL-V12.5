using System.Text;
using System.Collections.Concurrent;

namespace ToolTikTokV12.Utils;

public sealed class Logger : IDisposable
{
    const long MaxActiveLogBytes = 64L * 1024 * 1024;
    static readonly TimeSpan BufferedFlushInterval = TimeSpan.FromMilliseconds(200);
    static readonly ConcurrentDictionary<string, System.Threading.Timer> CleanupTimers = new(StringComparer.OrdinalIgnoreCase);
    readonly string _dir;
    readonly string _logRoot;
    readonly string? _fixedFileName;
    readonly object _lock = new();
    readonly System.Threading.Timer _flushTimer;
    StreamWriter? _writer;
    FileStream? _writerStream;
    string _activePath = "";
    bool _disposed;
    public event Action<string>? LineWritten;

    public Logger(string baseDir, string? scope = null, string? fixedFileName = null)
    {
        _logRoot = Path.GetFullPath(Path.Combine(baseDir, "logs"));
        _dir = string.IsNullOrWhiteSpace(scope) ? _logRoot : Path.Combine(_logRoot, scope);
        _fixedFileName = fixedFileName;
        Directory.CreateDirectory(_dir);
        ScheduleLogCleanup();

        // Reuse one append stream instead of opening, flushing and closing a
        // file for every Manager status/IPC line.  The 200 ms flush window keeps
        // the file current while removing avoidable synchronous I/O from UI/IPC.
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
            var fileName = string.IsNullOrWhiteSpace(_fixedFileName) ? $"{now:yyyy-MM-dd}.log" : _fixedFileName;
            var path = Path.Combine(_dir, fileName);
            EnsureWriter(path);
            RotateIfNeeded(path);
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
        _writerStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.SequentialScan);
        _writer = new StreamWriter(_writerStream, new UTF8Encoding(true), 16 * 1024, leaveOpen: false) { AutoFlush = false };
        _activePath = path;
    }

    void RotateIfNeeded(string activePath)
    {
        try
        {
            var length = _writerStream is not null && activePath.Equals(_activePath, StringComparison.OrdinalIgnoreCase)
                ? _writerStream.Length
                : new FileInfo(activePath).Length;
            if (length < MaxActiveLogBytes) return;

            if (activePath.Equals(_activePath, StringComparison.OrdinalIgnoreCase))
                CloseWriterNoThrow();
            var directory = Path.GetDirectoryName(activePath)!;
            var baseName = Path.GetFileNameWithoutExtension(activePath);
            var archive = Path.Combine(directory, $"{baseName}-{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.Move(activePath, archive);
        }
        catch (FileNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
        _writerStream = null;
        _activePath = "";
    }

    void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Logger));
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
