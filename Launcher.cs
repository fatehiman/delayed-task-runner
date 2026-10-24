namespace DelayedTaskRunner;

/// <summary>Starts one task command with ShellExecuteEx (works for .exe, .bat, .cmd, documents...).</summary>
internal static class Launcher
{
    /// <summary>Splits "path args" into file and arguments.</summary>
    public static (string File, string Args) Split(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end < 0) return (command.Trim('"'), "");
            return (command[1..end], command[(end + 1)..].Trim());
        }

        // No quotes: a path with spaces may still be written without quotes.
        // Use the shortest prefix (cut at a space) that is an existing file.
        for (int i = command.IndexOf(' '); i > 0; i = command.IndexOf(' ', i + 1))
        {
            var prefix = command[..i];
            if (System.IO.File.Exists(prefix)) return (prefix, command[(i + 1)..].Trim());
        }
        if (System.IO.File.Exists(command)) return (command, "");

        int sp = command.IndexOf(' ');
        return sp < 0 ? (command, "") : (command[..sp], command[(sp + 1)..].Trim());
    }

    /// <summary>Full path of the file, or null when it does not exist (also looks in PATH for bare names).</summary>
    public static string? Resolve(string file)
    {
        file = Environment.ExpandEnvironmentVariables(file);
        if (Path.IsPathRooted(file) || file.Contains('\\') || file.Contains('/'))
            return File.Exists(file) ? Path.GetFullPath(file) : null;

        var exts = Path.HasExtension(file)
            ? new[] { "" }
            : new[] { "" }.Concat((Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD").Split(';')).ToArray();
        var dirs = new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var dir in dirs)
            foreach (var ext in exts)
            {
                try
                {
                    var p = Path.Combine(dir.Trim(), file + ext);
                    if (File.Exists(p)) return p;
                }
                catch { /* bad PATH entry */ }
            }
        return null;
    }

    /// <summary>Starts the task. Returns null on success, or an error text.</summary>
    public static string? Run(TaskItem task)
    {
        var (file, args) = Split(task.Command);
        var full = Resolve(file);
        if (full == null) return $"File not found: {file}";

        // Same "no focus" style as the old VB6 app (vbNormalNoFocus / vbMinimizedNoFocus).
        int show = task.Status switch
        {
            RunStatus.Minimized => Native.SW_SHOWMINNOACTIVE,
            RunStatus.Hidden => Native.SW_HIDE,
            _ => Native.SW_SHOWNOACTIVATE,
        };

        var err = Native.ShellExecute(full, args, Path.GetDirectoryName(full), show);
        return err == null ? null : $"Can not start {file}: {err}";
    }
}
