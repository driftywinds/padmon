/**
 * Browser counterpart to src/Padmon.Input/SdlControllerSource.cs.
 *
 * It reads navigator.getGamepads() and turns a browser Gamepad into the same
 * ControllerSnapshot shape the desktop app uses: buttons and axes are labelled the
 * way the desktop sources label them (A/Cross, LB, D-Pad Up, Left X, LT, ...), so the
 * ported PadInputState mapping and tester view model work unchanged.
 *
 * The Gamepad API does not expose a serial number, transport, battery or hats. Those
 * facts are reported as unknown / empty, and the stable key mirrors the desktop
 * model-fingerprint fallback: model:<vendor>:<product>:<name>.
 */

const FACE_BUTTONS_XBOX = ['A', 'B', 'X', 'Y'];
const FACE_BUTTONS_PLAYSTATION = ['Cross', 'Circle', 'Square', 'Triangle'];

// W3C "standard gamepad" button order.
const STANDARD_BUTTONS = [
    'A', 'B', 'X', 'Y', 'LB', 'RB', 'LT', 'RT', 'Back', 'Start', 'L3', 'R3',
    'D-Pad Up', 'D-Pad Down', 'D-Pad Left', 'D-Pad Right', 'Guide',
];

const STANDARD_AXES = ['Left X', 'Left Y', 'Right X', 'Right Y'];

const VENDOR_PRODUCT_PATTERN = /vendor:\s*([0-9a-f]{4})\b[\s\S]*?product:\s*([0-9a-f]{4})\b/i;
const DASHED_ID_PATTERN = /^([0-9a-f]{4})-([0-9a-f]{4})-/i;

/**
 * Watches the browser's gamepad list. Connect and disconnect events are inconsistent
 * across browsers (Firefox only reveals a pad after a button press), so the source
 * also exposes getGamepad(), and the app polls that every animation frame.
 */
export class GamepadSource {
    constructor({ onConnected, onDisconnected } = {}) {
        this.onConnected = onConnected ?? (() => {});
        this.onDisconnected = onDisconnected ?? (() => {});
        this.connected = new Map();
        this.started = false;
        this._handleConnected = (event) => this.register(event.gamepad);
        this._handleDisconnected = (event) => this.unregister(event.gamepad);
    }

    start() {
        if (this.started) {
            return;
        }

        this.started = true;
        window.addEventListener('gamepadconnected', this._handleConnected);
        window.addEventListener('gamepaddisconnected', this._handleDisconnected);

        // Chrome can already have a pad enumerated before the page loaded.
        this.poll();
    }

    /**
     * Keeps the connected map in sync with navigator.getGamepads(). The page calls this
     * every animation frame: some browsers expose a pad a moment before (or without)
     * firing a connect event, and polling is the only reliable detection path.
     */
    poll() {
        const pads = listGamepads();
        const present = new Set();

        for (const pad of pads) {
            if (!pad) {
                continue;
            }

            present.add(pad.index);

            if (!this.connected.has(pad.index)) {
                this.connected.set(pad.index, pad.id);
                this.onConnected(pad.index);
            }
        }

        for (const index of [...this.connected.keys()]) {
            if (!present.has(index)) {
                this.connected.delete(index);
                this.onDisconnected(index);
            }
        }
    }

    stop() {
        window.removeEventListener('gamepadconnected', this._handleConnected);
        window.removeEventListener('gamepaddisconnected', this._handleDisconnected);
        this.started = false;
    }

    register(pad) {
        if (!pad || this.connected.has(pad.index)) {
            return;
        }

        this.connected.set(pad.index, pad.id);
        this.onConnected(pad.index);
    }

    unregister(pad) {
        if (!pad || !this.connected.has(pad.index)) {
            return;
        }

        this.connected.delete(pad.index);
        this.onDisconnected(pad.index);
    }

    get indexes() {
        return [...this.connected.keys()].sort((left, right) => left - right);
    }

    get size() {
        return this.connected.size;
    }

    /** A fresh Gamepad record, or null. The record must be re-read every frame. */
    getGamepad(index) {
        return listGamepads()[index] ?? null;
    }
}

export function listGamepads() {
    if (typeof navigator.getGamepads !== 'function') {
        return [];
    }

    // Keep the array positions: a browser reports an empty slot as null, and the
    // controller index is part of the identity the page shows.
    return Array.prototype.slice.call(navigator.getGamepads());
}

export function gamepadToSnapshot(pad) {
    const device = gamepadToDevice(pad);
    const buttons = [];
    const axes = [];
    const standard = pad.mapping === 'standard';
    const faceButtons =
        device.vendorId === 0x054c ? FACE_BUTTONS_PLAYSTATION : FACE_BUTTONS_XBOX;

    for (let index = 0; index < pad.buttons.length; index++) {
        const button = pad.buttons[index];
        const label = standard ? buttonLabel(index, faceButtons) : `Button ${index + 1}`;

        buttons.push({
            index,
            label,
            pressed: Boolean(button.pressed),
            value: Number.isFinite(button.value) ? button.value : button.pressed ? 1 : 0,
        });
    }

    if (standard) {
        for (let index = 0; index < STANDARD_AXES.length; index++) {
            axes.push(
                axis(
                    index,
                    STANDARD_AXES[index],
                    pad.axes[index] ?? 0,
                    false,
                ),
            );
        }

        // SDL reports the analog triggers as axes, so mirror the browser's analog
        // button values into LT/RT axes for the raw-axis readouts.
        axes.push(axis(4, 'LT', buttons[6]?.value ?? 0, true));
        axes.push(axis(5, 'RT', buttons[7]?.value ?? 0, true));
    } else {
        for (let index = 0; index < pad.axes.length; index++) {
            axes.push(axis(index, `Axis ${index + 1}`, pad.axes[index], false));
        }
    }

    return {
        device,
        buttons,
        axes,
        // The Web Gamepad API does not expose hats.
        hats: [],
        observedAt: Date.now(),
    };
}

export function gamepadToDevice(pad) {
    const standard = pad.mapping === 'standard';
    const identity = parseIdentity(pad.id);

    return {
        // Same shape as DeviceIdentity.Create in SdlControllerSource.cs when a device
        // exposes neither a serial nor a GUID: model:<vendor>:<product>:<name>.
        stableKey: `model:${hex4(identity.vendorId)}:${hex4(identity.productId)}:${identity.name.toLowerCase()}`,
        instanceId: pad.index,
        name: identity.name,
        serial: null,
        vendorId: identity.vendorId,
        productId: identity.productId,
        sdlGuid: null,
        kind: standard ? 'Gamepad' : 'Joystick',
        connection: 'Unknown',
        batteryPercent: null,
        power: 'Unknown',
        isMapped: standard,
        path: null,
    };
}

function axis(index, label, value, isTrigger) {
    const normalized = Number.isFinite(value) ? value : 0;
    const clamped = Math.min(Math.max(normalized, -1), 1);

    return {
        index,
        label,
        value: clamped,
        rawValue: Math.trunc(clamped * 32767),
        isTrigger,
    };
}

function buttonLabel(index, faceButtons) {
    if (index < faceButtons.length) {
        return faceButtons[index];
    }

    return STANDARD_BUTTONS[index] ?? `Button ${index + 1}`;
}

/**
 * Chrome/Firefox ids look like
 *   "Xbox Wireless Controller (STANDARD GAMEPAD Vendor: 045e Product: 0b13)"
 *   "054c-09cc-Wireless Controller" (older Firefox)
 * so read the ids first and fall back to the dashed prefix. The display name is the id
 * with the vendor parenthetical removed.
 */
function parseIdentity(id) {
    const dashed = DASHED_ID_PATTERN.exec(id);
    let vendorId = 0;
    let productId = 0;

    if (dashed) {
        vendorId = Number.parseInt(dashed[1], 16);
        productId = Number.parseInt(dashed[2], 16);
    } else {
        const ids = VENDOR_PRODUCT_PATTERN.exec(id);
        if (ids) {
            vendorId = Number.parseInt(ids[1], 16);
            productId = Number.parseInt(ids[2], 16);
        }
    }

    let name = dashed
        ? id.slice(dashed[0].length)
        : id.replace(/\s*\((?:[^)]*(?:STANDARD GAMEPAD|Vendor:)[^)]*)\)\s*$/i, '');

    name = name.trim();
    if (!name) {
        name = 'Controller';
    }

    return { vendorId, productId, name };
}

function hex4(value) {
    return value.toString(16).toUpperCase().padStart(4, '0');
}
