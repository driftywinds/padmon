# padmon architecture

## Shape of the app

padmon is one per-user WinUI 3 process, not a Windows service. The window and the tray icon are two views of the same process. Closing the window hides it when background monitoring is enabled; **Exit** in the tray ends the process.

```
┌─────────────────────────────────────────────┐
│ Padmon.App                                  │
│  WinUI shell / pages / view models           │
│  tray icon / notifications / settings        │
└───────────────┬─────────────────────────────┘
                │ events + immutable snapshots
┌───────────────▼─────────────────────────────┐
│ Padmon.Input                                │
│  SdlControllerSource                        │
│  dedicated SDL thread                       │
└───────────────┬─────────────────────────────┘
                │ ControllerDevice / snapshots
┌───────────────▼─────────────────────────────┐
│ Padmon.Core                                 │
│  device model / identity / history          │
│  SQLite store / JSON settings               │
└─────────────────────────────────────────────┘
```

## Projects

### `Padmon.Core`

Framework-agnostic models and persistence.

- `ControllerDevice` is an immutable description of a connected controller.
- `ControllerSnapshot` is the live input state: buttons, axes, hats, battery, and transport.
- `SqliteDeviceHistoryStore` owns the local catalogue and session log.
- `JsonSettingsStore` owns the small user-settings file.
- `PadmonPaths` resolves `%LOCALAPPDATA%\padmon` and supports a `PADMON_DATA_DIR` override.

### `Padmon.Input`

`SdlControllerSource` owns one SDL thread. The SDL event loop handles hotplug; polling is only raised to 4 ms while a tester is visible. Snapshots are published at roughly 60 Hz so the UI never receives a 250–1000 Hz event storm.

- Gamepads use SDL's standard gamepad API for mapped buttons and axes.
- Unknown devices fall back to the raw joystick API and still appear in the UI.
- Device identity is serial-based when possible, then SDL GUID, then a model fingerprint. The next improvement is a Windows PnP container-ID lookup.
- `ControllerMonitor` bridges source events to the UI thread and persists connect/disconnect sessions.
- `WindowsGamingControllerSource` polls `Windows.Gaming.Input` on an STA thread as a fallback for Xbox-class devices SDL does not enumerate. Xbox-class devices use the standard `Gamepad` reading for correct stick/trigger directions; raw controllers use `RawGameController`. `CompositeControllerSource` suppresses WGI duplicates by vendor/product so one physical pad is never listed twice.

### `Padmon.App`

- `MainWindow` hosts the Fluent shell, Mica backdrop, navigation, tray icon, and close-to-tray behavior.
- `ControllersPage` shows connected controllers and the previously connected catalogue.
- `ControllerDetailPage` is the live input tester.
- `HistoryPage` shows known controllers and recent sessions.
- `SettingsPage` edits background monitoring, notifications, retention, startup, and theme.
- `TrayIconService` is a small Win32 `Shell_NotifyIcon` implementation, deliberately avoiding a third-party tray control tied to an older Windows App SDK.
- `NotificationService` has an opt-in native mode. It tries the Windows App SDK `AppNotificationManager`, then `DesktopToastService`, which installs a Start Menu shortcut with an AppUserModelID and raises a `ToastNotification` through `ToastNotificationManager`. If both fail, it falls back to a tray balloon, then an in-app notification card. The default is the in-app card. The fallback also covers restricted/low-integrity sessions where the Windows shell rejects tray and toast registration.
- `DesktopToastService` is the native Action Center path for the unpackaged build. It requires a normal-integrity desktop session; a Start Menu shortcut named `padmon` is created/updated automatically.
- The controller card exposes a `More information` dialog with identity, transport, mapping, history, and live-sample counts.

## Background model

The app is single-process and per-user. This avoids the session-0 problems of a real Windows service: toast notifications, tray icons, and interactive HID access are session-bound. A real service would need a session broker and would complicate the product for no user-visible gain.

When background monitoring is on:

1. Closing the window cancels the close and hides the window.
2. The SDL thread stays alive and continues to emit connection events.
3. Notifications use the controller's app-level name.
4. The tray menu can reopen the window, open settings, or exit.

Start with Windows writes a per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value that launches `Padmon.exe --background`. The app starts hidden in that mode.

## History and identity

The database has two tables:

- `controllers` — one row per stable device key, with hardware name, custom name, first/last seen, and session count.
- `sessions` — one row per connection, with transport, battery at connect, and disconnect time.

On startup, any sessions left open by an unclean exit are closed at their connect time. A normal close records the disconnect timestamp.

`NEW` means “this stable key was not in the catalogue before this connection.” Returning controllers do not get the badge. If a device exposes no serial and no stable GUID, identical models can share a model-level key; the UI should eventually mark that uncertainty explicitly.

## Deliberate non-goals

- Button remapping or profiles.
- Deadzone editing.
- Driver installation or virtual controllers.
- Cloud sync, accounts, telemetry.
- Writing configuration to controller hardware.
