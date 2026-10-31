# padmon privacy policy

_Last updated: 31 October 2026_

padmon is a local controller monitor for Windows. It does not require an
account, and it does not include telemetry, analytics, advertising, or cloud
sync.

## What padmon stores

padmon stores the following on your PC:

- Controller names, identifiers, first/last seen times, and connection sessions.
- App preferences such as theme, notification choices, retention, and startup.
- Local diagnostic logs.

For the portable and unpackaged builds these files are under
`%LOCALAPPDATA%\padmon`. For the Microsoft Store (MSIX) build they are stored in
the package's local data folder, which Windows removes when the package is
uninstalled.

## Network access

The portable and unpackaged builds can contact GitHub when you explicitly open
**Update** and press **Check for updates**. The request goes to
`https://api.github.com/repos/driftywinds/padmon/releases/latest` and downloads a
release only after you confirm an update.

The Microsoft Store build does not check GitHub or replace its own files.
Updates are delivered by Microsoft Store.

## Notifications

Notifications are generated locally. padmon does not send controller data to a
notification service.

## Sharing

padmon does not sell, rent, or share your data. It does not collect data for the
developer.

## Contact

Questions or requests: open an issue at
<https://github.com/driftywinds/padmon/issues>.

## Changes

If this policy changes, the updated version will be published in this repository.
