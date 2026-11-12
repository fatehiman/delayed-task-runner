using System.Text;

namespace DelayedTaskRunner;

/// <summary>One row of the close list window.</summary>
internal sealed class CloseEntry
{
    /// <summary>The app's file as written (task's file part, or the close-apps.conf line without "#").</summary>
    public string File { get; init; } = "";

    /// <summary>True = the app is in the task list. False = an add-on app (only in close-apps.conf).</summary>
    public bool InTaskList { get; init; }

    public bool Checked { get; set; }

    /// <summary>Image index of the task in the editor's icon list (-1 = none).</summary>
    public int ImageIndex { get; init; } = -1;
}

/// <summary>
/// close-apps.conf (next to the exe): the apps that "Close apps" closes.
///   ; comment
///   E:\path\app.exe      checked (full path: only this exe)
///   chrome.exe           checked (only a name: this exe in any folder)
///   #E:\path\other.exe   unchecked add-on app (kept in the file, not closed)
/// Apps of the task list are in the file only when checked. Add-on apps stay in the file also when unchecked.
/// </summary>
internal static class CloseList
{
    private static readonly string[] DefaultHeader =
    {
        "; DelayedTaskRunner close list (used by the \"Close apps\" button of the editor).",
        "; One app per line: a full exe path (only that exe), or only an exe name like chrome.exe (that exe in any folder).",
        "; \"#\" in front = unchecked (kept in the list, but not closed). Lines that start with \";\" are comments.",
    };

    public sealed record ConfLine(string File, bool Checked);

    /// <summary>Reads the file (always from disk, no cache). A missing file = empty list.</summary>
    public static (List<string> Comments, List<ConfLine> Apps) Read(string path)
    {
        var comments = new List<string>();
        var apps = new List<ConfLine>();
        if (!System.IO.File.Exists(path)) return (comments, apps);

        foreach (var raw in System.IO.File.ReadAllLines(path, Encoding.UTF8))
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith(';')) { comments.Add(raw.TrimEnd()); continue; }
            bool on = !t.StartsWith('#');
            var file = t.TrimStart('#').Trim().Trim('"').Trim();
            if (file.Length > 0) apps.Add(new ConfLine(file, on));
        }
        return (comments, apps);
    }

    /// <summary>
    /// Rows for the close list window: first the apps of the task list (each file once, in task order),
    /// then the add-on apps of close-apps.conf (in file order).
    /// </summary>
    public static List<CloseEntry> Build(IEnumerable<(string Command, int ImageIndex)> tasks, List<ConfLine> conf)
    {
        var result = new List<CloseEntry>();
        var seen = new HashSet<string>();

        foreach (var (command, imageIndex) in tasks)
        {
            var file = Launcher.Split(command).File.Trim().Trim('"');
            var id = Identity(file);
            if (file.Length == 0 || !seen.Add(id)) continue;
            result.Add(new CloseEntry
            {
                File = file,
                InTaskList = true,
                Checked = conf.Any(c => c.Checked && Identity(c.File) == id),
                ImageIndex = imageIndex,
            });
        }

        foreach (var c in conf)
        {
            if (!seen.Add(Identity(c.File))) continue; // in the task list, or a duplicate line
            result.Add(new CloseEntry { File = c.File, InTaskList = false, Checked = c.Checked });
        }
        return result;
    }

    /// <summary>Writes the file: comments, checked task-list apps, then all add-on apps ("#" = unchecked). CRLF, no BOM.</summary>
    public static void Save(string path, List<string> comments, IEnumerable<CloseEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var l in comments.Count > 0 ? comments : DefaultHeader.ToList()) sb.Append(l).Append("\r\n");
        foreach (var e in entries)
        {
            if (e.InTaskList && !e.Checked) continue; // unchecked task-list apps are not kept
            sb.Append(e.Checked ? "" : "#").Append(e.File).Append("\r\n");
        }
        System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// Compare key of a file text: a path (has a folder) = its full path; a bare name = the name.
    /// Lower case, environment variables expanded. No disk access.
    /// </summary>
    public static string Identity(string file)
    {
        var f = Environment.ExpandEnvironmentVariables(file.Trim().Trim('"')).Trim();
        if (f.Contains('\\') || f.Contains('/'))
        {
            try { f = Path.GetFullPath(f); } catch { /* keep the text */ }
        }
        return f.ToLowerInvariant();
    }

    /// <summary>True when the file can be a running app (an .exe, or a name without an extension).</summary>
    public static bool IsApp(string file)
    {
        var ext = Path.GetExtension(file.Trim().Trim('"'));
        return ext.Length == 0 || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }
}
