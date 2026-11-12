using System.Text;
using System.Text.RegularExpressions;

namespace DelayedTaskRunner;

/// <summary>What a close-apps.conf line does.</summary>
internal enum CloseAction
{
    /// <summary>Send WM_CLOSE to a running exe.</summary>
    Close,

    /// <summary>Start a command (exe, bat...), for example a script that shuts down VMs.</summary>
    Run,

    /// <summary>Like Close, but an app that is still running at 20 s is killed.</summary>
    Kill,
}

/// <summary>One row of the close list window.</summary>
internal sealed class CloseEntry
{
    /// <summary>Close or Kill can be changed in the window (right-click). Run lines stay Run.</summary>
    public CloseAction Action { get; set; }

    /// <summary>
    /// Close: the exe as written (task's file part, or the conf text). Run: the command with arguments
    /// (and the optional ",status" at the end), as written in the conf file.
    /// </summary>
    public string Details { get; init; } = "";

    /// <summary>True = the app is in the task list. False = an add-on line (only in close-apps.conf).</summary>
    public bool InTaskList { get; init; }

    public bool Checked { get; set; }

    /// <summary>Image index of the task in the editor's icon list (-1 = none).</summary>
    public int ImageIndex { get; init; } = -1;
}

/// <summary>
/// close-apps.conf (next to the exe): what "Close apps" does. One "action,details" line each:
///   ; comment
///   close,E:\path\app.exe             WM_CLOSE to that exe (full path: only this exe)
///   close,chrome.exe                  WM_CLOSE to that exe name in any folder
///   kill,E:\path\tray.exe              like close, but killed at 20 s if it is still running
///   run,"E:\scripts\stop vms.bat" x,2   start a command (optional ",status": 0 normal, 1 minimized, 2 hidden)
///   #close,E:\path\other.exe          unchecked add-on line (kept in the file, not done)
/// A line without a known action ("E:\path\app.exe", the first format) means close.
/// Task-list apps are in the file only when checked. Add-on lines stay in the file also when unchecked.
/// </summary>
internal static class CloseList
{
    private static readonly string[] DefaultHeader =
    {
        "; DelayedTaskRunner close list (used by the \"Close apps\" button of the editor). One \"action,details\" per line:",
        ";   close,E:\\path\\app.exe       send WM_CLOSE to that exe. Only a name (close,chrome.exe) = that exe in any folder.",
        ";   kill,E:\\path\\app.exe        like close, but the app is killed if it is still running after 20 s.",
        ";   run,\"E:\\scripts\\my script.bat\" arg1 arg2[,status]   run a command (status: 0=normal 1=minimized 2=hidden).",
        "; \"#\" in front = unchecked (kept in the list, but not done). Lines that start with \";\" are comments.",
    };

    /// <summary>Header lines that this app wrote before. They are replaced by the current header on save.</summary>
    private static readonly HashSet<string> OldHeaders = new(DefaultHeader)
    {
        "; DelayedTaskRunner close list (used by the \"Close apps\" button of the editor). One \"action,details\" per line:",
        ";   close,E:\\path\\app.exe       send WM_CLOSE to that exe. Only a name (close,chrome.exe) = that exe in any folder.",
        ";   run,\"E:\\scripts\\my script.bat\" arg1 arg2[,status]   run a command (status: 0=normal 1=minimized 2=hidden).",
        "; \"#\" in front = unchecked (kept in the list, but not done). Lines that start with \";\" are comments.",
        "; DelayedTaskRunner close list (used by the \"Close apps\" button of the editor).",
        "; One app per line: a full exe path (only that exe), or only an exe name like chrome.exe (that exe in any folder).",
        "; \"#\" in front = unchecked (kept in the list, but not closed). Lines that start with \";\" are comments.",
    };

    private static readonly Regex StatusTail = new(@"^(.*\S)\s*,\s*([0-2])\s*$", RegexOptions.Compiled);

    public sealed record ConfLine(CloseAction Action, string Details, bool Checked);

    /// <summary>Reads the file (always from disk, no cache). A missing file = empty list.</summary>
    public static (List<string> Comments, List<ConfLine> Lines) Read(string path)
    {
        var comments = new List<string>();
        var lines = new List<ConfLine>();
        if (!File.Exists(path)) return (comments, lines);

        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith(';')) { comments.Add(raw.TrimEnd()); continue; }
            bool on = !t.StartsWith('#');
            var (action, details) = ParseLine(t.TrimStart('#').Trim());
            if (details.Length > 0) lines.Add(new ConfLine(action, details, on));
        }
        return (comments, lines);
    }

    private static (CloseAction, string) ParseLine(string text)
    {
        int comma = text.IndexOf(',');
        if (comma > 0)
        {
            var word = text[..comma].Trim();
            var rest = text[(comma + 1)..].Trim();
            if (word.Equals("close", StringComparison.OrdinalIgnoreCase)) return (CloseAction.Close, rest.Trim('"').Trim());
            if (word.Equals("run", StringComparison.OrdinalIgnoreCase)) return (CloseAction.Run, rest);
            if (word.Equals("kill", StringComparison.OrdinalIgnoreCase)) return (CloseAction.Kill, rest.Trim('"').Trim());
        }
        return (CloseAction.Close, text.Trim('"').Trim()); // first format: only the exe
    }

    /// <summary>Splits run details into the command and the optional ",status" at the end.</summary>
    public static (string Command, RunStatus Status) SplitRun(string details)
    {
        var m = StatusTail.Match(details.Trim());
        return m.Success
            ? (m.Groups[1].Value.Trim(), (RunStatus)int.Parse(m.Groups[2].Value))
            : (details.Trim(), RunStatus.Normal);
    }

    /// <summary>
    /// Rows for the close list window: first the apps of the task list (each file once, in task order, action close),
    /// then the add-on lines of close-apps.conf (in file order).
    /// </summary>
    public static List<CloseEntry> Build(IEnumerable<(string Command, int ImageIndex)> tasks, List<ConfLine> conf)
    {
        var result = new List<CloseEntry>();
        var seen = new HashSet<string>();

        foreach (var (command, imageIndex) in tasks)
        {
            var file = Launcher.Split(command).File.Trim().Trim('"');
            var id = Identity(file);
            if (file.Length == 0 || !seen.Add(Key(CloseAction.Close, file))) continue;
            var match = conf.FirstOrDefault(c => c.Checked && c.Action != CloseAction.Run && Identity(c.Details) == id);
            result.Add(new CloseEntry
            {
                Action = match?.Action ?? CloseAction.Close,
                Details = file,
                InTaskList = true,
                Checked = match != null,
                ImageIndex = imageIndex,
            });
        }

        foreach (var c in conf)
        {
            if (!seen.Add(Key(c.Action, c.Details))) continue; // in the task list, or a duplicate line
            result.Add(new CloseEntry { Action = c.Action, Details = c.Details, InTaskList = false, Checked = c.Checked });
        }
        return result;
    }

    private static string Key(CloseAction action, string details) =>
        action != CloseAction.Run ? "close|" + Identity(details) : "run|" + details.Trim().ToLowerInvariant();

    /// <summary>
    /// Writes the file: header, the user's own comments, checked task-list apps, then all add-on lines
    /// ("#" = unchecked). CRLF, no BOM.
    /// </summary>
    public static void Save(string path, List<string> comments, IEnumerable<CloseEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var l in DefaultHeader) sb.Append(l).Append("\r\n");
        foreach (var l in comments.Where(c => !OldHeaders.Contains(c.Trim()))) sb.Append(l).Append("\r\n");
        foreach (var e in entries)
        {
            if (e.InTaskList && !e.Checked) continue; // unchecked task-list apps are not kept
            sb.Append(e.Checked ? "" : "#").Append(ActionName(e.Action)).Append(',').Append(e.Details).Append("\r\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    public static string ActionName(CloseAction action) => action switch
    {
        CloseAction.Run => "run",
        CloseAction.Kill => "kill",
        _ => "close",
    };

    /// <summary>
    /// Compare key of an exe text: a path (has a folder) = its full path; a bare name = the name.
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
