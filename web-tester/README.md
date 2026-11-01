# padmon web tester

A static port of padmon's controller tester, hostable on Cloudflare Pages (or any static
host). It reads the browser [Gamepad API](https://developer.mozilla.org/en-US/docs/Web/API/Gamepad_API)
and renders the same tester the desktop app shows: the controller diagram, stick graphs,
B0–B17 cells, readouts, and the classic tester cards.

There is no build step, no framework, and no server-side code — `index.html`, one CSS
file, and a handful of ES modules. Everything stays in the browser; the page makes no
network requests at runtime.

## What the page does

- **Diagram view** (default): controller outline in the hardwaretester.com style. Pressed
  buttons turn red, triggers fade in with travel, the sticks move, and the L/R stick
  graphs plot the last position. B0–B17 cells, index/connected/mapping/timestamp/vibration
  stats, and the numeric readouts sit around it.
- **Classic view**: the card layout from the app — sticks, triggers, buttons, raw axes,
  and device details.
- **Live input** from any controller the browser exposes with the standard gamepad
  mapping, plus a positional fallback for devices without one.
- **Preview mode** with animated simulated input when no controller is connected, exactly
  like the app's Settings → Controller tester preview.
- **Test rumble** through `Gamepad.vibrationActuator` where the browser supports it.
- **Disconnect handling** that keeps the last state on screen.
- **Dark / light / system theme** and a remembered diagram-vs-classic choice.

## Run it locally

ES modules cannot be loaded from `file://`, so serve the folder over HTTP:

```powershell
# Python
python -m http.server 8080 --directory web-tester

# or Node
npx serve web-tester
```

Then open `http://localhost:8080` and press a button on a connected controller. Firefox
only exposes a controller after the first button press; Chromium usually reports it as
soon as it is connected. The page must be focused for browsers to keep polling gamepads.

## Deploy to Cloudflare Pages

### Git integration

1. Push this repository to GitHub.
2. In the Cloudflare dashboard go to **Workers & Pages → Create → Pages → Connect to Git**.
3. Pick the repository and use:
   - **Framework preset**: None
   - **Build command**: leave empty
   - **Build output directory**: `web-tester`
4. Save and deploy. Pushes to the production branch redeploy; other branches get preview
   URLs.

The site also works from a subpath (all asset URLs are relative), so a custom domain or a
`/tester/` path is fine.

### Direct upload

```powershell
npx wrangler pages deploy web-tester --project-name padmon-web-tester
```

No environment variables, bindings, Functions, or headers configuration are required.
Cloudflare caches the assets; they are plain static files.

## Browser support

Current Chromium browsers (Chrome, Edge, Opera, Brave) and Firefox. The Gamepad API needs
a secure context, so `https://` or `localhost`. Key facts the page handles:

- Browsers expose a controller only after a button press in some cases (Firefox).
- Rumble exists only where `vibrationActuator` is implemented (Chromium). Elsewhere the
  **Rumble unavailable** info bar is shown and **VIBRATION** reads `unavailable`.
- The Gamepad API does not expose battery, transport, serial number, or hats, so the
  device card shows **Battery unknown**, **Unknown**, and a model-fingerprint stable key.
- Standard-mapping pads put the analog triggers in `buttons[6]`/`buttons[7]`; the snapshot
  mirrors those values to `LT`/`RT` axes so the raw-axis list and readouts match the
  desktop model.

## How the port maps to the app

Every tester value comes from a direct port of the desktop code; only the input source and
the DOM binding are new.

| Web tester | Desktop source |
| --- | --- |
| `js/pad-input-state.js` | `src/Padmon.App/ViewModels/PadInputState.cs` |
| `js/controller-tester-view-model.js` | `src/Padmon.App/ViewModels/ControllerTesterViewModel.cs` |
| `js/controller-view-model.js` | `src/Padmon.App/ViewModels/ControllerViewModel.cs` |
| `js/preview-snapshot-factory.js` | `src/Padmon.App/Services/PreviewSnapshotFactory.cs` |
| `js/controller-diagram.js` | `src/Padmon.App/Controls/ControllerDiagram.xaml(.cs)` |
| `js/stick-graph.js` | `src/Padmon.App/Controls/StickGraph.xaml(.cs)` |
| `js/gamepad-source.js` | `src/Padmon.Input/SdlControllerSource.cs` (browser replacement) |
| `js/app.js` | `src/Padmon.App/Views/ControllerDetailPage.xaml(.cs)` |
| `css/tester.css` | `src/Padmon.App/App.xaml`, `Styles/PadmonStyles.xaml`, page XAML |

The diagram geometry, sizes, spacing, colours, formatting (`0.00000`, `+0.00;-0.00;0.00`),
and even the banker's rounding used for trigger percentages are copied from the app. The
device stable key follows `DeviceIdentity.Create`'s model-fingerprint branch:
`model:<vendor>:<product>:<name>`.

## Deliberate differences

- The tester's **Back to controllers** button becomes **Back to padmon** and points at the
  project, because this is a single standalone tester page rather than the whole app. The
  diagram/classic choice (a setting in the desktop app) lives in the app bar here, next to
  the theme toggle.
- A controller picker appears when more than one controller is connected. If the selected
  controller disconnects while another is still connected, the tester moves to the
  remaining controller instead of freezing on a per-controller page.
- Iframes and `file://` are not targeted; serve over HTTP(S) as described above.
- PlayStation pads are labelled Cross/Circle/Square/Triangle by vendor id, mirroring SDL's
  face-button labels; everything else uses A/B/X/Y.

## Not part of the main app

Nothing under `src/` or the release workflow references this folder. It is an independent
static site; copy or link to it later if the app needs to point at it.
