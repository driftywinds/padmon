/**
 * Port of src/Padmon.App/Controls/ControllerDiagram.xaml(.cs).
 *
 * Controller outline in the style of hardwaretester.com. Pressed buttons fill red, the
 * triggers fade from transparent to red with travel, and the sticks move with the input.
 * The geometry below is copied from the XAML canvas (1120 x 1010 units) with the same
 * paths, wells, rotate transforms and theme brushes.
 */

import { SLOT } from './pad-input-state.js';

// How far a fully deflected stick travels, in diagram units.
const STICK_TRAVEL = 30;

// A stick counts as "in use" once it leaves the resting noise band.
const STICK_ACTIVE_THRESHOLD = 0.06;

const BODY_PATH =
    'M 330,268 L 790,268 C 880,268 935,310 965,390 C 1000,490 1050,640 1070,760 ' +
    'C 1082,860 1062,930 985,945 C 920,952 880,900 850,860 C 815,810 790,750 735,750 ' +
    'L 370,750 C 320,750 290,800 250,860 C 215,905 190,945 148,946 C 70,945 38,880 48,780 ' +
    'C 60,680 100,560 135,450 C 160,370 190,300 250,280 C 280,270 305,268 330,268 Z';

const D_PAD_PATH =
    'M 283,362 Q 305,355 327,362 Q 338,367 333,378 L 316,401 Q 305,411 294,401 ' +
    'L 277,378 Q 272,367 283,362 Z';

const FACE_PATH =
    'M 796,362 Q 818,355 840,362 Q 851,367 846,378 L 829,401 Q 818,411 807,401 ' +
    'L 790,378 Q 785,367 796,362 Z';

const MARKUP = `
<svg class="controller-diagram" viewBox="0 0 1120 1010" role="img" aria-label="Controller diagram">
    <!-- Body shadow, body, and the faint inner creases -->
    <g transform="translate(0 45)">
        <path class="diagram-body-shadow" d="${BODY_PATH}" />
    </g>
    <path class="diagram-body" d="${BODY_PATH}" />
    <line class="diagram-crease" x1="110" y1="548" x2="345" y2="750" />
    <line class="diagram-crease" x1="1010" y1="548" x2="775" y2="750" />

    <!-- Wells: D-pad, face buttons, sticks -->
    <circle class="diagram-well" cx="305" cy="428" r="90" />
    <circle class="diagram-well" cx="818" cy="428" r="90" />
    <circle class="diagram-well" cx="432" cy="613" r="90" />
    <circle class="diagram-well" cx="698" cy="614" r="90" />

    <!-- Triggers (fill fades from transparent to red) -->
    <rect id="LeftTriggerFill" class="diagram-trigger-fill" x="332" y="55" width="68" height="98" rx="32" ry="32" opacity="0" />
    <rect class="diagram-outline-shape" x="332" y="55" width="68" height="98" rx="32" ry="32" />
    <rect id="RightTriggerFill" class="diagram-trigger-fill" x="724" y="55" width="69" height="98" rx="32" ry="32" opacity="0" />
    <rect class="diagram-outline-shape" x="724" y="55" width="69" height="98" rx="32" ry="32" />

    <!-- Bumpers -->
    <rect id="LeftBumper" class="diagram-button" x="300" y="193" width="100" height="33" rx="16" ry="16" />
    <rect id="RightBumper" class="diagram-button" x="724" y="193" width="100" height="33" rx="16" ry="16" />

    <!-- Back / Start -->
    <circle id="BackButton" class="diagram-button" cx="476" cy="433" r="24" />
    <circle id="StartButton" class="diagram-button" cx="652" cy="433" r="24" />

    <!-- D-pad and face buttons -->
    <g transform="rotate(0 305 428)">
        <path id="DPadUp" class="diagram-button" d="${D_PAD_PATH}" />
    </g>
    <g transform="rotate(90 305 428)">
        <path id="DPadRight" class="diagram-button" d="${D_PAD_PATH}" />
    </g>
    <g transform="rotate(180 305 428)">
        <path id="DPadDown" class="diagram-button" d="${D_PAD_PATH}" />
    </g>
    <g transform="rotate(270 305 428)">
        <path id="DPadLeft" class="diagram-button" d="${D_PAD_PATH}" />
    </g>
    <g transform="rotate(0 818 428)">
        <path id="FaceY" class="diagram-button" d="${FACE_PATH}" />
    </g>
    <g transform="rotate(90 818 428)">
        <path id="FaceB" class="diagram-button" d="${FACE_PATH}" />
    </g>
    <g transform="rotate(180 818 428)">
        <path id="FaceA" class="diagram-button" d="${FACE_PATH}" />
    </g>
    <g transform="rotate(270 818 428)">
        <path id="FaceX" class="diagram-button" d="${FACE_PATH}" />
    </g>

    <!-- Sticks: knob backdrop hides the well; knob turns red when moved or clicked -->
    <circle id="LeftKnobBackdrop" class="diagram-knob-backdrop" cx="432" cy="613" r="68" />
    <circle id="LeftKnob" class="diagram-knob" cx="432" cy="613" r="68" />
    <circle id="RightKnobBackdrop" class="diagram-knob-backdrop" cx="698" cy="614" r="68" />
    <circle id="RightKnob" class="diagram-knob" cx="698" cy="614" r="68" />
</svg>
`;

export class ControllerDiagram {
    constructor(host) {
        host.innerHTML = MARKUP;

        const element = (id) => host.querySelector(`#${id}`);

        this.leftTriggerFill = element('LeftTriggerFill');
        this.rightTriggerFill = element('RightTriggerFill');

        this.pressedShapes = [
            [element('FaceA'), 0],
            [element('FaceB'), 1],
            [element('FaceX'), 2],
            [element('FaceY'), 3],
            [element('LeftBumper'), SLOT.LEFT_BUMPER],
            [element('RightBumper'), SLOT.RIGHT_BUMPER],
            [element('BackButton'), SLOT.BACK],
            [element('StartButton'), SLOT.START],
            [element('DPadUp'), SLOT.D_PAD_UP],
            [element('DPadDown'), SLOT.D_PAD_DOWN],
            [element('DPadLeft'), SLOT.D_PAD_LEFT],
            [element('DPadRight'), SLOT.D_PAD_RIGHT],
        ];

        this.sticks = [
            {
                knob: element('LeftKnob'),
                backdrop: element('LeftKnobBackdrop'),
                clickedSlot: SLOT.LEFT_STICK,
                x: (pad) => pad.leftX,
                y: (pad) => pad.leftY,
            },
            {
                knob: element('RightKnob'),
                backdrop: element('RightKnobBackdrop'),
                clickedSlot: SLOT.RIGHT_STICK,
                x: (pad) => pad.rightX,
                y: (pad) => pad.rightY,
            },
        ];
    }

    update(pad) {
        const buttons = pad.buttons;

        for (const [shape, slot] of this.pressedShapes) {
            shape.classList.toggle('is-pressed', buttons[slot] >= 0.5);
        }

        this.leftTriggerFill.setAttribute(
            'opacity',
            String(clamp(buttons[SLOT.LEFT_TRIGGER], 0, 1)),
        );
        this.rightTriggerFill.setAttribute(
            'opacity',
            String(clamp(buttons[SLOT.RIGHT_TRIGGER], 0, 1)),
        );

        for (const stick of this.sticks) {
            moveStick(
                stick.knob,
                stick.backdrop,
                stick.x(pad),
                stick.y(pad),
                buttons[stick.clickedSlot] >= 0.5,
            );
        }
    }
}

function moveStick(knob, backdrop, x, y, clicked) {
    let normalizedX = x;
    let normalizedY = y;
    let magnitude = Math.sqrt(normalizedX * normalizedX + normalizedY * normalizedY);

    if (magnitude > 1) {
        normalizedX /= magnitude;
        normalizedY /= magnitude;
        magnitude = 1;
    }

    const transform = `translate(${normalizedX * STICK_TRAVEL} ${normalizedY * STICK_TRAVEL})`;
    knob.setAttribute('transform', transform);
    backdrop.setAttribute('transform', transform);

    knob.classList.toggle(
        'is-pressed',
        clicked || magnitude > STICK_ACTIVE_THRESHOLD,
    );
}

function clamp(value, minimum, maximum) {
    return Math.min(Math.max(value, minimum), maximum);
}
