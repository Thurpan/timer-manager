# Validation

## Automated checks

On 17 September 2026, the Release build passed with no warnings or errors and all 45 core tests passed.

```powershell
dotnet build TimerManager.slnx -c Release
dotnet test tests/TimerManager.Tests/TimerManager.Tests.csproj -c Release
```

The tests cover concurrent expiry, saved deadlines, preserve-mode exits and sleep, clock jumps, mode changes, pause/edit/resume/restart/delete/dismiss, overdue batching, tag matching, stable sorting, invalid input, atomic state recovery, damaged and future-version data, missing fields, blocked storage, failed-command rollback, checkpoint recovery and durable alert claims.

The duration/finish editor tests cover creation by either input, time spent in the editor, edits that preserve elapsed time, paused-state retention, sleep exclusion, expiry during editing, rearmed alerts, restart duration, recovery of edited and completed deadlines, older finished records and invalid or ambiguous local finish times.

An isolated WPF host passed 16 editor checks on 17 September. It loaded the production dialog and styles, exercised its controls and save events, and checked creation choices, linked fields, running edits, name-only saves and validation. Rendered creation and edit views were inspected. This host did not access user timers or register notifications; it does not establish keyboard or screen-reader acceptance of the full app.

## Windows checks

The development host is Windows 11 Pro for Workstations, x64, build 26200. Timer checks use isolated data under `artifacts`. Startup registration was enabled and disabled through a fresh normal session, leaving it disabled with no user timers.

| Check | Evidence and status |
| --- | --- |
| Dashboard, timer editor and Settings layout | Inspected real WPF windows with Windows accessibility snapshots and screenshots. Dark-theme inheritance corrected during this check. |
| Concurrent display and completion | Seeded running and paused sample timers; observed countdowns and a persisted Finished state. |
| Pause/resume, tags and global mode | Exercised live dashboard controls and checked saved state. |
| Tray and second launch | Closing produced no visible main window while the process and countdown checkpoints continued. A second launch reopened the same process. |
| Full exit | The Settings Exit action saved remaining time and stopped the process. |
| Startup setting | Enabling wrote the quoted executable path with `--tray` to the current user's Run key and saved the setting. Disabling removed the value and saved false. A direct `--tray` launch ran with no visible main window. Actual sign-in remains pending. |
| Portable package on this host | Published, compressed, extracted and launched the ZIP. The extracted app loaded existing timers and registered notifications without errors. This is not a separate-PC test. |
| Notifications | Registered and submitted a completion without an API error after fixing the missing SDK resource. Visible banner delivery and notification-click activation remain unverified. |
| Sound | Completion invoked the system sound without an exception. Audible output remains unverified. |
| Timer editor input | The isolated WPF checks above verify control events and submitted values. End-to-end keyboard entry through the main app's owned modal window remains a manual check. |
| High display scaling | Per-monitor DPI awareness implemented; checks at 150% and 200% remain pending. |
| Sleep, hibernation, shutdown and sign-in | Deterministic timing tests pass. Actual OS transitions remain pending in a disposable environment or user-controlled session. |
| Separate Windows PC | Pending. Windows Sandbox is unavailable on this host; no second Windows environment was available. |

The build includes an SDK version-resource extraction target for an upstream self-contained notification issue. Validate notification registration again when changing Windows App SDK versions.

Markdown structural lint passed using the global policy configuration, which disables the line-length rule. Relative document links and the Git whitespace check also passed.

## Manual acceptance procedure

1. Extract the ZIP on a Windows 11 x64 PC without developer tools. Open `TimerManager.exe`.
1. Create a 15-second timer by duration and a timer with a finish two minutes from now, with overlapping tags. Check both displayed values, names, countdowns and any-tag filtering.
1. Edit a running timer's duration, then its finish. Confirm that each updates the other and elapsed time is retained. Let a timer expire while its editor is open, then save only its name and verify it stays Finished.
1. Pause one timer. Edit its name without changing its time, then change its duration and finish. Confirm it stays paused and shows the estimated finish if resumed now. Resume it and check remaining time.
1. Check shortest-time and newest-created ordering. Confirm deletion and restart prompts.
1. Let the short timer finish. Check one notification and sound, click the notification, then dismiss the finished timer.
1. Close the window into the tray. Check that time elapses, then reopen with the tray icon or another launch.
1. Use Exit. Reopen before and after a deadline in default mode; confirm the original deadline and one overdue alert.
1. Enable preserve mode. Exit and reopen; confirm preserved remaining time. Repeat through sleep, hibernation and restart.
1. Enable Start with Windows, sign out and back in, and verify a single tray instance. Disable startup after the check.
1. Check the dashboard, Settings and editor at 150% and 200% display scaling, including keyboard-only operation and a screen reader.

Do not claim the pending steps passed from the presence of an API call or a simulated clock test.
