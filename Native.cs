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
    // ---- Close apps ----

    private const uint WM_CLOSE = 0x0010;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, System.Text.StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>All top-level windows of a process (also hidden ones, like tray apps have), without IME windows.</summary>
    public static List<IntPtr> GetTopLevelWindows(int processId)
    {
        var result = new List<IntPtr>();
        var cls = new System.Text.StringBuilder(256);
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != processId) return true;
            cls.Clear();
            GetClassNameW(hWnd, cls, cls.Capacity);
            var name = cls.ToString();
            if (name != "IME" && name != "MSCTFIME UI") result.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Posts WM_CLOSE (does not wait). Returns null on success, or the Windows error text.</summary>
    public static string? PostClose(IntPtr hWnd) =>
        PostMessageW(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero) ? null : new Win32Exception(Marshal.GetLastWin32Error()).Message;

    /// <summary>Full exe path of a process, or null when it can not be read.</summary>
    public static string? GetProcessPath(int processId)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }
}
