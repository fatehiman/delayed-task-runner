namespace DelayedTaskRunner;

internal static class Program
{
    public const string AppName = "DelayedTaskRunner";

    [STAThread]
    private static int Main(string[] args)
    {
        // One instance only (both modes share the same mutex).
        using var mutex = new Mutex(true, @"Local\DelayedTaskRunnerSingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Native.ShowError($"{AppName} is already running.");
            return 1;
        }

        ApplicationConfiguration.Initialize();
        Logger.Init(Paths.LogFile);

        // A crash must leave the full error text in the log (not only a dialog).
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);

        try
        {
            if (args.Length > 0)
            {
                // Editor mode: list all tasks, check/uncheck, run by hand.
                Logger.Write("Editor mode started.");
                Application.Run(new EditorForm());
                Logger.Write("Editor mode closed.");
            }
            else
            {
                // Runner mode: tray icon, start tasks by their time, then exit.
                Logger.Write("App started.");
                Application.Run(new TrayRunner());
                Logger.Write("App exited.");
            }
        }
        catch (Exception ex)
        {
            ReportCrash(ex);
            return 2;
        }

        GC.KeepAlive(mutex);
        return 0;
    }

    private static void ReportCrash(Exception ex)
    {
        Logger.Write("CRASH: " + ex);
        Native.ShowError($"{AppName} error:\n\n{ex.Message}\n\nDetails are in the log:\n{Paths.LogFile}");
    }
}

internal static class Paths
{
    public static readonly string ExePath = Environment.ProcessPath ?? Application.ExecutablePath;
    public static readonly string Home = Path.GetDirectoryName(ExePath)!;
    public static readonly string TaskFile = Path.Combine(Home, "DelayedTaskRunner.txt");
    public static readonly string LogFile = Path.Combine(Home, "DelayedTaskRunner.log");
}

internal static class AppIcon
{
    public static Icon Load(int size = 0)
    {
        using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico")!;
        return size > 0 ? new Icon(s, size, size) : new Icon(s);
    }
}
