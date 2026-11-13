"""Tempo, beat and bar analysis for a downloaded music track.

    python beats.py <track.mp3> [bpm_hint]

Prints JSON: bpm, first downbeat time, bar times and a loudness value per bar. The video is then
re-timed to this bpm and the track is cut so a bar line falls on the video's first frame.
"""

import json
import subprocess
import sys

import numpy as np
from scipy import signal

SR = 22050


def load(path):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).copy()


def onset_envelope(x, hop=256, n_fft=1024):
    f, t, Z = signal.stft(x, SR, nperseg=n_fft, noverlap=n_fft - hop, boundary=None)
    mag = np.log1p(np.abs(Z) * 100)
    # weight low end (kick) a little more, it carries the downbeat
    w = np.ones(len(f))
    w[f < 150] = 2.0
    flux = np.maximum(0, np.diff(mag, axis=1)) * w[:, None]
    env = flux.sum(axis=0)
    env = env - signal.medfilt(env, 31)
    env = np.maximum(env, 0)
    return env / (env.max() + 1e-9), SR / hop


def low_envelope(x, hop=256):
    b = signal.butter(4, 140, 'low', fs=SR, output='sos')
    lo = signal.sosfilt(b, x)
    e = np.sqrt(np.convolve(lo ** 2, np.ones(hop) / hop, 'same'))[::hop]
    return e / (e.max() + 1e-9)


def tempo(env, fps, lo=70, hi=180, hint=None):
    ac = np.correlate(env, env, 'full')[len(env) - 1:]
    lags = np.arange(len(ac))
    bpms = 60 * fps / np.maximum(lags, 1)
    best, score = None, -1
    for bpm in np.arange(lo, hi, 0.05):
        lag = 60 * fps / bpm
        s = 0
        for k, wgt in ((1, 1.0), (2, .6), (4, .4)):
            L = lag * k
            i = int(L)
            if i + 1 >= len(ac):
                continue
            s += wgt * (ac[i] * (1 - (L - i)) + ac[i + 1] * (L - i))
        # mild prior towards 90-130 and towards the hint
        prior = np.exp(-0.5 * (np.log2(bpm / (hint or 112)) / .5) ** 2)
        s *= prior
        if s > score:
            best, score = bpm, s
    return float(best)


def beat_phase(env, fps, bpm):
    period = 60 * fps / bpm
    n = len(env)
    best, bscore = 0, -1
    for ph in np.arange(0, period, .25):
        idx = np.arange(ph, n - 1, period)
        i = idx.astype(int)
        s = (env[i] * (1 - (idx - i)) + env[i + 1] * (idx - i)).sum()
        if s > bscore:
            best, bscore = ph, s
    return best / fps


def refine_bpm(env, fps, bpm, phase):
    """Small search around bpm so the beat grid stays locked over the whole track."""
    best, bs = bpm, -1
    for b in np.arange(bpm - .6, bpm + .6, .01):
        period = 60 * fps / b
        for ph in np.arange(phase * fps - 2, phase * fps + 2, .5):
            if ph < 0:
                continue
            idx = np.arange(ph, len(env) - 1, period)
            i = idx.astype(int)
            s = env[i].sum()
            if s > bs:
                bs, best, bph = s, b, ph
    return float(best), float(bph / fps)


def kick_onsets(x, hop=256):
    """Positive slope of the sub-150 Hz envelope: where kicks and bass notes start."""
    b = signal.butter(4, 150, 'low', fs=SR, output='sos')
    lo = np.abs(signal.sosfilt(b, x))
    sm = signal.sosfilt(signal.butter(2, 40, 'low', fs=SR, output='sos'), lo)[::hop]
    d = np.maximum(0, np.diff(sm, prepend=sm[0]))
    return d / (d.max() + 1e-9)


def analyse(path, hint=None):
    x = load(path)
    env, fps = onset_envelope(x)
    bpm = tempo(env, fps, hint=hint)
    # phase from kicks first (they sit on the beat), the full-band onsets as a tie breaker
    grid = kick_onsets(x)[:len(env)] * 2 + env[:len(kick_onsets(x))] * .5
    ph = beat_phase(grid, fps, bpm)
    bpm, ph = refine_bpm(grid, fps, bpm, ph)
    # produced music sits on whole bpm values; snap and re-find the phase for that tempo
    if abs(bpm - round(bpm)) < .45:
        bpm = float(round(bpm))
        ph = beat_phase(grid, fps, bpm)
    beat = 60 / bpm
    beats = np.arange(ph, len(x) / SR, beat)
    # downbeat: the beat-in-bar position with the strongest low end
    lowe = low_envelope(x)
    lfps = SR / 256
    strength = []
    for k in range(4):
        sel = beats[k::4]
        ii = np.clip((sel * lfps).astype(int), 0, len(lowe) - 1)
        oi = np.clip((sel * fps).astype(int), 0, len(env) - 1)
        strength.append(lowe[ii].mean() + env[oi].mean())
    k0 = int(np.argmax(strength))
    bars = beats[k0::4]
    rms = []
    for i in range(len(bars)):
        a = int(bars[i] * SR)
        b = int((bars[i + 1] if i + 1 < len(bars) else bars[i] + 4 * beat) * SR)
        seg = x[a:b]
        rms.append(float(20 * np.log10(np.sqrt(np.mean(seg ** 2)) + 1e-9)) if len(seg) else -90.0)
    return {'bpm': round(bpm, 3), 'beat0': round(float(beats[0]), 4), 'downbeat': round(float(bars[0]), 4),
            'bars': [round(float(b), 4) for b in bars], 'bar_db': [round(v, 2) for v in rms], 'duration': round(len(x) / SR, 3)}


if __name__ == '__main__':
    hint = float(sys.argv[2]) if len(sys.argv) > 2 else None
    print(json.dumps(analyse(sys.argv[1], hint)))
