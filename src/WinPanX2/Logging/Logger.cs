namespace WinPanX2.Logging;

internal static class Logger
{
    private static readonly object _lock = new();
    private static string? _logPath;
    private static StreamWriter? _writer;
    private const long MaxLogSizeBytes = 500 * 1024; // 500 KB

    public static void Initialize(string path)
    {
        _logPath = path;
        RotateIfNeeded(path);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        if (_writer == null) return;

        lock (_lock)
        {
            if (_logPath != null)
                RotateIfNeeded(_logPath);

            _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][{level}] {message}");
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

    private static void RotateIfNeeded(string path)
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
        }
        catch
        {
            // Logging must never throw
        }
    }
}
