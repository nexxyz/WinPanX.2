using System;
using System.IO;

namespace WinPanX2.Logging;

internal static class Logger
{
    private static readonly object _lock = new();
    private static string? _logPath;
    private static StreamWriter? _writer;
    private const long MaxLogSizeBytes = 500 * 1024; // 500 KB

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static void Initialize(string path)
    {
        lock (_lock)
        {
            _logPath = path;

            try
            {
                RotateIfNeededLocked(path);
                EnsureWriterLocked(path);
            }
            catch
            {
                // Logging must never throw
            }
        }
    }

    public static void Trace(string message) => Write(LogLevel.Trace, message);
    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message)  => Write(LogLevel.Info, message);
    public static void Warn(string message)  => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    private static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel) return;

        lock (_lock)
        {
            try
            {
                if (_logPath == null)
                    return;

                RotateIfNeededLocked(_logPath);
                EnsureWriterLocked(_logPath);

                _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][{level}] {message}");
            }
            catch
            {
                // Logging must never throw
            }
        }
    }

    public static void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static void EnsureWriterLocked(string path)
    {
        if (_writer != null)
            return;

        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    private static void RotateIfNeededLocked(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            var info = new FileInfo(path);
            if (info.Length < MaxLogSizeBytes) return;

            _writer?.Dispose();
            _writer = null;

            var backup = path + ".old";
            if (File.Exists(backup))
                File.Delete(backup);

            File.Move(path, backup);

            EnsureWriterLocked(path);
        }
        catch
        {
            // Logging must never throw
        }
    }
}
