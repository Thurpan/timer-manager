# Timer Manager: project brief

## Purpose and status

Timer Manager is a simple, lightweight app for a Windows PC. It keeps several labelled countdown timers organised in one place and alerts the user when they finish.

This document records the product discussion. It is intended to be easy for a human or an LLM coding assistant to read without access to the original conversation. It describes a planned app, not an existing implementation.

## Confirmed identity and scope

- Product name: **Timer Manager**.
- Repository name: `timer-manager`.
- Development will use **Codex**. The earlier dictated word "Ketalx" meant Codex and is not a technology requirement.
- Windows is the initial platform. The first goal is to work on the creator's PC, with distribution suitable for other Windows PCs too.
- The app runs locally and should be simple and lightweight.
- The intended repository is public on GitHub.
- The intended app is free to use under an open-source licence. The specific licence remains undecided.
- The name should be straightforward and descriptive. Timer Manager was chosen to emphasise organising timers. This is a countdown timer utility, not an employee attendance or working-hours product.

## Core user workflow

1. Create a countdown timer and give it a useful name.
2. Optionally add tags to organise it.
3. Start it alongside any other timers that are already running.
4. View the timers together in a dashboard.
5. Sort them by the lowest time remaining or by creation date.
6. Filter the dashboard by tags.
7. Receive a Windows desktop notification and an alert sound when a timer finishes.

The user must be able to run multiple timers at once. The exact time-entry control, maximum duration, tag matching rules and tie-breaking sort order have not been specified.

## Window and background behaviour

Closing the main window sends the app to the system tray. The app continues to run, timers continue to count down and completion alerts remain available.

Fully exiting the app is different from closing its window. A PC that is asleep or shut down also cannot run the app normally. The global setting below defines how elapsed time is handled during these interruptions.

The app must save enough local state to recover its timers and settings after reopening or restarting the PC. The default must never restart a timer from its original duration merely because the application was reopened.

## One global timer setting

The Settings tab contains one toggle that applies to all timers. There are no per-timer overrides in the agreed scope. The exact toggle wording is still open.

| Situation | Toggle off: default | Toggle on: preserve remaining time |
| --- | --- | --- |
| Main window open | Timers count down. | Timers count down. |
| Window closed; app running in tray | Timers count down and can alert. | Timers count down and can alert. |
| App fully exited | Keep the original finish time. | Preserve the remaining duration. |
| PC asleep or shut down | Keep the original finish time. | Preserve the remaining duration. |
| App resumes or opens again | Calculate time remaining from the saved finish time. Alert if the timer expired while unavailable. | Resume from the preserved duration. |

### Default example

A 30-minute timer starts at 14:00 and is due at 14:30. The app is fully exited at 14:10.

- Reopening at 14:20 shows 10 minutes remaining.
- Reopening at 14:40 reports that the timer has expired and produces its completion alert.
- Reopening does not start another 30-minute countdown.

### Optional mode example

A 30-minute timer starts at 14:00. The app is fully exited at 14:10, with 20 minutes remaining.

- Reopening at 14:40 resumes with 20 minutes remaining.
- It will finish at 15:00 if there are no further interruptions.
- Simply closing the window to the tray at 14:10 would not pause it.

### Alert expectations

When a timer finishes while the app is running, show a desktop notification and play an alert sound. In the default mode, a timer that expires while the app cannot run should alert when the app next runs.

The app is not expected to make a sound while the computer is powered off. Waking the PC for an alarm has not been requested.

## Visual direction

The user wants a very clean, minimalist interface with a calm, Scandinavian-inspired feel. Dark mode is the preferred direction.

Reference: [offtable.works](https://offtable.works/), the user's website. The app should share its general visual character while having a design suited to a desktop timer utility. It does not need to reproduce the website.

The website inspected during the discussion used warm off-white, near-black, lime accents, bold typography, clear grid divisions and small restrained labels.

The following is the proposed interpretation of that reference, not a final approved mockup:

- Dark charcoal backgrounds and warm off-white text.
- Muted grey borders and secondary text.
- Small lime accents for primary actions and selected controls.
- Clear typography and prominent, easily scanned countdown numbers.
- Consistent spacing, minimal decoration and a low-clutter layout.
- A list dashboard with timer names and tags on the left, and remaining time and controls on the right.
- Sorting, tag filtering and an Add timer action above the list.
- A separate Settings tab containing the global timing toggle.

Exact colours, fonts, icons, dimensions and final control placement are not yet specified. No screenshot or finished app design has been approved.

## Development principles

- Reliability comes first, especially for saved timers and completion alerts.
- Keep the app lightweight in resource use and implementation complexity.
- Use the fewest dependencies that meet the requirements well.
- Prefer clear, maintainable code over cleverness.
- Keep core behaviour local; a remote backend is not needed for the agreed concept.
- Ensure another Windows user can run the app without machine-specific paths or assumptions about the creator's PC.

The language, framework, storage format and installer are open implementation choices. Codex is the tool used to help build the app, not its runtime.

## Open decisions

These items were not decided in the conversation. They should not be presented as confirmed features:

- The exact open-source licence.
- The technology stack, local storage and distribution format.
- Whether the app starts automatically with Windows.
- How changing the global toggle affects timers already in progress.
- Recovery details for an unexpected crash or power loss in the optional mode, including how precisely the last remaining time can be recovered.
- Notification acknowledgement, handling several overdue timers, repeated sounds and snoozing.
- Timer editing, manual pause, reset, deletion, repeat schedules and reusable presets.
- Detailed handling of system clock changes and tag filtering rules.
- The final interface design and any light theme.

Do not expand the initial scope to accounts, cloud sync, mobile apps, team management or billing without a new requirement. Cross-platform support beyond Windows is not required at this stage.

## Behaviour checks for a future implementation

1. Start several named timers with different durations; each counts down independently.
2. Add tags, filter the list and sort by remaining time or creation date.
3. Let a timer finish while the window is open; confirm a notification and audible alert.
4. Close the window to the tray; confirm the timers and alerts still work in both modes.
5. In the default mode, fully exit and reopen before a timer is due; confirm the original finish time is preserved.
6. In the default mode, reopen after a timer is due; confirm it is treated as expired and alerts.
7. In the optional mode, exit and reopen; confirm the remaining duration is preserved.
8. Repeat the relevant checks through Windows sleep and shutdown or restart.
9. Confirm timer names, tags and the global setting survive reopening.
10. Confirm the packaged app can run on another supported Windows PC.

These are acceptance examples for later development. No application tests have been run because the current repository contains documentation only.
