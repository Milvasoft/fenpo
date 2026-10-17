#!/usr/bin/env python3
"""Checks the built App Store assets against specs.json and writes CHECK.md: exact sizes, no alpha,
counts per folder, the previews' ffprobe values, metadata limits and words that may not appear.

Usage: check.py <design/store dir>   (build.py runs it at the end)
"""
import json
import os
import re
import subprocess
import sys

from PIL import Image

# Other platforms' and Apple's feature names, prices and claims (App Review 2.3.7, 2.3.10).
BANNED = [
    "free", "ücretsiz", "bedava", "best", "en iyi", "#1", "number one", "price", "fiyat", "sale", "indirim",
    "airdrop", "airplay", "handoff", "continuity", "android", "mac", "macos", "macbook", "ipad", "google", "samsung",
]


def banned_words(text):
    lower = text.lower()
    return [word for word in BANNED if re.search(r"(?<![\w#])" + re.escape(word) + r"(?!\w)", lower)]


def human_size(path):
    size = os.path.getsize(path)
    return f"{size / 1024 / 1024:.1f} MB" if size > 1024 * 1024 else f"{size / 1024:.0f} KB"


def check_images(store, specs, rows, problems):
    root = os.path.join(store, "appstore")
    for size_name, size in specs["screenshots"].items():
        for lang in specs["languages"]:
            for theme in specs["themes"]:
                folder = os.path.join(root, size_name, lang, theme)
                files = sorted(f for f in os.listdir(folder) if f.endswith(".png")) if os.path.isdir(folder) else []
                expected = len(specs["screens"])
                if len(files) != expected:
                    problems.append(f"{size_name}/{lang}/{theme}: {len(files)} images, expected {expected}")
                if len(files) > specs["max_per_size"]:
                    problems.append(f"{size_name}/{lang}/{theme}: more than {specs['max_per_size']} images")
                for name in files:
                    check_image(os.path.join(folder, name), size["size"], f"{size_name}/{lang}/{theme}/{name}", rows, problems)
    for lang in specs["languages"]:
        for name, spec in specs["creative"].items():
            path = os.path.join(root, "creative", lang, f"{name}.png")
            if not os.path.exists(path):
                problems.append(f"creative/{lang}/{name}.png is missing")
                continue
            check_image(path, spec["size"], f"creative/{lang}/{name}.png", rows, problems)


def check_image(path, size, label, rows, problems):
    image = Image.open(path)
    fine = image.size == tuple(size) and image.mode == "RGB" and image.format == "PNG"
    if image.size != tuple(size):
        problems.append(f"{label}: {image.width} x {image.height}, expected {size[0]} x {size[1]}")
    if image.mode != "RGB":
        problems.append(f"{label}: mode {image.mode} (must be RGB without alpha)")
    rows.append((label, f"{image.width} x {image.height}", image.mode, human_size(path), "✓" if fine else "✗"))


def probe(path):
    result = subprocess.run(
        ["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", path],
        check=True, capture_output=True, text=True,
    )
    return json.loads(result.stdout)


def check_previews(store, specs, rows, problems):
    preview = specs["preview"]
    root = os.path.join(store, "appstore", "preview")
    for lang in specs["languages"]:
        folder = os.path.join(root, lang)
        videos = sorted(f for f in os.listdir(folder) if f.endswith(".mp4")) if os.path.isdir(folder) else []
        if not videos:
            problems.append(f"preview/{lang}: no video")
            continue
        if len(videos) > preview["max_per_language"]:
            problems.append(f"preview/{lang}: more than {preview['max_per_language']} videos")
        for name in videos:
            path = os.path.join(folder, name)
            info = probe(path)
            video = next(s for s in info["streams"] if s["codec_type"] == "video")
            audio = [s for s in info["streams"] if s["codec_type"] == "audio"]
            num, den = (int(x) for x in video["avg_frame_rate"].split("/"))
            fps = num / den if den else 0
            duration = float(info["format"]["duration"])
            video_kbps = int(video.get("bit_rate", 0)) / 1000
            label = f"preview/{lang}/{name}"
            facts = [
                (f"{video['width']} x {video['height']}", [video["width"], video["height"]] == preview["size"]),
                (f"{fps:.2f} fps", fps <= preview["fps"] + 0.01),
                (f"{duration:.2f} s", preview["min_seconds"] <= duration <= preview["max_seconds"]),
                (f"{video['codec_name']} {video.get('profile', '')} L{video.get('level', 0) / 10:.1f}",
                 video["codec_name"] == "h264" and video.get("profile") == "High" and video.get("level", 99) <= 40),
                (f"{video_kbps:.0f} kbps", preview["video_min_kbps"] <= video_kbps <= preview["video_max_kbps"]),
                (video.get("pix_fmt", ""), video.get("pix_fmt") == "yuv420p"),
            ]
            if audio:
                a = audio[0]
                facts.append((f"{a['codec_name']} {int(a.get('bit_rate', 0)) // 1000} kbps {a['sample_rate']} Hz {a['channels']} ch",
                              a["codec_name"] == "aac" and int(a["sample_rate"]) == preview["audio_rate"] and a["channels"] == 2))
            else:
                facts.append(("no audio", False))
            size_mb = os.path.getsize(path) / 1024 / 1024
            facts.append((f"{size_mb:.1f} MB", size_mb <= preview["max_mb"]))
            for text, fine in facts:
                if not fine:
                    problems.append(f"{label}: {text}")
            rows.append((label, ", ".join(text for text, _ in facts), "✓" if all(f for _, f in facts) else "✗"))


def parse_metadata(path):
    fields, key, lines = {}, None, []
    with open(path, encoding="utf-8") as file:
        for line in file.read().splitlines():
            match = re.match(r"^## (\w+)\s*$", line)
            if match:
                if key:
                    fields[key] = "\n".join(lines).strip()
                key, lines = match.group(1), []
            elif key and not line.startswith("<!--"):
                lines.append(line)
    if key:
        fields[key] = "\n".join(lines).strip()
    return fields


def check_metadata(store, specs, rows, problems):
    limits = specs["metadata_limits"]
    for lang in specs["languages"]:
        path = os.path.join(store, "metadata", f"{lang}.md")
        if not os.path.exists(path):
            problems.append(f"metadata/{lang}.md is missing")
            continue
        fields = parse_metadata(path)
        name_words = [w.lower() for w in re.findall(r"\w+", fields.get("name", ""))]
        for key, limit in limits.items():
            text = fields.get(key, "")
            # Keywords are counted in UTF-8 bytes (a Turkish letter is two), the rest in characters.
            length, unit = (len(text.encode("utf-8")), "bytes") if key == "keywords" else (len(text), "characters")
            fine = 0 < length <= limit
            if not text:
                problems.append(f"metadata/{lang}: {key} is empty")
            elif length > limit:
                problems.append(f"metadata/{lang}: {key} is {length} {unit}, limit {limit}")
            found = banned_words(text)
            if found:
                fine = False
                problems.append(f"metadata/{lang}: {key} has {', '.join(found)}")
            rows.append((f"metadata/{lang}.md", key, f"{length} / {limit} {unit}", "✓" if fine else "✗"))
        keywords = fields.get("keywords", "")
        words = keywords.split(",")
        if ", " in keywords or any(w != w.strip() or not w for w in words):
            problems.append(f"metadata/{lang}: keywords must be separated by commas without spaces")
        if len({w.lower() for w in words}) != len(words):
            problems.append(f"metadata/{lang}: a keyword is repeated")
        short = [w for w in words if len(w) <= 2]
        if short:
            problems.append(f"metadata/{lang}: keywords must be longer than two characters: {', '.join(short)}")
        # The name and the company name are searched anyway.
        repeated = [w for w in words if w.lower() in name_words + ["milvasoft"]]
        if repeated:
            problems.append(f"metadata/{lang}: keywords repeat the name: {', '.join(repeated)}")
        for key in ("support_url", "marketing_url", "privacy_url"):
            if not fields.get(key, "").startswith("https://"):
                problems.append(f"metadata/{lang}: {key} must be an https link")


def check_copy(store, problems):
    with open(os.path.join(store, "copy.json"), encoding="utf-8") as file:
        copy = json.load(file)
    texts = [t for screen in copy["screens"].values() for pair in screen.values() for t in pair]
    texts += [t for pair in copy["creative"].values() for t in pair]
    for text in texts:
        found = banned_words(text)
        if found:
            problems.append(f"copy.json: \"{text}\" has {', '.join(found)}")


def run(store, specs):
    images, videos, metadata, problems = [], [], [], []
    check_images(store, specs, images, problems)
    try:
        check_previews(store, specs, videos, problems)
    except FileNotFoundError:
        problems.append("ffprobe is not installed; the previews were not checked")
    check_metadata(store, specs, metadata, problems)
    check_copy(store, problems)

    lines = [
        "# Check",
        "",
        "Written by `scripts/check.py` after every build; do not edit. Limits from `specs.json` (checked "
        f"{specs['checked']}, sources in `SPECS.md`).",
        "",
        f"**{'No problems' if not problems else f'{len(problems)} problem(s)'}.**",
        "",
    ]
    lines += [f"- {p}" for p in problems]
    lines += ["", "## Images", "", "| File | Size | Mode | File size | |", "|---|---|---|---|---|"]
    lines += [f"| {' | '.join(row)} |" for row in images]
    lines += ["", "## App previews", "", "| File | ffprobe | |", "|---|---|---|"]
    lines += [f"| {' | '.join(row)} |" for row in videos]
    lines += ["", "## Metadata", "", "| File | Field | Length | |", "|---|---|---|---|"]
    lines += [f"| {' | '.join(row)} |" for row in metadata]
    with open(os.path.join(store, "CHECK.md"), "w", encoding="utf-8", newline="\n") as file:
        file.write("\n".join(lines) + "\n")
    return problems


def main():
    store = os.path.abspath(sys.argv[1])
    with open(os.path.join(store, "specs.json"), encoding="utf-8") as file:
        specs = json.load(file)
    problems = run(store, specs)
    for problem in problems:
        print(problem)
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
