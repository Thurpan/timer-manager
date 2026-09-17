# Timer Manager

A local Windows app for multiple named countdown timers, with tags, tray operation and completion alerts.

## Status

The first implementation uses C# and WPF on .NET 10. Windows 11 x64 is the initial verification target. The source is licensed under [MIT](LICENSE). This repository does not publish a release or installer automatically.

Download the [v0.2.0 Windows x64 prerelease](https://github.com/Thurpan/timer-manager/releases/tag/v0.2.0). The app has automated timing and persistence tests. See [validation](docs/validation.md) for native checks and outstanding verification.

## Use the app

Extract the complete `TimerManager` folder from `TimerManager-win-x64.zip`, then open `TimerManager.exe`. Keep its included files together. The package includes its runtime dependencies.

- Create a named timer by choosing **Duration** or **Finish at**. The chosen method has editable fields; the other value appears as a labelled calculation. Enter a finish date as `YYYY-MM-DD` and local time as `HH:MM:SS` (24-hour). Tags are optional and separated by commas.
- Open the **⋯** menu at the right of each timer to pause/resume, restart, edit or delete it. Finished timers also offer Dismiss until acknowledged. While editing, change either duration or finish time; the other updates. Duration includes time already counted, so changing it does not reset the countdown.
- Running timers can be edited without pausing. The editor shows counted and remaining time **when opened**, alongside the linked total-duration and finish inputs. Paused timers stay paused and label their finish as an estimate if resumed now. Shortening a running timer below its elapsed time finishes it; extending a finished timer into the future starts it again.
- Sort by shortest remaining time or newest creation date. Selected tags match any tag, without case sensitivity. The summary shows how many timers are visible and retains labelled overall state counts. **Clear** resets the filters; long tag lists wrap and scroll.
- Running rows show their finish time, labelled **est.** in preserve mode. Paused rows show remaining and total duration. Hover over the timing detail for the total duration.
- Finished timers produce one notification and one sound. A lime status and left edge mark timers awaiting dismissal. Paused and dismissed countdowns are muted. Dismiss clears the attention state; restart and delete remain available.
- Delete and unfinished-timer restart confirmations name the timer and default to **Cancel**. Form errors appear beside their fields; submitting an invalid form focuses the first error.
- Close the window to keep counting in the tray. Use **Exit** in the tray menu or **Exit Timer Manager** in Settings to stop the app.
- Optionally enable **Start with Windows** in Settings. It opens into the tray at sign-in and is disabled by default.
- Use **Open folder** in Settings to open the local data directory in Explorer.

Keep the extracted folder in place after enabling startup. To move it, disable startup, exit, move the folder, reopen and enable startup again. Disable startup before removing the app.

## Timer behaviour

The global Settings toggle is **Pause timers while the app is closed or the PC is asleep.**

| Situation | Off: default | On: preserve remaining time |
| --- | --- | --- |
| Window open or closed to tray | Count down. | Count down. |
| App fully exited | Keep the UTC finish time. | Preserve saved remaining time. |
| PC asleep or shut down | Keep the UTC finish time. | Preserve remaining time. |
| Reopen or resume | Recalculate; alert for overdue timers. | Resume counting while Windows is awake. |

Switching modes preserves current remaining time and leaves manually paused timers paused. In default mode, changing the system clock affects deadlines; changing timezone does not. Preserve mode uses the Windows awake-time clock.

When creating by duration, the countdown starts on **Start timer**. When creating by finish time, the selected time stays fixed while the editor is open. Finish times are estimates in preserve mode because sleep or a full exit postpones completion. An entered finish must be in the future. The editor rejects missing or ambiguous local times at daylight-saving transitions. Durations calculated from finish times round up to whole seconds; the selected deadline remains exact.

The app saves timer actions immediately, saves on orderly exit and checkpoints running timers every five seconds in preserve mode. An unexpected termination can restore approximately five seconds of extra remaining time when storage is working normally. A failed or delayed save can increase that gap.

Simultaneous or overdue completions are grouped into one notification and sound. The app saves an alert claim before delivery to prevent repeated sounds after reopening. A crash between that save and delivery can interrupt the alert; the timer remains Finished. Windows notification settings and audio volume affect whether alerts are visible or audible.

## Local data

Data lives in `%LOCALAPPDATA%\TimerManager`:

- `state.json`: versioned timer state and settings.
- `state.json.bak`: the previous successful state, retained during atomic replacement.
- `app.log`: local operational errors; rotated at the next launch after exceeding 1 MB.

If the primary state is damaged, the app attempts backup recovery and displays a message. It retains a damaged primary file on the next save. Recovery can lose recent changes or repeat an alert recorded only in the damaged version. If neither file is readable, the app stops without replacing them. Unknown state versions are not downgraded.

## Develop

Install the .NET SDK specified in [global.json](global.json). On this workstation the SDK was installed under `%USERPROFILE%\.dotnet`; prepend that directory for a development shell if the system `dotnet` is older:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --version
dotnet build TimerManager.slnx -c Release
dotnet test tests/TimerManager.Tests/TimerManager.Tests.csproj -c Release
dotnet run --project tests/TimerManager.UiChecks -c Release -- artifacts/ui-checks
dotnet run --project src/TimerManager.App -c Release
```

Exit the app before rebuilding its executable. Closing its window only hides it.

The UI check runner requires an interactive Windows desktop. It loads production views with in-memory timer state, without startup registration, tray integration or notifications. It checks control behaviour and writes rendered PNGs to the supplied output directory. Popup focus checks cannot run reliably on a restricted sandbox desktop. See [validation](docs/validation.md) for remaining native checks.

If a restricted build environment cannot start parallel MSBuild workers, add `-m:1` to the build command.

For isolated testing, pass a separate data directory. Windows startup registration is disabled in this mode. Only one app instance runs per Windows user, including diagnostic copies.

```powershell
dotnet run --project src/TimerManager.App -c Release -- --data-directory C:\Temp\TimerManager-Test
```

## Package

```powershell
.\scripts\package.ps1
```

If necessary, specify the SDK host explicitly:

```powershell
.\scripts\package.ps1 -Dotnet "$env:USERPROFILE\.dotnet\dotnet.exe"
```

The script publishes into a fresh staging folder, includes licences and dependency notices, and creates `artifacts/TimerManager-win-x64.zip` with a SHA-256 sidecar. It does not upload anything. The package is unsigned.

## Structure

- `src/TimerManager.Core`: timer state, injectable clocks, filtering, persistence and completion coordination.
- `src/TimerManager.App`: WPF interface and Windows tray, notifications, startup and activation services.
- `tests/TimerManager.Tests`: deterministic behaviour and storage tests.
- `tests/TimerManager.UiChecks`: isolated WPF interaction and rendering checks.
- [Project brief](docs/project-brief.md): confirmed product decisions and deferred scope.
- [Agent instructions](AGENTS.md): contributor guidance and checks.

Windows App SDK Foundation supplies local notification APIs. The Runtime package supplies its version resource. A build target extracts that resource for self-contained deployment because the stable SDK omits it from output; see [upstream issue 6071](https://github.com/microsoft/WindowsAppSDK/issues/6071). Reassess this target when upgrading the SDK.
