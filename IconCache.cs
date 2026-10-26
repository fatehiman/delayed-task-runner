using System.Security.Cryptography;
using System.Text;

namespace DelayedTaskRunner;

/// <summary>
/// Saves the icons found by "Fetch icons" as small PNG files in %LOCALAPPDATA%\DelayedTaskRunner\icons.
/// The editor loads them when it opens. A saved icon is never checked again, only replaced by a new fetch.
/// The key is the task's file as written in the task file (not the resolved path), so a file that is
/// missing right now still shows its saved icon.
/// </summary>
internal static class IconCache
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Program.AppName, "icons");

    /// <summary>Cache key of a task command: a hash of its file part.</summary>
    public static string KeyFor(string command)
    {
        var file = Environment.ExpandEnvironmentVariables(Launcher.Split(command).File).Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(file)));
    }

    private static string PathFor(string key) => Path.Combine(Dir, key + ".png");

    /// <summary>The saved icon, or null when there is none (or it can not be read).</summary>
    public static Bitmap? Load(string key)
    {
        try
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return null;
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var img = new Bitmap(ms);
            return new Bitmap(img); // a copy, so the stream is not needed any more
        }
        catch { return null; }
    }

    public static void Save(string key, Bitmap icon)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            icon.Save(PathFor(key), System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex) { Logger.Write("Can not save icon: " + ex.Message); }
    }
}
