/**
 * Port of the tester page wiring in
 * src/Padmon.App/Views/ControllerDetailPage.xaml(.cs).
 *
 * The desktop page binds the tester view model to XAML, polls the controller source
 * for snapshots, shows a simulated preview when no controller is connected, and keeps
 * the last state on screen after a disconnect. This file does the same against the DOM,
 * with the browser Gamepad API in place of SDL.
 */

import { GamepadSource, gamepadToDevice, gamepadToSnapshot } from './gamepad-source.js';
import { ControllerTesterViewModel } from './controller-tester-view-model.js';
import { PREVIEW_DEVICE, createPreviewSnapshot } from './preview-snapshot-factory.js';
import { ControllerDiagram } from './controller-diagram.js';
import { StickGraph } from './stick-graph.js';

const THEME_KEY = 'padmon-web-theme';
const VIEW_KEY = 'padmon-web-view';
const THEMES = ['system', 'light', 'dark'];

// Same wide/narrow breakpoint as PageRoot_SizeChanged in ControllerDetailPage.
const WIDE_LAYOUT_WIDTH = 1060;

const elements = {
    pageRoot: document.getElementById('pageRoot'),
    controllerName: document.getElementById('controllerName'),
    controllerSubtitle: document.getElementById('controllerSubtitle'),
    batteryText: document.getElementById('batteryText'),
    rumbleButton: document.getElementById('rumbleButton'),
    rumbleBar: document.getElementById('rumbleBar'),
    rumbleBarClose: document.getElementById('rumbleBarClose'),
    disconnectedBar: document.getElementById('disconnectedBar'),
    previewBar: document.getElementById('previewBar'),

    diagramView: document.getElementById('diagramView'),
    classicView: document.getElementById('classicView'),

    indexText: document.getElementById('indexText'),
    connectedText: document.getElementById('connectedText'),
    mappingText: document.getElementById('mappingText'),
    timestampText: document.getElementById('timestampText'),
    vibrationText: document.getElementById('vibrationText'),

    padButtonCells: document.getElementById('padButtonCells'),
    leftAxisCells: document.getElementById('leftAxisCells'),
    rightAxisCells: document.getElementById('rightAxisCells'),
    leftGraph: document.getElementById('leftGraph'),
    rightGraph: document.getElementById('rightGraph'),
    diagram: document.getElementById('diagram'),

    leftXText: document.getElementById('leftXText'),
    leftYText: document.getElementById('leftYText'),
    leftMagnitudeText: document.getElementById('leftMagnitudeText'),
    rightXText: document.getElementById('rightXText'),
    rightYText: document.getElementById('rightYText'),
    rightMagnitudeText: document.getElementById('rightMagnitudeText'),
    leftTriggerValueText: document.getElementById('leftTriggerValueText'),
    rightTriggerValueText: document.getElementById('rightTriggerValueText'),
    leftBumperText: document.getElementById('leftBumperText'),
    rightBumperText: document.getElementById('rightBumperText'),

    leftStickDot: document.getElementById('leftStickDot'),
    rightStickDot: document.getElementById('rightStickDot'),
    leftStickText: document.getElementById('leftStickText'),
    rightStickText: document.getElementById('rightStickText'),
    leftTriggerText: document.getElementById('leftTriggerText'),
    rightTriggerText: document.getElementById('rightTriggerText'),
    leftTriggerBar: document.getElementById('leftTriggerBar'),
    rightTriggerBar: document.getElementById('rightTriggerBar'),

    buttonGrid: document.getElementById('buttonGrid'),
    mappingBadge: document.getElementById('mappingBadge'),
    axisList: document.getElementById('axisList'),

    hardwareText: document.getElementById('hardwareText'),
    kindText: document.getElementById('kindText'),
    connectionText: document.getElementById('connectionText'),
    stableKeyText: document.getElementById('stableKeyText'),
    hatsText: document.getElementById('hatsText'),

    controllerSelect: document.getElementById('controllerSelect'),
    themeButton: document.getElementById('themeButton'),
    themeIcons: {
        system: document.getElementById('themeIconSystem'),
        light: document.getElementById('themeIconLight'),
        dark: document.getElementById('themeIconDark'),
    },
    viewButtons: [...document.querySelectorAll('[data-view]')],
};

const lightScheme = window.matchMedia('(prefers-color-scheme: light)');

class TesterApp {
    constructor() {
        this.tester = null;
        this.mode = 'preview';
        this.activeIndex = null;
        this.previewStart = 0;

        this.buttonSignature = null;
        this.axisSignature = null;
        this.hatsLabel = null;
        this.buttonElements = [];
        this.axisElements = [];

        this.diagram = new ControllerDiagram(elements.diagram);
        this.leftGraph = new StickGraph(elements.leftGraph);
        this.rightGraph = new StickGraph(elements.rightGraph);

        this.padCells = createCells(elements.padButtonCells, 18, (index) => `B${index}`, '0.00');
        this.leftAxisCells = createCells(
            elements.leftAxisCells,
            2,
            (index) => `AXIS ${index}`,
            '0.00000',
        );
        this.rightAxisCells = createCells(
            elements.rightAxisCells,
            2,
            (index) => `AXIS ${index + 2}`,
            '0.00000',
        );

        this.source = new GamepadSource({
            onConnected: (index) => this.handleConnected(index),
            onDisconnected: (index) => this.handleDisconnected(index),
        });

        this.tick = this.tick.bind(this);

        this.bindEvents();
        this.applyTheme(readPreference());
        this.applyView(readStorage(VIEW_KEY) === 'classic' ? 'classic' : 'diagram');
        this.observeLayout();
    }

    start() {
        this.source.start();

        // Attaching a pad that the browser reports before the first frame can fail;
        // fall back to the preview so the page always has a tester to draw.
        if (this.mode !== 'live') {
            this.startPreview();
        }

        this.render();
        requestAnimationFrame(this.tick);
    }

    bindEvents() {
        elements.rumbleButton.addEventListener('click', () => this.rumble());

        elements.rumbleBarClose.addEventListener('click', () => {
            elements.rumbleBar.hidden = true;
        });

        elements.themeButton.addEventListener('click', () => {
            const current = readPreference();
            const next = THEMES[(THEMES.indexOf(current) + 1) % THEMES.length];
            writeStorage(THEME_KEY, next);
            this.applyTheme(next);
        });

        for (const button of elements.viewButtons) {
            button.addEventListener('click', () => this.applyView(button.dataset.view));
        }

        elements.controllerSelect.addEventListener('change', () => {
            this.attachGamepad(Number.parseInt(elements.controllerSelect.value, 10));
        });

        // The system preference can change while the page is open.
        const onSchemeChange = () => {
            if (readPreference() === 'system') {
                this.applyTheme('system');
            }
        };

        if (typeof lightScheme.addEventListener === 'function') {
            lightScheme.addEventListener('change', onSchemeChange);
        } else if (typeof lightScheme.addListener === 'function') {
            lightScheme.addListener(onSchemeChange);
        }
    }

    observeLayout() {
        if (typeof ResizeObserver !== 'function') {
            return;
        }

        const observer = new ResizeObserver((entries) => {
            const entry = entries[0];
            const width =
                entry.borderBoxSize?.[0]?.inlineSize ??
                entry.target.getBoundingClientRect().width;

            elements.pageRoot.classList.toggle('is-narrow', width < WIDE_LAYOUT_WIDTH);
        });

        observer.observe(elements.pageRoot);
    }

    handleConnected(index) {
        this.refreshControllerSelect();

        // A second controller joining should not steal the tester from the first.
        if (this.mode === 'live') {
            return;
        }

        this.attachGamepad(index);
    }

    handleDisconnected(index) {
        this.refreshControllerSelect();

        if (this.mode !== 'live' || index !== this.activeIndex) {
            return;
        }

        this.activePadLost();
    }

    /** The tester follows a connected pad; only freeze once nothing else is connected. */
    activePadLost() {
        const next = this.source.indexes.find((index) => index !== this.activeIndex);

        if (next !== undefined) {
            this.attachGamepad(next);
            return;
        }

        this.markDisconnected();
        this.render();
    }

    attachGamepad(index) {
        const pad = this.source.getGamepad(index);
        if (!pad) {
            return;
        }

        this.mode = 'live';
        this.activeIndex = index;
        this.tester = new ControllerTesterViewModel(gamepadToDevice(pad));
        this.tester.resetClock();

        elements.previewBar.hidden = true;
        elements.disconnectedBar.hidden = true;
        elements.rumbleBar.hidden = true;

        this.applyStaticUi({ preview: false });
        this.tester.applySnapshot(gamepadToSnapshot(pad));
        this.refreshControllerSelect();
        this.render();
    }

    startPreview() {
        this.mode = 'preview';
        this.activeIndex = null;
        this.previewStart = performance.now();
        this.tester = new ControllerTesterViewModel(PREVIEW_DEVICE);

        elements.disconnectedBar.hidden = true;
        elements.rumbleBar.hidden = true;
        elements.previewBar.hidden = false;

        this.applyStaticUi({ preview: true });
        this.refreshControllerSelect();
    }

    markDisconnected() {
        this.mode = 'disconnected';

        if (this.tester) {
            this.tester.connectedText = 'No';
        }

        elements.disconnectedBar.hidden = false;
        elements.previewBar.hidden = true;
    }

    applyStaticUi({ preview }) {
        const tester = this.tester;
        if (!tester) {
            return;
        }

        elements.controllerName.textContent = preview ? 'Preview controller' : tester.displayName;
        elements.controllerSubtitle.textContent = preview
            ? 'Simulated input · no controller connected'
            : tester.subtitle;
        elements.batteryText.textContent = preview ? 'Preview' : tester.batteryLabel;
        elements.mappingBadge.textContent = preview ? 'PREVIEW' : tester.deviceBadge;
        elements.indexText.textContent = String(preview ? 0 : tester.device.instanceId ?? 0);

        elements.hardwareText.textContent = tester.hardwareName;
        elements.kindText.textContent = tester.kindLabel;
        elements.connectionText.textContent = tester.connectionLabel;
        elements.stableKeyText.textContent = tester.stableKey;
    }

    applyTheme(preference) {
        const resolved =
            preference === 'system'
                ? lightScheme.matches
                    ? 'light'
                    : 'dark'
                : preference;

        document.documentElement.dataset.theme = resolved;

        for (const [name, icon] of Object.entries(elements.themeIcons)) {
            icon.hidden = name !== preference;
        }

        elements.themeButton.title = `Theme: ${preference}`;
        elements.themeButton.setAttribute(
            'aria-label',
            `Theme: ${preference}. Change theme`,
        );
    }

    applyView(view) {
        const diagram = view !== 'classic';

        elements.diagramView.hidden = !diagram;
        elements.classicView.hidden = diagram;

        for (const button of elements.viewButtons) {
            const selected = button.dataset.view === (diagram ? 'diagram' : 'classic');
            button.classList.toggle('is-selected', selected);
            button.setAttribute('aria-pressed', String(selected));
        }

        writeStorage(VIEW_KEY, diagram ? 'diagram' : 'classic');
    }

    refreshControllerSelect() {
        const indexes = this.source.indexes;

        if (indexes.length < 2) {
            elements.controllerSelect.hidden = true;
            return;
        }

        const options = indexes.map((index) => {
            const pad = this.source.getGamepad(index);
            const name = pad ? gamepadToDevice(pad).name : 'Controller';
            return new Option(`${index} · ${name}`, String(index));
        });

        elements.controllerSelect.replaceChildren(...options);
        elements.controllerSelect.hidden = false;

        elements.controllerSelect.value = String(
            indexes.includes(this.activeIndex) ? this.activeIndex : indexes[0],
        );
    }

    async rumble() {
        if (!this.tester) {
            return;
        }

        if (this.mode === 'preview') {
            this.tester.vibrationText = 'n/a (preview)';
            this.render();
            return;
        }

        const pad = this.mode === 'live' ? this.source.getGamepad(this.activeIndex) : null;
        const actuator = pad?.vibrationActuator;

        if (!actuator || typeof actuator.playEffect !== 'function') {
            this.showRumbleUnavailable();
            return;
        }

        try {
            await actuator.playEffect('dual-rumble', {
                startDelay: 0,
                duration: 1500,
                weakMagnitude: 1,
                strongMagnitude: 1,
            });

            elements.rumbleBar.hidden = true;
            this.tester.vibrationText = 'ok';
        } catch {
            this.showRumbleUnavailable();
        }

        this.render();
    }

    showRumbleUnavailable() {
        elements.rumbleBar.hidden = false;
        if (this.tester) {
            this.tester.vibrationText = 'unavailable';
        }

        this.render();
    }

    tick(now) {
        requestAnimationFrame(this.tick);

        if (!this.tester) {
            return;
        }

        // A browser can report a pad a frame before getGamepads() returns it.
        if (this.mode === 'preview' && this.source.size > 0) {
            this.attachGamepad(this.source.indexes[0]);
        }

        if (this.mode === 'preview') {
            const seconds = (now - this.previewStart) / 1000;
            this.tester.applySnapshot(createPreviewSnapshot(seconds));
            this.render();
            return;
        }

        if (this.mode !== 'live') {
            return;
        }

        const pad = this.source.getGamepad(this.activeIndex);
        if (!pad || pad.connected === false) {
            this.activePadLost();
            return;
        }

        this.tester.applySnapshot(gamepadToSnapshot(pad));
        this.render();
    }

    render() {
        const tester = this.tester;
        if (!tester) {
            return;
        }

        elements.connectedText.textContent = tester.connectedText;
        elements.mappingText.textContent = tester.mappingText;
        elements.timestampText.textContent = tester.timestampText;
        elements.vibrationText.textContent = tester.vibrationText;

        for (let index = 0; index < this.padCells.length; index++) {
            updateCell(this.padCells[index], tester.padButtonCells[index]);
        }

        for (let index = 0; index < this.leftAxisCells.length; index++) {
            updateCell(this.leftAxisCells[index], tester.leftAxisCells[index]);
            updateCell(this.rightAxisCells[index], tester.rightAxisCells[index]);
        }

        setText(elements.leftXText, tester.leftXText);
        setText(elements.leftYText, tester.leftYText);
        setText(elements.leftMagnitudeText, tester.leftMagnitudeText);
        setText(elements.rightXText, tester.rightXText);
        setText(elements.rightYText, tester.rightYText);
        setText(elements.rightMagnitudeText, tester.rightMagnitudeText);
        setText(elements.leftTriggerValueText, tester.leftTriggerValueText);
        setText(elements.rightTriggerValueText, tester.rightTriggerValueText);
        setText(elements.leftBumperText, tester.leftBumperText);
        setText(elements.rightBumperText, tester.rightBumperText);

        // Classic tester cards
        setDot(elements.leftStickDot, tester.leftStickX, tester.leftStickY);
        setDot(elements.rightStickDot, tester.rightStickX, tester.rightStickY);
        setText(elements.leftStickText, tester.leftStickText);
        setText(elements.rightStickText, tester.rightStickText);
        setText(elements.leftTriggerText, tester.leftTriggerText);
        setText(elements.rightTriggerText, tester.rightTriggerText);

        elements.leftTriggerBar.style.width = `${clamp(tester.leftTriggerPercent, 0, 100)}%`;
        elements.rightTriggerBar.style.width = `${clamp(tester.rightTriggerPercent, 0, 100)}%`;

        this.renderButtons(tester);
        this.renderAxes(tester);
        this.renderHats(tester);

        // Diagram tester
        const pad = tester.pad;
        this.diagram.update(pad);
        this.leftGraph.setPosition(pad.leftX, pad.leftY);
        this.rightGraph.setPosition(pad.rightX, pad.rightY);
    }

    renderButtons(tester) {
        const signature = tester.buttons.map((button) => button.label).join('\u0000');

        if (signature !== this.buttonSignature) {
            this.buttonSignature = signature;
            this.buttonElements = tester.buttons.map((button) => {
                const element = document.createElement('span');
                element.className = 'pad-toggle';
                element.textContent = button.label;
                return element;
            });

            elements.buttonGrid.replaceChildren(...this.buttonElements);
        }

        for (let index = 0; index < this.buttonElements.length; index++) {
            this.buttonElements[index].classList.toggle(
                'is-pressed',
                tester.buttons[index].pressed,
            );
        }
    }

    renderAxes(tester) {
        const signature = tester.axes.map((axis) => axis.label).join('\u0000');

        if (signature !== this.axisSignature) {
            this.axisSignature = signature;
            this.axisElements = tester.axes.map((axis) => {
                const row = document.createElement('div');
                row.className = 'axis-row';

                const label = document.createElement('span');
                label.className = 'axis-label';
                label.textContent = axis.label;

                const value = document.createElement('span');
                value.className = 'axis-value';

                row.append(label, value);
                return { row, value };
            });

            elements.axisList.replaceChildren(...this.axisElements.map((item) => item.row));
        }

        for (let index = 0; index < this.axisElements.length; index++) {
            setText(this.axisElements[index].value, tester.axes[index].valueText);
        }
    }

    renderHats(tester) {
        const label = tester.hats.length
            ? tester.hats.map((hat) => hat.valueText).join(', ')
            : '—';

        if (label !== this.hatsLabel) {
            this.hatsLabel = label;
            elements.hatsText.textContent = label;
        }
    }
}

function createCells(host, count, labelFor, valueText) {
    const cells = [];

    for (let index = 0; index < count; index++) {
        const cell = document.createElement('div');
        cell.className = 'pad-cell';

        const bar = document.createElement('div');
        bar.className = 'pad-cell-bar';

        const fill = document.createElement('div');
        fill.className = 'pad-cell-fill';
        bar.append(fill);

        const text = document.createElement('div');
        text.className = 'pad-cell-text';

        const label = document.createElement('span');
        label.className = 'pad-cell-label';
        label.textContent = labelFor(index);

        const value = document.createElement('span');
        value.className = 'pad-cell-value';
        value.textContent = valueText;

        text.append(label, value);
        cell.append(bar, text);
        host.append(cell);

        cells.push({ fill, value });
    }

    return cells;
}

function updateCell(cell, model) {
    setText(cell.value, model.valueText);
    cell.fill.style.height = `${model.fillHeight}px`;
}

function setText(element, text) {
    if (element.textContent !== text) {
        element.textContent = text;
    }
}

function setDot(circle, x, y) {
    // Matches radius = 52 in UpdateStickVisuals; +y points down.
    circle.setAttribute('cx', String(71 + x * 52));
    circle.setAttribute('cy', String(71 + y * 52));
}

function clamp(value, minimum, maximum) {
    return Math.min(Math.max(value, minimum), maximum);
}

function readStorage(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

function writeStorage(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch {
        // Private modes and blocked storage should not break the tester.
    }
}

function readPreference() {
    const stored = readStorage(THEME_KEY);
    return THEMES.includes(stored) ? stored : 'system';
}

const app = new TesterApp();
app.start();
