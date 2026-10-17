#!/usr/bin/env python3
"""Turns the raw 6.9-inch screenshots into App Store images: a short headline in Turkish or English
above the real screen in a phone frame, on a background in Fenpo's colors, light or dark.

Follows Apple's asset best practices: the real interface, a short phrase that adds to it rather than
describing it, localized, the focal point centered, and no prices, URLs, awards or other platforms.

Library for design/store/scripts/build; also usable alone:
frames.py <raw image> <output image> <width> <height> <title> <subtitle> <light|dark> [bold font] [regular font]
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

DESIGN_WIDTH = 1320  # Every measure below is for this width and scales with the canvas.

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


def background(theme, width, height, scale):
    top, bottom = theme["top"], theme["bottom"]
    image = Image.new("RGB", (width, height))
    draw = ImageDraw.Draw(image)
    for y in range(height):
        t = y / (height - 1)
        draw.line([(0, y), (width, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    image = image.convert("RGBA")
    for color, strength, center, size in theme["glows"]:
        center = (round(center[0] * scale), round(center[1] * scale))
        size = round(size * scale)
        # The gradient reaches 128 at the square's edge: double it so the glow fades out before the edge.
        mask = Image.radial_gradient("L").resize((size, size)).point(lambda v: round(max(0, 255 - 2 * v) * strength))
        mask = mask.filter(ImageFilter.GaussianBlur(40 * scale))
        layer = Image.new("RGBA", (width, height), color + (0,))
        alpha = Image.new("L", (width, height), 0)
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


def phone(raw, theme, screen_width, scale):
    """The real screen in a simple phone frame, transparent around it."""
    corner, bezel = round(SCREEN_CORNER * scale), max(2, round(BEZEL * scale))
    screen_height = round(raw.height * screen_width / raw.width)
    screen = raw.convert("RGBA").resize((screen_width, screen_height), Image.LANCZOS)
    frame_size = (screen_width + 2 * bezel, screen_height + 2 * bezel)
    frame = Image.new("RGBA", frame_size, theme["bezel"] + (255,))
    frame.putalpha(rounded_mask(frame_size, corner + bezel))
    frame.paste(screen, (bezel, bezel), rounded_mask(screen.size, corner))
    return frame


def place(image, frame, left, top, theme, scale):
    """Puts a phone on the image with a soft shadow under it."""
    shadow = Image.new("RGBA", image.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        [left, top + 30 * scale, left + frame.width, top + 30 * scale + frame.height],
        radius=round((SCREEN_CORNER + BEZEL) * scale), fill=(0, 0, 0, theme["shadow"]),
    )
    image = Image.alpha_composite(image, shadow.filter(ImageFilter.GaussianBlur(45 * scale)))
    image.alpha_composite(frame, (round(left), round(top)))
    return image


def compose(raw, theme, title, subtitle, bold_font, regular_font, width=1320, height=2868):
    scale = width / DESIGN_WIDTH
    image = background(theme, width, height, scale)
    draw = ImageDraw.Draw(image)

    # Headline and line under it, centered in the space above the phone.
    title_face = font(bold_font, round(92 * scale), "Bold")
    subtitle_face = font(regular_font, round(50 * scale), "Regular")
    title_lines = wrap(title, title_face, 1160 * scale)
    y = (190 if len(title_lines) == 1 else 130) * scale
    for line in title_lines:
        draw.text((width / 2, y), line, font=title_face, fill=theme["title"], anchor="ma")
        y += 108 * scale
    draw.text((width / 2, y + 18 * scale), subtitle, font=subtitle_face, fill=theme["subtitle"], anchor="ma")

    frame = phone(raw, theme, round(SCREEN_WIDTH * scale), scale)
    image = place(image, frame, (width - frame.width) / 2, SCREEN_TOP * scale, theme, scale)
    return image.convert("RGB")


def main():
    raw, out, width, height, title, subtitle, appearance = sys.argv[1:8]
    bold_font = sys.argv[8] if len(sys.argv) > 8 else "/System/Library/Fonts/SFNS.ttf"
    regular_font = sys.argv[9] if len(sys.argv) > 9 else bold_font
    image = compose(Image.open(raw), THEMES[appearance], title, subtitle, bold_font, regular_font, int(width), int(height))
    image.save(out, optimize=True)


if __name__ == "__main__":
    main()
