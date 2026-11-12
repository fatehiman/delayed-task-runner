using System.Diagnostics;

namespace DelayedTaskRunner;

/// <summary>
/// Closes the apps of the close list: sends WM_CLOSE to all top-level windows of each running process
/// (like "taskkill" without /F), at once. Sends it again after 10 s and 20 s to the processes that are still running.
/// Stops waiting after 30 s. It never kills a process. Every result is written to the log.
/// </summary>
internal static class AppCloser
{
    private const int Rounds = 3;
    private static readonly TimeSpan RoundTime = TimeSpan.FromSeconds(10);

    private sealed class Tracked
    {
        public required Process Process { get; init; }
        public required string Entry { get; init; }
        public required string Name { get; init; }
        public bool Gone { get; set; }
        public bool NoWindowLogged { get; set; }
        public bool DeniedLogged { get; set; }
        public override string ToString() => $"{Name} (PID {Process.Id}, from '{Entry}')";
    }

    /// <summary>
    /// Runs on the UI thread (the slow process scan runs on a background thread).
    /// <paramref name="status"/> gets a short text for the window. Returns the number of apps still running.
    /// </summary>
    public static async Task<int> CloseAllAsync(IReadOnlyList<string> entries, Action<string> status)
    {
        var clock = Stopwatch.StartNew();
        var tracked = new Dictionary<int, Tracked>();
        var maxTime = RoundTime * Rounds;
        Logger.Write($"Close apps: {entries.Count} app(s) in the close list.");

        for (int round = 0; round < Rounds; round++)
        {
            // Scan again every round: an app may have started a new process.
            var found = await Task.Run(() => Find(entries));
            foreach (var t in found)
            {
                if (tracked.ContainsKey(t.Process.Id)) t.Process.Dispose();
                else tracked[t.Process.Id] = t;
            }

            if (round == 0)
            {
                foreach (var e in entries)
                {
                    if (tracked.Values.Any(t => t.Entry == e)) continue;
                    if (!CloseList.IsApp(e)) Logger.Warn($"Close apps: '{e}' is not an exe, nothing to close.");
                    else Logger.Write($"Close apps: '{e}' is not running.");
                }
            }

            var alive = tracked.Values.Where(t => !IsGone(t, clock)).ToList();
            if (alive.Count == 0) break;

            foreach (var t in alive) SendClose(t, round);

            var until = RoundTime * (round + 1);
            while (clock.Elapsed < until)
            {
                int left = tracked.Values.Count(t => !IsGone(t, clock));
                if (left == 0) break;
                status($"Closing apps: {left} left, {Math.Max(0, (int)(maxTime - clock.Elapsed).TotalSeconds)} s...");
                await Task.Delay(250);
            }
            if (tracked.Values.All(t => IsGone(t, clock))) break;
        }

        var notClosed = tracked.Values.Where(t => !IsGone(t, clock)).ToList();
        foreach (var t in notClosed)
            Logger.Warn($"Close apps: NOT closed after {clock.Elapsed.TotalSeconds:0} s: {t} {Native.GetProcessPath(t.Process.Id)}");
        Logger.Write($"Close apps: done in {clock.Elapsed.TotalSeconds:0.0} s. Closed: {tracked.Count - notClosed.Count}, not closed: {notClosed.Count}.");

        foreach (var t in tracked.Values) t.Process.Dispose();
        return notClosed.Count;
    }

    /// <summary>Running processes of the entries (path entry = that exe only; name entry = that exe name in any folder).</summary>
    private static List<Tracked> Find(IReadOnlyList<string> entries)
    {
        var targets = entries
            .Where(CloseList.IsApp)
            .Select(e =>
            {
                var id = CloseList.Identity(e);
                bool byPath = id.Contains('\\') || id.Contains('/');
                var name = Path.GetFileName(id);
                if (name.EndsWith(".exe")) name = name[..^4];
                return (Entry: e, ByPath: byPath, Path: id, Name: name);
            })
            .ToList();

        var result = new List<Tracked>();
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            bool keep = false;
            try
            {
                if (p.Id == self || p.Id <= 4) continue;
                var pname = p.ProcessName.ToLowerInvariant();
                string? path = null;
                foreach (var t in targets)
                {
                    if (t.Name != pname) continue;
                    if (t.ByPath)
                    {
                        path ??= Native.GetProcessPath(p.Id)?.ToLowerInvariant() ?? "";
                        if (path != t.Path) continue;
                    }
                    result.Add(new Tracked { Process = p, Entry = t.Entry, Name = p.ProcessName + ".exe" });
                    keep = true;
                    break;
                }
            }
            catch (Exception ex) { Logger.Warn($"Close apps: can not check process {p.Id}: {ex.Message}"); }
            finally { if (!keep) p.Dispose(); }
        }
        return result;
    }

    private static void SendClose(Tracked t, int round)
    {
        var windows = Native.GetTopLevelWindows(t.Process.Id);
        if (windows.Count == 0)
        {
            if (!t.NoWindowLogged) Logger.Warn($"Close apps: {t} has no window, can not send WM_CLOSE.");
            t.NoWindowLogged = true;
            return;
        }

        int sent = 0;
        string? error = null;
        foreach (var w in windows)
        {
            var err = Native.PostClose(w);
            if (err == null) sent++;
            else error = err;
        }

        if (sent > 0)
            Logger.Write($"Close apps: {(round == 0 ? "sent" : $"retry {round}: sent")} WM_CLOSE to {t} ({sent} window(s)).");
        else if (!t.DeniedLogged)
        {
            // Usually "Access is denied": the app runs as administrator and this app does not.
            Logger.Error($"Close apps: can not send WM_CLOSE to {t}: {error}");
            t.DeniedLogged = true;
        }
    }

    private static bool IsGone(Tracked t, Stopwatch clock)
    {
        if (t.Gone) return true;
        bool gone;
        try { gone = t.Process.HasExited; }
        catch
        {
            // No access to the process handle: check if the PID still exists.
            try { using var p = Process.GetProcessById(t.Process.Id); gone = false; }
            catch (ArgumentException) { gone = true; }
        }
        if (gone)
        {
            t.Gone = true;
            Logger.Write($"Close apps: closed {t} after {clock.Elapsed.TotalSeconds:0.0} s.");
        }
        return gone;
    }
}
