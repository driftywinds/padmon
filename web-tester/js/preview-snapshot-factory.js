/**
 * Port of src/Padmon.App/Services/PreviewSnapshotFactory.cs.
 *
 * Builds animated sample input for the tester's preview mode, which the desktop app
 * opens from Settings when no controller is connected. Every button is pressed in
 * turn, the sticks sweep and the triggers ramp, so the whole tester UI can be
 * exercised without hardware. The web tester uses the same animation when the
 * browser exposes no gamepad yet.
 */

const BUTTON_LABELS = [
    'A', 'B', 'X', 'Y', 'LB', 'RB', 'Back', 'Start', 'L3', 'R3',
    'D-Pad Up', 'D-Pad Down', 'D-Pad Left', 'D-Pad Right', 'Guide',
];

// Seconds each button stays pressed; a few empty steps pad the loop.
const STEP_SECONDS = 0.6;
const EMPTY_STEPS = 4;

export const PREVIEW_DEVICE = Object.freeze({
    stableKey: 'preview:demo',
    instanceId: 0,
    name: 'Preview controller',
    serial: null,
    vendorId: 0,
    productId: 0,
    sdlGuid: null,
    kind: 'Gamepad',
    connection: 'Virtual',
    batteryPercent: 100,
    power: 'Wired',
    isMapped: true,
    path: null,
});

export function createPreviewSnapshot(seconds) {
    const step =
        Math.trunc(seconds / STEP_SECONDS) % (BUTTON_LABELS.length + EMPTY_STEPS);

    const buttons = BUTTON_LABELS.map((label, index) => {
        const pressed = index === step;
        return { index, label, pressed, value: pressed ? 1 : 0 };
    });

    const leftReach = 0.6 + 0.4 * Math.sin(seconds * 0.7);
    const leftX = Math.sin(seconds * 1.2) * leftReach;
    const leftY = Math.cos(seconds * 1.2) * leftReach;
    const rightX = Math.sin(seconds * 0.8) * 0.9;
    const rightY = Math.cos(seconds * 1.3) * 0.6;
    const leftTrigger = (Math.sin(seconds * 0.9) + 1) / 2;
    const rightTrigger = (Math.sin(seconds * 0.6 + 1.4) + 1) / 2;

    const axes = [
        axis(0, 'Left X', leftX, false),
        axis(1, 'Left Y', leftY, false),
        axis(2, 'Right X', rightX, false),
        axis(3, 'Right Y', rightY, false),
        axis(4, 'LT', leftTrigger, true),
        axis(5, 'RT', rightTrigger, true),
    ];

    return {
        device: PREVIEW_DEVICE,
        buttons,
        axes,
        hats: [],
        observedAt: Date.now(),
    };
}

function axis(index, label, value, isTrigger) {
    // The desktop model stores the short reading alongside the normalized value;
    // the cast in PreviewSnapshotFactory truncates toward zero.
    return {
        index,
        label,
        value,
        rawValue: Math.trunc(value * 32767),
        isTrigger,
    };
}
