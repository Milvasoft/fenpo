"""Builds Fenpo for Android's Google Play store assets (design/store/android/README.md).

One command, from the repository root:

    python design/store/android/scripts/build.py

1. Renders every screen with Robolectric (FENPO_PLAYSHOTS=1 ./gradlew recordRoborazziDebug): raw and
   designed phone screenshots, icon, feature graphics and the video scenes.
2. Removes the alpha channel where Play does not accept one (everything but the icon).
3. Checks the store text in metadata/<lang>.md against the limits in specs.json.
4. Builds the promo video per language with ffmpeg; the music is downloaded into build/ and never
   committed (the Mixkit license does not allow sharing the file on its own).
5. Writes a contact sheet into every image folder and CHECK.md.

Options: --skip-render (use the images already there), --skip-video.
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.request
from pathlib import Path

from PIL import Image

STORE = Path(__file__).resolve().parent.parent
REPO = STORE.parent.parent.parent
ANDROID = REPO / "android"
SPECS = json.loads((STORE / "scripts" / "specs.json").read_text(encoding="utf-8"))
BUILD = STORE / "build"

SECTIONS = {
    "title": ("Başlık", "Title"),
    "short": ("Kısa açıklama", "Short description"),
    "full": ("Tam açıklama", "Full description"),
    "featureGraphicText": ("Öne çıkan grafik metni", "Feature graphic text"),
    "featureGraphicAlt": ("Öne çıkan grafik alt metni", "Feature graphic alt text"),
    "headlines": ("Ekran görüntüsü başlıkları", "Screenshot headlines"),
    "captions": ("Video altyazıları", "Video captions"),
}


def tool(name: str) -> str:
    found = shutil.which(name)
    if found:
        return found
    winget = Path(os.environ.get("LOCALAPPDATA", "")) / "Microsoft" / "WinGet" / "Links" / f"{name}.exe"
    if winget.exists():
        return str(winget)
    sys.exit(f"{name} not found: install it (winget install Gyan.FFmpeg)")


# 1. Render ------------------------------------------------------------------------------------

def render() -> None:
    env = dict(os.environ, FENPO_PLAYSHOTS="1")
    jdk = Path.home() / ".jdks" / "temurin-21.0.12"
    if "JAVA_HOME" not in env and jdk.exists():
        env["JAVA_HOME"] = str(jdk)
    gradlew = ANDROID / ("gradlew.bat" if os.name == "nt" else "gradlew")
    print("Rendering screens with Robolectric…")
    subprocess.run([str(gradlew), "recordRoborazziDebug", "--rerun-tasks", "-q"], cwd=ANDROID, env=env, check=True)


# 2. Alpha -------------------------------------------------------------------------------------

def flatten(path: Path) -> None:
    image = Image.open(path)
    if image.mode == "RGB":
        return
    rgba = image.convert("RGBA")
    flat = Image.new("RGB", rgba.size, (255, 255, 255))
    flat.paste(rgba, mask=rgba.split()[3])
    flat.save(path, optimize=True)


def flatten_all() -> None:
    icon = STORE / "play" / SPECS["icon"]["file"]
    for path in list(STORE.glob("raw/**/*.png")) + list(STORE.glob("play/**/*.png")) + list(BUILD.glob("video/**/*.png")):
        if path != icon:
            flatten(path)


# 3. Metadata ----------------------------------------------------------------------------------

def sections(lang: str) -> dict[str, list[str]]:
    lines = (STORE / "metadata" / f"{lang}.md").read_text(encoding="utf-8").splitlines()
    found: dict[str, list[str]] = {}
    current = None
    for line in lines:
        if line.startswith("## "):
            name = line[3:].strip()
            current = next((key for key, names in SECTIONS.items() if name in names), None)
            if current:
                found[current] = []
        elif current:
            found[current].append(line)
    missing = set(SECTIONS) - set(found)
    if missing:
        sys.exit(f"metadata/{lang}.md is missing {sorted(missing)}")
    return found


def text(lines: list[str]) -> str:
    return "\n".join(lines).strip()


def entries(lines: list[str]) -> list[tuple[str, str]]:
    out = []
    for line in lines:
        match = re.match(r"^- ([\w-]+): (.+)$", line.strip())
        if match:
            out.append((match.group(1), match.group(2).strip()))
    return out


EMOJI = re.compile("[\U0001F000-\U0001FAFF☀-➿️]")


def check_metadata(lang: str, problems: list[str]) -> dict:
    found = sections(lang)
    limits = SPECS["metadata"]
    values = {
        "title": text(found["title"]),
        "short": text(found["short"]),
        "full": text(found["full"]),
        "featureGraphicAlt": text(found["featureGraphicAlt"]),
    }
    report = {}
    for key, value in values.items():
        report[key] = len(value)
        if len(value) > limits[key]:
            problems.append(f"{lang} {key}: {len(value)} > {limits[key]}")
    headlines = entries(found["headlines"])
    captions = entries(found["captions"])
    if [i for i, _ in headlines] != SPECS["phone"]["ids"]:
        problems.append(f"{lang} headlines do not match the screenshot ids")
    for kind, items, limit in (("headline", headlines, limits["screenshotHeadline"]), ("caption", captions, limits["videoCaption"])):
        for item_id, value in items:
            if len(value) > limit:
                problems.append(f"{lang} {kind} {item_id}: {len(value)} > {limit}")
    every = "\n".join(list(values.values()) + [v for _, v in headlines + captions] + [text(found["featureGraphicText"])])
    if EMOJI.search(every):
        problems.append(f"{lang}: emoji")
    for word in SPECS["forbidden"]:
        if re.search(rf"(?<![\w]){re.escape(word)}(?![\w])", every, re.IGNORECASE):
            problems.append(f"{lang}: forbidden word '{word}'")
    for word in re.findall(r"\b[A-ZÇĞİÖŞÜ][A-ZÇĞİÖŞÜ-]{3,}\b", every):
        if word not in SPECS["allowedCaps"]:
            problems.append(f"{lang}: capitals '{word}'")
    report["headlines"] = headlines
    report["captions"] = captions
    return report


# 4. Video -------------------------------------------------------------------------------------

def music() -> Path:
    spec = SPECS["video"]["music"]
    target = BUILD / "music" / Path(spec["url"]).name
    if not target.exists():
        target.parent.mkdir(parents=True, exist_ok=True)
        print(f"Downloading the music: {spec['title']} by {spec['artist']} ({spec['url']})")
        request = urllib.request.Request(spec["url"], headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(request) as response, open(target, "wb") as out:
            out.write(response.read())
    return target


def build_video(lang: str, captions: list[tuple[str, str]]) -> Path:
    spec = SPECS["video"]
    ffmpeg = tool("ffmpeg")
    scenes = [BUILD / "video" / lang / f"{scene}.png" for scene, _ in captions]
    for scene in scenes:
        if not scene.exists():
            sys.exit(f"Missing video scene {scene}")
    seconds, fade, fps = spec["sceneSeconds"], spec["fadeSeconds"], spec["fps"]
    total = len(scenes) * seconds - (len(scenes) - 1) * fade
    out = STORE / "play" / "video" / spec["file"].format(lang=lang)
    out.parent.mkdir(parents=True, exist_ok=True)
    args = [ffmpeg, "-y", "-hide_banner", "-loglevel", "error"]
    for scene in scenes:
        args += ["-loop", "1", "-framerate", str(fps), "-t", str(seconds), "-i", str(scene)]
    args += ["-i", str(music())]
    filters = []
    # Still scenes (a moving zoom made the picture shake: zoompan rounds to whole pixels every frame);
    # one scene slides smoothly into the next.
    for i in range(len(scenes)):
        filters.append(f"[{i}:v]scale={spec['width']}:{spec['height']},setsar=1,fps={fps},format=yuv420p[v{i}]")
    last = "v0"
    for i in range(1, len(scenes)):
        offset = i * (seconds - fade)
        filters.append(f"[{last}][v{i}]xfade=transition={spec['transition']}:duration={fade}:offset={offset}[x{i}]")
        last = f"x{i}"
    audio = len(scenes)
    filters.append(f"[{audio}:a]atrim=0:{total},afade=t=in:d=1,afade=t=out:st={total - 2}:d=2,volume=0.8[a]")
    args += [
        "-filter_complex", ";".join(filters),
        "-map", f"[{last}]", "-map", "[a]",
        "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p", "-r", str(fps),
        "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-t", str(total), str(out),
    ]
    print(f"Building the {lang} promo video…")
    subprocess.run(args, check=True)
    return out


def probe(path: Path) -> dict:
    result = subprocess.run(
        [tool("ffprobe"), "-v", "error", "-show_entries", "format=duration,size:stream=codec_name,width,height,r_frame_rate,pix_fmt",
         "-of", "json", str(path)],
        check=True, capture_output=True, text=True,
    )
    return json.loads(result.stdout)


# 5. Contact sheets and CHECK.md -----------------------------------------------------------------

def contact_sheet(folder: Path) -> None:
    images = sorted(p for p in folder.glob("*.png"))
    if not images:
        return
    thumb_height = 360
    thumbs = []
    for path in images:
        image = Image.open(path).convert("RGB")
        width = round(image.width * thumb_height / image.height)
        thumbs.append(image.resize((width, thumb_height)))
    gap = 16
    sheet = Image.new("RGB", (sum(t.width for t in thumbs) + gap * (len(thumbs) + 1), thumb_height + gap * 2), (27, 31, 59))
    x = gap
    for thumb in thumbs:
        sheet.paste(thumb, (x, gap))
        x += thumb.width + gap
    sheet.save(folder / "contact.jpg", quality=85)


def image_rows(problems: list[str]) -> list[str]:
    rows = []
    icon = STORE / "play" / SPECS["icon"]["file"]
    for path in sorted(list(STORE.glob("play/**/*.png")) + list(STORE.glob("raw/**/*.png"))):
        image = Image.open(path)
        rel = path.relative_to(STORE).as_posix()
        size = path.stat().st_size
        rows.append(f"| `{rel}` | {image.width}×{image.height} | {image.mode} | {size // 1024} KB |")
        if path == icon:
            want = (SPECS["icon"]["width"], SPECS["icon"]["height"])
            if size > SPECS["icon"]["maxBytes"]:
                problems.append(f"{rel}: over 1 MB")
            if image.mode == "RGBA" and image.getextrema()[3][0] < 255:
                problems.append(f"{rel}: the icon must be opaque")
        elif "feature-graphic" in rel:
            want = (SPECS["featureGraphic"]["width"], SPECS["featureGraphic"]["height"])
        else:
            want = (SPECS["phone"]["width"], SPECS["phone"]["height"])
        if (image.width, image.height) != want:
            problems.append(f"{rel}: {image.width}×{image.height}, want {want[0]}×{want[1]}")
        if path != icon and image.mode != "RGB":
            problems.append(f"{rel}: has alpha")
    return rows


def write_check(metadata: dict, videos: dict, problems: list[str]) -> None:
    lines = ["# Denetim (CHECK.md)", "", "`scripts/build.py` yazar; elle düzenlenmez.", ""]
    lines += ["## Sayılar", ""]
    for lang in SPECS["languages"]:
        for theme in SPECS["themes"]:
            count = len(list((STORE / "play" / "phone" / lang / theme).glob("*.png")))
            ok = SPECS["phone"]["min"] <= count <= SPECS["phone"]["max"]
            if not ok:
                problems.append(f"{lang}/{theme}: {count} screenshots")
            lines.append(f"- `play/phone/{lang}/{theme}`: {count} ekran görüntüsü ({'uygun' if ok else 'SINIR DIŞI'}, 2–8)")
    lines += ["", "## Meta veri", "", "| Dil | Başlık | Kısa | Tam | Grafik alt metni |", "|---|---|---|---|---|"]
    limits = SPECS["metadata"]
    for lang, report in metadata.items():
        lines.append(
            f"| {lang} | {report['title']}/{limits['title']} | {report['short']}/{limits['short']} | "
            f"{report['full']}/{limits['full']} | {report['featureGraphicAlt']}/{limits['featureGraphicAlt']} |"
        )
    lines += ["", "## Video", ""]
    if not videos:
        lines.append("Video bu çalıştırmada üretilmedi.")
    for lang, info in videos.items():
        stream = next(s for s in info["streams"] if s.get("width"))
        audio = [s["codec_name"] for s in info["streams"] if not s.get("width")]
        lines.append(
            f"- `play/video/{SPECS['video']['file'].format(lang=lang)}`: {stream['width']}×{stream['height']}, "
            f"{stream['codec_name']} {stream['pix_fmt']} {stream['r_frame_rate']} fps, ses {', '.join(audio)}, "
            f"{float(info['format']['duration']):.1f} sn, {int(info['format']['size']) // 1024} KB"
        )
        if (stream["width"], stream["height"]) != (SPECS["video"]["width"], SPECS["video"]["height"]):
            problems.append(f"video {lang}: wrong size")
    rows = image_rows(problems)
    lines += ["", "## Görseller", "", "| Dosya | Ölçü | Renk | Boyut |", "|---|---|---|---|"] + rows
    lines += ["", "## Sorunlar", ""] + ([f"- {p}" for p in problems] or ["Yok."]) + [""]
    (STORE / "CHECK.md").write_text("\n".join(lines), encoding="utf-8", newline="\n")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--skip-render", action="store_true")
    parser.add_argument("--skip-video", action="store_true")
    options = parser.parse_args()

    if not options.skip_render:
        render()
    flatten_all()
    problems: list[str] = []
    metadata = {lang: check_metadata(lang, problems) for lang in SPECS["languages"]}
    videos = {}
    if not options.skip_video:
        for lang in SPECS["languages"]:
            videos[lang] = probe(build_video(lang, metadata[lang]["captions"]))
    for folder in {p.parent for p in list(STORE.glob("play/**/*.png")) + list(STORE.glob("raw/**/*.png"))}:
        contact_sheet(folder)
    write_check(metadata, videos, problems)
    print("CHECK.md written;", "no problems." if not problems else f"{len(problems)} problem(s):")
    for problem in problems:
        print(" -", problem)
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
