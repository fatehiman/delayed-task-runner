using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DelayedTaskRunner;

internal static class Native
{
    public const int SW_HIDE = 0;
    public const int SW_SHOWMINNOACTIVE = 7;
    public const int SW_SHOWNOACTIVATE = 4;

    private const uint MB_ICONERROR = 0x10;
    private const uint MB_TOPMOST = 0x40000;
    private const uint SEE_MASK_NOASYNC = 0x100;
    private const uint SEE_MASK_FLAG_NO_UI = 0x400;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>Error box that works before WinForms is started.</summary>
    public static void ShowError(string text) => MessageBoxW(IntPtr.Zero, text, Program.AppName, MB_ICONERROR | MB_TOPMOST);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO info);

    /// <summary>Returns null on success, or the Windows error text.</summary>
    public static string? ShellExecute(string file, string args, string? dir, int show)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI,
            lpFile = file,
            lpParameters = string.IsNullOrEmpty(args) ? null : args,
            lpDirectory = dir,
            nShow = show,
        };
        return ShellExecuteExW(ref info) ? null : new Win32Exception(Marshal.GetLastWin32Error()).Message;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Small shell icon of a file (its own icon for an exe), or null when none can be read.</summary>
    public static Bitmap? GetSmallIcon(string path)
    {
        var info = new SHFILEINFO();
        SHGetFileInfoW(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            using var icon = Icon.FromHandle(info.hIcon);
            return icon.ToBitmap();
        }
        finally { DestroyIcon(info.hIcon); }
    }
}
