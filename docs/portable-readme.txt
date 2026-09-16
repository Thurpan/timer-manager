Timer Manager

Extract the entire TimerManager folder, then open TimerManager.exe.
Keep all included files together. No .NET installation is required.
Windows 11 x64 is the initial verification target.

Closing the window keeps timers running in the system tray.
To stop the app, choose Exit from its tray menu or Exit Timer Manager in Settings.

Timers and settings are saved in %LOCALAPPDATA%\TimerManager.
Default: timers keep their scheduled finish times through exits and sleep.
Optional: enable preservation in Settings to count only while the app runs and
the PC is awake. A crash can restore about five seconds of extra remaining time.

Windows startup is optional and disabled by default. Keep this folder in place
after enabling it. To move the folder, disable startup, exit, move the folder,
then reopen and enable startup again. Disable startup before removing the app.

Windows notification settings and audio volume affect alerts. Finished timers
remain visible. Reopening does not repeat an alert already claimed by the app.

See LICENSE for the app licence and ThirdPartyNotices for bundled dependencies.
Source and documentation: https://github.com/Thurpan/timer-manager
