# CLAUDE.md

C# .NET 8 WinForms rewrite of an old VB6 tray app. See [README.md](README.md) for behavior and file format.

## Files

- `Program.cs` – single-instance mutex, picks the mode (no args = runner, any arg = editor), paths.
- `TrayRunner.cs` – runner mode: tray icon, menu, task clock (pausable `Stopwatch`), opens `PauseForm` on left click, hidden window for clean `WM_CLOSE`.
- `PauseForm.cs` – pause window (manual mode): Continue / Next / Ignore this / Exit. Talks to the runner via `IPauseHost`.
  Next launches on a background STA thread (`Launcher.RunAsync`) and waits only for the launch.
- `EditorForm.cs` – editor mode: `ListView` with checkboxes, right-click Run / Open folder, Fetch icons (shell icons via `Native.GetSmallIcon`, read on a background thread), Save/Cancel.
- `CloseList.cs` – read/save `close-apps.conf` (`close,<exe>` / `run,<command>[,status]`; a bare path or old `kill,` = close) and build the Close list rows (task-list files first, then add-on lines).
- `CloseListForm.cs` – modal Close list window (checkboxes, Save/Cancel). Reads the conf file again every time.
- `AppCloser.cs` – Close apps: `run` lines start once (`Launcher.StartAsync`, waits for the process handle); `close` lines get `WM_CLOSE` to all top-level windows of matching processes, retry at 10 s with `WM_CLOSE` + `WM_QUERYENDSESSION`/`WM_ENDSESSION` (`Native.SendEndSession`); at 20 s (last try) every remaining process is killed. Stops at 30 s. No confirm dialog.
- `IconCache.cs` – saved icons as PNG in `%LOCALAPPDATA%\DelayedTaskRunner\icons`, key = SHA1 of the task's file text. Loaded when the editor opens, never re-checked; only Fetch icons replaces them.
- `TaskFile.cs` – parse/save `DelayedTaskRunner.txt`. Save only changes the `#` of changed task lines; CRLF, no BOM.
- `Launcher.cs` – splits command into file + args, resolves it (also via PATH), starts it with `ShellExecuteEx` (no-focus show modes).
- `AutoStart.cs` – HKCU Run key. `Logger.cs` – small log, rotated at 1 MB (`Warn`/`Error` add a `WARNING:`/`ERROR:` prefix).

## Rules

- **Never run the installed `E:\appServices\DelayedTaskRunnerHome\DelayedTaskRunner.exe` without arguments to test.**
  It starts every task of the real file (VMs, mounts, many apps). Test with a copy of the exe in a temp folder
  with its own test `DelayedTaskRunner.txt`. Editor mode (`/edit`) is safe, but Save writes the real file.
- Do not commit the real `DelayedTaskRunner.txt` or `close-apps.conf` (public repo). Use `DelayedTaskRunner.sample.txt`.
- **Every time you compile a new version, use `.\build.ps1 -Install`** so the installed exe is always overwritten
  (it renames the old exe to `.old` and copies the new one).
  The original VB6 exe is kept as `DelayedTaskRunner.exe.bak-20261024-165641` in the install folder; do not delete it.
