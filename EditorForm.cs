namespace DelayedTaskRunner;

/// <summary>
/// Editor mode (app started with any argument): lists all tasks with a checkbox.
/// Checked = enabled, unchecked = commented out with "#". Right-click > Run starts a task now.
/// Save writes the file and closes; Cancel just closes.
/// </summary>
internal sealed class EditorForm : Form
{
    private readonly ListView _list;
    private readonly TaskFile? _file;

    public EditorForm()
    {
        Text = $"{Program.AppName} - {Paths.TaskFile}";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        ClientSize = new Size(820, 480);
        MinimumSize = new Size(500, 300);
        Font = SystemFonts.MessageBoxFont ?? Font;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            GridLines = false,
            MultiSelect = false,
            HideSelection = false,
            ShowItemToolTips = true,
        };
        _list.Columns.Add("Time", 80);
        _list.Columns.Add("Command", 560);
        _list.Columns.Add("Run as", 90);
        _list.ClientSizeChanged += (_, _) =>
            _list.Columns[1].Width = Math.Max(150, _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[2].Width);

        var runItem = new ToolStripMenuItem("Run");
        runItem.Click += (_, _) => RunSelected();
        var menu = new ContextMenuStrip();
        menu.Items.Add(runItem);
        menu.Opening += (_, e) => e.Cancel = _list.SelectedItems.Count == 0;
        _list.ContextMenuStrip = menu;
        _list.MouseDown += (_, e) =>
        {
            // Right-click selects the item under the mouse first.
            if (e.Button == MouseButtons.Right && _list.GetItemAt(e.X, e.Y) is { } hit)
            {
                _list.SelectedItems.Clear();
                hit.Selected = true;
                hit.Focused = true;
            }
        };

        var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(90, 30) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(90, 30) };
        save.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => Close();
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
            _file = TaskFile.Load(Paths.TaskFile);
            Fill();
        }
        catch (Exception ex)
        {
            save.Enabled = false;
            Load += (_, _) => MessageBox.Show(this, $"Can not read the task file:\n{Paths.TaskFile}\n\n{ex.Message}",
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Fill()
    {
        _list.BeginUpdate();
        foreach (var t in _file!.Tasks)
        {
            var item = new ListViewItem(t.TimeText) { Checked = t.Enabled, Tag = t };
            item.SubItems.Add(t.Command);
            item.SubItems.Add(t.Status.ToString());

            var (file, _) = Launcher.Split(t.Command);
            if (Launcher.Resolve(file) == null)
            {
                item.ForeColor = Color.Firebrick;
                item.ToolTipText = "File not found: " + file;
            }
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    private void RunSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not TaskItem task) return;

        Logger.Write($"Manual run '{task.Command}' ({task.Status})...");
        string? error;
        try { error = Launcher.Run(task); }
        catch (Exception ex) { error = ex.Message; }

        Logger.Write(error == null ? "Done." : "Error: " + error);
        if (error != null)
            MessageBox.Show(this, error, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void SaveAndClose()
    {
        if (_file == null) { Close(); return; }

        foreach (ListViewItem item in _list.Items)
            ((TaskItem)item.Tag!).Enabled = item.Checked;
        try
        {
            _file.Save();
            Logger.Write("Task file saved.");
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Can not save the task file:\n{ex.Message}", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
