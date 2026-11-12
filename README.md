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
- **Fetch icons**: reads the icon of every task's file (checked and unchecked) and shows it at the left of the item.
  The icons are saved as small PNG files in `%LOCALAPPDATA%\DelayedTaskRunner\icons`.
  The next time the editor opens, it loads the saved icons. It does **not** check if the exe changed;
  click **Fetch icons** again to refresh them. Files that are not found get no new icon (a saved one stays).
  To clear all saved icons, delete that folder.
- **Save**: write the file and close. **Cancel** (or the X button): close without saving.
- **Close list** (bottom left): opens a window with the apps to close (see [The close list](#the-close-list)).
  Check or uncheck apps, then **Save** (writes `close-apps.conf`) or **Cancel**.
- **Close apps** (bottom left): after a Yes/No question, closes the checked apps of `close-apps.conf`, then the editor exits.
  - The whole window is locked while it runs (also the X button). Changes in the task list are **not** saved.
  - It sends `WM_CLOSE` to all top-level windows (also hidden ones) of each running app, all apps at once
    (the same as `taskkill` without `/F`). The app can still ask "Save changes?".
  - Apps that are still running get `WM_CLOSE` again after 10 s and after 20 s.
  - The editor exits when all apps are closed, or after **30 s** at most. It never kills an app.
  - Every step goes to the log. An app that is still running after 30 s is logged as `WARNING: ... NOT closed`.
  - Known limits: an app with no window (console / service apps) can not get `WM_CLOSE` (logged as a warning).
    An app that runs as administrator can not get it from a normal app (logged as an error).
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

## The close list

`close-apps.conf`, in the same folder as the exe. It is read again every time you click **Close list** or **Close apps**.

```
; comment
E:ppServices\stretchly\stretchly.exe
C:\Program Files\Some Apppp.exe
chrome.exe
#E:	ools\other.exe
```

- One app per line, without quotes and arguments.
  - A full path: only that exe is closed.
  - Only a name (like `chrome.exe`): that exe is closed in any folder.
- `#` in front = unchecked (kept in the list, but not closed). `;` = comment.
- The Close list window shows first every file of the task list (each file once, checked and unchecked tasks),
  then the **add-on apps**: lines of `close-apps.conf` that are not in the task list (you add them by hand).
- Save writes: the checked task-list apps, then all add-on apps.
  An unchecked task-list app is **removed** from the file. An unchecked add-on app **stays**, with `#` in front.
  `;` comments stay at the top of the file.
- `.bat` / `.cmd` / other non-exe files are shown in gray: they are not running apps, so there is nothing to close.
  To close what a batch file started, add that exe by hand.

## Log

`DelayedTaskRunner.log` in the same folder. One line per event (start, each task, errors, pause, exit, close apps).
Problems start with `WARNING:` or `ERROR:`.
If the app crashes, it shows an error box and writes a `CRASH:` line with the full error text.
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
