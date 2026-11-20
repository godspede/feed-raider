// Frame-time benchmark of the web build in headless Chromium, at a phone viewport with the CPU throttled.
//   npm run bench                      (from web/; publishes src/FeedRaider.Web first when no --site is given)
//   node bench/frame-bench.mjs [--boxes on|off] [--boss 1..10] [--desktop] [--still] [--chrome <browser exe>] [--site <published wwwroot>] [--seconds 30] [--rates 1,4,6] [--stage 8] [--adaptive] [--json out.json] [--shot out.png]
// Serves the published wwwroot, answers the Bluesky feed calls with canned posts (no network), opens
// ?handle=bench&feed=random&stage=N&fps=1 (the game starts at once on a busy stage), applies Emulation.setCPUThrottlingRate and
// reports per rate: p50/p95/max of requestAnimationFrame gaps, dropped frames (gaps over 25 ms), frames the game handed to xterm
// per second and their size, xterm's parse time per write, long tasks, and the main thread's busy time per game tick (script +
// layout + paint, from CDP Performance.getMetrics: the engine's WebAssembly tick, the interop call and xterm's parse all run there).
// One browser is launched for the whole run. PLAYWRIGHT_CORE names playwright-core's directory when it is not importable here.
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { writeFileSync } from 'node:fs';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';

const TICKS_PER_SECOND = 30;     // the game's simulation rate
const DROPPED_FRAME_MS = 25;     // a requestAnimationFrame gap longer than this counts as a dropped frame
const SETTLE_MS = 3000;          // the opening scene and then the throttled loop each get this long to reach a steady frame rate before measuring
const MIN_FRAME_CHARS = 20;      // a write shorter than this is not a frame (cursor moves, title updates)
const LOAD_TIMEOUT_MS = 120000;  // how long the game may take to start playing
const SAMPLE_MS = 500;           // the pause between ship moves while measuring
const MS_PER_SECOND = 1000;
const US_PER_MS = 1000;          // trace spans are timed in microseconds
const SLOW_SPAN_US = 80000;      // a trace span longer than this (in microseconds) is listed as slow
// The mouse walks a sawtooth over the lower field so the ship keeps moving: x from MOVE_X_LEFT across MOVE_X_SPAN px, y up
// from MOVE_Y_BOTTOM across MOVE_Y_SPAN px, inside the phone field; the steps do not divide the spans, so the path does not repeat soon.
const MOVE_X_LEFT = 60, MOVE_X_STEP = 37, MOVE_X_SPAN = 270;
const MOVE_Y_BOTTOM = 600, MOVE_Y_STEP = 13, MOVE_Y_SPAN = 200;
const PROFILE_ROWS_SHOWN = 14;   // --profile lists this many functions by self time
const SLOW_SPANS_LISTED = 40;    // --trace lists at most this many slow spans
const SPAN_TOTALS_LISTED = 16;   // --trace lists this many span names by total time per tick
const CONSOLE_CHARS_SHOWN = 200; // --debug cuts a page console line to this length
const PHONE_VIEWPORT = { width: 390, height: 780 }, PHONE_PIXEL_RATIO = 2;
const DESKTOP_VIEWPORT = { width: 1280, height: 720 };
const FEED_POSTS = 30;           // the canned feed's length
const POST_MAX_CHARS = 300;      // the most text a Bluesky post may hold
const WORD_SHIFT_PER_POST = 7;   // each post starts this many words further into the word list, so neighbours differ
const PICTURE_POST_EVERY = 3;    // every third post carries pictures
const MAX_PICTURES = 4;          // a post holds at most four pictures

const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i < 0 ? d : (process.argv[i + 1] ?? true); };
const seconds = Number(arg('seconds', 30));
const rates = String(arg('rates', '1,4,6')).split(',').map(Number);
const stage = Number(arg('stage', 8));
const bossArg = String(arg('boss', '0'));       // 1..10: open on that boss's warning (?boss=N); a list (1,2,3) runs each in the one browser; 0 is the ?stage= scene (the baseline)
const fieldsArg = process.argv.includes('--desktop') ? 'desktop' : String(arg('fields', 'phone'));   // phone (390x780), desktop (1280x720), or a list (phone,desktop) run in the one browser
const still = process.argv.includes('--still');      // the ship stands (the mouse stays put) and the weapon fires
const profile = process.argv.includes('--profile');   // also print the top self-time functions (the WebAssembly interpreter shows as one or two frames)
const tracing = process.argv.includes('--trace');
const extraQuery = String(arg('query', ''));   // e.g. --query &renderer=dom
// With --boxes on, the stage scene has enemy posts coming (darkness on), so speech boxes are up and the pass that lays units over them runs.
// With --boxes off, it is the same scene with darkness off, so no box is ever up and that pass is skipped. Run both and compare engineMs.
const boxes = arg('boxes', null);
const adaptive = process.argv.includes('--adaptive');
const shot = arg('shot', null);
const repo = fileURLToPath(new URL('../../', import.meta.url));
let site = arg('site', null);
if (!site) {
  site = join(tmpdir(), 'fr-bench-site');
  execFileSync('dotnet', ['publish', join(repo, 'src/FeedRaider.Web'), '-c', 'Release', '-o', site, '-v', 'q', '-nologo'], { stdio: 'inherit' });
  site = join(site, 'wwwroot');
}
site = resolve(site);

async function playwright() {
  try { return await import('playwright-core'); } catch (e) { if (e.code !== 'ERR_MODULE_NOT_FOUND') throw e; }   // not installed here: look in PLAYWRIGHT_CORE
  const dir = process.env.PLAYWRIGHT_CORE;
  if (!dir) throw new Error('install playwright-core, or set PLAYWRIGHT_CORE to its directory');
  return import(pathToFileURL(join(dir, 'index.mjs')).href);
}

const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.html': 'text/html', '.css': 'text/css', '.json': 'application/json', '.png': 'image/png', '.wasm': 'application/wasm' };
const server = createServer(async (req, res) => {
  const path = new URL(req.url, 'http://x').pathname;
  try {
    const body = await readFile(join(site, path === '/' ? 'index.html' : decodeURIComponent(path).replace(/^\/+/, '')));
    res.writeHead(200, { 'Content-Type': types[extname(path === '/' ? 'index.html' : path)] || 'application/octet-stream' });
    res.end(body);
  } catch (e) {
    if (!['ENOENT', 'ENOTDIR', 'EISDIR'].includes(e.code) && !(e instanceof URIError)) throw e;   // a missing file, a folder or a bad escape is a 404; any other failure is a real fault
    res.writeHead(404); res.end();
  }
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const base = `http://127.0.0.1:${server.address().port}/`;

const words = 'the quick brown fox jumps over a lazy dog while posting about coffee, cats, code and the weather today'.split(' ');
// Posts the way the feed has them: every author has a display name, and the text runs from a few words to the 300 characters
// a post may hold (the cards are laid out from these, so their height and the work to draw them vary).
const textOf = (i) => {
  const lens = [20, 60, 140, POST_MAX_CHARS, 90, 230, 45, POST_MAX_CHARS, 170, 110];
  let t = '';
  for (let k = 0; t.length < lens[i % lens.length]; k++) t += (k ? ' ' : '') + words[(i * WORD_SHIFT_PER_POST + k) % words.length];
  return t.slice(0, lens[i % lens.length]);
};
// Every third post carries one to four of the pictures built into the game (no network): the cards draw them as they do a
// real post's, so the frames measured include picture cards.
const builtin = [['harbour', 20, 10], ['tea', 12, 12], ['plant', 8, 16], ['toast', 12, 12], ['hill', 20, 10]];
const picturesOf = (i) => (i % PICTURE_POST_EVERY ? undefined : {
  $type: 'app.bsky.embed.images#view',
  images: Array.from({ length: (i / PICTURE_POST_EVERY) % MAX_PICTURES + 1 }, (_, k) => {
    const [name, width, height] = builtin[(i + k) % builtin.length];
    return { thumb: 'builtin:' + name, fullsize: 'builtin:' + name, alt: '', aspectRatio: { width, height } };
  }),
});
const feed = JSON.stringify({
  cursor: 'c',
  feed: Array.from({ length: FEED_POSTS }, (_, i) => ({
    post: {
      uri: `at://did:plc:bench${i}/app.bsky.feed.post/${i}`, cid: 'bafy' + i,
      author: { did: 'did:plc:bench' + i, handle: `user${i}.test`, displayName: `Bench Author ${i}` },
      record: { text: textOf(i) },
      embed: picturesOf(i),
      likeCount: i, repostCount: 1, replyCount: 2,
    },
  })),
});

async function printProfile(p) {
  const self = new Map(), byId = new Map(p.nodes.map((n) => [n.id, n]));
  p.samples.forEach((id, i) => self.set(id, (self.get(id) || 0) + p.timeDeltas[i]));
  const agg = new Map();
  for (const [id, t] of self) { const f = byId.get(id).callFrame; const k = `${f.functionName || '(anon)'} ${f.url.split('/').pop()}:${f.lineNumber}`; agg.set(k, (agg.get(k) || 0) + t); }
  const tot = [...agg.values()].reduce((a, c) => a + c, 0);
  for (const [k, t] of [...agg].sort((a, c) => c[1] - a[1]).slice(0, PROFILE_ROWS_SHOWN)) process.stdout.write(`  ${(100 * t / tot).toFixed(1).padStart(5)}%  ${k}\n`);
}

const pw = await playwright();
const browser = await pw.chromium.launch({ headless: true, executablePath: arg('chrome', undefined), args: ['--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--use-angle=swiftshader', '--enable-gpu-rasterization', ...String(arg('chrome-args', '')).split(' ').filter(Boolean)] });
const out = [];
const runs = [];
for (const f of fieldsArg.split(',')) for (const b of bossArg.split(',').map(Number)) for (const rate of rates) runs.push({ rate, boss: b, desktop: f === 'desktop' });
try {
  for (const { rate, boss, desktop } of runs) {
    const ctx = await browser.newContext({ ...(desktop ? { viewport: DESKTOP_VIEWPORT, deviceScaleFactor: 1 } : { viewport: PHONE_VIEWPORT, deviceScaleFactor: PHONE_PIXEL_RATIO, isMobile: true, hasTouch: true }) });
    const page = await ctx.newPage();
    if (process.argv.includes('--debug')) { const say = (t) => process.stdout.write(t + '\n'); page.on('console', (m) => say('  console: ' + m.text().slice(0, CONSOLE_CHARS_SHOWN))); page.on('requestfailed', (q) => say('  failed: ' + q.url())); page.on('pageerror', (e) => say('  pageerror: ' + e.message)); }
    await page.route(/bsky\.app|bsky\.social|bsky\.network/, (r) => {
      const u = r.request().url();
      return u.includes('getFeed') || u.includes('getAuthorFeed') ? r.fulfill({ status: 200, contentType: 'application/json', body: feed }) : r.abort();
    });
    // the script runs in the page, which cannot see this module's constants: the frame threshold goes in as its argument
    await page.addInitScript((minFrameChars) => {
      const s = (globalThis.__bench = { writes: 0, chars: 0, gaps: [], longTasks: 0, longMs: 0, on: false, cbMs: [] });
      let T; // xterm's Terminal arrives as a script global: count what the game hands it
      Object.defineProperty(window, 'Terminal', {
        configurable: true, get: () => T,
        set: (v) => {
          T = v;
          const w = v.prototype.write;
          v.prototype.write = function (d, cb) {
            s.term = this;
            if (s.on && typeof d === 'string' && d.length > minFrameChars) {
              s.writes++; s.chars += d.length;
              const t0 = performance.now();
              return w.call(this, d, () => { s.cbMs.push(performance.now() - t0); cb?.(); });
            }
            return w.call(this, d, cb);
          };
        },
      });
      try { new PerformanceObserver((l) => { if (s.on) for (const e of l.getEntries()) { s.longTasks++; s.longMs += e.duration; } }).observe({ entryTypes: ['longtask'] }); } catch (e) { if (!(e instanceof TypeError)) throw e; }   // no 'longtask' entries in this browser: the long-task counters stay at zero
      let last = 0;
      const tick = (t) => { if (s.on && last) s.gaps.push(t - last); last = t; requestAnimationFrame(tick); };
      requestAnimationFrame(tick);
    }, MIN_FRAME_CHARS);
    const cdp = await ctx.newCDPSession(page);
    await cdp.send('Performance.enable');
    await page.goto(`${base}?handle=bench&feed=random&${boss > 0 ? `boss=${boss}` : `stage=${stage}`}&fps=1${extraQuery}${adaptive ? '&quality=adaptive' : ''}${boxes ? `&darkness=${boxes === 'off' ? 'off' : 'on'}` : ''}`, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => document.body.classList.contains('playing'), null, { timeout: LOAD_TIMEOUT_MS });
    await page.waitForTimeout(SETTLE_MS);
    if (process.argv.includes('--pause')) await page.keyboard.press('Escape');   // baseline: the pause menu open, the game holding still
    await cdp.send('Emulation.setCPUThrottlingRate', { rate });
    await page.waitForTimeout(SETTLE_MS);
    const metric = async () => Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map((m) => [m.name, m.value]));
    const trace = [];
    if (tracing) { cdp.on('Tracing.dataCollected', (e) => trace.push(...e.value)); await cdp.send('Tracing.start', { categories: 'toplevel,devtools.timeline,blink,cc,gpu,viz,v8', transferMode: 'ReportEvents' }); }
    if (profile) { await cdp.send('Profiler.enable'); await cdp.send('Profiler.start'); }
    await page.evaluate(() => { globalThis.__bench.on = true; });
    const m0 = await metric(); const t0 = Date.now();
    for (let i = 0; i < seconds * MS_PER_SECOND / SAMPLE_MS; i++) {
      if (!still) await page.mouse.move(MOVE_X_LEFT + (i * MOVE_X_STEP) % MOVE_X_SPAN, MOVE_Y_BOTTOM - (i * MOVE_Y_STEP) % MOVE_Y_SPAN);
      await page.waitForTimeout(SAMPLE_MS);
    }
    const wall = (Date.now() - t0) / MS_PER_SECOND;
    // the game's own "NNfps N.Nms" readout (?fps=1): mean ms of one step + render + encode as the engine measures it, read off the terminal
    const engineMs = await page.evaluate(() => { const b = globalThis.__bench.term?.buffer.active; for (let y = 0; b && y < b.length; y++) { const m = /(\d+)fps (\d+\.\d)ms/.exec(b.getLine(y)?.translateToString() ?? ''); if (m) return +m[2]; } return null; });
    const m1 = await metric();
    if (tracing) {
      const done = new Promise((r) => cdp.once('Tracing.tracingComplete', r));
      await cdp.send('Tracing.end'); await done;
      const msByName = new Map();
      for (const e of trace) if (e.ph === 'X' && e.dur) msByName.set(e.name, (msByName.get(e.name) || 0) + e.dur / US_PER_MS);
      const slowSpans = trace.filter((e) => e.ph === 'X' && e.dur > SLOW_SPAN_US).sort((a, b) => a.ts - b.ts);
      for (const e of slowSpans.slice(0, SLOW_SPANS_LISTED)) process.stdout.write(`  slow ${(e.dur / US_PER_MS).toFixed(0).padStart(5)} ms  tid ${e.tid} ${e.name}\n`);
      for (const [k, v] of [...msByName].sort((a, b) => b[1] - a[1]).slice(0, SPAN_TOTALS_LISTED)) process.stdout.write(`  ${(v / (wall * TICKS_PER_SECOND)).toFixed(2).padStart(7)} ms/tick  ${k}\n`);
    }
    if (profile) await printProfile((await cdp.send('Profiler.stop')).profile);
    const s = await page.evaluate(() => { const b = globalThis.__bench; b.on = false; return b; });
    const pct = (a, p) => { const v = [...a].sort((x, y) => x - y); return v.length ? v[Math.min(v.length - 1, Math.floor(v.length * p))] : 0; };
    const ticks = wall * TICKS_PER_SECOND;
    const metricDeltaMs = (k) => (m1[k] - m0[k]) * MS_PER_SECOND;   // the metrics are in seconds
    const result = {
      scene: (boss > 0 ? `boss ${boss}` : `stage ${stage}`) + (boxes ? `, boxes ${boxes}` : ''), field: desktop ? 'desktop' : 'phone', rate, engineMs, seconds: +wall.toFixed(1),
      rafP50: +pct(s.gaps, 0.5).toFixed(1), rafP95: +pct(s.gaps, 0.95).toFixed(1), rafMax: +Math.max(0, ...s.gaps).toFixed(1),
      dropped: s.gaps.filter((g) => g > DROPPED_FRAME_MS).length, rafFrames: s.gaps.length,
      gameFps: +(s.writes / wall).toFixed(1), charsPerFrame: Math.round(s.chars / Math.max(1, s.writes)),
      xtermParseP95: +pct(s.cbMs, 0.95).toFixed(1),
      longTasks: s.longTasks, longMs: Math.round(s.longMs),
      busyMsPerTick: +(metricDeltaMs('TaskDuration') / ticks).toFixed(2), scriptMsPerTick: +(metricDeltaMs('ScriptDuration') / ticks).toFixed(2),
      busyPct: +(metricDeltaMs('TaskDuration') / (wall * MS_PER_SECOND) * 100).toFixed(0),
    };
    result.metrics = Object.fromEntries(['LayoutDuration', 'RecalcStyleDuration', 'ScriptDuration', 'TaskDuration', 'V8CompileDuration'].map((k) => [k, +(metricDeltaMs(k) / ticks).toFixed(2)]));
    out.push(result);
    if (process.argv.includes('--metrics')) process.stdout.write(`  ms per tick ${JSON.stringify(result.metrics)}\n`);
    process.stdout.write(`${result.scene} ${result.field} cpu ${rate}x  raf p50 ${result.rafP50} p95 ${result.rafP95} max ${result.rafMax} ms | dropped ${result.dropped}/${result.rafFrames} | game ${result.gameFps} fps, ${result.charsPerFrame} chars/frame, xterm parse p95 ${result.xtermParseP95} ms | long tasks ${result.longTasks} (${result.longMs} ms) | engine step+render+encode ${result.engineMs} ms/frame | main thread ${result.busyMsPerTick} ms/tick (script ${result.scriptMsPerTick}), ${result.busyPct}% busy\n`);
    if (shot && rate === rates[0]) await page.screenshot({ path: shot });   // the game as the first run left it
    await ctx.close();
  }
} finally {
  await browser.close();
  server.close();
}
const jsonPath = arg('json', null);
if (jsonPath) writeFileSync(jsonPath, JSON.stringify(out, null, 1));
