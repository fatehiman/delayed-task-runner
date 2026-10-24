using System.Text;
using System.Text.RegularExpressions;

namespace DelayedTaskRunner;

/// <summary>How the started program window is shown (the optional 3rd field).</summary>
internal enum RunStatus { Normal = 0, Minimized = 1, Hidden = 2 }

/// <summary>One task line: hh:mm:ss,task[,status]. A "#" in front = disabled.</summary>
internal sealed class TaskItem
{
    public int LineIndex { get; init; }
    public TimeSpan Time { get; init; }
    public string Command { get; init; } = "";
    public RunStatus Status { get; init; }
    public bool Enabled { get; set; }

    /// <summary>The line text without the leading "#" (kept as it was in the file).</summary>
    public string Body { get; init; } = "";

    public string TimeText => Time.ToString(@"hh\:mm\:ss");
}

/// <summary>
/// Reads and writes DelayedTaskRunner.txt. Format (same as the old VB6 app):
///   #comment
///   hh:mm:ss,task[,status]     status: 0=normal 1=minimized 2=hidden
/// Saving changes only the "#" in front of task lines. All other lines stay the same.
/// </summary>
internal sealed class TaskFile
{
    private static readonly Regex TaskLine = new(@"^\s*(\d{1,2}):(\d{1,2}):(\d{1,2})\s*,(.*)$", RegexOptions.Compiled);
    private static readonly Regex StatusTail = new(@"^(.*\S)\s*,\s*([0-9])\s*$", RegexOptions.Compiled);

    public string Path { get; }
    public List<string> Lines { get; } = new();
    public List<TaskItem> Tasks { get; } = new();

    private TaskFile(string path) => Path = path;

    public static TaskFile Load(string path)
    {
        var tf = new TaskFile(path);
        var text = File.ReadAllText(path, Encoding.UTF8);
        if (text.EndsWith("\r\n")) text = text[..^2];
        else if (text.EndsWith("\n")) text = text[..^1];
        tf.Lines.AddRange(text.Split('\n').Select(l => l.TrimEnd('\r')));

        for (int i = 0; i < tf.Lines.Count; i++)
        {
            var line = tf.Lines[i];
            var trimmed = line.TrimStart();
            bool enabled = !trimmed.StartsWith('#');
            var body = enabled ? line : trimmed.TrimStart('#');

            var item = Parse(body, i, enabled);
            if (item != null) tf.Tasks.Add(item);
        }
        return tf;
    }

    private static TaskItem? Parse(string body, int index, bool enabled)
    {
        var m = TaskLine.Match(body);
        if (!m.Success) return null;

        var time = new TimeSpan(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        var rest = m.Groups[4].Value.Trim();
        var status = RunStatus.Normal;

        var s = StatusTail.Match(rest);
        if (s.Success)
        {
            rest = s.Groups[1].Value.Trim();
            int n = int.Parse(s.Groups[2].Value);
            status = n is >= 0 and <= 2 ? (RunStatus)n : RunStatus.Normal;
        }
        if (rest.Length == 0) return null;

        return new TaskItem { LineIndex = index, Time = time, Command = rest, Status = status, Enabled = enabled, Body = body };
    }

    /// <summary>Writes the file back. Only the enabled/disabled state of task lines changes. CRLF, no BOM.</summary>
    public void Save()
    {
        var lines = new List<string>(Lines);
        foreach (var t in Tasks)
        {
            var original = Lines[t.LineIndex];
            bool wasEnabled = !original.TrimStart().StartsWith('#');
            if (wasEnabled == t.Enabled) continue;
            lines[t.LineIndex] = t.Enabled ? t.Body : "#" + t.Body;
        }

        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l).Append("\r\n");
        File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
    }
}
