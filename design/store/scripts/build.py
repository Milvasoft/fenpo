#!/usr/bin/env python3
"""Builds every App Store asset of Fenpo for iPhone from the raw screens and recordings
(design/store/raw, design/store/recordings), with the sizes in specs.json and the words in copy.json:

  appstore/iphone-6.3/<lang>/<theme>/NN_name.png   1206 x 2622, required
  appstore/iphone-6.9/<lang>/<theme>/NN_name.png   1320 x 2868
  appstore/creative/<lang>/{header-21x9,search-3x2,universal-16x9}.png
  appstore/preview/<lang>/fenpo-preview[-music].mp4  886 x 1920
  contact.jpg in every folder of images, and CHECK.md

Usage: build.py <design/store dir> [--skip-preview]
Needs Pillow; the preview needs ffmpeg and ffprobe. Fonts: SF Pro on a Mac, Segoe UI on Windows.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import urllib.request

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import frames  # noqa: E402
import check  # noqa: E402

FONT_CANDIDATES = [
    ("/System/Library/Fonts/SFNS.ttf", "/System/Library/Fonts/SFNS.ttf"),
    ("C:/Windows/Fonts/segoeuib.ttf", "C:/Windows/Fonts/segoeui.ttf"),
    ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
]


def fonts():
    for bold, regular in FONT_CANDIDATES:
        if os.path.exists(bold):
            return bold, regular
    sys.exit("No font found")


def load(store):
    with open(os.path.join(store, "specs.json"), encoding="utf-8") as file:
        specs = json.load(file)
    with open(os.path.join(store, "copy.json"), encoding="utf-8") as file:
        copy = json.load(file)
    return specs, copy


def raw_path(store, lang, theme, screen_id):
    return os.path.join(store, "raw", lang, theme, f"{screen_id}.png")


# Screenshots -------------------------------------------------------------------------------------

def build_screenshots(store, specs, copy, bold, regular):
    made = 0
    for size_name, size in specs["screenshots"].items():
        width, height = size["size"]
        for lang in specs["languages"]:
            for theme in specs["themes"]:
                folder = os.path.join(store, "appstore", size_name, lang, theme)
                os.makedirs(folder, exist_ok=True)
                for screen in specs["screens"]:
                    source = raw_path(store, lang, theme, screen["id"])
                    if not os.path.exists(source):
                        continue
                    title, subtitle = copy["screens"][screen["id"]][lang]
                    image = frames.compose(Image.open(source), frames.THEMES[theme], title, subtitle, bold, regular, width, height)
                    image.save(os.path.join(folder, f"{screen['id']}.png"), optimize=True)
                    made += 1
    print(f"Screenshots: {made}")


# Creative assets ---------------------------------------------------------------------------------

def app_icon(size):
    path = os.path.join(os.path.dirname(__file__), "..", "..", "..", "ios", "Fenpo", "Resources", "Assets.xcassets", "AppIcon.appiconset", "AppIcon.png")
    icon = Image.open(path).convert("RGBA").resize((size, size), Image.LANCZOS)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=round(size * 0.2237), fill=255)
    icon.putalpha(mask)
    return icon


def safe_box(width, height, crops):
    """The part that survives every crop (centered crops of the given aspect ratios)."""
    left, top, right, bottom = 0, 0, width, height
    for ratio_w, ratio_h in crops:
        if width / height > ratio_w / ratio_h:
            crop_w = height * ratio_w / ratio_h
            left, right = max(left, (width - crop_w) / 2), min(right, (width + crop_w) / 2)
        else:
            crop_h = width * ratio_h / ratio_w
            top, bottom = max(top, (height - crop_h) / 2), min(bottom, (height + crop_h) / 2)
    return left, top, right - left, bottom - top


def creative(store, specs, copy, lang, name, spec, bold, regular):
    width, height = spec["size"]
    sx, sy, sw, sh = safe_box(width, height, spec.get("crops", []))
    theme = frames.THEMES["dark"]
    scale = width / frames.DESIGN_WIDTH
    image = frames.background(theme, width, height, scale)

    # Icon and headline in one centered row, the line under them, near the top of the safe area.
    title, subtitle = copy["creative"][lang]
    # Sized from the height, smaller when the row would not fit in 88% of the safe width.
    fit = 1.0
    while True:
        title_face = frames.font(bold, round(sh * 0.085 * fit), "Bold")
        subtitle_face = frames.font(regular, round(sh * 0.036 * fit), "Regular")
        icon_size = round(sh * 0.13 * fit)
        gap = round(icon_size * 0.28)
        row_width = icon_size + gap + title_face.getlength(title)
        if max(row_width, subtitle_face.getlength(subtitle)) <= sw * 0.88:
            break
        fit *= 0.96
    row_left = sx + (sw - row_width) / 2
    row_top = sy + sh * 0.07
    image.alpha_composite(app_icon(icon_size), (round(row_left), round(row_top)))
    draw = ImageDraw.Draw(image)
    draw.text((row_left + icon_size + gap, row_top + icon_size / 2), title, font=title_face, fill=theme["title"], anchor="lm")
    subtitle_top = row_top + icon_size + sh * 0.035
    draw.text((sx + sw / 2, subtitle_top), subtitle, font=subtitle_face, fill=theme["subtitle"], anchor="ma")

    # Three light-theme phones under the words; the middle one in front, all running off the bottom.
    phones_top = subtitle_top + sh * 0.11
    center_width = round(min(sw * 0.23, sh * 0.42))
    side_width = round(center_width * 0.88)
    spacing = center_width * 1.08
    ids = copy["creative_phones"]
    light = frames.THEMES["light"]
    order = [(ids[1], -1, side_width), (ids[2], 1, side_width), (ids[0], 0, center_width)]
    for screen_id, position, phone_width in order:
        raw = Image.open(raw_path(store, lang, "light", screen_id))
        phone_scale = phone_width / frames.SCREEN_WIDTH
        frame = frames.phone(raw, light, phone_width, phone_scale)
        left = sx + sw / 2 + position * spacing - frame.width / 2
        top = phones_top + (0 if position == 0 else sh * 0.06)
        image = frames.place(image, frame, left, top, theme, phone_scale)

    folder = os.path.join(store, "appstore", "creative", lang)
    os.makedirs(folder, exist_ok=True)
    image.convert("RGB").save(os.path.join(folder, f"{name}.png"), optimize=True)


def build_creative(store, specs, copy, bold, regular):
    made = 0
    for lang in specs["languages"]:
        if not os.path.exists(raw_path(store, lang, "light", copy["creative_phones"][0])):
            continue
        for name, spec in specs["creative"].items():
            creative(store, specs, copy, lang, name, spec, bold, regular)
            made += 1
    print(f"Creative assets: {made}")


# App preview -------------------------------------------------------------------------------------

def run(args):
    return subprocess.run(args, check=True, capture_output=True, text=True)


def transitions(recording, count, step):
    """The demo tour's screen changes in the recording: the `count` picture changes `step` apart
    that fit best (the app's launch and animations inside a screen are changes too)."""
    result = subprocess.run(
        ["ffmpeg", "-hide_banner", "-i", recording, "-vf", "select='gt(scene,0.06)',showinfo", "-f", "null", "-"],
        capture_output=True, text=True,
    )
    times = [float(line.split("pts_time:")[1].split()[0]) for line in result.stderr.splitlines() if "pts_time:" in line]
    best, best_score = None, -1
    for first in times:
        found = []
        for k in range(count):
            near = [t for t in times if abs(t - (first + k * step)) < 0.7]
            found.append(min(near, key=lambda t: abs(t - (first + k * step))) if near else None)
        score = sum(t is not None for t in found)
        if score > best_score:
            best, best_score = (first, found), score
    if best is None or best_score < count - 1:
        return None
    first, found = best
    return [t if t is not None else first + k * step for k, t in enumerate(found)]


def build_preview(store, specs, copy, bold, regular):
    preview = specs["preview"]
    width, height = preview["size"]
    step = preview["step_seconds"]
    screens = [screen["id"] for screen in specs["screens"]]
    music = None
    made = 0
    for lang in specs["languages"]:
        recording = os.path.join(store, "recordings", f"{lang}.mov")
        if not os.path.exists(recording):
            continue
        cuts = transitions(recording, len(screens) - 1, step)
        if cuts is None:
            sys.exit(f"{lang}: the demo tour's {len(screens) - 1} screen changes are not in the recording")
        print(f"{lang}: screen changes at " + ", ".join(f"{t:.2f}" for t in cuts))
        starts = [max(0.0, cuts[0] - preview["first_step_seconds"])] + cuts
        ends = cuts + [cuts[-1] + step]
        durations = [end - start for start, end in zip(starts, ends)]
        total = sum(durations)

        work = tempfile.mkdtemp()
        # One card per step: background, words and an empty phone; the recording goes into the screen.
        black = Image.new("RGB", tuple(specs["raw_size"]), (0, 0, 0))
        theme = frames.THEMES[preview["theme"]]
        concat = []
        for index, screen_id in enumerate(screens):
            title, subtitle = copy["screens"][screen_id][lang]
            card = frames.compose(black, theme, title, subtitle, bold, regular, width, height)
            path = os.path.join(work, f"card{index}.png")
            card.save(path)
            concat += [f"file '{path}'", f"duration {durations[index]:.3f}"]
        concat.append(f"file '{os.path.join(work, f'card{len(screens) - 1}.png')}'")
        with open(os.path.join(work, "cards.txt"), "w", encoding="utf-8") as file:
            file.write("\n".join(concat) + "\n")

        # Where the screen sits on a card (frames.compose's layout at this width).
        scale = width / frames.DESIGN_WIDTH
        screen_w = round(frames.SCREEN_WIDTH * scale)
        screen_h = round(specs["raw_size"][1] * screen_w / specs["raw_size"][0])
        bezel = max(2, round(frames.BEZEL * scale))
        x = round((width - (screen_w + 2 * bezel)) / 2) + bezel
        y = round(frames.SCREEN_TOP * scale) + bezel
        mask = frames.rounded_mask((screen_w, screen_h), round(frames.SCREEN_CORNER * scale))
        mask_path = os.path.join(work, "mask.png")
        mask.save(mask_path)

        folder = os.path.join(store, "appstore", "preview", lang)
        os.makedirs(folder, exist_ok=True)
        for with_music in (False, True):
            if with_music and music is None:
                music = os.path.join(work, "music.mp3")
                urllib.request.urlretrieve(preview["music"]["url"], music)
            audio = ["-i", music] if with_music else ["-f", "lavfi", "-t", f"{total:.3f}", "-i", f"anullsrc=r={preview['audio_rate']}:cl=stereo"]
            fade = f"afade=t=in:d=0.6,afade=t=out:st={total - 1.8:.3f}:d=1.8," if with_music else ""
            graph = (
                f"[1:v]trim=start={starts[0]:.3f}:end={ends[-1]:.3f},setpts=PTS-STARTPTS,fps={preview['fps']},"
                f"scale={screen_w}:{screen_h},format=rgba[screen];"
                f"[2:v]format=gray,scale={screen_w}:{screen_h}[mask];"
                f"[screen][mask]alphamerge[rounded];"
                f"[0:v]fps={preview['fps']},format=rgba[cards];"
                f"[cards][rounded]overlay={x}:{y}:shortest=1,format=yuv420p[video];"
                f"[3:a]atrim=0:{total:.3f},{fade}aresample={preview['audio_rate']},aformat=channel_layouts=stereo[audio]"
            )
            name = "fenpo-preview-music.mp4" if with_music else "fenpo-preview.mp4"
            run([
                "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
                "-f", "concat", "-safe", "0", "-i", os.path.join(work, "cards.txt"),
                "-i", recording, "-loop", "1", "-i", mask_path, *audio,
                "-filter_complex", graph, "-map", "[video]", "-map", "[audio]",
                "-t", f"{total:.3f}", "-r", str(preview["fps"]),
                "-c:v", "libx264", "-profile:v", preview["profile"], "-level:v", preview["level"],
                "-b:v", f"{preview['video_kbps']}k", "-maxrate", f"{preview['video_max_kbps']}k",
                "-bufsize", f"{2 * preview['video_max_kbps']}k", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", f"{preview['audio_kbps']}k", "-ar", str(preview["audio_rate"]), "-ac", "2",
                "-movflags", "+faststart", os.path.join(folder, name),
            ])
            made += 1
        # The poster frame App Store Connect picks by default.
        run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-ss", str(preview["poster_seconds"]),
             "-i", os.path.join(folder, "fenpo-preview.mp4"), "-frames:v", "1", os.path.join(folder, "poster-5s.png")])
        shutil.rmtree(work, ignore_errors=True)
    print(f"App previews: {made}")


# Contact sheets ----------------------------------------------------------------------------------

def contact_sheets(store, specs):
    root = os.path.join(store, "appstore")
    for folder, _, files in os.walk(root):
        images = sorted(f for f in files if f.endswith(".png"))
        if not images:
            continue
        thumbs = []
        for name in images:
            image = Image.open(os.path.join(folder, name)).convert("RGB")
            image.thumbnail((480, 480))
            thumbs.append(image)
            if name.startswith("universal"):
                # How the universal image looks cropped to 21:9 and 3:2.
                full = Image.open(os.path.join(folder, name)).convert("RGB")
                for ratio in specs["creative"]["universal-16x9"]["crops"]:
                    x, y, w, h = safe_box(full.width, full.height, [ratio])
                    crop = full.crop((round(x), round(y), round(x + w), round(y + h)))
                    crop.thumbnail((480, 480))
                    thumbs.append(crop)
        cell_w = max(t.width for t in thumbs) + 24
        cell_h = max(t.height for t in thumbs) + 24
        columns = min(len(thumbs), 7)
        rows = (len(thumbs) + columns - 1) // columns
        sheet = Image.new("RGB", (columns * cell_w, rows * cell_h), (240, 241, 246))
        for index, thumb in enumerate(thumbs):
            sheet.paste(thumb, ((index % columns) * cell_w + 12, (index // columns) * cell_h + 12))
        sheet.save(os.path.join(folder, "contact.jpg"), quality=85)


def main():
    store = os.path.abspath(sys.argv[1])
    skip_preview = "--skip-preview" in sys.argv
    specs, copy = load(store)
    bold, regular = fonts()
    shutil.rmtree(os.path.join(store, "appstore"), ignore_errors=True)
    build_screenshots(store, specs, copy, bold, regular)
    build_creative(store, specs, copy, bold, regular)
    if not skip_preview:
        build_preview(store, specs, copy, bold, regular)
    contact_sheets(store, specs)
    problems = check.run(store, specs)
    print(f"Check: {len(problems)} problem(s), see CHECK.md")
    if problems:
        sys.exit(1)


if __name__ == "__main__":
    main()
