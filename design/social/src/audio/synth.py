"""Small offline synthesizer for the Fenpo social videos.

Everything is numpy: instruments return mono (N,) or stereo (2, N) float arrays at SR, the Song
places them on buses, and render() mixes buses with reverb, delay and sidechain into a stereo master.
"""

import numpy as np
from scipy import signal

SR = 44100
RNG = np.random.default_rng(11)


# ---------------------------------------------------------------- helpers

def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


NOTE = {'C': 0, 'C#': 1, 'Db': 1, 'D': 2, 'D#': 3, 'Eb': 3, 'E': 4, 'F': 5, 'F#': 6, 'Gb': 6, 'G': 7, 'G#': 8, 'Ab': 8, 'A': 9, 'A#': 10, 'Bb': 10, 'B': 11}


def n(name):
    """'C4' -> midi number."""
    pitch = name[:-1]
    octave = int(name[-1])
    return 12 * (octave + 1) + NOTE[pitch]


def tvec(dur):
    return np.arange(int(round((dur) * SR))) / SR


def noise(dur, seed=None):
    r = RNG if seed is None else np.random.default_rng(seed)
    return r.uniform(-1, 1, int(round((dur) * SR)))


def env_adsr(dur, a=.005, d=.1, s=.7, r=.2, hold=None):
    """ADSR where `dur` is the gate length; the release is appended after it."""
    total = int(round((dur + r) * SR))
    e = np.zeros(total)
    ia, idd = int(round((a) * SR)), int(round((d) * SR))
    ig = int(round((dur) * SR))
    ia = max(ia, 1)
    e[:ia] = np.linspace(0, 1, ia)
    end_d = min(ia + idd, total)
    if end_d > ia:
        e[ia:end_d] = np.linspace(1, s, end_d - ia)
    if ig > end_d:
        e[end_d:ig] = s
    lvl = e[min(ig, total) - 1] if ig > 0 else 0
    rel = total - ig
    if rel > 0:
        e[ig:] = lvl * np.exp(-np.linspace(0, 6, rel))
    return e


def fit(e, N):
    """Trims or zero-pads an envelope to N samples."""
    if len(e) >= N:
        return e[:N]
    return np.concatenate([e, np.zeros(N - len(e))])


def exp_decay(dur, tau):
    return np.exp(-tvec(dur) / tau)


def lp(x, f, order=2):
    f = min(max(f, 20), SR / 2 - 100)
    return signal.sosfilt(signal.butter(order, f, 'low', fs=SR, output='sos'), x, axis=-1)


def hp(x, f, order=2):
    f = min(max(f, 10), SR / 2 - 100)
    return signal.sosfilt(signal.butter(order, f, 'high', fs=SR, output='sos'), x, axis=-1)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(signal.butter(order, [max(lo, 20), min(hi, SR / 2 - 100)], 'band', fs=SR, output='sos'), x, axis=-1)


def sweep(x, f0, f1, kind='low', order=2, curve='exp', block=256, q=None):
    """Time varying Butterworth filter, cutoff moving from f0 to f1 over the signal."""
    x = np.atleast_2d(x)
    N = x.shape[-1]
    out = np.zeros_like(x)
    nb = (N + block - 1) // block
    zi = None
    for b in range(nb):
        p = b / max(nb - 1, 1)
        if callable(curve):
            f = curve(p)
        elif curve == 'exp':
            f = f0 * (f1 / f0) ** p
        else:
            f = f0 + (f1 - f0) * p
        f = min(max(f, 25), SR / 2 - 200)
        sos = signal.butter(order, f, kind, fs=SR, output='sos')
        if zi is None:
            zi = np.zeros((x.shape[0], sos.shape[0], 2))
        seg = x[:, b * block:(b + 1) * block]
        for c in range(x.shape[0]):
            y, zi[c] = signal.sosfilt(sos, seg[c], zi=zi[c])
            out[c, b * block:(b + 1) * block] = y
    return out if out.shape[0] > 1 else out[0]


def osc(kind, freq, dur, phase=0.0):
    """freq may be a scalar or an array (for glides)."""
    N = int(round((dur) * SR))
    f = np.full(N, freq, dtype=float) if np.isscalar(freq) else np.asarray(freq)[:N]
    ph = phase + np.cumsum(f) / SR
    if kind == 'sin':
        return np.sin(2 * np.pi * ph)
    frac = ph % 1.0
    if kind == 'saw':
        # PolyBLEP saw
        y = 2 * frac - 1
        dt = f / SR
        y -= _blep(frac, dt)
        return y
    if kind == 'square':
        y = np.where(frac < .5, 1.0, -1.0)
        dt = f / SR
        y += _blep(frac, dt)
        y -= _blep((frac + .5) % 1, dt)
        return y
    if kind == 'tri':
        return 2 * np.abs(2 * frac - 1) - 1
    raise ValueError(kind)


def _blep(t, dt):
    y = np.zeros_like(t)
    m1 = t < dt
    x = t[m1] / dt[m1] if np.ndim(dt) else t[m1] / dt
    y[m1] = x + x - x * x - 1
    m2 = t > 1 - dt
    x = (t[m2] - 1) / (dt[m2] if np.ndim(dt) else dt)
    y[m2] = x * x + x + x + 1
    return y


def stereo(x, pan=0.0, width=0.0):
    if x.ndim == 2:
        return x
    l = np.cos((pan + 1) * np.pi / 4)
    r = np.sin((pan + 1) * np.pi / 4)
    return np.vstack([x * l * 1.414, x * r * 1.414])


def norm(x, peak=1.0):
    m = np.max(np.abs(x)) + 1e-9
    return x * (peak / m)


# ---------------------------------------------------------------- drums

def kick(dur=.5, f0=160, f1=48, tau=.16, click=.6, drive=1.4):
    t = tvec(dur)
    f = f1 + (f0 - f1) * np.exp(-t / .035)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / tau)
    c = hp(noise(dur, 1), 2500) * np.exp(-t / .004) * click
    y = np.tanh((body + c) * drive) / np.tanh(drive)
    return y * env_fade(len(y))


def kick808(note=n('A1'), dur=1.2, glide=2.0):
    t = tvec(dur)
    base = midi(note)
    f = base * (1 + (glide - 1) * np.exp(-t / .04))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / (dur * .45))
    y = np.tanh(body * 2.2) / np.tanh(2.2)
    return y * env_fade(len(y))


def env_fade(N, ms=8):
    e = np.ones(N)
    k = min(int(ms / 1000 * SR), N)
    e[-k:] = np.linspace(1, 0, k)
    return e


def snare(dur=.3, tone=190, snap=1.0):
    t = tvec(dur)
    body = np.sin(2 * np.pi * tone * t) * np.exp(-t / .05) * .7
    nz = bp(noise(dur, 2), 1200, 9000) * np.exp(-t / .07) * snap
    return np.tanh((body + nz) * 1.5) * .9


def clap(dur=.35):
    t = tvec(dur)
    nz = bp(noise(dur, 3), 900, 5000)
    e = np.zeros_like(t)
    for k, d in enumerate([0, .011, .022]):
        m = t >= d
        e[m] += np.exp(-(t[m] - d) / (.007 if k < 2 else .09))
    return nz * e * 1.1


def hat(dur=.06, open_=False, tone=7500):
    d = .32 if open_ else dur
    t = tvec(d)
    nz = hp(noise(d, 4 if open_ else 5), tone, 4)
    # metallic part: a few detuned squares
    met = sum(osc('square', f, d) for f in [3200, 4300, 5100, 6800]) * .12
    met = hp(met, 6000)
    return (nz * .8 + met) * np.exp(-t / (.09 if open_ else .018))


def shaker(dur=.08):
    t = tvec(dur)
    e = np.minimum(t / .02, 1) * np.exp(-np.maximum(t - .02, 0) / .03)
    return bp(noise(dur, 6), 5000, 12000) * e


def rim(dur=.08):
    t = tvec(dur)
    return (np.sin(2 * np.pi * 1700 * t) * .6 + bp(noise(dur, 7), 2000, 6000) * .5) * np.exp(-t / .012)


def tom(note=n('G2'), dur=.5):
    t = tvec(dur)
    f = midi(note) * (1 + .6 * np.exp(-t / .03))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / .18)


def crash(dur=2.5):
    t = tvec(dur)
    nz = hp(noise(dur, 8), 4000, 2)
    return nz * np.exp(-t / .7) * .6


def reverse_cymbal(dur=1.5):
    return crash(dur)[::-1] * np.linspace(0, 1, int(round((dur) * SR))) ** 2


# ---------------------------------------------------------------- tonal

def supersaw(notes, dur, voices=5, detune=.18, cutoff=3000, a=.01, d=.3, s=.8, r=.3, seed=0, bright_end=None):
    """Stereo detuned saw stack, notes is a list of midi numbers."""
    rng = np.random.default_rng(seed)
    e = env_adsr(dur, a, d, s, r)
    N = len(e)
    L = np.zeros(N)
    R = np.zeros(N)
    for note in notes:
        f0 = midi(note)
        for v in range(voices):
            det = (v - (voices - 1) / 2) / max((voices - 1) / 2, 1) * detune
            f = f0 * 2 ** (det / 12)
            y = osc('saw', f, N / SR, phase=rng.random())
            pan = (v / max(voices - 1, 1)) * 2 - 1
            L += y * np.cos((pan * .8 + 1) * np.pi / 4)
            R += y * np.sin((pan * .8 + 1) * np.pi / 4)
    st = np.vstack([L, R]) / (len(notes) * voices) * 2.2
    if bright_end is not None:
        st = sweep(st, cutoff, bright_end, 'low')
    else:
        st = lp(st, cutoff)
    return st * e


def pad(notes, dur, cutoff=1800, a=.6, r=1.2, voices=4, detune=.12, seed=1):
    return supersaw(notes, dur, voices=voices, detune=detune, cutoff=cutoff, a=a, d=.5, s=.85, r=r, seed=seed)


def pluck(note, dur=.3, bright=6000, dark=500, tau=.12, kind='saw', decay=.25):
    """Saw or square with a fast closing filter (crossfade between bright and dark versions)."""
    D = dur + decay
    t = tvec(D)
    f = midi(note)
    y = osc(kind, f, D) * .6 + osc(kind, f * 1.004, D) * .4
    k = np.exp(-t / tau)
    yb, yd = lp(y, bright), lp(y, dark)
    out = yb * k + yd * (1 - k)
    e = np.exp(-t / (dur * .6 + .02)) * np.minimum(t / .002, 1)
    return out * e


def epiano(note, dur=.8, vel=1.0):
    """FM electric piano: 1:1 carrier with decaying index plus a 14x tine."""
    D = dur + .6
    t = tvec(D)
    f = midi(note)
    idx = (1.6 * vel) * np.exp(-t / .35) + .2
    mod = np.sin(2 * np.pi * f * t)
    car = np.sin(2 * np.pi * f * t + idx * mod)
    tine = np.sin(2 * np.pi * f * 14 * t) * np.exp(-t / .02) * .15 * vel
    e = fit(env_adsr(dur, .003, .9, .35, .6), len(t))
    trem = 1 + .12 * np.sin(2 * np.pi * 4.5 * t)
    return (car * .8 + tine) * e * trem * vel


def bell(note, dur=1.2, ratio=3.5, index=2.5, tau=.6):
    t = tvec(dur)
    f = midi(note)
    idx = index * np.exp(-t / (tau * .5))
    y = np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * f * ratio * t))
    return y * np.exp(-t / tau) * np.minimum(t / .002, 1)


def piano(note, dur=1.0, vel=1.0):
    """Additive piano-ish tone with inharmonic partials and per-partial decay."""
    D = dur + 1.2
    t = tvec(D)
    f = midi(note)
    y = np.zeros_like(t)
    B = .0004
    for k in range(1, 9):
        fk = f * k * np.sqrt(1 + B * k * k)
        if fk > 16000:
            break
        amp = (1 / k ** 1.3) * vel
        y += amp * np.sin(2 * np.pi * fk * t) * np.exp(-t * (0.9 + k * .55) / (1 + (60 - min(note, 60)) * .02))
    hammer = bp(noise(.02, 9), 1500, 6000) * .06 * vel
    y[:len(hammer)] += hammer
    rel = np.ones_like(t)
    g = int(round((dur) * SR))
    if g < len(t):
        rel[g:] = np.exp(-np.arange(len(t) - g) / (.15 * SR))
    return y * rel * .5


def bass(note, dur, kind='saw', cutoff=600, env_amt=1200, tau=.08, sub=.6, drive=1.5):
    D = dur + .05
    t = tvec(D)
    f = midi(note)
    y = osc(kind, f, D)
    k = np.exp(-t / tau)
    y = lp(y, cutoff + env_amt) * k + lp(y, cutoff) * (1 - k)
    y = y + osc('sin', f, D) * sub
    e = fit(env_adsr(dur, .004, .1, .9, .04), len(t))
    return np.tanh(y * drive) * e * .7


def reese(note, dur, cutoff=900):
    D = dur + .05
    f = midi(note)
    y = osc('saw', f * 2 ** (-.12 / 12), D) + osc('saw', f * 2 ** (.12 / 12), D)
    y = lp(y, cutoff, 4) * .5 + osc('sin', f, D) * .5
    return np.tanh(y * 1.6) * fit(env_adsr(dur, .01, .1, .9, .04), int(round((D) * SR))) * .7


def sub(note, dur, glide_from=None):
    t = tvec(dur + .05)
    f = np.full(len(t), midi(note))
    if glide_from is not None:
        f = midi(note) + (midi(glide_from) - midi(note)) * np.exp(-t / .06)
    y = np.sin(2 * np.pi * np.cumsum(f) / SR)
    return np.tanh(y * 1.3) * fit(env_adsr(dur, .005, .1, .95, .05), len(t))


def lead(note, dur, kind='square', cutoff=3500, vib=.15, glide_from=None):
    D = dur + .1
    t = tvec(D)
    f = np.full(len(t), midi(note))
    if glide_from is not None:
        f = midi(note) + (midi(glide_from) - midi(note)) * np.exp(-t / .04)
    f = f * (1 + vib / 100 * np.sin(2 * np.pi * 5.5 * t) * np.minimum(t / .3, 1))
    y = osc(kind, f, D) * .6 + osc('saw', f * 1.003, D) * .4
    return lp(y, cutoff) * fit(env_adsr(dur, .01, .2, .7, .1), len(t))


def strings(notes, dur, cutoff=2600, a=.4, r=.9):
    st = supersaw(notes, dur, voices=6, detune=.09, cutoff=cutoff, a=a, d=.2, s=.9, r=r, seed=5)
    t = np.arange(st.shape[1]) / SR
    return st * (1 + .06 * np.sin(2 * np.pi * 5 * t))


def choir(notes, dur, a=.5, r=1.0):
    """Formant-ish 'aah' pad: saw through two vowel band-passes."""
    out = None
    for note in notes:
        y = osc('saw', midi(note) * (1 + .002 * np.sin(2 * np.pi * 5 * tvec(dur + r))), dur + r)
        v = bp(y, 650, 1100) + bp(y, 1000, 1400) * .6 + lp(y, 400) * .4
        out = v if out is None else out + v
    return stereo(out / len(notes) * fit(env_adsr(dur, a, .3, .9, r), len(out)), 0)


# ---------------------------------------------------------------- fx

def riser(dur=4.0, f0=300, f1=9000):
    t = tvec(dur)
    nz = sweep(noise(dur, 10), f0, f1, 'low', order=2)
    nz = hp(nz, 200)
    pitch = osc('saw', 110 * 2 ** (t / dur * 3), dur) * .25
    pitch = sweep(pitch, 400, 6000, 'low')
    e = (t / dur) ** 2.2
    return (nz * .8 + pitch) * e


def downlifter(dur=2.0):
    return riser(dur)[::-1] * np.linspace(1, 0, int(round((dur) * SR))) ** .5


def impact(dur=3.0):
    t = tvec(dur)
    f = 30 + 60 * np.exp(-t / .12)
    boom = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / .7)
    nz = lp(noise(dur, 11), 3000) * np.exp(-t / .25) * .5
    return np.tanh((boom + nz) * 1.8) * .9


def whoosh(dur=.6, up=True):
    t = tvec(dur)
    curve = (lambda p: 400 * (12 ** p)) if up else (lambda p: 5000 / (12 ** p))
    nz = sweep(noise(dur, 12), 0, 0, 'low', curve=curve)
    e = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 1.5
    return nz * e


def swish(dur=.35):
    t = tvec(dur)
    nz = sweep(noise(dur, 13), 0, 0, 'band' if False else 'low', curve=lambda p: 1500 + 8000 * p)
    return hp(nz, 800) * np.sin(np.pi * t / dur) ** 2


# Key of the current track, so UI sounds stay in tune with the music.
SFX_KEY = 0        # semitones above C
SFX_MINOR = False


def _deg(i):
    scale = [0, 2, 3, 5, 7, 8, 10] if SFX_MINOR else [0, 2, 4, 5, 7, 9, 11]
    return scale[i % 7] + 12 * (i // 7)


def ping(pitch=0, dur=.9):
    base = n('C6') + SFX_KEY + [_deg(0), _deg(2), _deg(4), 12][pitch % 4]
    a = bell(base, dur, ratio=2.0, index=1.2, tau=.25)
    b = bell(base + 7 if pitch % 4 != 2 else base + 5, dur, ratio=2.0, index=1.0, tau=.3)
    off = int(.07 * SR)
    y = np.zeros(len(a) + off)
    y[:len(a)] += a
    y[off:off + len(b)] += b * .8
    return y * .5


def blip(pitch=0, dur=.18):
    t = tvec(dur)
    f = midi(n('C6') + SFX_KEY + [_deg(0), _deg(2), _deg(4), 12][pitch % 4]) * (1 + .4 * np.exp(-t / .01))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / .05) * .5


def pop(dur=.25):
    t = tvec(dur)
    f = 300 + 900 * np.exp(-t / .015)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / .05)


def tick(pitch=0, dur=.12):
    t = tvec(dur)
    f = midi(n('C7') + SFX_KEY + _deg(pitch % 8))
    return np.sin(2 * np.pi * f * t) * np.exp(-t / .02) * .35


def click(dur=.05):
    t = tvec(dur)
    return (bp(noise(dur, 14), 2000, 8000) * np.exp(-t / .004) + np.sin(2 * np.pi * 3000 * t) * np.exp(-t / .006) * .4) * .7


def key(dur=.06, seed=0):
    t = tvec(dur)
    return bp(noise(dur, 100 + seed), 1500 + (seed % 5) * 300, 7000) * np.exp(-t / .008) * .5


def hit(dur=.5):
    k = kick(dur, f0=200, f1=55, tau=.09, click=1.0)
    c = clap(min(dur, .35))
    y = k.copy()
    y[:len(c)] += c * .5
    return y


def glitch(dur=.25, seed=0):
    r = np.random.default_rng(200 + seed)
    y = np.zeros(int(round((dur) * SR)))
    pos = 0
    while pos < len(y):
        L = int(r.uniform(.008, .04) * SR)
        f = r.choice([220, 440, 880, 1760, 3520])
        seg = osc('square', f, L / SR) * r.uniform(.2, .6)
        y[pos:pos + L] = seg[:len(y) - pos]
        pos += L + int(r.uniform(0, .02) * SR)
    return hp(y, 300) * .5


def crackle(dur):
    r = np.random.default_rng(15)
    N = int(round((dur) * SR))
    y = np.zeros(N)
    idx = r.integers(0, N, int(dur * 25))
    y[idx] = r.uniform(-1, 1, len(idx))
    y = bp(y, 800, 6000) * 2
    hiss = hp(noise(dur, 16), 5000) * .02
    return y + hiss


def heartbeat(dur=.8):
    a = kick(.3, f0=90, f1=40, tau=.08, click=0)
    y = np.zeros(int(round((dur) * SR)))
    y[:len(a)] += a
    o = int(.22 * SR)
    y[o:o + len(a)] += a * .7
    return lp(y, 200)


def lock_sfx():
    a = click(.05)
    b = tick(0) * 1.5
    y = np.zeros(int(.3 * SR))
    y[:len(a)] += a
    o = int(.08 * SR)
    y[o:o + len(b)] += b
    return y


def success(dur=1.0):
    y = np.zeros(int(round((dur) * SR)))
    for i, nn in enumerate([n('C6') + SFX_KEY + _deg(k) for k in (0, 2, 4)]):
        b = bell(nn, .8, ratio=2, index=.8, tau=.3)
        o = int(i * .07 * SR)
        y[o:o + len(b)] += b[:len(y) - o]
    return y * .4


def ringtone(dur=1.6):
    """Soft marimba-like ring pattern."""
    y = np.zeros(int(round((dur) * SR)))
    r = n('C6') + SFX_KEY
    seq = [r + 12, r + 7, r + 12, r + 12 + _deg(2)]
    for i, nn in enumerate(seq):
        b = bell(nn, .5, ratio=4.0, index=1.0, tau=.15)
        o = int(i * .14 * SR)
        y[o:o + len(b)] += b[:len(y) - o]
    return y * .45


def tape_stop(x, dur):
    """Slows a stereo signal to a halt (used for outros)."""
    N = x.shape[-1]
    t = np.linspace(0, 1, N)
    speed = (1 - t) ** 1.5
    pos = np.cumsum(speed)
    pos = pos / pos[-1] * (N - 1) * (dur * SR / N if False else 1) * .55
    idx = np.clip(pos.astype(int), 0, N - 1)
    return x[..., idx] * (1 - t) ** .5


SFX = {
    'hit': lambda e: hit(),
    'impact': lambda e: impact(),
    'riser': lambda e: riser(e.get('dur', 3.0)),
    'downlifter': lambda e: downlifter(e.get('dur', 2.0)),
    'whoosh': lambda e: whoosh(e.get('dur', .6)),
    'whooshdown': lambda e: whoosh(e.get('dur', .6), up=False),
    'swish': lambda e: swish(),
    'ping': lambda e: ping(e.get('pitch', 0)),
    'blip': lambda e: blip(e.get('pitch', 0)),
    'pop': lambda e: pop(),
    'tick': lambda e: tick(e.get('pitch', 0)),
    'click': lambda e: click(),
    'key': lambda e: key(seed=e.get('pitch', 0)),
    'glitch': lambda e: glitch(seed=e.get('pitch', 0)),
    'heartbeat': lambda e: heartbeat(),
    'lock': lambda e: lock_sfx(),
    'success': lambda e: success(),
    'ring': lambda e: ringtone(),
    'crash': lambda e: crash(),
    'revcym': lambda e: reverse_cymbal(e.get('dur', 1.5)),
    'buzz': lambda e: buzz(),
}


def buzz(dur=.45):
    t = tvec(dur)
    y = osc('saw', 150, dur) * (np.sin(2 * np.pi * 30 * t) > 0)
    return lp(y, 400) * .35 * np.minimum(1, (dur - t) / .05)


# ---------------------------------------------------------------- reverb and delay

def make_ir(rt=2.0, pre=.015, damp=6000, seed=20):
    r = np.random.default_rng(seed)
    N = int(round((rt) * SR))
    t = np.arange(N) / SR
    decay = np.exp(-6.9 * t / rt)
    ir = r.standard_normal((2, N)) * decay
    ir = sweep(ir, damp, damp / 4, 'low', block=2048)
    p = int(round((pre) * SR))
    ir = np.concatenate([np.zeros((2, p)), ir], axis=1)
    # a few early reflections
    for k, (d, g) in enumerate([(.011, .5), (.017, .4), (.023, .35), (.031, .3)]):
        i = int(round((d) * SR))
        ir[k % 2, i] += g
    return ir / np.sqrt(np.sum(ir ** 2) / 2)


def reverb(x, rt=2.0, damp=6000, pre=.015):
    ir = make_ir(rt, pre, damp)
    x = np.atleast_2d(x)
    if x.shape[0] == 1:
        x = np.vstack([x, x])
    out = np.vstack([signal.fftconvolve(x[c], ir[c])[:x.shape[1]] for c in range(2)])
    return out


def delay(x, time, fb=.35, taps=6, pingpong=True, damp=4500):
    x = np.atleast_2d(x)
    if x.shape[0] == 1:
        x = np.vstack([x, x])
    out = np.zeros_like(x)
    d = int(round((time) * SR))
    cur = x
    for k in range(1, taps + 1):
        cur = lp(cur, damp) * fb
        sh = np.zeros_like(x)
        if d * k < x.shape[1]:
            sh[:, d * k:] = cur[:, :x.shape[1] - d * k]
        if pingpong and k % 2 == 1:
            sh = sh[::-1]
        out += sh
    return out


# ---------------------------------------------------------------- song

class Song:
    BUSES = ['drums', 'bass', 'music', 'lead', 'pad', 'fx', 'sfx']

    def __init__(self, bpm, duration, swing=0.0):
        self.bpm = bpm
        self.beat = 60.0 / bpm
        self.bar = 4 * self.beat
        self.duration = duration
        self.N = int(round((duration + 0.05) * SR))
        self.buses = {b: np.zeros((2, self.N)) for b in self.BUSES}
        self.kicks = []
        self.swing = swing
        self.sends = {'drums': .08, 'bass': 0, 'music': .25, 'lead': .3, 'pad': .35, 'fx': .4, 'sfx': .18}
        self.dsend = {'music': 0, 'lead': .25}
        self.duck = {'bass': .8, 'music': .45, 'pad': .55, 'lead': .25, 'fx': 0, 'drums': 0, 'sfx': 0}
        self.rt = 2.2
        self.bus_gain = {b: 1.0 for b in self.BUSES}
        self.autos = []

    def b(self, beats):
        """Beat position -> seconds, with swing on off-beat sixteenths."""
        half = beats * 2
        if self.swing and abs(half - round(half)) < 1e-6 and int(round(half)) % 2 == 1:
            beats += self.swing / 6
        return beats * self.beat

    def add(self, x, t, bus='music', gain=1.0, pan=0.0):
        if t < 0:
            cut = int(-t * SR)
            x = x[..., cut:]
            t = 0
        s = stereo(np.asarray(x, dtype=float), pan) * gain
        i = int(round(t * SR))
        if i >= self.N:
            return
        L = min(s.shape[1], self.N - i)
        self.buses[bus][:, i:i + L] += s[:, :L]

    def at(self, beats, x, bus='music', gain=1.0, pan=0.0):
        self.add(x, self.b(beats), bus, gain, pan)

    def kick_at(self, beats, x=None, gain=1.0):
        x = kick() if x is None else x
        self.at(beats, x, 'drums', gain * .72)
        self.kicks.append(self.b(beats))

    def sfx_events(self, events, gain=1.0):
        for e in events:
            fn = SFX.get(e['name'])
            if fn is None:
                continue
            self.add(fn(e), e['t'], 'sfx', gain * e.get('gain', 1.0), pan=e.get('pan', 0.0))

    def automate(self, bus, t0, t1, kind, f0, f1):
        """Filter sweep on a bus between two times (used for filtered intros and builds)."""
        self.autos.append((bus, t0, t1, kind, f0, f1))

    def render(self, master_gain=1.0, fade_out=1.2):
        N = self.N
        # sidechain envelope from kick times
        sc = np.ones(N)
        rel = .22 * (120 / self.bpm)
        for tk in self.kicks:
            i = int(round((tk) * SR))
            L = min(int(round((rel) * SR)), N - i)
            if L <= 0:
                continue
            ramp = np.linspace(0, 1, L) ** .6
            sc[i:i + L] = np.minimum(sc[i:i + L], ramp)
        for bus, t0, t1, kind, f0, f1 in self.autos:
            i0, i1 = int(round((t0) * SR)), min(int(round((t1) * SR)), N)
            seg = self.buses[bus][:, i0:i1]
            if seg.shape[1] > 0:
                self.buses[bus][:, i0:i1] = sweep(seg, f0, f1, kind)
        mix = np.zeros((2, N))
        send = np.zeros((2, N))
        dsend = np.zeros((2, N))
        for name, x in self.buses.items():
            d = self.duck.get(name, 0)
            y = x * (1 - d * (1 - sc)) * self.bus_gain[name]
            mix += y
            send += y * self.sends.get(name, 0)
            dsend += y * self.dsend.get(name, 0)
        if np.any(dsend):
            send += delay(dsend, self.beat * .75, fb=.4)
            mix += delay(dsend, self.beat * .75, fb=.4) * .5
        mix += reverb(send, self.rt)
        mix = hp(mix, 28)
        # gentle glue: soft clip after normalising
        mix = norm(mix, 1.0) * 1.6 * master_gain
        mix = np.tanh(mix) / np.tanh(1.6)
        # fades
        fi = int(.01 * SR)
        mix[:, :fi] *= np.linspace(0, 1, fi)
        fo = int(round((fade_out) * SR))
        mix[:, -fo:] *= np.linspace(1, 0, fo) ** 1.5
        return norm(mix, .89)


def write_wav(path, x):
    from scipy.io import wavfile
    y = np.clip(x.T, -1, 1)
    wavfile.write(path, SR, (y * 32767).astype(np.int16))
