namespace DelayedTaskRunner;

/// <summary>
/// Modal "Close list" window: all apps of the task list (action close), then the add-on lines of close-apps.conf
/// (close or run), with a checkbox.
/// close-apps.conf is read again every time this window opens. Save writes it and closes; Cancel just closes.
/// </summary>
internal sealed class CloseListForm : Form
{
    private readonly ListView _list;
    private readonly List<string> _comments = new();
    private readonly List<CloseEntry> _entries = new();

    public CloseListForm(IEnumerable<(string Command, int ImageIndex)> tasks, ImageList icons)
    {
        Text = "Close list - " + Paths.CloseAppsFile;
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(700, 440);
        MinimumSize = new Size(400, 250);
        Font = SystemFonts.MessageBoxFont ?? Font;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            ShowItemToolTips = true,
            SmallImageList = icons,
        };
        _list.Columns.Add("Action", 70);
        _list.Columns.Add("App / command", 450);
        _list.Columns.Add("From", 130);
        _list.ClientSizeChanged += (_, _) =>
            _list.Columns[1].Width = Math.Max(150, _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[2].Width);

        var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(90, 30) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(90, 30), DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => SaveAndClose();
        AcceptButton = save;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        Controls.Add(_list);
        Controls.Add(buttons);

        try
        {
            var (comments, conf) = CloseList.Read(Paths.CloseAppsFile);
            _comments.AddRange(comments);
            _entries.AddRange(CloseList.Build(tasks, conf));
            Fill();
        }
        catch (Exception ex)
        {
            // Do not allow Save: it would overwrite a file we could not read.
            save.Enabled = false;
            Logger.Error($"Can not read {Paths.CloseAppsFile}: {ex.Message}");
            Load += (_, _) => MessageBox.Show(this, $"Can not read the close list:\n{Paths.CloseAppsFile}\n\n{ex.Message}",
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Fill()
    {
        _list.BeginUpdate();
        foreach (var e in _entries)
        {
            var item = new ListViewItem(CloseList.ActionName(e.Action)) { Checked = e.Checked, Tag = e, ImageIndex = e.ImageIndex };
            item.SubItems.Add(e.Details);
            item.SubItems.Add(e.InTaskList ? "Task list" : "close-apps.conf");
            if (e.Action == CloseAction.Close && !CloseList.IsApp(e.Details))
            {
                item.ForeColor = SystemColors.GrayText;
                item.ToolTipText = "Not an exe: there is no app to close for this file.";
            }
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    private void SaveAndClose()
    {
        foreach (ListViewItem item in _list.Items)
            ((CloseEntry)item.Tag!).Checked = item.Checked;
        try
        {
            CloseList.Save(Paths.CloseAppsFile, _comments, _entries);
            Logger.Write($"Close list saved ({_entries.Count(e => e.Checked)} checked).");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error($"Can not save {Paths.CloseAppsFile}: {ex.Message}");
            MessageBox.Show(this, $"Can not save the close list:\n{ex.Message}", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
