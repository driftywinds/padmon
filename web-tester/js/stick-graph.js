/**
 * Port of src/Padmon.App/Controls/StickGraph.xaml(.cs).
 *
 * Circle-and-crosshair plot of one stick, like the L STICK / R STICK graphs on
 * hardwaretester.com. Coordinates match the XAML canvas: 130 x 130, centre 65, radius 60.
 */

const CENTER = 65;
const RADIUS = 60;

const MARKUP = `
<svg class="stick-graph" viewBox="0 0 130 130" role="img" aria-label="Stick position">
    <circle class="stick-graph-grid" cx="65" cy="65" r="60" />
    <line class="stick-graph-grid" x1="65" y1="5" x2="65" y2="125" />
    <line class="stick-graph-grid" x1="5" y1="65" x2="125" y2="65" />
    <line class="stick-graph-needle" x1="65" y1="65" x2="65" y2="65" />
    <circle class="stick-graph-dot" cx="65" cy="65" r="5" />
</svg>
`;

export class StickGraph {
    constructor(host) {
        host.innerHTML = MARKUP;
        this.needle = host.querySelector('.stick-graph-needle');
        this.dot = host.querySelector('.stick-graph-dot');
    }

    /** Plots a stick position; x and y run -1..1 with +y pointing down. */
    setPosition(x, y) {
        const px = CENTER + clamp(x, -1, 1) * RADIUS;
        const py = CENTER + clamp(y, -1, 1) * RADIUS;

        this.needle.setAttribute('x2', String(px));
        this.needle.setAttribute('y2', String(py));
        this.dot.setAttribute('cx', String(px));
        this.dot.setAttribute('cy', String(py));
    }
}

function clamp(value, minimum, maximum) {
    return Math.min(Math.max(value, minimum), maximum);
}
