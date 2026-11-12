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
    private const uint SEE_MASK_NOCLOSEPROCESS = 0x40;

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
    public static string? ShellExecute(string file, string args, string? dir, int show) =>
        ShellExecute(file, args, dir, show, false, out _);

    /// <summary>
    /// Returns null on success, or the Windows error text. With <paramref name="wantProcess"/>, <paramref name="process"/>
    /// is the started process handle (close it with <see cref="CloseProcessHandle"/>), or zero when Windows gives none.
    /// </summary>
    public static string? ShellExecute(string file, string args, string? dir, int show, bool wantProcess, out IntPtr process)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI | (wantProcess ? SEE_MASK_NOCLOSEPROCESS : 0),
            lpFile = file,
            lpParameters = string.IsNullOrEmpty(args) ? null : args,
            lpDirectory = dir,
            nShow = show,
        };
        bool ok = ShellExecuteExW(ref info);
        var error = ok ? null : new Win32Exception(Marshal.GetLastWin32Error()).Message;
        process = ok ? info.hProcess : IntPtr.Zero;
        return error;
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

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    /// <summary>True when the process of the handle has ended (does not wait). <paramref name="exitCode"/> is set then.</summary>
    public static bool HasProcessEnded(IntPtr process, out uint exitCode)
    {
        exitCode = 0;
        if (WaitForSingleObject(process, 0) != 0) return false; // 0 = WAIT_OBJECT_0
        GetExitCodeProcess(process, out exitCode);
        return true;
    }

    public static void CloseProcessHandle(IntPtr process)
    {
        if (process != IntPtr.Zero) CloseHandle(process);
    }

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

    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION = 0x0016;
    private const uint ENDSESSION_CLOSEAPP = 0x1;
    private const uint SMTO_ABORTIFHUNG = 0x2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>
    /// The messages Windows (Restart Manager) sends to close an app for an update / shutdown:
    /// WM_QUERYENDSESSION, then WM_ENDSESSION(TRUE) with ENDSESSION_CLOSEAPP. Waits up to 2 s for each window.
    /// Returns null on success, or the Windows error text.
    /// </summary>
    public static string? SendEndSession(IntPtr hWnd)
    {
        var lParam = (IntPtr)ENDSESSION_CLOSEAPP;
        if (SendMessageTimeoutW(hWnd, WM_QUERYENDSESSION, IntPtr.Zero, lParam, SMTO_ABORTIFHUNG, 2000, out _) == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 0) return new Win32Exception(err).Message;
        }
        // Like Restart Manager: WM_ENDSESSION(TRUE) also when the app said "no" (we close it anyway).
        if (SendMessageTimeoutW(hWnd, WM_ENDSESSION, (IntPtr)1, lParam, SMTO_ABORTIFHUNG, 2000, out _) == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 0) return new Win32Exception(err).Message;
        }
        return null;
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
