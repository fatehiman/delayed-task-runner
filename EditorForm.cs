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
    private readonly ImageList _icons;
    private readonly Button _fetch;
    private readonly Label _fetchStatus;

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
        var folderItem = new ToolStripMenuItem("Open folder");
        folderItem.Click += (_, _) => OpenFolderOfSelected();
        var menu = new ContextMenuStrip();
        menu.Items.Add(runItem);
        menu.Items.Add(folderItem);
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

        _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = SystemInformation.SmallIconSize };
        _list.SmallImageList = _icons;

        var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(90, 30) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(90, 30) };
        _fetch = new Button { Text = "Fetch icons", AutoSize = true, MinimumSize = new Size(90, 30) };
        _fetchStatus = new Label { AutoSize = true, Margin = new Padding(12, 8, 3, 0) };
        save.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => Close();
        _fetch.Click += (_, _) => FetchIcons();
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
        buttons.Controls.Add(_fetch);
        buttons.Controls.Add(_fetchStatus);

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

            // Saved icon from an earlier "Fetch icons" (no check if the file changed).
            var key = IconCache.KeyFor(t.Command);
            if (!_icons.Images.ContainsKey(key) && IconCache.Load(key) is { } saved)
            {
                _icons.Images.Add(key, saved);
                saved.Dispose();
            }
            if (_icons.Images.ContainsKey(key))
                item.ImageIndex = _icons.Images.IndexOfKey(key);
        }
        _list.EndUpdate();
    }

    /// <summary>
    /// Reads the icon of every task's file (checked and unchecked), shows it at the left of the item and
    /// saves it in the icon cache (see <see cref="IconCache"/>). The slow part (disk / network access, saving)
    /// runs on a background thread. Files that are not found get no new icon (a saved one stays).
    /// </summary>
    private async void FetchIcons()
    {
        var work = _list.Items.Cast<ListViewItem>().Select(i => (Item: i, Command: ((TaskItem)i.Tag!).Command)).ToList();
        _fetch.Enabled = false;
        _fetchStatus.Text = "Fetching icons...";

        var found = await Task.Run(() =>
        {
            var byPath = new Dictionary<string, Bitmap?>(StringComparer.OrdinalIgnoreCase);
            var result = new List<(ListViewItem Item, string Key, Bitmap Icon)>();
            foreach (var (item, command) in work)
            {
                var full = Launcher.Resolve(Launcher.Split(command).File);
                if (full == null) continue;
                if (!byPath.TryGetValue(full, out var icon))
                {
                    try { icon = Native.GetSmallIcon(full); }
                    catch { icon = null; }
                    byPath[full] = icon;
                }
                if (icon == null) continue;

                var key = IconCache.KeyFor(command);
                IconCache.Save(key, icon);
                result.Add((item, key, icon));
            }
            return result;
        });

        if (IsDisposed) return;
        _list.BeginUpdate();
        foreach (var (item, key, icon) in found)
        {
            // Never remove an image: that moves the index of the images after it, and the items would show the wrong icons.
            // A new fetch replaces the old image at the same index.
            int index = _icons.Images.IndexOfKey(key);
            if (index < 0)
            {
                _icons.Images.Add(key, icon);
                index = _icons.Images.IndexOfKey(key);
            }
            else
            {
                _icons.Images[index] = icon;
                _icons.Images.SetKeyName(index, key);
            }
            item.ImageIndex = index;
        }
        _list.EndUpdate();
        foreach (var icon in found.Select(f => f.Icon).Distinct()) icon.Dispose();

        _fetchStatus.Text = $"Icons: {found.Count} of {work.Count}";
        _fetch.Enabled = true;
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

    /// <summary>Opens Explorer with the task's file selected (or the nearest existing folder if the file is missing).</summary>
    private void OpenFolderOfSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not TaskItem task) return;

        var (file, _) = Launcher.Split(task.Command);
        var full = Launcher.Resolve(file);
        string? error;
        if (full != null)
        {
            error = Native.ShellExecute("explorer.exe", $"/select,\"{full}\"", null, 1);
        }
        else
        {
            var dir = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(file));
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
            error = string.IsNullOrEmpty(dir)
                ? $"Folder not found: {file}"
                : Native.ShellExecute("explorer.exe", $"\"{dir}\"", null, 1);
        }

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
