// node --test web/test: music.js against a fake clock and a fake Audio. No browser, no network.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { readMusicManifest } from '../music-manifest.mjs';

const wwwroot = fileURLToPath(new URL('../../src/FeedRaider.Web/wwwroot/', import.meta.url));
const DURATION = 60; // seconds, every fake track

let n = 0;
/** A fresh music.js on a fake page. Time only moves when `advance` is called. */
async function setup({ manifest, gameplay = 3, random, connection } = {}) {
  let now = 0;
  const intervals = new Set();
  const els = [], starts = [];
  class FakeAudio {
    constructor() { this.src = ''; this.loop = false; this.paused = true; this.currentTime = 0; this.volume = 1; els.push(this); }
    get duration() { return this.src.startsWith('blob:') ? DURATION : NaN; }
    play() { if (this.src.startsWith('blob:')) starts.push(this.src.slice(this.src.lastIndexOf('/') + 1)); this.paused = false; return Promise.resolve(); }
    pause() { this.paused = true; }
  }
  class FakeBlob { constructor(parts) { this.parts = parts; } }
  const fetched = [];
  const list = manifest ?? [
    { file: 'fr-start.mp3', role: 'menu' },
    ...Array.from({ length: gameplay }, (_, i) => ({ file: `fr-g${i + 1}.mp3`, role: 'gameplay' })),
    { file: 'fr-boss.mp3', role: 'boss' },
    { file: 'fr-gameover.mp3', role: 'gameover' },
  ];
  Object.assign(globalThis, {
    window: { addEventListener() {}, removeEventListener() {} },
    document: { baseURI: 'https://example.test/raider/', getElementById: () => null, addEventListener() {}, hidden: false },
    Audio: FakeAudio,
    Blob: FakeBlob,
    setInterval: (fn, ms) => { const t = { fn, ms }; intervals.add(t); return t; },
    clearInterval: (t) => intervals.delete(t),
    fetch: async (url) => {
      fetched.push(url);
      if (url.endsWith('tracks.json')) return { ok: true, json: async () => list };
      return { ok: true, arrayBuffer: async () => url };
    },
  });
  const revoked = [];
  URL.createObjectURL = (b) => `blob:${b.parts[0]}`;
  URL.revokeObjectURL = (u) => revoked.push(u.slice(u.lastIndexOf('/') + 1));
  Object.defineProperty(globalThis, 'navigator', { value: { connection }, configurable: true });
  Object.defineProperty(globalThis, 'performance', { value: { now: () => now }, configurable: true });

  const music = await import(`../../src/FeedRaider.Web/wwwroot/music.js?run=${n++}`);
  let seed = 7;
  music.setRandom(random ?? (() => (seed = (seed * 16807) % 2147483647) / 2147483647));
  const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
  /** Moves the clock by `sec` seconds in 40 ms steps, playing elements age and the page's intervals fire. */
  const advance = async (sec) => {
    for (let t = 0; t < sec * 1000; t += 40) {
      now += 40;
      for (const e of els) if (!e.paused && e.src.startsWith('blob:')) e.currentTime = e.loop && e.currentTime >= DURATION ? 0 : Math.min(DURATION, e.currentTime + 0.04);
      for (const i of [...intervals]) if (i.ms <= 40 || Math.floor(now / i.ms) !== Math.floor((now - 40) / i.ms)) i.fn();
      await flush();
    }
  };
  const names = () => els.filter((e) => !e.paused && e.src.startsWith('blob:')).map((e) => e.src.slice(e.src.lastIndexOf('/') + 1));
  return { music, advance, flush, els, fetched, names, starts, revoked };
}

async function start(opts = {}) {
  let unlock;
  const c = await setup(opts);
  window.addEventListener = (ev, fn) => { if (fn.name === 'unlock') unlock ??= fn; };
  c.music.init({ manifestUrl: 'music/tracks.json' });
  await c.flush();
  unlock?.();
  await c.flush();
  return c;
}

/** The gameplay tracks the rotation has started, in order, once `count` of them have. */
async function playThrough(c, count) {
  for (let i = 0; i < count * (DURATION + 1) * 5 && c.starts.filter((x) => x.startsWith('fr-g')).length < count; i++) await c.advance(0.2);
  return c.starts.filter((x) => x.startsWith('fr-g'));
}

test('tracks.json is fetched relative to the page and its roles are honoured', async () => {
  const c = await start();
  assert.equal(c.fetched[0], 'https://example.test/raider/music/tracks.json');
  c.music.cue('menu');
  await c.advance(1);
  assert.ok(c.fetched.includes('https://example.test/raider/music/fr-start.mp3'));
  assert.ok(c.names().every((x) => x === 'fr-start.mp3'), `menu plays fr-start, got ${c.names()}`);
});

test('a cue that comes before the manifest is played once it has loaded', async () => {
  let unlock;
  const c = await setup();
  window.addEventListener = (ev, fn) => { if (fn.name === 'unlock') unlock ??= fn; };
  c.music.init({ manifestUrl: 'music/tracks.json' });
  c.music.cue('gameplay');          // before the fetch of tracks.json has resolved
  unlock();
  await c.advance(2);
  assert.ok(c.names().some((x) => x.startsWith('fr-g')));
});

test('the bundle manifest keeps role and url and rejects what is not usable', () => {
  const c = readMusicManifest([
    { role: 'menu', url: 'https://x.test/a.mp3' }, { role: 'gameplay', url: 'https://x.test/b.mp3' },
    { role: 'boss', url: 'https://x.test/c.mp3' }, { role: 'gameover', url: 'https://x.test/d.mp3' },
  ]);
  assert.equal(c.length, 4);
  assert.throws(() => readMusicManifest([{ role: 'calm', url: 'https://x.test/a.mp3' }]));
  assert.throws(() => readMusicManifest([{ role: 'menu', url: 'a.mp3' }]));
  assert.throws(() => readMusicManifest([{ role: 'menu', url: 'https://x.test/a.mp3' }]), /gameplay/);
});

test('the shipped tracks.json lists existing files in the four roles, and the rotation roles', () => {
  const list = JSON.parse(readFileSync(wwwroot + 'music/tracks.json', 'utf8'));
  for (const role of ['menu', 'gameplay', 'boss', 'gameover']) assert.ok(list.some((t) => t.role === role), role);
  for (const t of list) readFileSync(wwwroot + 'music/' + t.file);
  assert.deepEqual(list.filter((t) => t.role === 'gameplay').map((t) => t.file).sort(), ['fr-play-electro-house.mp3', 'fr-play-filter-house.mp3', 'fr-play-italo-night.mp3', 'fr-play-liquid-dnb.mp3', 'fr-play-tech-house.mp3', 'fr-play-uk-garage.mp3']);
});

test('the rotation plays each track once, never the same one twice in a row', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  const seen = await playThrough(c, 7);
  assert.equal(seen.length, 7, String(seen));
  for (let i = 1; i < seen.length; i++) assert.notEqual(seen[i], seen[i - 1], `back to back at ${i}: ${seen}`);
  assert.deepEqual(new Set(seen.slice(0, 3)).size, 3, 'the first cycle plays every track once');
});

test('the next track fades in about 2 s before the end of the current one', async () => {
  const c = await start({ gameplay: 2 });
  c.music.cue('gameplay');
  await c.advance(3);
  const first = c.names();
  assert.equal(first.length, 1);
  await c.advance(DURATION - 3 - 3);                // 3 s before the end: still one track
  assert.equal(c.names().length, 1);
  await c.advance(1.5);                              // 1.5 s before the end: both are playing
  assert.equal(c.names().length, 2, `crossfading: ${c.names()}`);
  const incoming = c.els.find((e) => !e.paused && e.currentTime < 5);
  assert.ok(incoming.currentTime < 2, 'the new track has just started');
  await c.advance(3);
  assert.equal(c.names().length, 1);
  assert.notEqual(c.names()[0], first[0]);
});

test('a boss interrupts and the rotation resumes with the next track, not the interrupted one', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(10);
  const [interrupted] = c.names();
  c.music.cue('boss');
  await c.advance(3);
  assert.deepEqual(c.names(), ['fr-boss.mp3']);
  assert.ok(c.els.find((e) => e.src.endsWith('fr-boss.mp3')).loop, 'the boss track loops');
  c.music.cue('gameplay');
  await c.advance(3);
  const [resumed] = c.names();
  assert.notEqual(resumed, interrupted);
  assert.ok(c.els.find((e) => e.src.endsWith(resumed)).currentTime < 4, 'the next track starts from 0');
  assert.ok(!c.els.some((e) => !e.paused && e.src.endsWith(interrupted)), 'the interrupted track is not restarted');
});

test('game over and the menu stop the rotation; a new run starts a fresh one', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(10);
  const [before] = c.names();
  c.music.cue('gameover');
  await c.advance(3);
  assert.deepEqual(c.names(), ['fr-gameover.mp3']);
  await c.advance(DURATION * 2);                     // far longer than any track: nothing rotates in
  assert.deepEqual(c.names(), ['fr-gameover.mp3']);
  c.music.cue('menu');
  await c.advance(3);
  assert.deepEqual(c.names(), ['fr-start.mp3']);
  c.music.cue('gameplay');
  await c.advance(3);
  const [after] = c.names();
  assert.ok(after.startsWith('fr-g'));
  assert.notEqual(after, before, 'a new run does not start on the track played last');
});

test('a new run starts at a random rotation track', async () => {
  const firsts = new Set();
  for (const r of [0, 0.34, 0.67, 0.99]) {
    const c = await start({ gameplay: 3, random: () => r });
    c.music.cue('gameplay');
    await c.advance(3);
    firsts.add(c.names()[0]);
  }
  assert.ok(firsts.size >= 2, `starts vary with the dice: ${[...firsts]}`);
});

test('with one gameplay track it loops', async () => {
  const c = await start({ gameplay: 1 });
  c.music.cue('gameplay');
  await c.advance(5);
  const el = c.els.find((e) => !e.paused && e.src.startsWith('blob:'));
  assert.ok(el.loop);
  await c.advance(DURATION + 5);
  assert.equal(c.names().length, 1);
});

const mp3s = (c) => c.fetched.filter((u) => u.endsWith('.mp3')).map((u) => u.slice(u.lastIndexOf('/') + 1));

test('the menu cue fetches the menu track and no gameplay track ahead of time', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('menu');
  await c.advance(3);
  assert.deepEqual(mp3s(c), ['fr-start.mp3']);
});

test('gameplay fetches its own track and the next one only, not the boss or game over tracks', async () => {
  const c = await start({ gameplay: 4 });
  c.music.cue('gameplay');
  await c.advance(3);
  const got = mp3s(c).filter((x) => x !== 'fr-start.mp3');
  assert.equal(got.length, 2, `current and next only: ${got}`);
  assert.ok(got.every((x) => x.startsWith('fr-g')), String(got));
});

test('the boss and game over tracks are fetched when they are cued', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(3);
  assert.ok(!mp3s(c).includes('fr-boss.mp3'));
  c.music.cue('boss');
  await c.advance(3);
  assert.ok(mp3s(c).includes('fr-boss.mp3'));
  assert.ok(!mp3s(c).includes('fr-gameover.mp3'));
  c.music.cue('gameover');
  await c.advance(3);
  assert.deepEqual(c.names(), ['fr-gameover.mp3']);
});

for (const connection of [{ saveData: true }, { effectiveType: 'slow-2g' }, { effectiveType: '2g' }, { effectiveType: '3g' }]) {
  test(`nothing is fetched ahead on ${JSON.stringify(connection)}, but a cued track still plays`, async () => {
    const c = await start({ gameplay: 3, connection });
    assert.deepEqual(mp3s(c), [], 'no menu track up front');
    c.music.cue('gameplay');
    await c.advance(3);
    assert.equal(mp3s(c).length, 1, `only the playing track: ${mp3s(c)}`);
    assert.equal(c.names().length, 1);
  });
}

test('a 4g connection without data saver still fetches the next track', async () => {
  const c = await start({ gameplay: 3, connection: { saveData: false, effectiveType: '4g' } });
  c.music.cue('gameplay');
  await c.advance(3);
  assert.equal(mp3s(c).filter((x) => x.startsWith('fr-g')).length, 2);
});

test('a rotation track that has been played through has its blob URL revoked', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(3);
  const [first] = c.names();
  assert.deepEqual(c.revoked, []);
  await c.advance(DURATION);                         // past the crossfade: the first track has been released
  assert.deepEqual(c.revoked, [first]);
});

test('a hidden tab pauses the music where it is and a return carries on, without rotating in between', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(5);
  const [playing] = c.names();
  c.music.setPaused(true);
  assert.deepEqual(c.names(), [], 'nothing plays in the background');
  const el = c.els.find((e) => e.src.endsWith(playing));
  const at = el.currentTime;
  await c.advance(DURATION * 2);
  assert.equal(el.currentTime, at, 'the track did not move');
  assert.equal(c.starts.filter((x) => x.startsWith('fr-g')).length, 1, 'no other track was started while hidden');
  c.music.setPaused(false);
  await c.advance(1);
  assert.deepEqual(c.names(), [playing], 'the same track plays on');
  assert.ok(el.currentTime > at);
});
