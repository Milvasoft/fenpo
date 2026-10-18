// Renders an animated HTML page frame by frame and pipes the frames into ffmpeg.
//   node render.mjs video <page.html> <lang> <out.mp4>      -> silent video + <out>.json (meta, sound cues)
//   node render.mjs still <page.html> <lang> <out.png> [t]  -> one frame as PNG (posts, previews)
// Needs PLAYWRIGHT_DIR pointing at a folder with the playwright package installed.

import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const require = createRequire(path.join(process.env.PLAYWRIGHT_DIR, 'package.json'));
const { chromium } = require('playwright');

const [mode, page, lang, out, extra] = process.argv.slice(2);

const browser = await chromium.launch({ args: ['--force-color-profile=srgb', '--disable-lcd-text', '--font-render-hinting=none', '--hide-scrollbars'] });

const open = async (w, h, scale = 1) => {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: scale });
  const p = await ctx.newPage();
  p.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') console.error('[page]', m.text()); });
  p.on('pageerror', e => console.error('[pageerror]', e.message));
  return p;
};

const [pagePath, pageQuery] = page.split('?');
const url = q => pathToFileURL(path.resolve(pagePath)).href + '?render&lang=' + lang + (pageQuery ? '&' + pageQuery : '') + q;

// Reads META from the page with a throwaway viewport, then reopens at the right size.
const probe = await open(400, 400);
await probe.goto(url(''));
await probe.waitForFunction(() => window.META);
const meta = await probe.evaluate(() => window.META);
await probe.close();

const p = await open(meta.w, meta.h, meta.scale || 1);
await p.goto(url(''));
await p.waitForFunction(() => window.READY === true, null, { timeout: 60000 });

if (mode === 'still') {
  const t = parseFloat(extra ?? meta.still ?? meta.duration - 0.01);
  await p.evaluate(t => window.seek(t), t);
  await p.waitForTimeout(50);
  await p.screenshot({ path: out, type: 'png' });
  console.log('still', out, t);
  await browser.close();
  process.exit(0);
}

const fps = meta.fps || 30;
const frames = Math.round(meta.duration * fps);
const cdp = await p.context().newCDPSession(p);

const ff = spawn('ffmpeg', ['-y', '-hide_banner', '-loglevel', 'error',
  '-f', 'image2pipe', '-framerate', String(fps), '-c:v', 'mjpeg', '-i', '-',
  '-c:v', 'h264_nvenc', '-preset', 'p7', '-tune', 'hq', '-rc', 'vbr', '-cq', '16', '-b:v', '0', '-profile:v', 'high',
  '-pix_fmt', 'yuv420p', '-r', String(fps), out], { stdio: ['pipe', 'inherit', 'inherit'] });

const t0 = Date.now();
for (let i = 0; i < frames; i++) {
  await p.evaluate(t => window.seek(t), i / fps);
  const { data } = await cdp.send('Page.captureScreenshot', { format: 'jpeg', quality: 96, optimizeForSpeed: false, captureBeyondViewport: false });
  const buf = Buffer.from(data, 'base64');
  if (!ff.stdin.write(buf)) await new Promise(r => ff.stdin.once('drain', r));
  if (i % 60 === 0) process.stdout.write(`\r${path.basename(out)} ${i}/${frames} ${((Date.now() - t0) / 1000).toFixed(0)}s   `);
}
ff.stdin.end();
await new Promise(r => ff.on('close', r));

const events = await p.evaluate(() => window.getEvents());
fs.writeFileSync(out.replace(/\.mp4$/, '.json'), JSON.stringify({ ...meta, lang, frames, events }, null, 2));
console.log(`\r${path.basename(out)} done, ${frames} frames in ${((Date.now() - t0) / 1000).toFixed(0)}s`);
await browser.close();
