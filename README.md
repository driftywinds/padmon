# padmon

**padmon** is a native Windows app for monitoring game controllers — connected now, seen before, and what their inputs are doing.

It is deliberately **not** a controller configuration suite. There are no remaps, macros, deadzone editors, or per-game profiles. The job is to make controllers visible, understandable, and pleasant to keep track of.

![padmon controllers view](docs/images/controllers.png)

## What is in the working preview

- SDL3 controller discovery and hotplug monitoring on Windows 11, with a Windows.Gaming.Input fallback for Xbox-class pads SDL misses.
- Xbox, PlayStation, Switch, 8BitDo, GameSir, EasySMX, and generic controllers through SDL's gamepad mappings, with raw joystick fallback for unrecognized devices.
- A persistent local catalogue of controllers, including first-seen, last-seen, and session count.
- New/known device handling, battery and transport information where the device exposes it.
- App-level renaming that uses the chosen name throughout the UI and notifications.
- A live tester with buttons, sticks, triggers, shoulders, hats, and raw axis values.
- A "More information" stats-for-nerds dialog with identity, transport, mapping, history, and live sample details.
- A tray-resident background mode, close-to-tray behavior, and opt-in start with Windows.
- Windows notifications for connect/disconnect, with a selectable native Windows 11 style, and tray-balloon/in-app fallbacks when the modern notification API is unavailable.
- SQLite history with retention settings; no cloud, telemetry, or account.

## Requirements

- Windows 11 22H2 or newer.
- .NET 10 SDK. The repo installs a local SDK into `.dotnet` so it does not depend on a machine-wide SDK.
- Windows App SDK 2.6.4 (restored through NuGet).
- SDL 3.4.18 native binaries (restored through `SDL3-CS.Windows`).

## Build and run

```powershell
# First time only: install the local .NET 10 SDK
pwsh tools/install-dotnet.ps1

# Build the solution
pwsh tools/build.ps1

# Run the app
pwsh tools/run.ps1
```

The direct SDK wrapper is `eng/dotnet.ps1`:

```powershell
.\eng\dotnet.ps1 build Padmon.sln --configuration Debug --property:Platform=x64
```

If the build is run inside a restricted environment, the wrapper keeps NuGet caches, temp files, and the SDK under the repository so it does not need the user profile. In a normal desktop session the standard `.NET` CLI can also be used after installing the SDK.

## Development helpers

```powershell
# Run with a simulated controller (Debug builds only)
pwsh tools/run.ps1 -Simulate

# Start hidden in the tray, as the login startup path does
pwsh tools/run.ps1 -Background

# Use an isolated data directory for experiments
$env:PADMON_DATA_DIR = 'W:\repos\padmon\.tmp\padmon-data'
```

`PADMON_SIMULATE_FLAP=1` makes the simulator disconnect and reconnect periodically so notification and tray behavior can be exercised without hardware.

The app icon and tray icon are generated from `tools/generate-icon.py` (Pillow).

## Releases

`.github/workflows/build-release.yml` builds the app on Windows and attaches a portable `padmon-win-x64.zip` to a GitHub Release when a `v*` tag is pushed.

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## Data and privacy

Everything stays local:

- `%LOCALAPPDATA%\padmon\padmon.db` — controller catalogue and connection sessions.
- `%LOCALAPPDATA%\padmon\settings.json` — app preferences.
- `%LOCALAPPDATA%\padmon\logs` — reserved for diagnostics.

`PADMON_DATA_DIR` overrides the data directory for development or portable experiments.

## Architecture

- `src/Padmon.App` — WinUI 3 shell, pages, view models, tray, notifications.
- `src/Padmon.Core` — controller domain models, settings, SQLite history.
- `src/Padmon.Input` — SDL3 controller source on a dedicated background thread.
- `src/Padmon.App/Services` — WGI fallback, source deduplication, tray, notifications, settings.

See [docs/architecture.md](docs/architecture.md) for the details.

## Known limits and next steps

- The controller support matrix still needs real-hardware verification. The current test set is GameSir Cyclone 2, EasySMX X20, an original Xbox One controller, and an older PowerA controller.
- Controllers without serial numbers and without a PnP container ID may share a model fingerprint; that is shown as a model-level identity rather than pretending per-unit identity.
- Native Windows notifications are opt-in in Settings and default to the in-app card. Low-integrity test sessions block native shell notifications; the in-app fallback is used there. Native toasts should be tested from a normal Explorer launch.
- Single-instance behavior and a graceful “forget controller” flow are good next additions.
- There is intentionally no MSIX packaging. Release packaging can be a portable self-contained folder first and MSIX later if the distribution story calls for it.
