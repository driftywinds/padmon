# padmon

**padmon** is a native Windows app for monitoring game controllers — connected now, seen before, and what their inputs are doing.

It is deliberately **not** a controller configuration suite. There are no remaps, macros, deadzone editors, or per-game profiles. The job is to make controllers visible, understandable, and pleasant to keep track of.

## Product principles

1. **UI first.** padmon should feel like a polished Windows utility: calm, fast, legible, and at home in Windows 11. Every state — empty, loading, connected, disconnected, unknown device — deserves a considered design.
2. **Monitoring, not configuration.** Read devices, present them clearly, and remember them. Do not turn into a tinkering tool.
3. **Broad device support.** Xbox and PlayStation pads are the baseline, not the boundary. Generic USB/Bluetooth gamepads, fight sticks, wheels, and unusual controllers should work wherever the underlying input stack can expose them.
4. **Names are personal.** A controller can be renamed inside padmon, and that name is used everywhere the app refers to it, including notifications.
5. **Background when asked.** padmon can stay running in the tray to watch for connections and disconnections, and it should be obvious when it is doing so.
6. **No surprises.** No telemetry, no cloud account, no driver installation, no background process the user did not opt into.

## Planned capabilities

- A live list of connected controllers, including connection type and available battery information.
- A persistent catalogue of controllers that have been seen before, with first-seen and last-seen history.
- New-device and returning-device identification, so a previously connected pad is recognised as familiar.
- App-level renaming, with Windows notifications that use the chosen name.
- A quick, read-only input tester: buttons, sticks, triggers, shoulders, hats, and other exposed controls.
- A tray-resident monitoring mode with an opt-in start-with-Windows setting.
- Graceful handling of controllers that cannot be fully identified or mapped.

## Status

Planning. The UI framework, packaging model, and background-monitoring architecture are being decided before implementation begins.

## Name

The product is **padmon** (lowercase in user-facing copy). C# namespaces and project names use `Padmon` where language conventions require it.
