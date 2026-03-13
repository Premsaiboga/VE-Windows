using System.Collections.Concurrent;
using System.IO;

namespace VE.Windows.Helpers;

/// <summary>
/// File-based logging with rotation. Logs to %AppData%/VE/Logs/main.log.
/// Equivalent to macOS FileLogger.
/// </summary>
public sealed class FileLogger : IDisposable
{
    public static FileLogger Instance { get; } = new();

    private readonly string _logDir;
    private readonly string _logPath;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly Timer _flushTimer;
    private readonly object _fileLock = new();
    private const long MaxFileSize = 10 * 1024 * 1024; // 10MB
    private const int MaxFiles = 5;

    private FileLogger()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDir = Path.Combine(appData, "VE", "Logs");
        Directory.CreateDirectory(_logDir);
        _logPath = Path.Combine(_logDir, "main.log");
        _flushTimer = new Timer(Flush, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    public void Debug(string category, string message) => Log("DEBUG", category, message);
    public void Info(string category, string message) => Log("INFO", category, message);
    public void Warning(string category, string message) => Log("WARN", category, message);
    public void Error(string category, string message) => Log("ERROR", category, message);
    public void Critical(string category, string message) => Log("CRITICAL", category, message);

    private void Log(string level, string category, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[{timestamp}] [{level}] [{category}] {message}";
        _queue.Enqueue(line);

#if DEBUG
        System.Diagnostics.Debug.WriteLine(line);
#endif
    }

    private void Flush(object? state)
    {
        if (_queue.IsEmpty) return;

        var lines = new List<string>();
        while (_queue.TryDequeue(out var line))
        {
            lines.Add(line);
        }

        if (lines.Count == 0) return;

        lock (_fileLock)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllLines(_logPath, lines);
            }
            catch { }
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath)) return;
            var fi = new FileInfo(_logPath);
            if (fi.Length < MaxFileSize) return;

            // Rotate: main.log -> main.1.log -> ... -> main.4.log
            for (int i = MaxFiles - 1; i >= 1; i--)
            {
                var src = Path.Combine(_logDir, $"main.{i}.log");
                var dst = Path.Combine(_logDir, $"main.{i + 1}.log");
                if (File.Exists(dst)) File.Delete(dst);
                if (File.Exists(src)) File.Move(src, dst);
            }

            var rotated = Path.Combine(_logDir, "main.1.log");
            File.Move(_logPath, rotated);
        }
        catch { }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush(null);
    }
}
