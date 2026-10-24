using System.Diagnostics;

namespace DelayedTaskRunner;

/// <summary>
/// Runner mode: shows a tray icon and starts the enabled tasks one by one at their time
/// (time after app start). Exits when all tasks are started.
/// Left click on the icon = pause and ask Continue/Cancel.
/// </summary>
internal sealed class TrayRunner : ApplicationContext
{
    private const int TickMs = 250;
    private static readonly TimeSpan NotifyShowTime = TimeSpan.FromSeconds(6);

    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HiddenWindow _window;

    private readonly List<TaskItem> _queue;
    private readonly int _total;
    private int _next;

    // The task clock: it stops while paused or while the Continue/Cancel dialog is open.
    private readonly Stopwatch _clock = new();
    private bool _dialogOpen;
    private DateTime _finishedAt = DateTime.MinValue;
    private DateTime _lastNotify = DateTime.MinValue;
    private bool _exiting;

    public TrayRunner()
    {
        _queue = LoadQueue();
        _total = _queue.Count;

        _pauseItem = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => OnPauseChanged();

        _autoStartItem = new ToolStripMenuItem("Auto start") { Checked = SafeAutoStartIsOn() };
        _autoStartItem.Click += (_, _) => ToggleAutoStart();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Quit("Exit clicked in tray menu.");

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] { _pauseItem, _autoStartItem, new ToolStripSeparator(), exitItem });

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Load(16),
            Text = Program.AppName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) AskContinueOrCancel(); };

        // Hidden window, so WM_CLOSE (for example from a stop script or Windows logoff) closes the app cleanly.
        _window = new HiddenWindow(() => Quit("Close message received."));

        _timer = new System.Windows.Forms.Timer { Interval = TickMs };
        _timer.Tick += (_, _) => Tick();

        Logger.Write($"{_total} tasks scheduled to run.");
        _clock.Start();
        _timer.Start();
        UpdateTooltip();
    }

    private List<TaskItem> LoadQueue()
    {
        try
        {
            Logger.Write($"Opening '{Paths.TaskFile}' to read.");
            var file = TaskFile.Load(Paths.TaskFile);
            // Sort by time; tasks with the same time keep the file order.
            return file.Tasks.Where(t => t.Enabled).OrderBy(t => t.Time).ThenBy(t => t.LineIndex).ToList();
        }
        catch (Exception ex)
        {
            Logger.Write($"Can not read task file: {ex.Message}");
            MessageBox.Show($"Can not read the task file:\n{Paths.TaskFile}\n\n{ex.Message}",
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return new List<TaskItem>();
        }
    }

    private bool Paused => _pauseItem.Checked || _dialogOpen;

    private void Tick()
    {
        if (_exiting) return;

        if (_next >= _queue.Count)
        {
            // All started. Wait a little if a notification was just shown, so the user can read it.
            if (_finishedAt == DateTime.MinValue)
            {
                _finishedAt = DateTime.Now;
                Logger.Write($"{_next}/{_total} tasks ran, so exit.");
            }
            if (DateTime.Now - _lastNotify >= NotifyShowTime) Quit(null);
            return;
        }

        if (!Paused && _clock.Elapsed >= _queue[_next].Time)
        {
            // Start only one task per tick, so tasks with the same time start slowly one after another.
            RunTask(_queue[_next]);
            _next++;
        }
        UpdateTooltip();
    }

    private void RunTask(TaskItem task)
    {
        Logger.Write($"Running '{task.Command}' ({task.Status})...");
        string? error;
        try { error = Launcher.Run(task); }
        catch (Exception ex) { error = ex.Message; }

        if (error == null)
        {
            Logger.Write("Done.");
            return;
        }

        // Do not stop or pause: just tell the user.
        Logger.Write("Error: " + error);
        _lastNotify = DateTime.Now;
        _tray.ShowBalloonTip((int)NotifyShowTime.TotalMilliseconds, Program.AppName, $"{error}\n{task.Command}", ToolTipIcon.Warning);
    }

    private void UpdateTooltip()
    {
        string text;
        if (_next >= _queue.Count) text = $"{Program.AppName}\nAll {_total} tasks started";
        else
        {
            var wait = _queue[_next].Time - _clock.Elapsed;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            text = $"{Program.AppName}{(Paused ? " (paused)" : "")}\n{_next}/{_total} tasks started\nNext in {wait:mm\\:ss}";
        }
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    private void OnPauseChanged()
    {
        Logger.Write(_pauseItem.Checked ? "Paused from menu." : "Resumed from menu.");
        UpdateClock();
        UpdateTooltip();
    }

    private void UpdateClock()
    {
        if (Paused) _clock.Stop();
        else _clock.Start();
    }

    private void AskContinueOrCancel()
    {
        if (_dialogOpen || _exiting || _next >= _queue.Count) return;

        _dialogOpen = true;
        UpdateClock();
        UpdateTooltip();
        Logger.Write("Paused by click on tray icon.");

        var btnContinue = new TaskDialogButton("Continue");
        var btnCancel = new TaskDialogButton("Cancel");
        var page = new TaskDialogPage
        {
            Caption = Program.AppName,
            Heading = "Starting apps is paused.",
            Text = $"{_next} of {_total} tasks are started.\n\n" +
                   "Continue: start the remaining apps.\n" +
                   "Cancel: close DelayedTaskRunner (the remaining apps will not start).",
            Icon = TaskDialogIcon.Information,
            Buttons = { btnContinue, btnCancel },
            DefaultButton = btnContinue,
            AllowCancel = true,
        };

        TaskDialogButton result;
        try
        {
            result = TaskDialog.ShowDialog(_window.Handle, page, TaskDialogStartupLocation.CenterScreen);
        }
        finally
        {
            _dialogOpen = false;
        }

        if (result == btnContinue)
        {
            Logger.Write("Continue clicked.");
            _pauseItem.Checked = false; // Continue also ends a Pause from the menu.
            UpdateClock();
            UpdateTooltip();
        }
        else
        {
            Quit($"Cancel clicked. {_total - _next} tasks will not run.");
        }
    }

    private static bool SafeAutoStartIsOn()
    {
        try { return AutoStart.IsOn(); }
        catch { return false; }
    }

    private void ToggleAutoStart()
    {
        bool on = !_autoStartItem.Checked;
        try
        {
            AutoStart.Set(on);
            _autoStartItem.Checked = on;
            Logger.Write(on ? "Auto start turned on." : "Auto start turned off.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Can not change auto start:\n{ex.Message}", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Quit(string? reason)
    {
        if (_exiting) return;
        _exiting = true;
        if (reason != null) Logger.Write(reason);
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _window.Dispose();
        ExitThread();
    }

    /// <summary>Invisible top-level window that turns WM_CLOSE / WM_ENDSESSION into a clean exit.</summary>
    private sealed class HiddenWindow : NativeWindow, IDisposable
    {
        private const int WM_CLOSE = 0x0010;
        private const int WM_ENDSESSION = 0x0016;
        private readonly Action _onClose;

        public HiddenWindow(Action onClose)
        {
            _onClose = onClose;
            CreateHandle(new CreateParams { Caption = Program.AppName + " (hidden)" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLOSE || (m.Msg == WM_ENDSESSION && m.WParam != IntPtr.Zero))
            {
                _onClose();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }
}
