// Frame-time benchmark of the web build in headless Chromium, at a phone viewport with the CPU throttled.
//   npm run bench                      (from web/; publishes src/FeedRaider.Web first when no --site is given)
//   node bench/frame-bench.mjs [--site <published wwwroot>] [--seconds 30] [--rates 1,4,6] [--stage 8] [--adaptive] [--json out.json]
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
const SLOW_SPAN_US = 80000;      // a trace span longer than this (in microseconds) is listed as slow

const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i < 0 ? d : (process.argv[i + 1] ?? true); };
const seconds = Number(arg('seconds', 30));
const rates = String(arg('rates', '1,4,6')).split(',').map(Number);
const stage = Number(arg('stage', 8));
const profile = process.argv.includes('--profile');   // also print the top self-time functions (the WebAssembly interpreter shows as one or two frames)
const extraQuery = String(arg('query', ''));   // e.g. --query &renderer=dom
const adaptive = process.argv.includes('--adaptive');
const repo = fileURLToPath(new URL('../../', import.meta.url));
let site = arg('site', null);
if (!site) {
  site = join(tmpdir(), 'fr-bench-site');
  execFileSync('dotnet', ['publish', join(repo, 'src/FeedRaider.Web'), '-c', 'Release', '-o', site, '-v', 'q', '-nologo'], { stdio: 'inherit' });
  site = join(site, 'wwwroot');
}
site = resolve(site);

async function playwright() {
  try { return await import('playwright-core'); } catch { /* not installed here */ }
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
  } catch { res.writeHead(404); res.end(); }
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const base = `http://127.0.0.1:${server.address().port}/`;

const words = 'the quick brown fox jumps over a lazy dog while posting about coffee, cats, code and the weather today'.split(' ');
const feed = JSON.stringify({
  cursor: 'c',
  feed: Array.from({ length: 30 }, (_, i) => ({
    post: {
      uri: `at://did:plc:bench${i}/app.bsky.feed.post/${i}`, cid: 'bafy' + i,
      author: { did: 'did:plc:bench' + i, handle: `user${i}.test` },
      record: { text: Array.from({ length: 12 + i % 20 }, (_, k) => words[(i * 7 + k) % words.length]).join(' ') },
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
  for (const [k, t] of [...agg].sort((a, c) => c[1] - a[1]).slice(0, 14)) process.stdout.write(`  ${(100 * t / tot).toFixed(1).padStart(5)}%  ${k}\n`);
}

const pw = await playwright();
const browser = await pw.chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--use-angle=swiftshader', '--enable-gpu-rasterization', ...String(arg('chrome-args', '')).split(' ').filter(Boolean)] });
const out = [];
try {
  for (const rate of rates) {
    const ctx = await browser.newContext({ viewport: { width: 390, height: 780 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
    const page = await ctx.newPage();
    await page.route(/bsky\.app|bsky\.social|bsky\.network/, (r) => {
      const u = r.request().url();
      return u.includes('getFeed') || u.includes('getAuthorFeed') ? r.fulfill({ status: 200, contentType: 'application/json', body: feed }) : r.abort();
    });
    await page.addInitScript(() => {
      const s = (globalThis.__bench = { writes: 0, chars: 0, gaps: [], longTasks: 0, longMs: 0, on: false, cbMs: [] });
      let T; // xterm's Terminal arrives as a script global: count what the game hands it
      Object.defineProperty(window, 'Terminal', {
        configurable: true, get: () => T,
        set: (v) => {
          T = v;
          const w = v.prototype.write;
          v.prototype.write = function (d, cb) {
            s.term = this;
            if (s.on && typeof d === 'string' && d.length > 20) {
              s.writes++; s.chars += d.length;
              const t0 = performance.now();
              return w.call(this, d, () => { s.cbMs.push(performance.now() - t0); cb?.(); });
            }
            return w.call(this, d, cb);
          };
        },
      });
      try { new PerformanceObserver((l) => { if (s.on) for (const e of l.getEntries()) { s.longTasks++; s.longMs += e.duration; } }).observe({ entryTypes: ['longtask'] }); } catch { /* no 'longtask' entries in this browser: the long-task counters stay at zero */ }
      let last = 0;
      const tick = (t) => { if (s.on && last) s.gaps.push(t - last); last = t; requestAnimationFrame(tick); };
      requestAnimationFrame(tick);
    });
    const cdp = await ctx.newCDPSession(page);
    await cdp.send('Performance.enable');
    await page.goto(`${base}?handle=bench&feed=random&stage=${stage}&fps=1${extraQuery}${adaptive ? '&quality=adaptive' : ''}`);
    await page.waitForFunction(() => document.body.classList.contains('playing'), null, { timeout: 120000 });
    await page.waitForTimeout(3000);
    if (process.argv.includes('--pause')) await page.keyboard.press('Escape');   // baseline: the pause menu open, the game holding still
    await cdp.send('Emulation.setCPUThrottlingRate', { rate });
    await page.waitForTimeout(3000);
    const metric = async () => Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map((m) => [m.name, m.value]));
    const trace = [];
    if (process.argv.includes('--trace')) { cdp.on('Tracing.dataCollected', (e) => trace.push(...e.value)); await cdp.send('Tracing.start', { categories: 'toplevel,devtools.timeline,blink,cc,gpu,viz,v8', transferMode: 'ReportEvents' }); }
    if (profile) { await cdp.send('Profiler.enable'); await cdp.send('Profiler.start'); }
    await page.evaluate(() => { globalThis.__bench.on = true; });
    const m0 = await metric(); const t0 = Date.now();
    for (let i = 0; i < seconds * 2; i++) {
      await page.mouse.move(60 + (i * 37) % 270, 600 - (i * 13) % 200);   // keep the player moving
      await page.waitForTimeout(500);
    }
    const wall = (Date.now() - t0) / 1000;
    // the game's own "NNfps N.Nms" readout (?fps=1): mean ms of one step + render + encode as the engine measures it, read off the terminal
    const engineMs = await page.evaluate(() => { const b = globalThis.__bench.term?.buffer.active; for (let y = 0; b && y < b.length; y++) { const m = /(\d+)fps (\d+\.\d)ms/.exec(b.getLine(y)?.translateToString() ?? ''); if (m) return +m[2]; } return null; });
    const m1 = await metric();
    if (trace.length || process.argv.includes('--trace')) {
      const done = new Promise((r) => cdp.once('Tracing.tracingComplete', r));
      await cdp.send('Tracing.end'); await done;
      const by = new Map();
      for (const e of trace) if (e.ph === 'X' && e.dur) by.set(e.name, (by.get(e.name) || 0) + e.dur / 1000);
      const big = trace.filter((e) => e.ph === 'X' && e.dur > SLOW_SPAN_US).sort((a, b) => a.ts - b.ts);
      for (const e of big.slice(0, 40)) process.stdout.write(`  slow ${(e.dur / 1000).toFixed(0).padStart(5)} ms  tid ${e.tid} ${e.name}\n`);
      for (const [k, v] of [...by].sort((a, b) => b[1] - a[1]).slice(0, 16)) process.stdout.write(`  ${(v / (wall * TICKS_PER_SECOND)).toFixed(2).padStart(7)} ms/tick  ${k}\n`);
    }
    if (profile) await printProfile((await cdp.send('Profiler.stop')).profile);
    const s = await page.evaluate(() => { const b = globalThis.__bench; b.on = false; return b; });
    const pct = (a, p) => { const v = [...a].sort((x, y) => x - y); return v.length ? v[Math.min(v.length - 1, Math.floor(v.length * p))] : 0; };
    const ticks = wall * TICKS_PER_SECOND;
    const d = (k) => (m1[k] - m0[k]) * 1000;
    const r = {
      rate, engineMs, seconds: +wall.toFixed(1),
      rafP50: +pct(s.gaps, 0.5).toFixed(1), rafP95: +pct(s.gaps, 0.95).toFixed(1), rafMax: +Math.max(0, ...s.gaps).toFixed(1),
      dropped: s.gaps.filter((g) => g > DROPPED_FRAME_MS).length, rafFrames: s.gaps.length,
      gameFps: +(s.writes / wall).toFixed(1), charsPerFrame: Math.round(s.chars / Math.max(1, s.writes)),
      xtermParseP95: +pct(s.cbMs, 0.95).toFixed(1),
      longTasks: s.longTasks, longMs: Math.round(s.longMs),
      busyMsPerTick: +(d('TaskDuration') / ticks).toFixed(2), scriptMsPerTick: +(d('ScriptDuration') / ticks).toFixed(2),
      busyPct: +(d('TaskDuration') / 10 / wall).toFixed(0),
    };
    r.metrics = Object.fromEntries(['LayoutDuration', 'RecalcStyleDuration', 'ScriptDuration', 'TaskDuration', 'V8CompileDuration'].map((k) => [k, +(d(k) / ticks).toFixed(2)]));
    out.push(r);
    if (process.argv.includes('--metrics')) process.stdout.write(`  ms per tick ${JSON.stringify(r.metrics)}\n`);
    process.stdout.write(`cpu ${rate}x  raf p50 ${r.rafP50} p95 ${r.rafP95} max ${r.rafMax} ms | dropped ${r.dropped}/${r.rafFrames} | game ${r.gameFps} fps, ${r.charsPerFrame} chars/frame, xterm parse p95 ${r.xtermParseP95} ms | long tasks ${r.longTasks} (${r.longMs} ms) | engine step+render+encode ${r.engineMs} ms/frame | main thread ${r.busyMsPerTick} ms/tick (script ${r.scriptMsPerTick}), ${r.busyPct}% busy\n`);
    await ctx.close();
  }
} finally {
  await browser.close();
  server.close();
}
const j = arg('json', null);
if (j) writeFileSync(j, JSON.stringify(out, null, 1));
