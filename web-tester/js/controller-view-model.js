/**
 * Port of the display properties in src/Padmon.App/ViewModels/ControllerViewModel.cs.
 *
 * The desktop app keeps a richer device record (serial, SDL GUID, battery, transport).
 * The Gamepad API exposes only a name, an index, vendor/product in the id string and
 * the mapping, so the labels below fall back the same way the desktop ones do when a
 * fact is unknown.
 */

const KIND_LABELS = {
    Gamepad: 'Gamepad',
    Joystick: 'Joystick',
    Wheel: 'Wheel',
    ArcadeStick: 'Arcade stick',
    FlightStick: 'Flight stick',
    DancePad: 'Dance pad',
    Guitar: 'Guitar',
    DrumKit: 'Drum kit',
    ArcadePad: 'Arcade pad',
    Throttle: 'Throttle',
};

const CONNECTION_LABELS = {
    Usb: 'USB',
    Bluetooth: 'Bluetooth',
    Virtual: 'Virtual',
};

export function describeController(device) {
    const kindLabel = KIND_LABELS[device.kind] ?? 'Controller';
    const connectionLabel = CONNECTION_LABELS[device.connection] ?? 'Unknown';

    return {
        stableKey: device.stableKey,
        displayName: device.name,
        hardwareName: device.name,
        kindLabel,
        connectionLabel,
        subtitle: `${kindLabel} · ${connectionLabel}`,
        batteryLabel: batteryLabel(device),
        deviceBadge: device.isMapped ? 'MAPPED' : 'RAW INPUT',
    };
}

function batteryLabel(device) {
    const percent = device.batteryPercent;

    switch (device.power) {
        case 'Charging':
            return Number.isInteger(percent) ? `Charging · ${percent}%` : 'Charging';
        case 'Charged':
            return 'Charged';
        case 'OnBattery':
            return Number.isInteger(percent) ? `${percent}%` : 'On battery';
        case 'Wired':
            return 'USB powered';
        default:
            return Number.isInteger(percent) ? `${percent}%` : 'Battery unknown';
    }
}
