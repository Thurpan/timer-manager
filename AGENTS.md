# Agent instructions

## Read first

1. Read `README.md` for the project summary and current status.
2. Read `docs/project-brief.md` for the product requirements and open decisions.
3. Inspect the current repository before proposing or changing an implementation.

This is Timer Manager, a lightweight local Windows app for managing multiple countdown timers. The initial repository contains documentation only.

## Working approach

- Use simple language and small, reviewable changes.
- Prioritise reliability and simplicity over performance tricks.
- Avoid unnecessary dependencies, abstraction layers and large refactors.
- Work on the requested scope. Documentation work does not imply permission to build the whole application.
- Treat explicit user instructions as authoritative. Keep the project brief aligned with subsequent decisions.
- Distinguish confirmed requirements, implementation suggestions and unresolved choices. Do not present a suggestion as a user decision.
- Make routine implementation choices using the existing requirements. Ask only when an unresolved choice materially changes the product, cost or scope.

## Requirements to preserve

- Windows is the initial target; other Windows users should be able to run the app.
- Timers and settings are stored locally. Core use should not need a remote service.
- Multiple timers can run simultaneously and have names and optional tags.
- The dashboard supports sorting by remaining time or creation date and filtering by tags.
- Finished timers produce a desktop notification and an audible alert when the app is able to run.
- Closing the window leaves the app running in the system tray.
- The default behaviour is based on saved finish times. App exits and PC sleep or restarts must not reset a timer's duration.
- A single global toggle in the Settings tab enables preservation of remaining time during sleep, shutdown or a full app exit.
- The toggle applies to every timer. Do not introduce a per-timer override without a new requirement.
- Both modes keep counting when only the window is closed to the tray.
- Follow the calm, minimalist dark visual direction in the project brief.

## Implementation guidance

- No language, framework, storage format or packaging method has been selected. Do not assume Codex is an application framework.
- When implementation is requested, choose the simplest practical stack that supports Windows tray behaviour, desktop notifications, sound and durable local state.
- In the default mode, derive remaining time from the saved finish time. A periodic UI refresh must not be the only record of elapsed time.
- Keep timing and persistence logic separate enough from the interface to verify them reliably, without creating unnecessary layers.
- Persist the data needed to recover timer names, tags, timing state and the global setting.
- Avoid duplicate completion alerts caused by repeated display updates. Define restart and acknowledgement behaviour before relying on it.
- Do not add accounts, cloud sync, a backend, subscriptions or unrelated productivity features unless requested.
- Choose an explicit open-source licence before describing a release as licensed open source.

## Verification and reporting

- For documentation changes, check consistency, relative links and the diff. Do not add tests that only restate the documentation.
- When timing behaviour is implemented, verify concurrent timers, expiry, tray operation, recovery after a full exit and both global modes.
- Verify native notifications, sound, sleep and restart behaviour on Windows. State clearly when the available environment cannot test these.
- Check sorting, filtering and saved data when those features are implemented.
- Report what changed, what was checked and any remaining limitation. Do not claim the app runs or tests pass without evidence.
- Keep setup and test commands in the README once they actually exist.
