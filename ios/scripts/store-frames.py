#!/usr/bin/env python3
"""Turns the raw 6.9-inch screenshots into App Store images: a short headline in Turkish or English
above the real screen in a phone frame, on a background in Fenpo's colors, light or dark.

Follows Apple's asset best practices: the real interface, a short phrase that adds to it rather than
describing it, localized, the focal point centered, and no prices, URLs, awards or other platforms.

Usage: store-frames.py <raw dir> <output dir> [bold font] [regular font]
The raw files are <scene>-<light|dark>-<tr|en>.png; output is <lang>/<appearance>/<n>-<scene>.png.
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

WIDTH, HEIGHT = 1320, 2868

# The order the App Store shows them in: what Fenpo is, then each feature.
SCENES = [
    ("home", {
        "tr": ("Telefonun ve bilgisayarın tek ekip", "Pano, dosya ve kamera tek dokunuşta"),
        "en": ("Your phone and PC, one team", "Clipboard, files and camera in one tap"),
    }),
    ("transfers", {
        "tr": ("Dosyalar saniyeler içinde", "Fotoğraf ve videolar Wi‑Fi ile gider"),
        "en": ("Files across in seconds", "Photos and videos go over Wi‑Fi"),
    }),
    ("camera-ready", {
        "tr": ("iPhone’un web kameran olsun", "Görüntülü görüşmelerde net bir görüntü"),
        "en": ("Your iPhone is your webcam", "A sharp picture for every video call"),
    }),
    ("pc", {
        "tr": ("Bilgisayarın avucunda", "Müzik, sunum, fare ve klavye"),
        "en": ("Your PC in your hand", "Music, slides, mouse and keyboard"),
    }),
    ("touchpad", {
        "tr": ("Telefonun fare ve klavye olsun", "Kanepeden bilgisayarı kullan"),
        "en": ("A touchpad in your pocket", "Use your PC from across the room"),
    }),
    ("presenter", {
        "tr": ("Sunumu cebinden yönet", "İleri, geri ve lazer işaretçi"),
        "en": ("Present from your pocket", "Next, back and a laser pointer"),
    }),
    ("settings", {
        "tr": ("Sunucu yok, hesap yok", "Her şey kendi cihazlarında kalır"),
        "en": ("No server, no account", "Everything stays on your devices"),
    }),
]

# Fenpo's palette (CLAUDE.md, "Tasarım sözlüğü").
INK = (0x1B, 0x1F, 0x3B)
BLOOM = (0xE8, 0x57, 0x7E)
DUSK = (0x4E, 0x54, 0xC8)
GLASS = (0xDC, 0xE6, 0xF5)
SILL = (0xF6, 0xF7, 0xFB)

THEMES = {
    "light": {
        "top": SILL, "bottom": GLASS,
        "glows": [(BLOOM, 0.22, (1120, 260), 1500), (DUSK, 0.14, (120, 2700), 1700)],
        "title": INK, "subtitle": (0x5B, 0x60, 0x80), "bezel": (0x1B, 0x1F, 0x3B), "shadow": 70,
    },
    "dark": {
        "top": (0x2B, 0x2F, 0x5A), "bottom": (0x12, 0x14, 0x2A),
        "glows": [(BLOOM, 0.45, (1100, 240), 1500), (DUSK, 0.45, (160, 2650), 1800)],
        "title": (0xFF, 0xFF, 0xFF), "subtitle": (0xC9, 0xCC, 0xE6), "bezel": (0x05, 0x06, 0x0E), "shadow": 140,
    },
}

SCREEN_WIDTH = 1000
SCREEN_CORNER = 138
BEZEL = 22
SCREEN_TOP = 560


def font(path, size, variation):
    face = ImageFont.truetype(path, size)
    if variation:
        try:
            face.set_variation_by_name(variation)
        except (OSError, ValueError):
            pass
    return face


def background(theme):
    top, bottom = theme["top"], theme["bottom"]
    image = Image.new("RGB", (WIDTH, HEIGHT))
    draw = ImageDraw.Draw(image)
    for y in range(HEIGHT):
        t = y / (HEIGHT - 1)
        draw.line([(0, y), (WIDTH, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    image = image.convert("RGBA")
    for color, strength, center, size in theme["glows"]:
        # The gradient reaches 128 at the square's edge: double it so the glow fades out before the edge.
        mask = Image.radial_gradient("L").resize((size, size)).point(lambda v: round(max(0, 255 - 2 * v) * strength))
        mask = mask.filter(ImageFilter.GaussianBlur(40))
        layer = Image.new("RGBA", (WIDTH, HEIGHT), color + (0,))
        alpha = Image.new("L", (WIDTH, HEIGHT), 0)
        alpha.paste(mask, (center[0] - size // 2, center[1] - size // 2))
        layer.putalpha(alpha)
        image = Image.alpha_composite(image, layer)
    return image


def wrap(text, face, width):
    words, lines, line = text.split(), [], ""
    for word in words:
        trial = f"{line} {word}".strip()
        if face.getlength(trial) <= width or not line:
            line = trial
        else:
            lines.append(line)
            line = word
    lines.append(line)
    return lines


def rounded_mask(size, radius):
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size[0] - 1, size[1] - 1], radius=radius, fill=255)
    return mask


def compose(raw, theme, title, subtitle, bold_font, regular_font):
    image = background(theme)
    draw = ImageDraw.Draw(image)

    # Headline and line under it, centered in the space above the phone.
    title_face = font(bold_font, 92, "Bold")
    subtitle_face = font(regular_font, 50, "Regular")
    title_lines = wrap(title, title_face, 1160)
    y = 190 if len(title_lines) == 1 else 130
    for line in title_lines:
        draw.text((WIDTH / 2, y), line, font=title_face, fill=theme["title"], anchor="ma")
        y += 108
    draw.text((WIDTH / 2, y + 18), subtitle, font=subtitle_face, fill=theme["subtitle"], anchor="ma")

    # The real screen in a simple phone frame with a soft shadow.
    screen_height = round(raw.height * SCREEN_WIDTH / raw.width)
    screen = raw.convert("RGBA").resize((SCREEN_WIDTH, screen_height), Image.LANCZOS)
    frame_size = (SCREEN_WIDTH + 2 * BEZEL, screen_height + 2 * BEZEL)
    left = (WIDTH - frame_size[0]) // 2

    shadow = Image.new("RGBA", (WIDTH, HEIGHT), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        [left, SCREEN_TOP + 30, left + frame_size[0], SCREEN_TOP + 30 + frame_size[1]],
        radius=SCREEN_CORNER + BEZEL, fill=(0, 0, 0, theme["shadow"]),
    )
    image = Image.alpha_composite(image, shadow.filter(ImageFilter.GaussianBlur(45)))

    frame = Image.new("RGBA", frame_size, theme["bezel"] + (255,))
    frame.putalpha(rounded_mask(frame_size, SCREEN_CORNER + BEZEL))
    frame.paste(screen, (BEZEL, BEZEL), rounded_mask(screen.size, SCREEN_CORNER))
    image.alpha_composite(frame, (left, SCREEN_TOP))
    return image.convert("RGB")


def main():
    raw_dir, out_dir = sys.argv[1], sys.argv[2]
    bold_font = sys.argv[3] if len(sys.argv) > 3 else "/System/Library/Fonts/SFNS.ttf"
    regular_font = sys.argv[4] if len(sys.argv) > 4 else bold_font
    made = 0
    for appearance, theme in THEMES.items():
        for lang in ("tr", "en"):
            for number, (scene, captions) in enumerate(SCENES, start=1):
                source = os.path.join(raw_dir, f"{scene}-{appearance}-{lang}.png")
                if not os.path.exists(source):
                    continue
                title, subtitle = captions[lang]
                image = compose(Image.open(source), theme, title, subtitle, bold_font, regular_font)
                folder = os.path.join(out_dir, lang, appearance)
                os.makedirs(folder, exist_ok=True)
                image.save(os.path.join(folder, f"{number}-{scene}.png"), optimize=True)
                made += 1
    print(f"Made {made} App Store images in {out_dir}")


if __name__ == "__main__":
    main()
