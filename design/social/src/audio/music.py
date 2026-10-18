"""One original track per video, written to the video's bar grid.

    python music.py <meta.json> <out.wav>

The meta file comes from render.mjs and carries bpm, duration and the sound cues the animation
placed (pings, whooshes, impacts); those are mixed on the sfx bus on top of the music.
"""

import json
import sys

import numpy as np

import synth
from synth import *  # noqa: F401,F403


def chord(names):
    return [n(x) for x in names.split()]


def key(root, minor=False):
    synth.SFX_KEY = root
    synth.SFX_MINOR = minor


def pat(s, b0, b1, rows):
    """Step sequencer. rows: (pattern, sound, bus, gain, pan[, is_kick]). 'x' = full, 'o' = soft, '.' = rest.
    A pattern of 16 steps is one bar of sixteenths and repeats from bar b0 to b1."""
    for row in rows:
        p, snd, bus, gain, pan = row[:5]
        is_kick = len(row) > 5 and row[5]
        steps = len(p)
        per_bar = 16
        for bar in range(b0, b1):
            for i, ch in enumerate(p):
                if ch == '.':
                    continue
                if steps > per_bar and (bar - b0) % (steps // per_bar) != i // per_bar:
                    continue
                beat = bar * 4 + (i % per_bar) / 4
                g = gain * (1.0 if ch == 'x' else .55)
                x = snd() if callable(snd) else snd
                if is_kick:
                    s.kick_at(beat, x, g)
                else:
                    s.at(beat, x, bus, g, pan)


def drums_house(s, b0, b1, clap_=True, open_hat=True, hats16=True, kick_=True, gain=1.0):
    for bar in range(b0, b1):
        for q in range(4):
            beat = bar * 4 + q
            if kick_:
                s.kick_at(beat, gain=.95 * gain)
            if clap_ and q in (1, 3):
                s.at(beat, clap(), 'drums', .55 * gain)
            if open_hat:
                s.at(beat + .5, hat(open_=True), 'drums', .22 * gain, pan=.15)
            if hats16:
                for k in (.25, .75):
                    s.at(beat + k, hat(), 'drums', .16 * gain, pan=-.2)


def snare_roll(s, b0, bars, gain=.5, snd=None):
    total = bars * 4
    t = 0.0
    while t < total - 1e-6:
        p = t / total
        step = 1.0 if p < .5 else .5 if p < .75 else .25 if p < .9 else .125
        s.at(b0 * 4 + t, (snd or (lambda: snare(.2)))(), 'drums', gain * (.35 + .65 * p))
        t += step


def prog_pad(s, bar0, prog, bars_each=1, fn=None, bus='pad', gain=.5, **kw):
    fn = fn or (lambda notes, dur: pad(notes, dur, **kw))
    for i, c in enumerate(prog):
        s.at((bar0 + i * bars_each) * 4, fn(c, bars_each * s.bar), bus, gain)


def arp(s, b0, b1, prog, pattern, fn, rate=.25, gain=.25, bus='music', octave=12, pan_alt=.3):
    """Arpeggiates the chord of each bar. pattern indexes chord tones (may exceed, wrapping up an octave)."""
    k = 0
    for bar in range(b0, b1):
        c = prog[(bar - b0) % len(prog)]
        steps = int(4 / rate)
        for i in range(steps):
            idx = pattern[i % len(pattern)]
            if idx is None:
                continue
            nn = c[idx % len(c)] + octave + 12 * (idx // len(c))
            s.at(bar * 4 + i * rate, fn(nn), bus, gain, pan=(pan_alt if k % 2 else -pan_alt))
            k += 1


def strum(s, beat, notes, fn, spread=.025, bus='music', gain=.3):
    for i, nn in enumerate(notes):
        s.add(fn(nn), s.b(beat) + i * spread, bus, gain, pan=(i / max(len(notes) - 1, 1) - .5) * .6)


# ------------------------------------------------------------------ r01 house 124, F minor

def r01(s, m):
    key(5, True)
    F = [chord('F3 Ab3 C4 Eb4'), chord('F3 Ab3 Db4 F4'), chord('Eb3 Ab3 C4 Eb4'), chord('Eb3 G3 Bb3 Eb4')]
    roots = [n('F1'), n('Db2'), n('Ab1'), n('Eb2')]
    prog_pad(s, 0, F[:2], 1, gain=.55, cutoff=1400)
    for bar in range(0, 2):
        for q in range(4):
            s.kick_at(bar * 4 + q, gain=.7)
            s.at(bar * 4 + q + .5, hat(), 'drums', .12)
    s.automate('drums', 0, 2 * s.bar, 'low', 500, 6000)
    prog_pad(s, 2, F[2:], 1, gain=.6, cutoff=1800)
    for bar in range(2, 4):
        for q in range(4):
            if bar == 3 and q >= 2:
                continue
            s.kick_at(bar * 4 + q, gain=.85)
    snare_roll(s, 2, 2, .45)
    for k in range(16):
        s.at(8 + k * .5, pluck(n('F4') + [0, 7, 12, 7][k % 4], .18, bright=3000 + k * 400), 'music', .3, pan=(-.4 if k % 2 else .4))
    s.automate('pad', 2 * s.bar, 4 * s.bar, 'low', 500, 9000)
    drums_house(s, 4, 8)
    s.at(16, crash(), 'drums', .35)
    s.at(24, crash(), 'drums', .25)
    for bar in range(4, 8):
        r = roots[(bar - 4) % 4]
        for q in range(4):
            s.at(bar * 4 + q + .5, bass(r + 12, s.beat * .45, cutoff=500, env_amt=1800), 'bass', .75)
        c = F[(bar - 4) % 4]
        for k in (0, 1.5, 2.5, 3.5):
            s.at(bar * 4 + k, supersaw([x + 12 for x in c], s.beat * .35, cutoff=4500, a=.003, d=.15, s=.3, r=.12, seed=bar), 'music', .5)
        s.at(bar * 4, pad(c, s.bar, cutoff=2200), 'pad', .3)
    hook = [(0, 'C5'), (.75, 'Ab4'), (1.5, 'F4'), (2.5, 'Eb5'), (3, 'C5')]
    for bar in range(4, 8):
        for off, nm in hook:
            s.at(bar * 4 + off, pluck(n(nm) + 12, .2, bright=7000, kind='square'), 'lead', .24, pan=.1)
    s.at(32, pad(chord('F2 C3 F3 Ab3 C4'), 3.2, cutoff=2400, a=.02, r=2.5), 'pad', .7)
    s.at(32, sub(n('F1'), 2.5), 'bass', .6)
    for i, nm in enumerate(['C6', 'Ab5', 'F5', 'C6', 'Eb6']):
        s.at(32 + 1 + i * .5, bell(n(nm), 1.4, ratio=2, index=1, tau=.5), 'lead', .2, pan=(-.3 + i * .15))
    s.rt = 2.6


# ------------------------------------------------------------------ r02 playful pluck pop 110, Bb major

def marimba(nn, dur=.45):
    return bell(nn, dur, ratio=4.0, index=.7, tau=.16) * .9 + bell(nn, dur, ratio=1.0, index=.3, tau=.25) * .4


def r02(s, m):
    key(-2)
    P = [chord('Bb3 D4 F4'), chord('A3 C4 F4'), chord('G3 Bb3 D4'), chord('G3 Bb3 Eb4')]
    R = [n('Bb1'), n('A1'), n('G1'), n('Eb2')]
    riff = [0, None, 2, 1, None, 2, 0, None, 1, None, 2, None, 3, 2, None, 1]
    # intro: marimba riff and finger snaps
    arp(s, 0, 8, P, riff, lambda x: marimba(x), rate=.25, gain=.28, octave=12)
    pat(s, 0, 2, [('....x.......x...', lambda: clap(.2), 'drums', .35, .1)])
    pat(s, 1, 2, [('x.......x.......', None, 'drums', .7, 0, True)])
    # groove
    pat(s, 2, 8, [
        ('x.....x...x.....', None, 'drums', .9, 0, True),
        ('....x.......x...', clap, 'drums', .55, 0),
        ('x.x.x.x.x.x.x.x.', shaker, 'drums', .28, .25),
        ('..o...o...o...oo', hat, 'drums', .18, -.25),
    ])
    for bar in range(2, 8):
        r = R[bar % 4]
        for off, ln in ((0, .4), (.75, .2), (1.5, .4), (2.5, .2), (3, .4), (3.5, .2)):
            s.at(bar * 4 + off, bass(r + 12, s.beat * ln * 1.6, kind='square', cutoff=400, env_amt=900, sub=.8), 'bass', .6)
    for bar in range(2, 8):
        s.at(bar * 4, pad([x + 12 for x in P[bar % 4]], s.bar, cutoff=2500, a=.05), 'pad', .25)
    mel = [(0, 'F5'), (.5, 'D5'), (1, 'F5'), (1.5, 'G5'), (2.5, 'F5'), (3, 'D5'), (6, 'C5'), (6.5, 'D5'), (7, 'F5'),
           (8, 'G5'), (8.5, 'F5'), (9, 'D5'), (10, 'Bb4'), (12, 'C5'), (13, 'D5'), (14, 'Eb5'), (15, 'D5')]
    for rep in (4, 6):
        for off, nm in mel:
            if rep * 4 + off < 32:
                s.at(rep * 4 + off, bell(n(nm), .9, ratio=2, index=1.2, tau=.35), 'lead', .22)
    # lift: open hats
    pat(s, 6, 8, [('..x...x...x...x.', lambda: hat(open_=True), 'drums', .18, .2)])
    s.at(24, crash(), 'drums', .25)
    # outro
    s.at(32, pad(chord('Bb2 F3 Bb3 D4 F4'), 3.0, cutoff=2600, a=.02, r=2.5), 'pad', .65)
    s.at(32, sub(n('Bb1'), 2.0), 'bass', .5)
    for i, nm in enumerate(['D6', 'F6', 'Bb6']):
        s.at(32.5 + i * .5, marimba(n(nm), .8), 'lead', .3)


# ------------------------------------------------------------------ r03 lo-fi 90, C major, swing

def r03(s, m):
    key(0)
    s.swing = .55
    C = [chord('F3 A3 C4 E4'), chord('E3 G3 B3 D4'), chord('D3 F3 A3 C4'), chord('C3 E3 G3 B3')]
    R = [n('F2'), n('E2'), n('D2'), n('C2')]
    s.add(crackle(s.duration), 0, 'fx', .25)
    for bar in range(0, 7):
        c = C[bar % 4]
        strum(s, bar * 4, c, lambda x: epiano(x, s.bar * .9, .8), spread=.03, gain=.42)
        strum(s, bar * 4 + 2.5, c[1:], lambda x: epiano(x, s.beat * .6, .5), spread=.02, gain=.25)
    pat(s, 1, 7, [
        ('x.........x.x...', lambda: kick(.4, f0=120, f1=50, tau=.12, click=.2), 'drums', .9, 0, True),
        ('....x.......x...', lambda: lp(snare(.25, tone=180), 5000), 'drums', .5, .05),
        ('x.x.x.x.x.x.x.x.', lambda: lp(hat(), 7000), 'drums', .2, -.2),
        ('...............o', rim, 'drums', .3, .3),
    ])
    for bar in range(1, 7):
        r = R[bar % 4]
        s.at(bar * 4, sub(r, s.beat * 1.6), 'bass', .55)
        s.at(bar * 4 + 2.5, sub(r, s.beat * 1.2), 'bass', .45)
    mel = [(0, 'E5'), (.5, 'G5'), (1.5, 'A5'), (3, 'G5'), (4.5, 'E5'), (5, 'D5'), (6, 'C5'), (8, 'A4'), (9, 'C5'), (10.5, 'D5'), (12, 'E5'), (14, 'G5')]
    for off, nm in mel:
        s.at(8 + off, epiano(n(nm), .5, .6), 'lead', .28, pan=.2)
    s.automate('music', 0, s.bar, 'low', 900, 6000)
    s.bus_gain['drums'] = .9
    s.at(28, epiano(n('C4'), 3, .8), 'music', .3)
    for i, nm in enumerate(['C4', 'E4', 'G4', 'B4', 'D5']):
        s.at(28 + i * .25, epiano(n(nm), 3.2, .7), 'music', .3, pan=-.3 + i * .15)
    s.at(28, sub(n('C2'), 2.5), 'bass', .5)
    s.rt = 1.8


# ------------------------------------------------------------------ r04 minimal tech 128, A minor

def r04(s, m):
    key(-3, True)
    A = [chord('A3 C4 E4 G4'), chord('A3 C4 E4 G4'), chord('F3 A3 C4 E4'), chord('G3 B3 D4 F#4')]
    R = [n('A1'), n('A1'), n('F1'), n('G1')]
    pat(s, 0, 8, [
        ('x...x...x...x...', None, 'drums', .9, 0, True),
        ('..x...x...x...x.', rim, 'drums', .3, .3),
        ('xoxoxoxoxoxoxoxo', shaker, 'drums', .22, -.25),
    ])
    pat(s, 2, 8, [('....x.......x...', clap, 'drums', .5, 0)])
    pat(s, 4, 8, [('..x...x...x...x.', lambda: hat(open_=True), 'drums', .16, .15)])
    s.automate('drums', 0, 2 * s.bar, 'low', 700, 9000)
    for bar in range(0, 8):
        r = R[bar % 4]
        for i in range(16):
            if i % 4 == 0:
                continue
            s.at(bar * 4 + i / 4, bass(r + 12 + (12 if i in (6, 14) else 0), s.beat * .2, kind='saw', cutoff=300, env_amt=1500, tau=.05), 'bass', .5)
    for bar in range(4, 8):
        c = A[bar % 4]
        for off in (0, .75, 1.5, 2.25, 3.5):
            s.at(bar * 4 + off, supersaw([x + 12 for x in c], s.beat * .22, cutoff=3800, a=.002, d=.1, s=.2, r=.1, seed=bar), 'music', .4)
    for bar in range(2, 4):
        arp(s, bar, bar + 1, [A[bar % 4]], [0, 2, 1, 3], lambda x: pluck(x, .12, bright=5000), rate=.25, gain=.22, octave=12)
    s.at(32, crash(), 'drums', .2)
    # bar 8: "Copy. Paste. Done." stabs on beats 0, 1, 2 then silence
    for i in range(3):
        s.at(32 + i, supersaw([x + 12 for x in A[0]], s.beat * .5, cutoff=5000, a=.002, d=.2, s=.4, r=.2, seed=i), 'music', .5)
        s.at(32 + i, sub(n('A1'), s.beat * .6), 'bass', .6)
    s.at(36, pad(chord('A2 E3 B3 C4 E4'), 3.4, cutoff=2400, a=.02, r=2.5), 'pad', .6)
    s.at(36, sub(n('A1'), 2.4), 'bass', .5)
    for i, nm in enumerate(['E5', 'G5', 'B5', 'C6']):
        s.at(36.5 + i * .5, bell(n(nm), 1.2, ratio=2, index=1, tau=.4), 'lead', .18, pan=-.3 + i * .2)
    s.rt = 1.9


# ------------------------------------------------------------------ r05 future bass 140 half-time, D major

def r05(s, m):
    key(2)
    D = [chord('G3 B3 D4 F#4'), chord('A3 C#4 E4 A4'), chord('F#3 A3 C#4 E4'), chord('B3 D4 F#4 A4')]
    R = [n('G1'), n('A1'), n('F#1'), n('B1')]
    # intro: filtered choir chords
    for bar in range(0, 2):
        s.at(bar * 4, choir([x for x in D[bar % 4]], s.bar), 'pad', .5)
    s.automate('pad', 0, 2 * s.bar, 'low', 600, 4000)
    snare_roll(s, 1, 1, .4)
    s.at(8, crash(), 'drums', .3)
    # drop: half-time
    pat(s, 2, 9, [
        ('x.........x.....', lambda: kick(.5, f0=170, f1=45, tau=.18), 'drums', 1.0, 0, True),
        ('........x.......', lambda: snare(.35, tone=200, snap=1.2), 'drums', .65, 0),
        ('........x.......', clap, 'drums', .35, 0),
        ('x.x.x.x.x.x.xxxx', hat, 'drums', .2, -.2),
    ])
    for bar in range(2, 9):
        c = D[bar % 4]
        # chopped chords with an eighth-note volume wobble
        ch = supersaw([x + 12 for x in c], s.bar, voices=6, detune=.22, cutoff=5200, a=.01, d=.3, s=.9, r=.2, seed=bar)
        t = np.arange(ch.shape[1]) / SR
        wob = .35 + .65 * (np.cos(2 * np.pi * t / (s.beat / 2)) * .5 + .5) ** 1.5
        s.at(bar * 4, ch * wob, 'music', .55)
        s.at(bar * 4, kick808(R[bar % 4] + 12, s.bar * .9, glide=1.3), 'bass', .55)
    # topline
    mel = [(0, 'F#5'), (.5, 'A5'), (1, 'B5'), (2, 'A5'), (2.75, 'F#5'), (3.5, 'E5'), (4, 'D5'), (5, 'E5'), (6, 'F#5'), (7, 'A5')]
    for rep in (3, 5, 7):
        for off, nm in mel:
            s.at(rep * 4 + off, pluck(n(nm) + 12, .25, bright=8000, kind='square', tau=.08), 'lead', .2)
    s.at(20, crash(), 'drums', .2)
    s.at(36, pad(chord('D3 A3 C#4 E4 F#4'), 3.2, cutoff=3000, a=.02, r=2.5), 'pad', .65)
    s.at(36, kick808(n('D2'), 2.5, glide=1.5), 'bass', .5)
    for i, nm in enumerate(['A5', 'C#6', 'E6', 'F#6']):
        s.at(36.5 + i * .5, bell(n(nm), 1.2, ratio=2, index=1, tau=.4), 'lead', .2, pan=-.3 + i * .2)
    s.rt = 2.4
    s.duck['music'] = .6


# ------------------------------------------------------------------ r06 synthwave 100, A minor

def r06(s, m):
    key(-3, True)
    A = [chord('A3 C4 E4'), chord('F3 A3 C4'), chord('C4 E4 G4'), chord('G3 B3 D4')]
    R = [n('A1'), n('F1'), n('C2'), n('G1')]
    # pulse bass from the start, filter opening
    for bar in range(0, 7):
        r = R[bar % 4]
        for i in range(8):
            s.at(bar * 4 + i / 2, bass(r + (12 if i % 2 else 0) + 12, s.beat * .4, kind='saw', cutoff=500, env_amt=1200), 'bass', .5)
        s.at(bar * 4, pad([x for x in A[bar % 4]], s.bar, cutoff=1600, a=.3), 'pad', .45)
    s.automate('bass', 0, 3 * s.bar, 'low', 300, 4000)
    # arp from bar 1.5
    def ar(x):
        return pluck(x, .14, bright=4500, dark=700, tau=.06)
    arp(s, 2, 7, A, [0, 1, 2, 3, 2, 1, 0, 1], ar, rate=.25, gain=.2, octave=12)
    for i in range(8):
        s.at(6 + i * .25, ar(A[1][i % 3] + 12), 'music', .2)
    s.sfx_events([{'t': s.b(8), 'name': 'riser', 'dur': s.bar * .5 + .1, 'gain': .5}], 1.0)
    # drums from bar 3 with gated snare
    def gated():
        L = int(.32 * SR)
        x = np.pad(snare(.3, tone=180), (0, L))
        g = reverb(x, rt=1.2)[:, :L] * np.linspace(1, .8, L)
        return g * .6 + stereo(x[:L], 0)
    pat(s, 3, 7, [
        ('x.......x.......', None, 'drums', .9, 0, True),
        ('....x.......x...', gated, 'drums', .5, 0),
        ('..x...x...x...x.', hat, 'drums', .18, .2),
    ])
    s.at(12, crash(), 'drums', .3)
    lead_ = [(0, 'E5', 1), (1, 'D5', .5), (1.5, 'C5', .5), (2, 'D5', 1.5), (4, 'C5', 1), (5, 'B4', .5), (5.5, 'A4', .5), (6, 'G4', 1.5),
             (8, 'E5', 1), (9, 'G5', 1), (10, 'A5', 2), (12, 'G5', 1), (13, 'E5', 1), (14, 'D5', 2)]
    for off, nm, ln in lead_:
        s.at(12 + off, lead(n(nm), s.beat * ln, kind='saw', cutoff=3200), 'lead', .2)
    s.at(28, pad(chord('A2 E3 A3 B3 C4 E4'), 3.4, cutoff=2400, a=.02, r=2.6), 'pad', .65)
    s.at(28, sub(n('A1'), 2.4), 'bass', .5)
    s.rt = 2.6
    s.dsend['music'] = .25


# ------------------------------------------------------------------ r07 dark cinematic 84, D minor -> F major

def r07(s, m):
    key(2, True)
    # drone
    t = tvec(4 * s.bar)
    drone = np.sin(2 * np.pi * midi(n('D1')) * t) * .6 + lp(noise(4 * s.bar, 30), 300) * .3
    drone *= np.minimum(1, t / 2) * np.minimum(1, (4 * s.bar - t) / .5)
    s.add(drone, 0, 'bass', .5)
    s.at(0, pad(chord('D3 A3 F4'), 2 * s.bar, cutoff=900, a=1.5, r=1), 'pad', .45)
    for i, (off, nm) in enumerate([(0, 'D5'), (1.5, 'A4'), (2, 'F4'), (3, 'E4'), (5, 'D5'), (6.5, 'C5'), (7, 'A4')]):
        s.at(off, piano(n(nm), 1.5, .7), 'music', .35, pan=-.2 + i * .06)
    for q in range(8):
        s.at(q, heartbeat(), 'drums', .45)
    s.at(6.5, reverse_cymbal(s.beat * 1.5), 'fx', .5)
    # statements: toms and strings
    Dm = [chord('D3 F3 A3'), chord('Bb2 D3 F3'), chord('G2 Bb2 D3'), chord('A2 C#3 E3')]
    for i, c in enumerate([Dm[0], Dm[1]]):
        s.at(8 + i * 4, strings(c + [x + 12 for x in c], s.bar, cutoff=2200), 'pad', .45)
    for b in (8, 10.5, 13):
        for k, tt in enumerate((0, .25, .5)):
            s.at(b + tt, tom(n('D2') - k * 2, .6), 'drums', .7 - k * .15)
    # resolve to F major
    F = [chord('F3 A3 C4'), chord('C3 E3 G3'), chord('D3 F3 A3'), chord('Bb2 D3 F3')]
    s.at(16, pad(chord('F3 C4 A4'), s.bar, cutoff=2600, a=.3), 'pad', .55)
    arp(s, 4, 6, F, [0, 1, 2, 1], lambda x: piano(x, .5, .6), rate=.5, gain=.3, octave=12)
    pat(s, 5, 6, [
        ('x.......x.......', lambda: kick(.4, f0=120, f1=48), 'drums', .7, 0, True),
        ('..x...x...x...x.', hat, 'drums', .15, .2),
    ])
    s.at(20, pad(chord('F3 C4 E4 A4'), s.bar, cutoff=3000), 'pad', .5)
    s.at(20, sub(n('F1'), s.bar), 'bass', .45)
    s.at(24, strings(chord('F3 A3 C4 F4 A4'), 3.4, cutoff=3200, a=.1, r=2.5), 'pad', .6)
    s.at(24, sub(n('F1'), 2.6), 'bass', .5)
    for i, nm in enumerate(['C6', 'A5', 'F5', 'C6']):
        s.at(24.5 + i * .5, bell(n(nm), 1.4, ratio=2, index=.9, tau=.5), 'lead', .2, pan=-.3 + i * .2)
    s.rt = 3.2


# ------------------------------------------------------------------ r08 disco house 118, D dorian

def r08(s, m):
    key(2, True)
    C = [chord('D3 F3 A3 C4'), chord('G3 B3 D4 F4')]
    C2 = [chord('Bb3 D4 F4 A4'), chord('C4 E4 G4 Bb4')]
    pat(s, 0, 1, [('x...x...x...x...', None, 'drums', .7, 0, True)])
    s.automate('drums', 0, s.bar, 'low', 600, 8000)
    drums_house(s, 1, 9)
    for b in (4, 12, 20, 28):
        s.at(b, crash(), 'drums', .22)
    slap = [(0, 0), (.5, 12), (.75, 0), (1.5, 0), (2, 12), (2.5, 3), (2.75, 5), (3.5, 12)]
    for bar in range(1, 9):
        prog = C if bar < 5 else C2
        root = prog[bar % 2][0] - 12
        for off, iv in slap:
            s.at(bar * 4 + off, pluck(root - 12 + iv, .18, bright=2500, dark=300, kind='square', tau=.05), 'bass', .55)
        for off in (.5, 1.5, 2.5, 3.5):
            for nn in prog[bar % 2]:
                s.at(bar * 4 + off, epiano(nn + 12, s.beat * .3, .7), 'music', .14)
    for b in (4, 12, 20, 28):
        for i, nm in enumerate(['A4', 'C5', 'D5', 'F5', 'A5']):
            s.at(b - .5 + i * .1, lead(n(nm), .12, kind='saw', cutoff=5000, vib=0), 'lead', .16)
    s.at(36, pad(chord('D3 A3 C4 E4 F4'), 3.4, cutoff=2600, a=.02, r=2.5), 'pad', .6)
    s.at(36, sub(n('D2'), 2.4), 'bass', .5)
    for i, nm in enumerate(['A5', 'C6', 'E6', 'F6']):
        s.at(36.5 + i * .4, bell(n(nm), 1.2, ratio=2, index=1, tau=.4), 'lead', .2, pan=-.3 + i * .2)
    s.rt = 2.0


# ------------------------------------------------------------------ l01 inspiring 104, G major

def l01(s, m):
    key(7)
    G = [chord('G3 B3 D4'), chord('F#3 A3 D4'), chord('E3 G3 B3'), chord('C3 E3 G3')]
    R = [n('G1'), n('F#1'), n('E1'), n('C2')]
    total = 19
    arp(s, 0, total, G, [0, 1, 2, 4, 2, 1, 3, 1], lambda x: piano(x, .45, .6), rate=.5, gain=.32, octave=12, pan_alt=.2)
    for bar in range(0, total):
        s.at(bar * 4, strings([x for x in G[bar % 4]] + [G[bar % 4][0] + 12], s.bar, cutoff=1800 if bar < 5 else 2600, a=.5), 'pad', .32 if bar < 5 else .4)
    # bass and soft beat
    for bar in range(2, total):
        r = R[bar % 4]
        for i in range(8 if bar >= 5 else 2):
            off = i / 2 if bar >= 5 else i * 2
            s.at(bar * 4 + off, bass(r + 12, s.beat * .45, kind='saw', cutoff=380, env_amt=500, sub=.9), 'bass', .45)
    pat(s, 2, 4, [('x.......x.......', None, 'drums', .6, 0, True), ('x.x.x.x.x.x.x.x.', shaker, 'drums', .18, .2)])
    s.at(16, crash(), 'drums', .35)
    pat(s, 5, 17, [
        ('x...x...x...x...', None, 'drums', .75, 0, True),
        ('....x.......x...', clap, 'drums', .42, 0),
        ('..x...x...x...x.', hat, 'drums', .2, -.2),
        ('x.x.x.x.x.x.x.x.', shaker, 'drums', .14, .25),
    ])
    pat(s, 17, 19, [
        ('x...x...x...x...', None, 'drums', .85, 0, True),
        ('....x.......x...', clap, 'drums', .5, 0),
        ('..x...x...x...x.', lambda: hat(open_=True), 'drums', .2, -.2),
    ])
    for b in (20, 36, 52, 68):
        s.at(b, crash(), 'drums', .2)
    mel = [(0, 'D5'), (1, 'B4'), (1.5, 'D5'), (2, 'E5'), (3, 'D5'), (4, 'A4'), (6, 'D5'), (7, 'F#5'), (8, 'G5'), (9.5, 'E5'), (10, 'D5'), (12, 'C5'), (13, 'B4'), (14, 'A4'), (15, 'B4')]
    for rep in (5, 9, 13):
        for off, nm in mel:
            s.at(rep * 4 + off, bell(n(nm), 1.0, ratio=2, index=1.0, tau=.4), 'lead', .2)
    for off, nm in mel:
        s.at(17 * 4 + off * .5, pluck(n(nm) + 12, .2, bright=6000), 'lead', .16)
    s.at(76, pad(chord('G2 D3 G3 B3 D4 A4'), 4.6, cutoff=2600, a=.02, r=3), 'pad', .65)
    s.at(76, sub(n('G1'), 3.5), 'bass', .5)
    s.at(76, crash(), 'drums', .3)
    for i, nm in enumerate(['D6', 'B5', 'G5', 'A5', 'D6']):
        s.at(76.5 + i * .5, bell(n(nm), 1.6, ratio=2, index=.9, tau=.6), 'lead', .2, pan=-.4 + i * .2)
    s.rt = 2.4


# ------------------------------------------------------------------ l02 ambient electronica 92, E minor

def l02(s, m):
    key(4, True)
    E = [chord('E3 G3 B3 D4 F#4'), chord('C3 E3 G3 B3'), chord('G3 B3 D4'), chord('F#3 A3 D4')]
    R = [n('E1'), n('C2'), n('G1'), n('D2')]
    total = 14
    for bar in range(0, total):
        s.at(bar * 4, pad(E[bar % 4], s.bar, cutoff=1500 + (bar > 7) * 1200, a=.6, r=1.2, voices=4), 'pad', .42)
    arp(s, 0, total, E, [0, 2, 4, 1, 3, 2, 4, 3], lambda x: bell(x, .5, ratio=3.0, index=.8, tau=.18), rate=.25, gain=.17, octave=12)
    s.dsend['music'] = .35
    pat(s, 2, 10, [
        ('x.........x.....', lambda: kick(.45, f0=130, f1=48, tau=.14, click=.3), 'drums', .8, 0, True),
        ('....x.......x...', rim, 'drums', .32, .25),
        ('..x...x.x.x...xx', lambda: hp(hat(), 9000), 'drums', .2, -.25),
    ])
    for bar in range(2, total):
        s.at(bar * 4, sub(R[bar % 4] + 12, s.bar * .9), 'bass', .5)
    for b in (8, 16, 24, 32, 40, 48):
        s.at(b + 3.5, glitch(.2, seed=b), 'fx', .35, pan=.4)
    for bar in range(6, 8):
        s.at(bar * 4, choir(E[bar % 4][:3], s.bar), 'pad', .25)
    for bar in range(8, 10):
        for off in (0, 1.5, 3):
            for nn in E[bar % 4]:
                s.at(bar * 4 + off, pluck(nn + 12, .2, bright=3500), 'music', .1)
    pat(s, 10, 12, [('x...............', None, 'drums', .8, 0, True)])
    pat(s, 12, 14, [
        ('x.........x.....', None, 'drums', .85, 0, True),
        ('....x.......x...', clap, 'drums', .4, 0),
        ('..x...x...x...x.', hat, 'drums', .2, -.2),
    ])
    s.at(56, pad(chord('E3 B3 D4 F#4 G4'), 4.6, cutoff=2400, a=.02, r=3), 'pad', .6)
    s.at(56, sub(n('E2'), 3.5), 'bass', .5)
    for i, nm in enumerate(['B5', 'D6', 'F#6', 'G6']):
        s.at(56.5 + i * .5, bell(n(nm), 1.6, ratio=2, index=.9, tau=.6), 'lead', .2, pan=-.3 + i * .2)
    s.rt = 3.0


# ------------------------------------------------------------------ dispatch

TRACKS = {
    'r01-tanitim': r01, 'r02-bildirimler': r02, 'r03-aramalar-mesajlar': r03, 'r04-pano': r04,
    'r05-dosyalar': r05, 'r06-kamera': r06, 'r07-gizlilik': r07, 'r08-super-gucler': r08,
    'l01-tanitim': l01, 'l02-nasil-calisir': l02,
}
TRACK_SFX_GAIN = {'r03-aramalar-mesajlar': .6, 'l01-tanitim': .55, 'l02-nasil-calisir': .55}


def main():
    meta_path, out = sys.argv[1], sys.argv[2]
    m = json.load(open(meta_path, encoding='utf-8'))
    s = Song(m['bpm'], m['duration'])
    TRACKS[m['id']](s, m)
    s.sfx_events(m.get('events', []), gain=TRACK_SFX_GAIN.get(m['id'], .7))
    x = s.render(fade_out=1.4)
    write_wav(out, x)
    print('wrote', out, f'{x.shape[1] / SR:.2f}s')


if __name__ == '__main__':
    main()
