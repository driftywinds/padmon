/**
 * Port of src/Padmon.App/ViewModels/PadInputState.cs.
 *
 * A controller snapshot is normalised onto the W3C "standard gamepad" layout
 * (B0..B17 plus two sticks), which is what the diagram tester is drawn against.
 * Sources label buttons differently (SDL, Windows.Gaming.Input, the simulator, the
 * browser Gamepad API), so buttons are matched by label rather than by index.
 */

export const BUTTON_SLOTS = 18;

export const SLOT = Object.freeze({
    LEFT_BUMPER: 4,
    RIGHT_BUMPER: 5,
    LEFT_TRIGGER: 6,
    RIGHT_TRIGGER: 7,
    BACK: 8,
    START: 9,
    LEFT_STICK: 10,
    RIGHT_STICK: 11,
    D_PAD_UP: 12,
    D_PAD_DOWN: 13,
    D_PAD_LEFT: 14,
    D_PAD_RIGHT: 15,
});

/** One value cell in the tester: a small label, a number, and a level bar. */
export class PadCell {
    static BAR_HEIGHT = 34;

    constructor(label, valueText = '0.00') {
        this.label = label;
        this.valueText = valueText;
        this.fillHeight = 0;
    }

    set(value, text) {
        this.valueText = text;
        this.fillHeight =
            Math.min(Math.max(Math.abs(value), 0), 1) * PadCell.BAR_HEIGHT;
    }
}

export class PadInputState {
    constructor() {
        /** 0..1 value for each of B0..B17. */
        this.buttons = new Array(BUTTON_SLOTS).fill(0);
        this.leftX = 0;
        this.leftY = 0;
        this.rightX = 0;
        this.rightY = 0;

        /** True when the controller exposed recognised gamepad buttons or axes. */
        this.isStandard = false;
    }

    static from(snapshot) {
        const state = new PadInputState();
        let matchedButton = false;
        let matchedAxis = false;

        for (const button of snapshot.buttons) {
            const slot = PadInputState.slotForLabel(button.label);
            if (slot < 0) {
                continue;
            }

            matchedButton = true;
            state.buttons[slot] = Math.max(
                state.buttons[slot],
                button.pressed ? Math.max(button.value, 1) : button.value,
            );
        }

        for (const axis of snapshot.axes) {
            switch (axis.label) {
                case 'Left X':
                    state.leftX = axis.value;
                    matchedAxis = true;
                    break;
                case 'Left Y':
                    state.leftY = axis.value;
                    matchedAxis = true;
                    break;
                case 'Right X':
                    state.rightX = axis.value;
                    matchedAxis = true;
                    break;
                case 'Right Y':
                    state.rightY = axis.value;
                    matchedAxis = true;
                    break;
                case 'LT':
                    state.buttons[SLOT.LEFT_TRIGGER] = clamp(axis.value, 0, 1);
                    matchedAxis = true;
                    break;
                case 'RT':
                    state.buttons[SLOT.RIGHT_TRIGGER] = clamp(axis.value, 0, 1);
                    matchedAxis = true;
                    break;
            }
        }

        state.isStandard = matchedButton || matchedAxis;

        if (!state.isStandard) {
            // Raw joystick: no gamepad mapping, so fall back to positional guesses
            // (button N -> B(N-1), first four axes -> sticks, first hat -> D-pad).
            for (const button of snapshot.buttons) {
                if (button.index >= 0 && button.index < BUTTON_SLOTS) {
                    state.buttons[button.index] = button.pressed ? 1 : 0;
                }
            }

            for (const axis of snapshot.axes) {
                switch (axis.index) {
                    case 0:
                        state.leftX = axis.value;
                        break;
                    case 1:
                        state.leftY = axis.value;
                        break;
                    case 2:
                        state.rightX = axis.value;
                        break;
                    case 3:
                        state.rightY = axis.value;
                        break;
                }
            }

            if (snapshot.hats.length > 0) {
                const hat = snapshot.hats[0].value;
                state.buttons[SLOT.D_PAD_UP] = (hat & 1) !== 0 ? 1 : 0;
                state.buttons[SLOT.D_PAD_DOWN] = (hat & 4) !== 0 ? 1 : 0;
                state.buttons[SLOT.D_PAD_LEFT] = (hat & 8) !== 0 ? 1 : 0;
                state.buttons[SLOT.D_PAD_RIGHT] = (hat & 2) !== 0 ? 1 : 0;
            }
        }

        return state;
    }

    static format(value) {
        // Five decimals like hardwaretester.com; never print "-0.00000".
        let normalized = value;
        if (Math.abs(normalized) < 0.000005) {
            normalized = 0;
        }

        return normalized.toFixed(5);
    }

    static formatShort(value) {
        return value.toFixed(2);
    }

    static slotForLabel(label) {
        switch (label) {
            case 'A':
            case 'Cross':
                return 0;
            case 'B':
            case 'Circle':
                return 1;
            case 'X':
            case 'Square':
                return 2;
            case 'Y':
            case 'Triangle':
                return 3;
            case 'LB':
                return SLOT.LEFT_BUMPER;
            case 'RB':
                return SLOT.RIGHT_BUMPER;
            case 'LT':
                return SLOT.LEFT_TRIGGER;
            case 'RT':
                return SLOT.RIGHT_TRIGGER;
            case 'Back':
                return SLOT.BACK;
            case 'Start':
                return SLOT.START;
            case 'L3':
                return SLOT.LEFT_STICK;
            case 'R3':
                return SLOT.RIGHT_STICK;
            case 'D-Pad Up':
                return SLOT.D_PAD_UP;
            case 'D-Pad Down':
                return SLOT.D_PAD_DOWN;
            case 'D-Pad Left':
                return SLOT.D_PAD_LEFT;
            case 'D-Pad Right':
                return SLOT.D_PAD_RIGHT;
            case 'Guide':
                return 16;
            case 'Touchpad':
            case 'Share':
                return 17;
            default:
                return -1;
        }
    }
}

function clamp(value, minimum, maximum) {
    return Math.min(Math.max(value, minimum), maximum);
}
