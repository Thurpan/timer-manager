# Timer Manager: project brief

## Purpose and status

Timer Manager is a local Windows countdown utility. Several named timers run together, remain organised with optional tags and produce completion alerts.

The first implementation is present. The user approved the implementation plan on 16 September 2026. [Validation](validation.md) distinguishes completed checks from outstanding native verification.

## Confirmed requirements

- Windows is the initial platform. Other Windows users should be able to run the packaged app.
- Core use and saved state remain local. No remote service or account is required.
- Multiple timers can run simultaneously, with names and optional tags.
- The dashboard sorts by remaining time or creation date and filters by tags.
- Controls include create, pause/resume, restart, delete, dismiss and editing names/tags. Duration changes are available while paused.
- Finished timers produce one desktop notification and one sound when the app can run. Finished state remains visible until the user acts.
- Closing the main window leaves the app running in the system tray.
- One global setting controls interruptions for every timer. There are no per-timer overrides.
- Windows startup is optional, disabled by default and opens into the tray at sign-in.
- The project uses the MIT licence. Publication remains a separate action.
- The intended repository is public on GitHub and the app is free to use.

Prioritise reliable timers, clear code and low resource use. Keep dependencies and implementation layers to the minimum required. The app does not wake the PC for alarms.

## Timing and interruptions

The Settings toggle is **Pause timers while the app is closed or the PC is asleep.**

| Situation | Off: default | On: preserve remaining time |
| --- | --- | --- |
| Window open | Count down. | Count down. |
| Window closed into tray | Count down and alert. | Count down and alert. |
| Full exit or shutdown | Keep the original UTC finish time. | Preserve remaining time. |
| Sleep or hibernation | Keep the original UTC finish time. | Exclude sleep from elapsed time. |
| Reopen or resume | Recalculate remaining time; alert if overdue. | Resume from remaining time. |

For a 30-minute timer started at 14:00 and exited at 14:10, reopening at 14:40 produces an overdue completion in default mode. In preserve mode, it resumes with 20 minutes remaining.

Changing the global mode keeps current remaining time and applies immediately. Manually paused timers stay paused. Default mode follows system-clock changes; preserve mode uses elapsed awake time. UTC deadlines are independent of timezone changes.

Actions and orderly exits save immediately. Preserve mode checkpoints every five seconds while timers run. A crash can restore approximately five seconds of extra time under normal storage conditions. Storage failures or delayed writes can increase the recovery gap.

## Controls and alerts

- Names contain 1 to 120 characters after trimming. Duration input accepts positive whole seconds, with days plus hours, minutes and seconds. Overflow is rejected.
- Tags are trimmed and deduplicated without case sensitivity. Multiple selected tags use any-tag matching.
- Default sorting is shortest remaining time, then creation date and stable identifier. Newest sorting uses descending creation date, then identifier.
- Editing duration while paused replaces both the configured duration and remaining time. Editing only names or tags does not change time.
- Restart begins the configured duration immediately and clears prior completion state. Restarting an unfinished timer requests confirmation.
- Dismiss clears a finished timer's attention state without deleting it. Deletion requests confirmation.
- Simultaneous or overdue completions use one combined notification and one sound.
- Completion and its alert claim are saved before delivery. This prevents ordinary reopening from repeating alerts, but a crash between the save and delivery can interrupt an alert.
- Notification or sound failures leave the timer Finished and display an operational message.

## Implementation decisions

- C# and WPF on .NET 10, with a separate, testable timer engine.
- Windows Forms tray icon, Windows App SDK local notifications and Windows awake-time clock.
- Versioned JSON under the user's local application-data folder, atomic replacement and a previous-state backup.
- One instance per Windows user, with activation forwarded to the existing window.
- A Windows x64 ZIP containing runtime dependencies. Windows 11 x64 is the initial verification target.
- An unsigned portable folder for the first build; no installer or automatic update service.

These are the adopted implementation choices. Codex is the development assistant, not the application framework.

## Visual direction

Follow the recorded [Off Table reference](https://offtable.works/) interpretation: charcoal backgrounds, warm off-white text, muted borders and restrained lime accents. Use prominent countdown numbers, clear typography and minimal decoration.

The implemented interface has Dashboard and Settings tabs, timer rows, sorting, tag filters and a focused timer editor. It provides keyboard navigation, visible focus and Windows accessibility controls. No separate finished mockup was approved before implementation.

## Deferred scope

Installers, automatic updates, snoozing, repeating schedules, reusable presets, a light theme and additional operating systems remain deferred. Do not add accounts, cloud sync, team management, subscriptions or billing without a new requirement.

## Acceptance

Verify concurrent timers, controls, sorting and filtering; notification and sound; tray operation; saved names/tags/settings; both modes through exits, sleep and restart; and the extracted package on another Windows PC. Keep evidence and remaining gaps in [validation](validation.md).
