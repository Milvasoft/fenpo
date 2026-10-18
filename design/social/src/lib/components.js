// Building blocks shared by every video and post: background, logo, phone, cursor, end card.

const SEED_PATH = 'M32 15.5c3.4 4.2 3.7 9 0 11.6c-3.7-2.6-3.4-7.4 0-11.6z';

Object.assign(window.STR, {
  tr: Object.assign(window.STR.tr || {}, {
    'c.tagline': 'Telefonun ve Windows<br>aynı masada.',
    'c.platforms': 'Windows · iPhone · Android',
    'c.open': 'Açık kaynak',
    'c.nocloud': 'Bulut yok',
    'c.noaccount': 'Hesap yok',
    'c.url': 'github.com/Milvasoft/fenpo',
    'c.connected': 'Bağlı',
    'c.phoneName': 'Ayşe’nin iPhone’u',
    'c.date': '18 Ekim Cumartesi',
  }),
  en: Object.assign(window.STR.en || {}, {
    'c.tagline': 'Your phone and Windows,<br>at the same desk.',
    'c.platforms': 'Windows · iPhone · Android',
    'c.open': 'Open source',
    'c.nocloud': 'No cloud',
    'c.noaccount': 'No account',
    'c.url': 'github.com/Milvasoft/fenpo',
    'c.connected': 'Connected',
    'c.phoneName': 'Ayşe’s iPhone',
    'c.date': 'Saturday, October 18',
  }),
});

window.setupStage = (w, h) => {
  document.documentElement.style.setProperty('--W', w + 'px');
  document.documentElement.style.setProperty('--H', h + 'px');
  const sprite = document.createElement('div');
  sprite.innerHTML = window.ICONS;
  document.body.prepend(sprite.firstElementChild);
};

window.icon = (id, cls = '') => `<svg class="i ${cls}"><use href="#${id}"/></svg>`;

window.shotUrl = (name, theme = 'Dark', lang = window.LANG) => `../../../screenshots/windows/${lang}/${name}-${theme}.png`;

// ---------- background ----------
let grainUrl = null;
const makeGrain = () => {
  if (grainUrl) return grainUrl;
  const c = document.createElement('canvas');
  c.width = c.height = 256;
  const g = c.getContext('2d');
  const img = g.createImageData(256, 256);
  const r = rand(7);
  for (let i = 0; i < img.data.length; i += 4) {
    const v = Math.floor(r() * 255);
    img.data[i] = img.data[i + 1] = img.data[i + 2] = v;
    img.data[i + 3] = 255;
  }
  g.putImageData(img, 0, 0);
  grainUrl = c.toDataURL();
  return grainUrl;
};

// Slowly drifting colour blobs plus film grain. Returns the layer element.
window.background = (parent, opts = {}) => {
  const blobs = opts.blobs || [
    { col: '#E8577E', d: 1300, x: .15, y: .2, a: .5, sp: .11 },
    { col: '#4E54C8', d: 1500, x: .85, y: .75, a: .55, sp: .08 },
    { col: '#2F8F6B', d: 900, x: .8, y: .1, a: .22, sp: .13 },
    { col: '#6B3E86', d: 1200, x: .2, y: .9, a: .4, sp: .09 },
  ];
  const layer = document.createElement('div');
  layer.className = 'layer bg';
  layer.style.background = opts.base || 'radial-gradient(140% 100% at 50% 0%, #1E2247 0%, #12152B 60%, #0B0D1E 100%)';
  const aur = document.createElement('div');
  aur.className = 'aurora';
  layer.appendChild(aur);
  const els = blobs.map(b => {
    const e = document.createElement('div');
    e.className = 'blob';
    e.style.setProperty('--col', b.col);
    e.style.setProperty('--d', b.d + 'px');
    e.style.setProperty('--a', b.a);
    aur.appendChild(e);
    return { e, b };
  });
  if (opts.grid) { const gl = document.createElement('div'); gl.className = 'gridlines'; layer.appendChild(gl); }
  const vig = document.createElement('div');
  vig.className = 'vignette';
  layer.appendChild(vig);
  const grain = document.createElement('div');
  grain.className = 'grain';
  grain.style.backgroundImage = `url(${makeGrain()})`;
  layer.appendChild(grain);
  parent.prepend(layer);
  const W = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--W'));
  const H = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--H'));
  TL.fn(t => {
    for (const [i, { e, b }] of els.entries()) {
      const px = (b.x + Math.sin(t * b.sp * 2 + i * 1.7) * .12) * W * 1.4;
      const py = (b.y + Math.cos(t * b.sp * 1.6 + i * 2.3) * .1) * H * 1.4;
      e.style.setProperty('--px', px + 'px');
      e.style.setProperty('--py', py + 'px');
    }
    const f = Math.floor(t * 24);
    const rr = rand(f * 7919 + 13);
    grain.style.transform = `translate(${Math.floor(rr() * 256)}px, ${Math.floor(rr() * 256)}px)`;
  });
  return layer;
};

// ---------- logo ----------
window.logoHTML = size => {
  let seeds = '';
  for (let k = 0; k < 5; k++) {
    seeds += `<div class="lg-seed"><svg viewBox="0 0 64 64"><path fill="#1B1F3B" transform="rotate(${k * 72} 32 32)" d="${SEED_PATH}"/></svg></div>`;
  }
  return `<div class="logo" style="--size:${size}px"><div class="lg-sq"></div><div class="lg-ci"></div>${seeds}</div>`;
};

window.logoSVG = (size = 64) => `<svg width="${size}" height="${size}" viewBox="0 0 64 64"><rect x="3" y="3" width="58" height="58" rx="19" fill="#E8577E"/><circle cx="32" cy="32" r="21" fill="#FFF6F0"/><g fill="#1B1F3B">${[0, 72, 144, 216, 288].map(a => `<path transform="rotate(${a} 32 32)" d="${SEED_PATH}"/>`).join('')}</g></svg>`;

// Builds the logo: squircle pops, cream circle grows, seeds spin into place one by one.
window.animateLogo = (tl, root, t, opts = {}) => {
  const sq = $('.lg-sq', root), ci = $('.lg-ci', root), seeds = $$('.lg-seed', root);
  const sp = opts.speed || 1;
  tl.in(sq, t, .7 * sp, { s: 0, r: -25 }, 'outBackBig');
  tl.in(ci, t + .12 * sp, .6 * sp, { s: 0 }, 'outBack');
  seeds.forEach((s, i) => {
    tl.in(s, t + (.3 + i * .07) * sp, .75 * sp, { s: .2, r: -160, o: 0 }, 'outBack');
  });
  if (opts.sfx !== false) {
    tl.sfx(t, 'pop', .8);
    seeds.forEach((_, i) => tl.sfx(t + (.3 + i * .07) * sp, 'tick', .35, { pitch: i }));
  }
  return t + (.3 + 4 * .07 + .75) * sp;
};

// ---------- phone ----------
window.phoneHTML = (w, inner, opts = {}) => `
  <div class="phone ${opts.cls || ''}" style="--pw:${w}px">
    <div class="scr">
      ${inner}
      <div class="sb"><span>${opts.time || '09:41'}</span><span class="r">${icon('i-wifi')}<span class="bat"><i></i></span></span></div>
      <div class="pill"></div>
    </div>
  </div>`;

window.lockHTML = (time = '09:41') => `<div class="lock"><div class="date">${S('c.date')}</div><div class="time">${time}</div></div>`;

window.pnHTML = (app, color, title, body, tm) => `
  <div class="pn"><div class="ai" style="background:${color}">${app}</div><div><b>${title}</b><span>${body}</span></div><div class="tm">${tm}</div></div>`;

// ---------- cursor ----------
window.cursorHTML = () => `<svg class="cursor" viewBox="0 0 32 32"><path d="M6 3.5v22.2l5.6-5.3 3.6 8.3 3.7-1.6-3.6-8.2h7.8z" fill="#fff" stroke="#111" stroke-width="1.6" stroke-linejoin="round"/></svg>`;

// Moves the cursor along a list of keyframes [{t, x, y}] with smooth easing.
window.moveCursor = (tl, el, keys, ease = 'inOut') => {
  for (let i = 0; i < keys.length; i++) {
    const k = keys[i], p = keys[i - 1];
    if (!p) tl.set(el, k.t, { x: k.x, y: k.y });
    else tl.tw(el, p.t + (p.hold || 0), k.t - p.t - (p.hold || 0), { x: p.x, y: p.y }, { x: k.x, y: k.y }, ease);
  }
};

window.clickAt = (tl, parent, t, x, y, sfx = true) => {
  const r = document.createElement('div');
  r.className = 'click-ripple c';
  r.style.left = x + 'px';
  r.style.top = y + 'px';
  parent.appendChild(r);
  tl.set(r, 0, { o: 0 });
  tl.tw(r, t, .5, { s: .2, o: 1 }, { s: 1.2, o: 0 }, 'out');
  if (sfx) tl.sfx(t, 'click', .8);
};

// ---------- transitions ----------
window.flash = (tl, parent, t, dur = .35, peak = .85) => {
  const f = document.createElement('div');
  f.className = 'flash';
  f.style.zIndex = 90;
  parent.appendChild(f);
  tl.set(f, 0, { o: 0 });
  tl.tw(f, t, dur, { o: peak }, { o: 0 }, 'out');
};

// A coloured panel sweeps across the frame; the cut happens under it at t + dur/2.
window.wipe = (tl, parent, t, color = '#E8577E', dur = .6, dir = 'up') => {
  const w = document.createElement('div');
  w.className = 'wipe';
  w.style.background = color;
  w.style.zIndex = 80;
  parent.appendChild(w);
  const H = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--H'));
  const W = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--W'));
  const k = dir === 'up' ? 'y' : 'x';
  const D = dir === 'up' ? H : W;
  tl.set(w, 0, { [k]: D });
  tl.tw(w, t, dur / 2, { [k]: D }, { [k]: 0 }, 'inQuint');
  tl.tw(w, t + dur / 2, dur / 2, { [k]: 0 }, { [k]: -D }, 'outQuint');
  tl.sfx(t, 'whoosh', .7);
  return t + dur / 2;
};

// ---------- end card ----------
window.endCard = (tl, parent, t, opts = {}) => {
  const vertical = opts.vertical !== false;
  const el = document.createElement('div');
  el.className = 'layer center endcard';
  el.style.zIndex = 60;
  const size = vertical ? 300 : 230;
  el.innerHTML = `
    <div class="ec-logo">${logoHTML(size)}</div>
    <div class="wordmark ec-word" style="font-size:${vertical ? 190 : 150}px;margin-top:${vertical ? 50 : 34}px">Fenpo</div>
    <div class="h-s ec-tag" style="margin-top:${vertical ? 44 : 30}px;font-weight:700;font-size:${vertical ? 58 : 50}px;color:var(--glass)">${S(opts.tagline || 'c.tagline')}</div>
    <div class="ec-chips" style="display:flex;gap:16px;margin-top:${vertical ? 56 : 40}px;flex-wrap:wrap;justify-content:center;max-width:${vertical ? 900 : 1400}px">
      <span class="chip"><span class="dot"></span>${S('c.platforms')}</span>
      <span class="chip">${S('c.open')}</span>
      <span class="chip">${S('c.nocloud')}</span>
    </div>
    <div class="end-url ec-url" style="margin-top:${vertical ? 54 : 40}px">${icon('i-link')}${S('c.url')}</div>`;
  parent.appendChild(el);
  if (opts.bg) {
    el.style.background = opts.bg;
  }
  tl.set(el, 0, { o: 0 });
  tl.set(el, t, { o: 1 });
  animateLogo(tl, el, t + .05);
  const letters = splitChars($('.ec-word', el));
  tl.staggerIn(letters, t + .45, .045, .7, { y: 120, o: 0, r: 8 }, 'outBack');
  const tagWords = splitWords($('.ec-tag', el));
  tl.staggerIn(tagWords, t + .85, .05, .7, { y: 90 }, 'outQuint');
  tl.staggerIn($$('.chip', el), t + 1.25, .1, .6, { y: 40, o: 0, s: .9 }, 'outBack');
  tl.in($('.ec-url', el), t + 1.6, .6, { y: 30, o: 0 });
  // Gentle float once built.
  tl.tw($('.ec-logo', el), t + 1.2, 6, { y: 0 }, { y: -14 }, 'inOut');
  return el;
};
