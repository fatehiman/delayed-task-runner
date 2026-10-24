# DelayedTaskRunner

A small Windows tray app that starts your programs **slowly, one by one**, after you log in.
It keeps the PC fast right after login. Each program starts at its own time (seconds after the app starts).

This is a rewrite (C# / .NET 8 WinForms) of an old VB6 app with the same name.
It reads the **same** `DelayedTaskRunner.txt` file format.

## Two modes

### 1. Runner mode (no arguments)

```
DelayedTaskRunner.exe
```

- Shows an icon in the system tray.
- Starts every enabled task at its time. If two tasks have the same time, they start one after another (250 ms apart).
- If a file does not exist, it shows a Windows notification and **goes on** (it does not stop or pause).
- When all tasks are started, the app exits (the tray icon goes away).
- **Left click** on the tray icon: pauses and opens the pause window (manual mode).
  It shows how many tasks are done and `Next: <command>` (the task that Next or Ignore will use).
  - **Continue**: close the window and go on by time (also unchecks Pause).
  - **Next** (or Enter): start the next task now, even if its time has not come.
    The buttons are disabled until the program is launched (it does not wait for the program's window).
    Then the window shows the result (`Started: ...` or the error) and the new next task.
  - **Ignore this**: skip the next task (it will not run).
  - **Exit**: close the app. The remaining apps do not start.
  - The X button or Esc = Continue.
  - When no tasks are left, Next and Ignore are disabled. Continue or Exit then closes the app.
- **Right click** menu:
  - **Pause** (checkmark): pause until you uncheck it. No dialog.
  - **Auto start** (checkmark): adds/removes the app in
    `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so it starts after login.
  - **Exit**: close the app now.
- While paused (menu or pause window), the task clock stops. Remaining tasks keep their distance from each other.

### 2. Editor mode (any argument)

```
DelayedTaskRunner.exe /edit
```

- Opens a normal window (with a taskbar icon, no tray icon) in the center of the screen.
- Lists all tasks of the file: checkbox, time, command, run style (Normal / Minimized / Hidden).
- A red line = the file was not found.
- Checked = enabled. Unchecked = the line is commented out with `#`.
- Right click on a task > **Run**: start that task now.
- Right click on a task > **Open folder**: open the task's folder in File Explorer, with the file selected.
  If the file does not exist, it opens the nearest folder that exists.
- **Save**: write the file and close. **Cancel** (or the X button): close without saving.
- Save changes only the `#` in front of the changed lines. All other lines (comments, order, spacing) stay the same.

Only one copy of the app can run (in both modes). A second copy shows "DelayedTaskRunner is already running." and exits.

## The task file

`DelayedTaskRunner.txt`, in the same folder as the exe. See [DelayedTaskRunner.sample.txt](DelayedTaskRunner.sample.txt).

```
#format: hh:mm:ss,task[,status]
#status: 0=normal  1=minimized  2=hidden
00:00:10,E:\appServices\Today\today.exe /s
00:00:15,"C:\Program Files\Some App\app.exe" --flag value,1
00:00:20,"E:\scripts\start.bat",2
#00:00:25,E:\old\disabled.exe
```

- `hh:mm:ss` = time after the app starts (not a clock time).
- `task` = path and arguments. Put a path with spaces in double quotes.
  (A path with spaces without quotes also works, if the file exists.)
- `status` (optional): `0` normal, `1` minimized, `2` hidden. Programs start **without taking focus**
  (like the old app).
- A line starting with `#` is a comment / disabled task.
- The working folder of each program is the folder of its exe.
- Use CRLF line endings.

## Log

`DelayedTaskRunner.log` in the same folder. One line per event (start, each task, errors, pause, exit).
When it is bigger than 1 MB, it is renamed to `DelayedTaskRunner.log.old` at the next start.

## Build

Needs the .NET 8 SDK. The exe needs the .NET 8 Desktop Runtime.

```powershell
.\build.ps1            # builds dist\DelayedTaskRunner.exe
.\build.ps1 -Install   # also replaces E:\appServices\DelayedTaskRunnerHome\DelayedTaskRunner.exe
                       # (the old exe is renamed to DelayedTaskRunner.exe.old)
```

`tools\make-icon.ps1` creates `app.ico`.

## Where it runs on this PC

- Folder: `E:\appServices\DelayedTaskRunnerHome\`
- Started at login by the scheduled task `\Fatehi-startup` (5 s delay).
- If you turn on **Auto start** in the tray menu, also disable the scheduled task.
  Otherwise two copies start at login, and the second one shows the "already running" error.
