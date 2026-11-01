/**
 * Port of src/Padmon.App/ViewModels/ControllerTesterViewModel.cs.
 *
 * The view model owns every value the tester shows. Nothing here touches the DOM;
 * app.js binds these fields, the same way the XAML page binds the desktop model.
 */

import { PadCell, PadInputState, BUTTON_SLOTS, SLOT } from './pad-input-state.js';
import { describeController } from './controller-view-model.js';

export class ControllerTesterViewModel {
    constructor(device) {
        Object.assign(this, describeController(device));
        this.device = device;

        this.pad = new PadInputState();

        this.padButtonCells = Array.from(
            { length: BUTTON_SLOTS },
            (_, index) => new PadCell(`B${index}`),
        );
        this.leftAxisCells = [
            new PadCell('AXIS 0', '0.00000'),
            new PadCell('AXIS 1', '0.00000'),
        ];
        this.rightAxisCells = [
            new PadCell('AXIS 2', '0.00000'),
            new PadCell('AXIS 3', '0.00000'),
        ];

        this.buttons = [];
        this.axes = [];
        this.hats = [];

        this.leftStickX = 0;
        this.leftStickY = 0;
        this.rightStickX = 0;
        this.rightStickY = 0;
        this.leftTriggerPercent = 0;
        this.rightTriggerPercent = 0;

        this.leftStickText = '0.00, 0.00';
        this.rightStickText = '0.00, 0.00';
        this.leftTriggerText = '0%';
        this.rightTriggerText = '0%';

        this.connectedText = 'Yes';
        this.mappingText = 'standard';
        this.timestampText = '0.00000';
        this.vibrationText = 'n/a';

        this.leftMagnitudeText = '0.00000';
        this.rightMagnitudeText = '0.00000';
        this.leftXText = '0.00000';
        this.leftYText = '0.00000';
        this.rightXText = '0.00000';
        this.rightYText = '0.00000';
        this.leftBumperText = '0.00';
        this.rightBumperText = '0.00';
        this.leftTriggerValueText = '0.00';
        this.rightTriggerValueText = '0.00';

        // Equivalent of the desktop tester's Stopwatch, which restarts whenever the
        // page is opened for a controller. Reset it when the web tester switches pads.
        this.clockStart = performance.now();
    }

    get hasHats() {
        return this.hats.length > 0;
    }

    resetClock() {
        this.clockStart = performance.now();
        this.timestampText = '0.00000';
    }

    applySnapshot(snapshot) {
        this.syncButtons(snapshot.buttons);
        this.syncAxes(snapshot.axes);
        this.syncHats(snapshot.hats);

        const leftX = findAxis(snapshot.axes, 'Left X');
        const leftY = findAxis(snapshot.axes, 'Left Y');
        const rightX = findAxis(snapshot.axes, 'Right X');
        const rightY = findAxis(snapshot.axes, 'Right Y');
        const leftTrigger = findAxis(snapshot.axes, 'LT');
        const rightTrigger = findAxis(snapshot.axes, 'RT');

        this.leftStickX = leftX?.value ?? 0;
        this.leftStickY = leftY?.value ?? 0;
        this.rightStickX = rightX?.value ?? 0;
        this.rightStickY = rightY?.value ?? 0;
        this.leftTriggerPercent = roundHalfEven((leftTrigger?.value ?? 0) * 100);
        this.rightTriggerPercent = roundHalfEven((rightTrigger?.value ?? 0) * 100);

        this.leftStickText = `${formatSigned(this.leftStickX)}, ${formatSigned(this.leftStickY)}`;
        this.rightStickText = `${formatSigned(this.rightStickX)}, ${formatSigned(this.rightStickY)}`;
        this.leftTriggerText = `${this.leftTriggerPercent}%`;
        this.rightTriggerText = `${this.rightTriggerPercent}%`;

        this.applyPad(snapshot);
    }

    applyPad(snapshot) {
        const pad = PadInputState.from(snapshot);
        this.pad = pad;

        this.mappingText = pad.isStandard ? 'standard' : 'raw';
        this.timestampText = (performance.now() - this.clockStart).toFixed(5);

        for (let index = 0; index < BUTTON_SLOTS; index++) {
            this.padButtonCells[index].set(
                pad.buttons[index],
                PadInputState.formatShort(pad.buttons[index]),
            );
        }

        this.leftAxisCells[0].set(pad.leftX, PadInputState.format(pad.leftX));
        this.leftAxisCells[1].set(pad.leftY, PadInputState.format(pad.leftY));
        this.rightAxisCells[0].set(pad.rightX, PadInputState.format(pad.rightX));
        this.rightAxisCells[1].set(pad.rightY, PadInputState.format(pad.rightY));

        this.leftXText = PadInputState.format(pad.leftX);
        this.leftYText = PadInputState.format(pad.leftY);
        this.rightXText = PadInputState.format(pad.rightX);
        this.rightYText = PadInputState.format(pad.rightY);
        this.leftMagnitudeText = PadInputState.format(
            Math.sqrt(pad.leftX * pad.leftX + pad.leftY * pad.leftY),
        );
        this.rightMagnitudeText = PadInputState.format(
            Math.sqrt(pad.rightX * pad.rightX + pad.rightY * pad.rightY),
        );

        this.leftBumperText = PadInputState.formatShort(pad.buttons[SLOT.LEFT_BUMPER]);
        this.rightBumperText = PadInputState.formatShort(pad.buttons[SLOT.RIGHT_BUMPER]);
        this.leftTriggerValueText = PadInputState.formatShort(pad.buttons[SLOT.LEFT_TRIGGER]);
        this.rightTriggerValueText = PadInputState.formatShort(pad.buttons[SLOT.RIGHT_TRIGGER]);
    }

    syncButtons(buttons) {
        if (this.buttons.length !== buttons.length) {
            this.buttons = buttons.map((button) => new ButtonStateViewModel(button));
            return;
        }

        for (let index = 0; index < buttons.length; index++) {
            this.buttons[index].update(buttons[index]);
        }
    }

    syncAxes(axes) {
        if (this.axes.length !== axes.length) {
            this.axes = axes.map((axis) => new AxisStateViewModel(axis));
            return;
        }

        for (let index = 0; index < axes.length; index++) {
            this.axes[index].update(axes[index]);
        }
    }

    syncHats(hats) {
        if (this.hats.length !== hats.length) {
            this.hats = hats.map((hat) => new HatStateViewModel(hat));
            return;
        }

        for (let index = 0; index < hats.length; index++) {
            this.hats[index].update(hats[index]);
        }
    }
}

export class ButtonStateViewModel {
    constructor(state) {
        this.label = state.label;
        this.pressed = state.pressed;
    }

    update(state) {
        this.label = state.label;
        this.pressed = state.pressed;
    }
}

export class AxisStateViewModel {
    constructor(state) {
        this.label = state.label;
        this.valueText = format(state);
    }

    update(state) {
        this.label = state.label;
        this.valueText = format(state);
    }
}

export class HatStateViewModel {
    constructor(state) {
        this.label = state.label;
        this.valueText = describe(state.value);
    }

    update(state) {
        this.label = state.label;
        this.valueText = describe(state.value);
    }
}

function describe(value) {
    switch (value) {
        case 0:
            return 'Centered';
        case 1:
            return 'Up';
        case 2:
            return 'Right';
        case 3:
            return 'Up + right';
        case 4:
            return 'Down';
        case 6:
            return 'Down + right';
        case 8:
            return 'Left';
        case 9:
            return 'Up + left';
        case 12:
            return 'Down + left';
        default:
            return String(value);
    }
}

function format(state) {
    const normalized = state.isTrigger
        ? `${Math.round(state.value * 100)}%`
        : formatSigned(state.value);

    return `${normalized}  (${state.rawValue})`;
}

function findAxis(axes, label) {
    return axes.find((axis) => axis.label === label) ?? null;
}

/**
 * C# custom format "+0.00;-0.00;0.00": always show the sign, two decimals.
 * Matches the desktop tester, including the "-0.00" it can show for a tiny
 * negative axis value.
 */
function formatSigned(value, digits = 2) {
    const text = value.toFixed(digits);
    return value > 0 ? `+${text}` : text;
}

/**
 * C# Math.Round(double) uses banker's rounding; Math.round in JavaScript does not.
 * Trigger percentages are rounded with the same rule as the desktop tester.
 */
function roundHalfEven(value) {
    const floor = Math.floor(value);
    const fraction = value - floor;

    if (fraction > 0.5) {
        return floor + 1;
    }

    if (fraction < 0.5) {
        return floor;
    }

    return floor % 2 === 0 ? floor : floor + 1;
}
