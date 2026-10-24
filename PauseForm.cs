namespace DelayedTaskRunner;

/// <summary>What the pause window needs from the runner.</summary>
internal interface IPauseHost
{
    TaskItem? NextTask { get; }
    int Done { get; }
    int Total { get; }

    /// <summary>Starts the next task now (only waits for the launch, not for its window). Returns null or an error.</summary>
    Task<string?> RunNextAsync();

    void SkipNext();
}

/// <summary>
/// Window shown on a left click on the tray icon (the runner is paused while it is open).
/// Continue = go on by time, Next = start the next task now, Ignore this = skip it, Exit = close the app.
/// </summary>
internal sealed class PauseForm : Form
{
    public enum Choice { Continue, Exit }

    public Choice Result { get; private set; } = Choice.Continue;

    private readonly IPauseHost _host;
    private readonly Label _progress;
    private readonly Label _next;
    private readonly Label _nextInfo;
    private readonly Label _last;
    private readonly Button _btnContinue;
    private readonly Button _btnNext;
    private readonly Button _btnIgnore;
    private readonly Button _btnExit;
    private bool _busy;
    private DateTime _refilledAt = DateTime.MinValue;

    /// <summary>
    /// True for a short time after Next/Ignore. Then focus may have moved to Continue,
    /// and a fast extra Enter/click must not close the app by mistake.
    /// </summary>
    private bool JustRefilled => DateTime.Now - _refilledAt < TimeSpan.FromMilliseconds(600);

    public PauseForm(IPauseHost host)
    {
        _host = host;

        Text = Program.AppName;
        Icon = AppIcon.Load();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = SystemFonts.MessageBoxFont ?? Font;
        Padding = new Padding(12);

        const int textWidth = 620;
        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            Margin = new Padding(0, 0, 0, 10),
            Text = "Starting apps is paused.\n" +
                   "Continue: go on by time.   Next: start the next app now.\n" +
                   "Ignore this: skip the next app.   Exit: close DelayedTaskRunner (the remaining apps will not start).",
        };
        _progress = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        _next = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            Font = new Font(Font, FontStyle.Bold),
        };
        _nextInfo = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6) };
        _last = new Label { AutoSize = true, MaximumSize = new Size(textWidth, 0), Margin = new Padding(0, 0, 0, 10) };

        _btnContinue = MakeButton("&Continue");
        _btnNext = MakeButton("&Next");
        _btnIgnore = MakeButton("&Ignore this");
        _btnExit = MakeButton("E&xit");
        _btnContinue.Click += (_, _) => { if (!JustRefilled) { Result = Choice.Continue; Close(); } };
        _btnExit.Click += (_, _) => { if (!JustRefilled) { Result = Choice.Exit; Close(); } };
        _btnNext.Click += async (_, _) => await RunNextAsync();
        _btnIgnore.Click += (_, _) => Ignore();
        AcceptButton = _btnNext; // Enter = Next, to start apps quickly one after another.

        // Fixed cells (not flow order), so the buttons never change their place.
        var buttons = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, RowCount = 1, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        var row = new[] { _btnContinue, _btnNext, _btnIgnore, _btnExit };
        for (int i = 0; i < row.Length; i++) buttons.Controls.Add(row[i], i, 0);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, MinimumSize = new Size(textWidth, 0) };
        var lines = new Control[] { intro, _progress, _next, _nextInfo, _last, buttons };
        layout.RowCount = lines.Length;
        for (int i = 0; i < lines.Length; i++) layout.Controls.Add(lines[i], 0, i);
        Controls.Add(layout);

        // X button / Esc = Continue (safe). Not while a launch is running.
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && !_busy) Close(); };
        FormClosing += (_, e) => { if (_busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };

        Refill();
    }

    private static Button MakeButton(string text) =>
        new() { Text = text, AutoSize = true, MinimumSize = new Size(100, 32), Margin = new Padding(6, 0, 0, 0) };

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        _btnNext.Focus();
    }

    private void Refill()
    {
        var task = _host.NextTask;
        _progress.Text = $"{_host.Done} of {_host.Total} tasks done (started or ignored).";
        if (task == null)
        {
            _next.Text = "Next: (no more tasks)";
            _nextInfo.Text = "Click Continue or Exit to close DelayedTaskRunner.";
        }
        else
        {
            _next.Text = "Next: " + task.Command;
            _nextInfo.Text = $"Time: {task.TimeText}    Run as: {task.Status}";
        }
        SetBusy(false);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        bool hasNext = _host.NextTask != null;
        _btnContinue.Enabled = !busy;
        _btnExit.Enabled = !busy;
        _btnNext.Enabled = !busy && hasNext;
        _btnIgnore.Enabled = !busy && hasNext;
        UseWaitCursor = busy;
        if (!busy) (hasNext ? _btnNext : _btnContinue).Focus();
    }

    private async Task RunNextAsync()
    {
        var task = _host.NextTask;
        if (task == null || _busy) return;

        SetBusy(true);
        _last.ForeColor = SystemColors.ControlText;
        _last.Text = "Starting: " + task.Command;

        var error = await _host.RunNextAsync();

        _last.ForeColor = error == null ? Color.SeaGreen : Color.Firebrick;
        _last.Text = error == null ? "Started: " + task.Command : "Error: " + error;
        _refilledAt = DateTime.Now;
        Refill();
    }

    private void Ignore()
    {
        var task = _host.NextTask;
        if (task == null || _busy) return;

        _host.SkipNext();
        _last.ForeColor = SystemColors.GrayText;
        _last.Text = "Ignored: " + task.Command;
        _refilledAt = DateTime.Now;
        Refill();
    }
}
