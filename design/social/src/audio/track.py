"""Builds a video's soundtrack from a licensed music track plus the animation's sound cues.

    python track.py <meta.json> <out.wav>

The track for each video is listed in tracks.json (file, source, author, licence, analysed tempo).
The video was rendered at the track's tempo, so its beats match the track's; this script picks the
track beat to start from so the video's drop bar lands on the track's sharpest rise, cuts the track
to the video's length, fades it out under the end card and mixes the sound cues on top.
"""

import json
import subprocess
import sys
from pathlib import Path

import numpy as np

import synth

HERE = Path(__file__).resolve().parent
SR = synth.SR


def load(path):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', str(path), '-ac', '2', '-ar', str(SR), '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).T.copy()


def beat_db(x, beat0, beat):
    """Loudness of every beat of the track, in dB."""
    out = []
    t = beat0
    while t + beat <= x.shape[1] / SR:
        seg = x[:, int(t * SR):int((t + beat) * SR)]
        out.append(20 * np.log10(np.sqrt(np.mean(seg ** 2)) + 1e-9))
        t += beat
    return np.array(out)


def choose_start(x, an, video_bar, duration, drop=None, forced=None):
    """Returns the track beat the video starts on.

    The video's drop bar goes on the track's sharpest rise in loudness, ideally on one of the track's
    own bar lines; clips with silent stretches before the end card are avoided."""
    beat = 60 / an['bpm']
    if forced is not None:
        return forced
    rb = beat_db(x, an['beat0'], beat)
    # 50 ms loudness frames, to catch stops and gaps shorter than a beat
    hop = int(.05 * SR)
    mono = x.mean(axis=0)
    fr = 20 * np.log10(np.sqrt(np.convolve(mono ** 2, np.ones(hop) / hop, 'valid')[::hop]) + 1e-9)
    per_bar = int(round(video_bar / beat))
    clip = int(np.ceil(duration / beat))
    body = clip - int(np.ceil(4.5 / beat))          # end card starts here
    keep = duration - 1.6                            # everything before the final fade must have music
    down = int(round((an['downbeat'] - an['beat0']) / beat)) % 4
    d_off = (drop or 0) * per_bar
    best, bs = 0, -1e9
    for s0 in range(0, len(rb) - clip):
        d = s0 + d_off
        seg = rb[s0:s0 + body]
        jump = (rb[d:d + 4].mean() - rb[max(s0, d - 4):d].mean()) if drop else 0
        t0 = an['beat0'] + s0 * beat
        f = fr[int(t0 / .05):int((t0 + keep) / .05)]
        gaps = float((f < -34).mean()) if len(f) else 1.0
        quiet = max(0.0, -26 - seg.min()) + gaps * 60
        fullness = rb[d:s0 + body].mean() if d < s0 + body else seg.mean()
        score = jump / 3 + (fullness + 20) * .15 - quiet * .5 + (.6 if (d - down) % 4 == 0 else 0)
        if score > bs:
            best, bs = s0, score
    return best


def main():
    meta_path, out = sys.argv[1], sys.argv[2]
    m = json.load(open(meta_path, encoding='utf-8'))
    cfg = json.load(open(HERE / 'tracks.json', encoding='utf-8'))[m['id']]
    x = load(HERE / 'tracks' / cfg['file'])
    an = cfg['analysis']
    video_bar = 240 / m['bpm']
    beat = 60 / an['bpm']
    s = choose_start(x, an, video_bar, m['duration'], cfg.get('drop'), cfg.get('start_beat'))
    t0 = an['beat0'] + s * beat
    N = int(round(m['duration'] * SR))
    music = x[:, int(t0 * SR):int(t0 * SR) + N]
    if music.shape[1] < N:
        music = np.pad(music, ((0, 0), (0, N - music.shape[1])))
    fi = int(.02 * SR)
    music[:, :fi] *= np.linspace(0, 1, fi)
    fo = int(cfg.get('fade', 2.0) * SR)
    music[:, -fo:] *= np.linspace(1, 0, fo) ** 1.6
    # sound cues from the animation
    song = synth.Song(m['bpm'], m['duration'])
    synth.SFX_KEY = cfg.get('sfx_key', 0)
    synth.SFX_MINOR = cfg.get('sfx_minor', False)
    song.sfx_events(m.get('events', []), gain=1.0)
    sfx = song.buses['sfx'][:, :N]
    sfx = sfx + synth.reverb(sfx, 1.4) * .15
    peak_m = np.max(np.abs(music)) + 1e-9
    peak_s = np.max(np.abs(sfx)) + 1e-9
    mix = music / peak_m * .8 + sfx / peak_s * .8 * cfg.get('sfx_gain', .45)
    mix = np.tanh(mix * 1.1) / np.tanh(1.1)
    synth.write_wav(out, synth.norm(mix, .89))
    print(json.dumps({'id': m['id'], 'start_beat': s, 'start_s': round(t0, 3), 'bpm': m['bpm']}))


if __name__ == '__main__':
    main()
