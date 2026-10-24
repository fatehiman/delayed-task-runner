# CLAUDE.md

C# .NET 8 WinForms rewrite of an old VB6 tray app. See [README.md](README.md) for behavior and file format.

## Files

- `Program.cs` – single-instance mutex, picks the mode (no args = runner, any arg = editor), paths.
- `TrayRunner.cs` – runner mode: tray icon, menu, task clock (pausable `Stopwatch`), opens `PauseForm` on left click, hidden window for clean `WM_CLOSE`.
- `PauseForm.cs` – pause window (manual mode): Continue / Next / Ignore this / Exit. Talks to the runner via `IPauseHost`.
  Next launches on a background STA thread (`Launcher.RunAsync`) and waits only for the launch.
- `EditorForm.cs` – editor mode: `ListView` with checkboxes, right-click Run / Open folder, Save/Cancel.
- `TaskFile.cs` – parse/save `DelayedTaskRunner.txt`. Save only changes the `#` of changed task lines; CRLF, no BOM.
- `Launcher.cs` – splits command into file + args, resolves it (also via PATH), starts it with `ShellExecuteEx` (no-focus show modes).
- `AutoStart.cs` – HKCU Run key. `Logger.cs` – small log, rotated at 1 MB.

## Rules

- **Never run the installed `E:\appServices\DelayedTaskRunnerHome\DelayedTaskRunner.exe` without arguments to test.**
  It starts every task of the real file (VMs, mounts, many apps). Test with a copy of the exe in a temp folder
  with its own test `DelayedTaskRunner.txt`. Editor mode (`/edit`) is safe, but Save writes the real file.
- Do not commit the real `DelayedTaskRunner.txt` (public repo). Use `DelayedTaskRunner.sample.txt`.
- Build/install with `build.ps1` (`-Install` renames the old exe to `.old` and copies the new one).
  The original VB6 exe is kept as `DelayedTaskRunner.exe.bak-20261024-165641` in the install folder; do not delete it.
