"""Builds the finished social videos: frames -> silent mp4, music -> wav, then mux and loudness-normalise.

    python build.py [--lang tr,en] [--jobs 4] [--synth] [ids...]

By default each video uses the licensed track listed in audio/tracks.json and is rendered at that
track's tempo. --synth uses the original synthesised score (audio/music.py) at the video's own tempo.

Needs PLAYWRIGHT_DIR (a folder with the playwright npm package) in the environment.
"""

import argparse
import json
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT.parent / 'videos'
WORK = ROOT.parent / '.work'


def run(cmd, **kw):
    r = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace', **kw)
    if r.returncode != 0:
        raise RuntimeError(f'{cmd[0]} failed:\n{r.stdout[-3000:]}\n{r.stderr[-3000:]}')
    return r.stdout


TRACKS = json.load(open(ROOT / 'audio' / 'tracks.json', encoding='utf-8')) if (ROOT / 'audio' / 'tracks.json').exists() else {}


def build(vid, lang, skip_frames=False, synth=False):
    cfg = None if synth else TRACKS.get(vid)
    page = str(ROOT / 'videos' / f'{vid}.html') + (f'?bpm={cfg["video_bpm"]}' if cfg else '')
    WORK.mkdir(exist_ok=True)
    (OUT / lang).mkdir(parents=True, exist_ok=True)
    silent = WORK / f'{vid}.{lang}.mp4'
    meta = WORK / f'{vid}.{lang}.json'
    wav = WORK / f'{vid}.{lang}.wav'
    final = OUT / lang / f'fenpo-{vid}-{lang}.mp4'
    if not (skip_frames and silent.exists()):
        run(['node', str(ROOT / 'render.mjs'), 'video', str(page), lang, str(silent)], cwd=ROOT)
    score = 'track.py' if cfg else 'music.py'
    run([sys.executable, str(ROOT / 'audio' / score), str(meta), str(wav)], cwd=ROOT / 'audio')
    run(['ffmpeg', '-y', '-hide_banner', '-loglevel', 'error', '-i', str(silent), '-i', str(wav),
         '-map', '0:v', '-map', '1:a', '-c:v', 'libx264', '-preset', 'slow', '-crf', '18', '-maxrate', '12M', '-bufsize', '24M', '-pix_fmt', 'yuv420p', '-profile:v', 'high', '-r', '30',
         '-af', 'loudnorm=I=-14:TP=-1.5:LRA=11', '-c:a', 'aac', '-b:a', '256k', '-ar', '48000',
         '-shortest', '-movflags', '+faststart', str(final)])
    return final


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('ids', nargs='*')
    ap.add_argument('--lang', default='tr')
    ap.add_argument('--jobs', type=int, default=4)
    ap.add_argument('--audio-only', action='store_true', help='reuse rendered frames, redo music and mux')
    ap.add_argument('--synth', action='store_true', help='use the synthesised score instead of the licensed tracks')
    a = ap.parse_args()
    ids = a.ids or sorted(p.stem for p in (ROOT / 'videos').glob('*.html'))
    jobs = [(i, l) for l in a.lang.split(',') for i in ids]
    with ThreadPoolExecutor(a.jobs) as ex:
        futs = {ex.submit(build, i, l, a.audio_only, a.synth): (i, l) for i, l in jobs}
        for f, (i, l) in futs.items():
            try:
                print('ok  ', f.result())
            except Exception as e:  # noqa: BLE001
                print('FAIL', i, l, e)


if __name__ == '__main__':
    main()
