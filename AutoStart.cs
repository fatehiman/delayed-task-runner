using Microsoft.Win32;

namespace DelayedTaskRunner;

/// <summary>"Auto start" = a value in HKCU\...\Run (starts after user login).</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Program.AppName) != null;
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Program.AppName, $"\"{Paths.ExePath}\"");
        else key.DeleteValue(Program.AppName, throwOnMissingValue: false);
    }
}
