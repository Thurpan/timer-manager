# Timer Manager

A simple, lightweight Windows app for organising multiple countdown timers.

Timer Manager will run locally on your PC. Give timers names and tags, see them together in one dashboard, and get a desktop notification and an alert sound when they finish.

## Status

This repository currently contains the project brief and instructions for development with Codex. The app has not been implemented. No technology stack, installer or release is available yet.

## Planned features

- Run several countdown timers at the same time.
- Give each timer a name and optional tags.
- Sort the dashboard by time remaining or creation date.
- Filter timers by tags.
- Show a Windows desktop notification and play a sound when a timer finishes.
- Keep timers running in the system tray when the window is closed.
- Save timers and their timing state locally across app exits and PC restarts.
- Use a clean, minimalist dark interface inspired by [Off Table](https://offtable.works/).

## Timer behaviour

One global toggle in the Settings tab controls all timers:

| Setting | Behaviour |
| --- | --- |
| Off, the default | Keep each timer's scheduled finish time. Time continues to elapse during sleep, shutdown or a full app exit. If a timer expires while the app cannot alert, alert when it next runs. |
| On | Preserve the time remaining while the PC sleeps or is shut down, or the app is fully exited. Resume from that remaining time afterwards. |

Closing the window into the system tray keeps timers running in both modes. It is different from fully exiting the app.

## Project documents

- [Project brief](docs/project-brief.md): agreed requirements, visual direction, examples and open decisions.
- [Agent instructions](AGENTS.md): guidance for Codex and other contributors.

## Platform and distribution

Windows is the initial target. The app should work on other people's Windows PCs as well as the creator's PC. Support for other operating systems is not currently a requirement.

The intention is to publish the project publicly on GitHub and make it free to use under an open-source licence. The exact licence has not been selected, and no licence is granted by this statement alone.

## Development priorities

Reliability, simplicity and low resource use. Choose a small, practical implementation before adding dependencies or extra features. Codex is the development assistant; the app's language and framework remain undecided.
