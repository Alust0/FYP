"""Makes the hover pad images: one Vuforia target per pad in Assets/StreamingAssets/HoverPads,
and an A4 sheet to print them at real size in Printables/. Run again to remake them; the
random seeds are fixed, so the same pads come out every time.

Vuforia tracks an image by its corners, so each pad is many overlapping high-contrast shapes
(no repeats, no large flat areas) with a label in the middle so people can tell them apart."""

import os
import random

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TARGET_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "HoverPads")
PRINT_DIR = os.path.join(ROOT, "Printables")

# File name (= HoverPads.PadAction in Unity), label on the pad, line under it on the print sheet
PADS = [
    ("NextPhase", "NEXT\nPHASE", "Next Phase: ends your current phase"),
    ("Draw", "DRAW", "Draw: 1 card from your deck"),
    ("SoulCharge", "SOUL\nCHARGE", "Soul Charge: top of deck to soul"),
    ("CounterBlast", "COUNTER\nBLAST", "Counter Blast: pay 1 face-up damage"),
]

TARGET_PX = 600
DPI = 300
PAD_CM = 6.0  # must match HoverPads.padSize (0.06 m)


def font(size):
    for name in ("arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default(size=size)


def pad_image(seed, label):
    rnd = random.Random(seed)
    img = Image.new("RGB", (TARGET_PX, TARGET_PX), "white")
    draw = ImageDraw.Draw(img)

    for _ in range(240):
        x, y = rnd.randrange(TARGET_PX), rnd.randrange(TARGET_PX)
        w, h = rnd.randrange(10, 80), rnd.randrange(10, 80)
        if rnd.random() < 0.75:
            shade = rnd.choice([0, 0, 30, 80, 140, 200, 255])
            colour = (shade, shade, shade)
        else:
            colour = tuple(rnd.randrange(256) for _ in range(3))

        if rnd.random() < 0.55:
            draw.rectangle([x, y, x + w, y + h], fill=colour)
        else:
            draw.polygon([(x, y), (x + w, y + rnd.randrange(-40, 40)), (x + rnd.randrange(-40, 40), y + h)], fill=colour)

    text_font = font(70)
    left, top, right, bottom = draw.multiline_textbbox((0, 0), label, font=text_font, align="center", spacing=6)
    tw, th = right - left, bottom - top
    tx, ty = (TARGET_PX - tw) / 2, (TARGET_PX - th) / 2
    draw.rectangle([tx - 22, ty - 22, tx + tw + 22, ty + th + 22], fill="white", outline="black", width=8)
    draw.multiline_text((tx - left, ty - top), label, font=text_font, fill="black", align="center", spacing=6)

    draw.rectangle([0, 0, TARGET_PX - 1, TARGET_PX - 1], outline="black", width=16)
    return img


def print_sheet(pads):
    width, height = int(21.0 / 2.54 * DPI), int(29.7 / 2.54 * DPI)  # A4
    pad_px = round(PAD_CM / 2.54 * DPI)
    sheet = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(sheet)

    draw.text((width / 2, 170), "Hover pads", font=font(90), fill="black", anchor="mm")
    draw.text((width / 2, 290), "Print at 100% / actual size (not 'fit to page'). Each square should measure 6 cm.",
              font=font(40), fill="black", anchor="mm")
    draw.text((width / 2, 350), "Place them beside your mat where the camera sees them. Hold a hand over one to use it.",
              font=font(40), fill="black", anchor="mm")

    gap_x = (width - 2 * pad_px) / 3
    for i, ((_, _, caption), img) in enumerate(zip(PADS, pads)):
        col, row = i % 2, i // 2
        x = round(gap_x + col * (pad_px + gap_x))
        y = 550 + row * (pad_px + 420)
        sheet.paste(img.resize((pad_px, pad_px), Image.LANCZOS), (x, y))
        draw.text((x + pad_px / 2, y + pad_px + 70), caption, font=font(42), fill="black", anchor="mm")
        # Dashed cut guide around the pad, outside the pattern
        for side in range(0, pad_px + 80, 40):
            for sx, sy in ((x - 40 + side, y - 40), (x - 40 + side, y + pad_px + 20), (x - 40, y - 40 + side), (x + pad_px + 20, y - 40 + side)):
                draw.rectangle([sx, sy, sx + 18, sy + 18], fill=(170, 170, 170))
    return sheet


def main():
    os.makedirs(TARGET_DIR, exist_ok=True)
    os.makedirs(PRINT_DIR, exist_ok=True)

    pads = []
    for index, (name, label, _) in enumerate(PADS):
        img = pad_image(1000 + index * 7919, label)
        img.save(os.path.join(TARGET_DIR, name + ".png"))
        pads.append(img)

    sheet = print_sheet(pads)
    sheet.save(os.path.join(PRINT_DIR, "HoverPads_A4.png"), dpi=(DPI, DPI))
    sheet.save(os.path.join(PRINT_DIR, "HoverPads_A4.pdf"), resolution=DPI)
    print("Wrote", len(pads), "pads to", TARGET_DIR, "and the print sheet to", PRINT_DIR)


if __name__ == "__main__":
    main()
