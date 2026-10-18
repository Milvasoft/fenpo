// Deterministic timeline engine. Every frame is a pure function of time, so the renderer can
// seek to any t and screenshot it. Opened without ?render the page plays itself in real time.

(() => {
  const params = new URLSearchParams(location.search);
  const LANG = params.get('lang') || 'tr';
  const RENDER = params.has('render');
  window.LANG = LANG;
  window.RENDER = RENDER;

  // ---------- easing ----------
  const c1 = 1.70158;
  const E = {
    linear: t => t,
    in: t => t * t * t,
    out: t => 1 - Math.pow(1 - t, 3),
    inOut: t => (t < .5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2),
    inQuint: t => t * t * t * t * t,
    outQuint: t => 1 - Math.pow(1 - t, 5),
    inOutQuint: t => (t < .5 ? 16 * Math.pow(t, 5) : 1 - Math.pow(-2 * t + 2, 5) / 2),
    inExpo: t => (t === 0 ? 0 : Math.pow(2, 10 * t - 10)),
    outExpo: t => (t === 1 ? 1 : 1 - Math.pow(2, -10 * t)),
    inOutExpo: t => (t === 0 ? 0 : t === 1 ? 1 : t < .5 ? Math.pow(2, 20 * t - 10) / 2 : (2 - Math.pow(2, -20 * t + 10)) / 2),
    outBack: t => 1 + (c1 + 1) * Math.pow(t - 1, 3) + c1 * Math.pow(t - 1, 2),
    outBackBig: t => { const k = 3.2; return 1 + (k + 1) * Math.pow(t - 1, 3) + k * Math.pow(t - 1, 2); },
    inBack: t => (c1 + 1) * t * t * t - c1 * t * t,
    outElastic: t => (t === 0 ? 0 : t === 1 ? 1 : Math.pow(2, -10 * t) * Math.sin((t * 10 - .75) * (2 * Math.PI / 3)) + 1),
    spring: t => 1 - Math.exp(-7 * t) * Math.cos(11 * t),
    step: t => (t < 1 ? 0 : 1),
  };
  window.E = E;

  const DEF = { x: 0, y: 0, z: 0, s: 1, sx: 1, sy: 1, r: 0, rx: 0, ry: 0, o: 1, blur: 0, br: 1 };
  const TRANSFORM_KEYS = ['x', 'y', 'z', 's', 'sx', 'sy', 'r', 'rx', 'ry'];

  const $ = (q, root = document) => (typeof q === 'string' ? root.querySelector(q) : q);
  const $$ = (q, root = document) => (typeof q === 'string' ? [...root.querySelectorAll(q)] : Array.isArray(q) || q instanceof NodeList ? [...q].flatMap(x => (typeof x === 'string' ? $$(x, root) : [x])) : [q]);
  window.$ = $;
  window.$$ = $$;

  const clamp01 = v => (v < 0 ? 0 : v > 1 ? 1 : v);
  const lerp = (a, b, t) => a + (b - a) * t;
  window.clamp01 = clamp01;
  window.lerp = lerp;

  // Deterministic pseudo random.
  window.rand = seed => {
    let s = seed >>> 0 || 1;
    return () => {
      s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0;
      return s / 4294967296;
    };
  };

  class Timeline {
    constructor() {
      this.tracks = new Map(); // element -> { prop -> [tweens] }
      this.fns = [];
      this.events = [];
      this.sets = [];
      this.prepared = false;
    }

    // Tween an element (or elements) from -> to over [start, start+dur].
    tw(target, start, dur, from, to, ease = 'outQuint') {
      const els = $$(target);
      const fn = typeof ease === 'function' ? ease : E[ease];
      if (!fn) throw new Error('unknown ease ' + ease);
      for (const el of els) {
        if (!el) throw new Error('missing element ' + target);
        let tr = this.tracks.get(el);
        if (!tr) { tr = {}; this.tracks.set(el, tr); }
        const keys = new Set([...Object.keys(from || {}), ...Object.keys(to || {})]);
        for (const k of keys) {
          (tr[k] ||= []).push({ start, dur: Math.max(dur, 1e-6), from: from ? from[k] : undefined, to: to ? to[k] : undefined, ease: fn });
        }
      }
      this.prepared = false;
      return this;
    }

    // Shorthands.
    in(target, start, dur, from, ease = 'outQuint') {
      const to = {};
      for (const k of Object.keys(from)) to[k] = k.startsWith('--') ? 1 : DEF[k];
      return this.tw(target, start, dur, from, to, ease);
    }
    out(target, start, dur, to, ease = 'inOut') {
      return this.tw(target, start, dur, null, to, ease);
    }
    set(target, t, props) { return this.tw(target, t, 1e-6, props, props, 'linear'); }

    stagger(targets, start, each, dur, from, to, ease = 'outQuint') {
      $$(targets).forEach((el, i) => this.tw(el, start + i * each, dur, from, to, ease));
      return this;
    }
    staggerIn(targets, start, each, dur, from, ease = 'outQuint') {
      $$(targets).forEach((el, i) => this.in(el, start + i * each, dur, from, ease));
      return this;
    }

    fn(f) { this.fns.push(f); return this; }
    sfx(t, name, gain = 1, extra = {}) { this.events.push({ t: +t.toFixed(4), name, gain, ...extra }); return this; }

    prepare() {
      for (const tr of this.tracks.values()) {
        for (const k of Object.keys(tr)) {
          const list = tr[k].sort((a, b) => a.start - b.start);
          let prev = k in DEF ? DEF[k] : 0;
          for (const tw of list) {
            if (tw.from === undefined) tw.from = prev;
            if (tw.to === undefined) tw.to = tw.from;
            prev = tw.to;
          }
        }
      }
      this.prepared = true;
    }

    valueAt(list, t) {
      let cur = null;
      for (const tw of list) { if (tw.start <= t) cur = tw; else break; }
      if (!cur) return list[0].from;
      const p = clamp01((t - cur.start) / cur.dur);
      const a = cur.from, b = cur.to;
      if (typeof a === 'string' || typeof b === 'string') return p < 1 ? a : b;
      return lerp(a, b, cur.ease(p));
    }

    seek(t) {
      if (!this.prepared) this.prepare();
      for (const [el, tr] of this.tracks) {
        const v = {};
        for (const k of Object.keys(tr)) v[k] = this.valueAt(tr[k], t);
        let hasT = false;
        for (const k of TRANSFORM_KEYS) if (k in v) { hasT = true; break; }
        if (hasT) {
          const g = k => (k in v ? v[k] : DEF[k]);
          const pre = el.classList.contains('c') ? 'translate(-50%,-50%) ' : '';
          el.style.transform = `${pre}translate3d(${g('x').toFixed(2)}px,${g('y').toFixed(2)}px,${g('z').toFixed(2)}px) rotateX(${g('rx').toFixed(3)}deg) rotateY(${g('ry').toFixed(3)}deg) rotate(${g('r').toFixed(3)}deg) scale(${(g('s') * g('sx')).toFixed(4)},${(g('s') * g('sy')).toFixed(4)})`;
        }
        if ('o' in v) {
          const o = clamp01(v.o);
          el.style.opacity = o.toFixed(4);
          el.style.visibility = o <= 0.001 ? 'hidden' : '';
        }
        if ('blur' in v || 'br' in v) {
          const parts = [];
          if ('blur' in v && v.blur > 0.05) parts.push(`blur(${v.blur.toFixed(2)}px)`);
          if ('br' in v && Math.abs(v.br - 1) > 0.001) parts.push(`brightness(${v.br.toFixed(3)})`);
          el.style.filter = parts.join(' ');
        }
        for (const k of Object.keys(v)) {
          if (k.startsWith('--')) el.style.setProperty(k, typeof v[k] === 'number' ? v[k].toFixed(4) : v[k]);
          else if (k === 'text') el.textContent = v[k];
        }
      }
      for (const f of this.fns) f(t);
    }
  }
  window.Timeline = Timeline;

  // ---------- text helpers ----------
  window.STR = window.STR || {};
  window.S = key => {
    const d = window.STR[LANG] || {};
    if (!(key in d)) { console.warn('missing string', LANG, key); return key; }
    return d[key];
  };
  window.applyStrings = (root = document) => {
    for (const el of root.querySelectorAll('[data-s]')) el.innerHTML = S(el.dataset.s);
  };

  // Wrap words in masked spans. Returns the inner spans in reading order.
  window.splitWords = (target, masked = true) => {
    const el = $(target);
    const out = [];
    const walk = node => {
      for (const child of [...node.childNodes]) {
        if (child.nodeType === 3) {
          const frag = document.createDocumentFragment();
          for (const part of child.textContent.split(/(\s+)/)) {
            if (!part) continue;
            if (/^\s+$/.test(part)) { frag.appendChild(document.createTextNode(' ')); continue; }
            const w = document.createElement('span');
            w.className = masked ? 'wm' : 'wu';
            const i = document.createElement('span');
            i.className = 'wi';
            i.textContent = part;
            w.appendChild(i);
            frag.appendChild(w);
            out.push(i);
          }
          child.replaceWith(frag);
        } else if (child.nodeType === 1 && child.tagName !== 'BR') {
          const n0 = out.length;
          walk(child);
          // Gradient text cannot clip through inline-block children, so move the class down.
          if (child.classList.contains('grad-text')) {
            child.classList.remove('grad-text');
            for (const w of out.slice(n0)) w.classList.add('grad-text');
          }
        }
      }
    };
    walk(el);
    return out;
  };

  window.splitChars = target => {
    const el = $(target);
    const text = el.textContent;
    el.textContent = '';
    const out = [];
    for (const ch of text) {
      const s = document.createElement('span');
      s.className = 'ch';
      s.textContent = ch === ' ' ? ' ' : ch;
      el.appendChild(s);
      out.push(s);
    }
    return out;
  };

  // Typewriter: reveals text between start and start + text.length / cps.
  window.typeText = (tl, target, start, cps, text, caret = true) => {
    const el = $(target);
    const chars = [...text];
    tl.fn(t => {
      const n = Math.max(0, Math.min(chars.length, Math.floor((t - start) * cps)));
      const blink = caret && t >= start - .6 && (n < chars.length || Math.floor(t * 2.2) % 2 === 0) && t < start + chars.length / cps + 1.2;
      el.innerHTML = chars.slice(0, n).join('').replace(/&/g, '&amp;').replace(/</g, '&lt;') + (blink ? '<span class="caret"></span>' : '');
    });
    return start + chars.length / cps;
  };

  window.counter = (tl, target, start, dur, a, b, fmt = v => Math.round(v), ease = E.outQuint) => {
    const el = $(target);
    tl.fn(t => { el.textContent = fmt(lerp(a, b, ease(clamp01((t - start) / dur)))); });
  };

  // ---------- scene bootstrapping ----------
  window.TL = new Timeline();

  window.boot = async () => {
    await document.fonts.ready;
    await Promise.all([...document.images].map(img => (img.complete ? 0 : new Promise(r => { img.onload = img.onerror = r; }))));
    const meta = window.META;
    window.seek = t => TL.seek(t);
    window.getEvents = () => TL.events.slice().sort((a, b) => a.t - b.t);
    window.READY = true;
    if (!RENDER) {
      const t0 = performance.now() - (parseFloat(params.get('t') || '0') * 1000);
      const loop = () => {
        const t = ((performance.now() - t0) / 1000) % meta.duration;
        TL.seek(t);
        requestAnimationFrame(loop);
      };
      if (params.has('t') && params.has('still')) TL.seek(parseFloat(params.get('t')));
      else loop();
    } else {
      TL.seek(0);
    }
  };
})();
