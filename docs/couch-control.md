# Couch Control

Couch Control turns a selected controller into a Windows keyboard and mouse. It is
a clean-room implementation inspired by the behaviour of
[Gopher360-Enhanced](https://github.com/Ezzud/Gopher360-Enhanced); no Gopher360 code
is copied into padmon. Gopher360 is GPLv3, and padmon does not adopt that license.

Couch Control is available in the portable and normal-install builds only. It is
hidden and disabled in the Microsoft Store package, because the packaged build is
not allowed to inject input into other applications.

## Using it

1. Open **Couch Control** in the left navigation.
2. Pick the controller that should drive Windows.
3. Turn on **Enable Couch Control**.
4. Focus another application. Couch Control deliberately pauses while padmon itself
   is the foreground window so the page stays usable.
5. Press **L3 + R3** together to release everything and turn Couch Control off. The
   same chord turns it back on.

The status line says **Active** when input is being sent, or **Paused while padmon is
focused** when the app is in front.

## Default mappings

| Control | Action |
| --- | --- |
| A / Cross | Left click |
| B / Circle | Enter |
| X / Square | Right click |
| Y / Triangle | Hide padmon |
| Back / Share | Browser refresh |
| Guide | None |
| Start / Options | Left Windows |
| L3 | Middle click |
| R3 | F2 |
| LB / L1 | Browser back |
| RB / R1 | Browser forward |
| D-pad | Arrow keys |
| LT / L2 | Space |
| RT / R2 | Backspace |
| Left stick | Pointer |
| Right stick | Scroll |

Every controller control has at most one action. A mapping can be a keyboard key,
a left/right/middle click, **Hide padmon**, **On-screen keyboard**, or **None**.
**Reset mappings** restores the defaults above.

The panic chord is hard-coded and is not editable.

## Settings

- pointer speed
- stick dead zone
- scroll dead zone
- scroll speed
- pointer acceleration
- swap sticks
- pause while padmon is focused

Settings are stored in padmon's normal `settings.json` under `couchControl`.

## Diagnostics

The deterministic engine and a live SDL controller path are available without
launching the UI:

```powershell
# Engine self-tests (no real input is sent)
pwsh tools/test-couch.ps1 -Mode SelfTest

# List controllers and their stable keys
pwsh tools/test-couch.ps1 -Mode List

# Log the actions for a selected controller
pwsh tools/test-couch.ps1 -Mode Watch -Seconds 30

# Log actions for a specific controller
pwsh tools/test-couch.ps1 -Mode Watch -Controller "serial:054C:09CC:..." -Seconds 30

# Send real keyboard/mouse input while watching
pwsh tools/test-couch.ps1 -Mode Watch -Inject -Seconds 15
```

The harness uses the same `CouchEngine` and `ControllerSnapshot` shape as the app.
`-Mode Watch -Inject` sends real input; use it only in a window that tolerates it.

## Limitations

- `SendInput` cannot inject input into elevated (administrator) windows.
- Anti-cheat software may treat synthetic input as automation.
- Couch Control is not included in the Microsoft Store build.
- On-screen keyboard is started from `osk.exe`; Windows may take a moment to open it.
- The pointer is moved with relative mouse input; pointer acceleration is padmon's
  own curve, not the Windows pointer setting.
