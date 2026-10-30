"""Generate padmon's app icon and tray icon.

This intentionally uses only Pillow so the assets can be regenerated on any
machine without a design tool. The app icon is a dark rounded tile with a
mint gamepad; the tray icon is a transparent monochrome variant.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "Padmon.App" / "Assets"
ASSETS.mkdir(parents=True, exist_ok=True)

SCALE = 4
SIZE = 256
CANVAS = SIZE * SCALE

INK = (11, 18, 16, 255)
TILE_TOP = (24, 42, 35, 255)
TILE_BOTTOM = (9, 16, 14, 255)
MINT = (127, 227, 184, 255)
MINT_DIM = (45, 104, 82, 255)


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def gamepad(draw: ImageDraw.ImageDraw, scale: float = 1.0) -> None:
    """Draw a friendly, generic gamepad silhouette on the current canvas."""
    s = scale

    def box(x0: float, y0: float, x1: float, y1: float) -> tuple[int, int, int, int]:
        return (round(x0 * s), round(y0 * s), round(x1 * s), round(y1 * s))

    # Body and grips.
    draw.rounded_rectangle(box(42, 86, 214, 166), radius=round(36 * s), fill=MINT)
    draw.ellipse(box(40, 128, 108, 202), fill=MINT)
    draw.ellipse(box(148, 128, 216, 202), fill=MINT)

    # D-pad.
    draw.rounded_rectangle(box(78, 112, 112, 122), radius=round(4 * s), fill=INK)
    draw.rounded_rectangle(box(90, 100, 100, 134), radius=round(4 * s), fill=INK)

    # Face buttons.
    for cx, cy in ((172, 108), (188, 120), (172, 132), (156, 120)):
        draw.ellipse(box(cx - 6, cy - 6, cx + 6, cy + 6), fill=INK)

    # Sticks.
    draw.ellipse(box(118, 126, 138, 146), outline=INK, width=round(5 * s))
    draw.ellipse(box(150, 140, 170, 160), outline=INK, width=round(5 * s))

    # Center pill.
    draw.rounded_rectangle(box(123, 101, 137, 109), radius=round(4 * s), fill=INK)


def make_app_icon() -> Image.Image:
    image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    tile = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    tile_draw = ImageDraw.Draw(tile)

    # Vertical graphite gradient.
    for y in range(CANVAS):
        t = y / max(1, CANVAS - 1)
        color = tuple(round(TILE_TOP[i] * (1 - t) + TILE_BOTTOM[i] * t) for i in range(4))
        tile_draw.line((0, y, CANVAS, y), fill=color)

    tile.putalpha(rounded_mask(CANVAS, round(56 * SCALE)))
    image.alpha_composite(tile)

    # A soft inner ring gives the tile a little depth.
    ring = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    ring_draw = ImageDraw.Draw(ring)
    inset = round(9 * SCALE)
    ring_draw.rounded_rectangle(
        (inset, inset, CANVAS - inset - 1, CANVAS - inset - 1),
        radius=round(48 * SCALE),
        outline=(38, 66, 55, 255),
        width=round(2 * SCALE),
    )
    image.alpha_composite(ring)

    pad = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    gamepad(ImageDraw.Draw(pad), scale=SCALE)
    image.alpha_composite(pad)

    return image.resize((SIZE, SIZE), Image.Resampling.LANCZOS)


def make_tray_icon() -> Image.Image:
    image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    gamepad(ImageDraw.Draw(image), scale=SCALE)
    # The tray version should read at 16 px: remove the small interior details
    # that turn to mud, while keeping the silhouette.
    return image.resize((SIZE, SIZE), Image.Resampling.LANCZOS)


def main() -> None:
    app = make_app_icon()
    app.save(ASSETS / "padmon.png")
    app.save(
        ASSETS / "padmon.ico",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    tray = make_tray_icon()
    tray.save(
        ASSETS / "padmon-tray.ico",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64)],
    )
    print(f"Wrote {ASSETS / 'padmon.ico'}")
    print(f"Wrote {ASSETS / 'padmon-tray.ico'}")


if __name__ == "__main__":
    main()
