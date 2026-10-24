using System.Text;

namespace DelayedTaskRunner;

/// <summary>Short log file. Rotated to .log.old when it is bigger than 1 MB.</summary>
internal static class Logger
{
    private const long MaxSize = 1024 * 1024;
    private static string? _path;
    private static readonly object Lock = new();

    public static void Init(string path)
    {
        _path = path;
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length > MaxSize)
                File.Move(path, path + ".old", overwrite: true);
        }
        catch { /* log is optional */ }
    }

    public static void Write(string message)
    {
        if (_path == null) return;
        lock (Lock)
        {
            try
            {
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}\r\n", new UTF8Encoding(false));
            }
            catch { /* log is optional */ }
        }
    }
}
