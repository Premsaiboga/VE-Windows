using System.Collections.Concurrent;
using System.IO;

namespace VE.Windows.Helpers;

/// <summary>
/// File-based logging with rotation. Logs to %AppData%/VE/Logs/main.log.
/// Matches macOS Logger.swift: 5MB max file size, rotate to .old, thread-safe ConcurrentQueue.
/// Format: [2026-03-15 14:30:22.123] [INFO] [Category] Message
/// </summary>
public sealed class FileLogger : IDisposable
{
    public static FileLogger Instance { get; } = new();

    private readonly string _logDir;
    private readonly string _logPath;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly Timer _flushTimer;
    private readonly object _fileLock = new();
    private const long MaxFileSize = 5 * 1024 * 1024; // 5MB (matches macOS)

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

    /// <summary>
    /// Force flush all queued log entries immediately. Called before crash/exit.
    /// </summary>
    public void FlushNow()
    {
        Flush(null);
    }

    /// <summary>
    /// Rotate log file when it exceeds 5MB.
    /// Matches macOS: rename to main.old.log (single backup), overwriting previous.
    /// </summary>
    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_logPath)) return;
            var fi = new FileInfo(_logPath);
            if (fi.Length < MaxFileSize) return;

            var oldPath = Path.Combine(_logDir, "main.old.log");
            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(_logPath, oldPath);
        }
        catch { }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush(null);
    }
}
