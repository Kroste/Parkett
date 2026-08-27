"""
Parkett App-Icon-Generator.

Design: drei Kerzen eines Candlestick-Charts in Kroste-Gold auf abgerundetem
dunklem Grund. Ohne Text, damit es auch als 16x16-Favicon lesbar bleibt —
drei senkrechte Balken unterschiedlicher Höhe sind die kleinste Form, die
noch eindeutig nach Börse aussieht.

Erzeugt:
- Parkett/Assets/parkett.png  (256x256, master)
- Parkett/Assets/parkett.ico  (Windows-Multi-Res)
"""

import os

from PIL import Image, ImageDraw

# Kroste-Palette (aus App.axaml)
GOLD = (224, 177, 76, 255)        # #E0B14C — Akzent der App
GOLD_D = (168, 130, 48, 255)      # abgedunkelt für die fallende Kerze
SURFACE = (26, 29, 33, 255)       # #1A1D21
BORDER = (46, 52, 60, 255)        # #2E343C
TRANSP = (0, 0, 0, 0)

SIZE = 256
CORNER = 48

APP_NAME = "parkett"
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Parkett", "Assets")

# (x-Mitte, Docht oben, Körper oben, Körper unten, Docht unten, steigend)
# Werte in 256er-Koordinaten, von links nach rechts gelesen wie ein Chart.
CANDLES = [
    (74, 96, 120, 186, 206, True),
    (128, 62, 84, 150, 172, False),
    (182, 44, 66, 128, 152, True),
]


def make_icon(size: int) -> Image.Image:
    """Baut das Icon in der angegebenen Kantenlänge."""
    scale = size / 256
    img = Image.new("RGBA", (size, size), TRANSP)
    d = ImageDraw.Draw(img)

    corner = int(CORNER * scale)
    d.rounded_rectangle(
        [(0, 0), (size - 1, size - 1)],
        radius=corner,
        fill=SURFACE,
        outline=BORDER,
        width=max(1, int(2 * scale)),
    )

    body_half = max(2, int(17 * scale))
    wick_half = max(1, int(3 * scale))

    for cx, wick_top, body_top, body_bottom, wick_bottom, rising in CANDLES:
        colour = GOLD if rising else GOLD_D
        x = cx * scale

        # Docht: dünner Balken über die volle Höhe
        d.rectangle(
            [(x - wick_half, wick_top * scale), (x + wick_half, wick_bottom * scale)],
            fill=colour,
        )

        # Körper: breiter Balken, abgerundet damit es bei 256px nicht hart wirkt
        radius = max(1, int(4 * scale))
        d.rounded_rectangle(
            [(x - body_half, body_top * scale), (x + body_half, body_bottom * scale)],
            radius=radius,
            fill=colour,
        )

    return img


def main() -> None:
    out_dir = os.path.abspath(OUT_DIR)
    os.makedirs(out_dir, exist_ok=True)

    master = make_icon(SIZE)
    png_path = os.path.join(out_dir, f"{APP_NAME}.png")
    master.save(png_path)

    # Windows-Multi-Res: jede Größe einzeln rendern statt herunterzuskalieren,
    # sonst verschwimmen die Dochte bei 16x16.
    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [make_icon(s) for s in sizes]
    ico_path = os.path.join(out_dir, f"{APP_NAME}.ico")
    frames[-1].save(ico_path, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])

    print(f"geschrieben: {png_path}")
    print(f"geschrieben: {ico_path}")


if __name__ == "__main__":
    main()
