using System.Diagnostics;

namespace DelayedTaskRunner;

/// <summary>
/// Does the checked lines of the close list, all at once:
/// - run: starts the command one time (no retry) and waits for it to end.
/// - close: 0 s: WM_CLOSE to all top-level windows of each running process (like "taskkill" without /F).
///   10 s and 20 s: WM_CLOSE again plus the Windows shutdown messages (WM_QUERYENDSESSION / WM_ENDSESSION).
/// - kill: the same as close, but at 20 s a process that is still running is killed.
/// Stops waiting after 30 s. Only kill lines are killed (a run command that is still busy keeps running).
/// Every result is written to the log.
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
        public bool Kill { get; init; }
        public bool Gone { get; set; }
        public bool NoWindowLogged { get; set; }
        public bool DeniedLogged { get; set; }
        public override string ToString() => $"{Name} (PID {Process.Id}, from '{Entry}')";
    }

    /// <summary>A started run command.</summary>
    private sealed class Started
    {
        public required string Command { get; init; }
        public IntPtr Handle { get; set; }
        public bool Done => Handle == IntPtr.Zero;
    }

    /// <summary>
    /// Runs on the UI thread (starting commands and the slow process scan run on background threads).
    /// <paramref name="status"/> gets a short text for the window. Returns the number of apps / commands still running.
    /// </summary>
    public static async Task<int> CloseAllAsync(IReadOnlyList<CloseList.ConfLine> lines, Action<string> status)
    {
        var clock = Stopwatch.StartNew();
        var tracked = new Dictionary<int, Tracked>();
        var maxTime = RoundTime * Rounds;
        var entries = lines.Where(l => l.Action != CloseAction.Run).ToList();
        var commands = lines.Where(l => l.Action == CloseAction.Run).Select(l => l.Details).ToList();
        Logger.Write($"Close apps: {entries.Count} app(s) to close ({entries.Count(e => e.Action == CloseAction.Kill)} with kill), {commands.Count} command(s) to run.");

        // Run commands: start them all (in parallel, each on its own STA thread), one time only.
        var starts = commands.Select(async details =>
        {
            var (command, runStatus) = CloseList.SplitRun(details);
            var (error, handle) = await Launcher.StartAsync(command, runStatus);
            if (error != null) Logger.Error($"Close apps: run '{details}': {error}");
            else Logger.Write($"Close apps: started '{details}'" + (handle == IntPtr.Zero ? " (can not wait for it)." : "."));
            return new Started { Command = details, Handle = handle };
        }).ToList();
        var started = (await Task.WhenAll(starts)).ToList();
        int RunsLeft() => started.Count(r => !IsDone(r, clock));

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
                foreach (var e in entries.Select(l => l.Details))
                {
                    if (tracked.Values.Any(t => t.Entry == e)) continue;
                    if (!CloseList.IsApp(e)) Logger.Warn($"Close apps: '{e}' is not an exe, nothing to close.");
                    else Logger.Write($"Close apps: '{e}' is not running.");
                }
            }

            var alive = tracked.Values.Where(t => !IsGone(t, clock)).ToList();
            if (alive.Count == 0 && RunsLeft() == 0) break;

            // In parallel: the shutdown messages wait up to 2 s for each window.
            int r = round;
            await Task.WhenAll(alive.Select(t => Task.Run(() => SendClose(t, r))));

            var until = RoundTime * (round + 1);
            while (clock.Elapsed < until)
            {
                int apps = tracked.Values.Count(t => !IsGone(t, clock)), runs = RunsLeft();
                if (apps + runs == 0) break;
                status($"Closing: {apps} app(s), {runs} command(s) left, {Math.Max(0, (int)(maxTime - clock.Elapsed).TotalSeconds)} s...");
                await Task.Delay(250);
            }
            if (tracked.Values.All(t => IsGone(t, clock)) && RunsLeft() == 0) break;
        }

        foreach (var r in started.Where(r => !r.Done))
        {
            Logger.Warn($"Close apps: command still running after {clock.Elapsed.TotalSeconds:0} s (left running): '{r.Command}'");
            Native.CloseProcessHandle(r.Handle);
        }
        int runsNotDone = started.Count(r => !r.Done);

        var notClosed = tracked.Values.Where(t => !IsGone(t, clock)).ToList();
        foreach (var t in notClosed)
            Logger.Warn($"Close apps: NOT closed after {clock.Elapsed.TotalSeconds:0} s: {t} {Native.GetProcessPath(t.Process.Id)}");
        Logger.Write($"Close apps: done in {clock.Elapsed.TotalSeconds:0.0} s. Closed: {tracked.Count - notClosed.Count}, not closed: {notClosed.Count}, commands still running: {runsNotDone}.");

        foreach (var t in tracked.Values) t.Process.Dispose();
        return notClosed.Count + runsNotDone;
    }

    /// <summary>True when the run command has ended (logs its exit code one time).</summary>
    private static bool IsDone(Started r, Stopwatch clock)
    {
        if (r.Done) return true;
        if (!Native.HasProcessEnded(r.Handle, out uint code)) return false;
        Native.CloseProcessHandle(r.Handle);
        r.Handle = IntPtr.Zero;
        if (code == 0) Logger.Write($"Close apps: command ended after {clock.Elapsed.TotalSeconds:0.0} s: '{r.Command}'");
        else Logger.Warn($"Close apps: command ended with exit code {code} after {clock.Elapsed.TotalSeconds:0.0} s: '{r.Command}'");
        return true;
    }

    /// <summary>Running processes of the entries (path entry = that exe only; name entry = that exe name in any folder).</summary>
    private static List<Tracked> Find(IReadOnlyList<CloseList.ConfLine> entries)
    {
        var targets = entries
            .Where(l => CloseList.IsApp(l.Details))
            .Select(l =>
            {
                var e = l.Details;
                bool kill = l.Action == CloseAction.Kill;
                var id = CloseList.Identity(e);
                bool byPath = id.Contains('\\') || id.Contains('/');
                var name = Path.GetFileName(id);
                if (name.EndsWith(".exe")) name = name[..^4];
                return (Entry: e, Kill: kill, ByPath: byPath, Path: id, Name: name);
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
                    result.Add(new Tracked { Process = p, Entry = t.Entry, Kill = t.Kill, Name = p.ProcessName + ".exe" });
                    keep = true;
                    break;
                }
            }
            catch (Exception ex) { Logger.Warn($"Close apps: can not check process {p.Id}: {ex.Message}"); }
            finally { if (!keep) p.Dispose(); }
        }
        return result;
    }

    /// <summary>Round 0: WM_CLOSE. Round 1+: WM_CLOSE + shutdown messages. Last round of a kill line: kill.</summary>
    private static void SendClose(Tracked t, int round)
    {
        if (t.Kill && round == Rounds - 1)
        {
            try
            {
                t.Process.Kill();
                Logger.Warn($"Close apps: killed {t} (still running after {RoundTime.TotalSeconds * round:0} s).");
            }
            catch (Exception ex) { Logger.Error($"Close apps: can not kill {t}: {ex.Message}"); }
            return;
        }

        var windows = Native.GetTopLevelWindows(t.Process.Id);
        if (windows.Count == 0)
        {
            if (!t.NoWindowLogged)
                Logger.Warn($"Close apps: {t} has no window, can not send WM_CLOSE{(t.Kill ? " (it will be killed at 20 s)" : "")}.");
            t.NoWindowLogged = true;
            return;
        }

        int sent = 0;
        string? error = null;
        foreach (var w in windows)
        {
            var err = Native.PostClose(w);
            if (err == null && round > 0) err = Native.SendEndSession(w);
            if (err == null) sent++;
            else error = err;
        }

        var what = round == 0 ? "WM_CLOSE" : "WM_CLOSE + shutdown messages";
        if (sent > 0)
            Logger.Write($"Close apps: {(round == 0 ? "sent" : $"retry {round}: sent")} {what} to {t} ({sent} window(s)).");
        else if (!t.DeniedLogged)
        {
            // Usually "Access is denied": the app runs as administrator and this app does not.
            Logger.Error($"Close apps: can not send {what} to {t}: {error}");
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
