# Agent instructions

## Read first

1. Read `README.md` for the project summary and current status.
2. Read `docs/project-brief.md` for the product requirements and open decisions.
3. Inspect the current repository before proposing or changing an implementation.

This is Timer Manager, a local Windows app for managing multiple countdown timers. It uses C# and WPF on .NET 10, with a separate timer engine and automated tests.

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

- Preserve the selected C#/WPF stack, versioned JSON storage and portable Windows x64 packaging unless a requested change requires otherwise.
- Use Windows App SDK Foundation for local notifications and Windows Forms for the tray icon. Do not add a web runtime or backend.
- In the default mode, derive remaining time from the saved finish time. A periodic UI refresh must not be the only record of elapsed time.
- Keep timing and persistence logic separate enough from the interface to verify them reliably, without creating unnecessary layers.
- Persist the data needed to recover timer names, tags, timing state and the global setting.
- Avoid duplicate completion alerts caused by repeated display updates. Define restart and acknowledgement behaviour before relying on it.
- Do not add accounts, cloud sync, a backend, subscriptions or unrelated productivity features unless requested.
- Preserve the MIT licence and dependency notices when packaging.

## Verification and reporting

- For documentation changes, check consistency, relative links and the diff. Do not add tests that only restate the documentation.
- When timing behaviour is implemented, verify concurrent timers, expiry, tray operation, recovery after a full exit and both global modes.
- Verify native notifications, sound, sleep and restart behaviour on Windows. State clearly when the available environment cannot test these.
- Check sorting, filtering and saved data when those features are implemented.
- Report what changed, what was checked and any remaining limitation. Do not claim the app runs or tests pass without evidence.
- Keep setup and test commands in the README once they actually exist.

## Commands and structure

Use the .NET 10 SDK pinned in `global.json`. A per-user installation may require `%USERPROFILE%\.dotnet` before the system SDK on `PATH`.

```powershell
dotnet build TimerManager.slnx -c Release
dotnet test tests/TimerManager.Tests/TimerManager.Tests.csproj -c Release
dotnet run --project src/TimerManager.App -c Release
.\scripts\package.ps1
```

Exit the app before rebuilding it. For diagnostic data, pass `-- --data-directory C:\Temp\TimerManager-Test` to `dotnet run`. This disables startup registration and still uses the per-user single-instance boundary.

The core project owns timer transitions, storage and completion claims. The app project owns Windows integration and the interface. Tests use injected clocks; do not use real delays for core timing tests.

Read `docs/validation.md` before describing platform behaviour as verified. Do not put this PC to sleep or restart it for unattended tests. Use a disposable environment or a user-controlled session.
