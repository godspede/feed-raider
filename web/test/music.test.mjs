// node --test web/test: music.js against a fake clock and a fake Audio. No browser, no network.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { readMusicManifest } from '../music-manifest.mjs';
import { thinConnection } from '../../src/FeedRaider.Web/wwwroot/page.js';

const wwwroot = fileURLToPath(new URL('../../src/FeedRaider.Web/wwwroot/', import.meta.url));
const DURATION = 60; // seconds, every fake track
const STEP_MS = 40; // how far `advance` moves the clock at a time
const FLUSH_TURNS = 20; // microtask turns `flush` waits so chained promises settle
const CUE_SETTLE_SEC = 3; // twice music.js's 1.5 s cue fade, so a cued track has faded in and the last one out
const INTO_TRACK_SEC = 10; // well into a track and far from its crossfade
const WAV_HEADER_BYTES = 44; // a canonical PCM WAV header
const WAV_RIFF_SIZE_AT = 4, WAV_FORMAT_AT = 20, WAV_DATA_TAG_AT = 36, WAV_DATA_SIZE_AT = 40; // field offsets in that header
const WAV_RIFF_PREAMBLE_BYTES = 8; // the 'RIFF' tag and size field, which the RIFF size leaves out
const PARK_MILLER_MULTIPLIER = 16807, PARK_MILLER_MODULUS = 2147483647; // the minimal-standard seeded generator
const SHUFFLE_SEED = 7; // any nonzero start for that generator; fixed so a run's shuffles repeat
const isBlob = (src) => src.startsWith('blob:');
const lastSegment = (url) => url.slice(url.lastIndexOf('/') + 1);

let importRun = 0;
/** A fresh music.js on a fake page. Time only moves when `advance` is called. */
async function setup({ manifest, gameplay = 3, random, connection } = {}) {
  let now = 0;
  const intervals = new Set();
  const els = [], starts = [];
  class FakeAudio {
    constructor() { this.src = ''; this.loop = false; this.paused = true; this.currentTime = 0; this.volume = 1; els.push(this); }
    get duration() { return isBlob(this.src) ? DURATION : NaN; }
    play() { if (isBlob(this.src)) starts.push(lastSegment(this.src)); this.paused = false; return Promise.resolve(); }
    pause() { this.paused = true; }
  }
  class FakeBlob { constructor(parts) { this.parts = parts; } }
  const fetched = [];
  const list = manifest ?? [
    { file: 'fr-start.mp3', role: 'start' },
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
  URL.revokeObjectURL = (u) => revoked.push(lastSegment(u));
  Object.defineProperty(globalThis, 'navigator', { value: { connection }, configurable: true });
  Object.defineProperty(globalThis, 'performance', { value: { now: () => now }, configurable: true });

  const music = await import(`../../src/FeedRaider.Web/wwwroot/music.js?run=${importRun++}`);
  let seed = SHUFFLE_SEED;
  music.setRandom(random ?? (() => (seed = (seed * PARK_MILLER_MULTIPLIER) % PARK_MILLER_MODULUS) / PARK_MILLER_MODULUS));
  const flush = async () => { for (let i = 0; i < FLUSH_TURNS; i++) await Promise.resolve(); };
  /** Moves the clock by `sec` seconds in STEP_MS steps, playing elements age and the page's intervals fire. */
  const advance = async (sec) => {
    for (let t = 0; t < sec * 1000; t += STEP_MS) {
      now += STEP_MS;
      for (const e of els) if (!e.paused && isBlob(e.src)) e.currentTime = e.loop && e.currentTime >= DURATION ? 0 : Math.min(DURATION, e.currentTime + STEP_MS / 1000);
      for (const i of [...intervals]) if (i.ms <= STEP_MS || Math.floor(now / i.ms) !== Math.floor((now - STEP_MS) / i.ms)) i.fn();
      await flush();
    }
  };
  const names = () => els.filter((e) => !e.paused && isBlob(e.src)).map((e) => lastSegment(e.src));
  return { music, advance, flush, els, fetched, names, starts, revoked };
}

async function start(opts = {}) {
  let unlock;
  const c = await setup(opts);
  window.addEventListener = (ev, fn) => { if (fn.name === 'unlock') unlock ??= fn; };
  c.music.init({ manifestUrl: 'music/tracks.json', thin: thinConnection });
  await c.flush();
  unlock?.();
  await c.flush();
  return c;
}

const PLAY_THROUGH_STEP_SEC = 0.2; // how often playThrough looks for a newly started track

/** The gameplay tracks the rotation has started, in order, once `count` of them have. */
async function playThrough(c, count) {
  for (let i = 0; i < count * (DURATION + 1) / PLAY_THROUGH_STEP_SEC && c.starts.filter((x) => x.startsWith('fr-g')).length < count; i++) await c.advance(PLAY_THROUGH_STEP_SEC);
  return c.starts.filter((x) => x.startsWith('fr-g'));
}

test('tracks.json is fetched relative to the page and its roles are honoured', async () => {
  const c = await start();
  assert.equal(c.fetched[0], 'https://example.test/raider/music/tracks.json');
  c.music.cue('start');
  await c.advance(1);
  assert.ok(c.fetched.includes('https://example.test/raider/music/fr-start.mp3'));
  assert.ok(c.names().every((x) => x === 'fr-start.mp3'), `the start page's cue plays fr-start, got ${c.names()}`);
});

test('a cue that comes before the manifest is played once it has loaded', async () => {
  let unlock;
  const c = await setup();
  window.addEventListener = (ev, fn) => { if (fn.name === 'unlock') unlock ??= fn; };
  c.music.init({ manifestUrl: 'music/tracks.json', thin: thinConnection });
  c.music.cue('gameplay');          // before the fetch of tracks.json has resolved
  unlock();
  await c.advance(CUE_SETTLE_SEC);
  assert.ok(c.names().some((x) => x.startsWith('fr-g')));
});

test('the bundle manifest keeps role and url and rejects what is not usable', () => {
  const c = readMusicManifest([
    { role: 'start', url: 'https://x.test/a.mp3' }, { role: 'gameplay', url: 'https://x.test/b.mp3' },
    { role: 'boss', url: 'https://x.test/c.mp3' }, { role: 'gameover', url: 'https://x.test/d.mp3' },
  ]);
  assert.equal(c.length, 4);
  assert.throws(() => readMusicManifest([{ role: 'calm', url: 'https://x.test/a.mp3' }]));
  assert.throws(() => readMusicManifest([{ role: 'start', url: 'a.mp3' }]));
  assert.throws(() => readMusicManifest([{ role: 'start', url: 'https://x.test/a.mp3' }]), /gameplay/);
});

test('the shipped tracks.json lists existing files in the four roles, and the rotation roles', () => {
  const list = JSON.parse(readFileSync(wwwroot + 'music/tracks.json', 'utf8'));
  for (const role of ['start', 'gameplay', 'boss', 'gameover']) assert.ok(list.some((t) => t.role === role), role);
  for (const t of list) readFileSync(wwwroot + 'music/' + t.file);
  assert.deepEqual(list.filter((t) => t.role === 'gameplay').map((t) => t.file).sort(), ['fr-play-acid-trance.mp3', 'fr-play-afro-house.mp3', 'fr-play-electro-house.mp3', 'fr-play-filter-house.mp3', 'fr-play-goa-trance.mp3', 'fr-play-hard-trance-1.mp3', 'fr-play-hard-trance-2.mp3', 'fr-play-italo-night.mp3', 'fr-play-liquid-dnb.mp3', 'fr-play-psytrance-1.mp3', 'fr-play-psytrance-2.mp3', 'fr-play-tech-house.mp3', 'fr-play-trance.mp3', 'fr-play-uk-garage.mp3', 'fr-play-uplifting-psytrance.mp3']);
  const onDisk = readdirSync(wwwroot + 'music').filter((f) => /^fr-.*\.mp3$/.test(f)).sort();
  assert.deepEqual(onDisk, list.map((t) => t.file).sort());
});

test('the shipped tracks.json gives each boss its kept themes, and every other boss the shared track', () => {
  const list = JSON.parse(readFileSync(wwwroot + 'music/tracks.json', 'utf8'));
  const byBoss = {};
  for (const t of list) if (t.role === 'boss' && t.boss) (byBoss[t.boss] ??= []).push(t.file);
  assert.deepEqual(byBoss, {
    bulwark: ['fr-boss-bulwark.mp3'],
    shutter: ['fr-boss-shutter-1.mp3', 'fr-boss-shutter-2.mp3', 'fr-boss-shutter-3.mp3'],
    schism: ['fr-boss-schism-1.mp3', 'fr-boss-schism-2.mp3'],
  });
  assert.deepEqual(list.filter((t) => t.role === 'boss' && !t.boss).map((t) => t.file), ['fr-boss.mp3'], 'one shared track');
});

test('the unlock clip is a whole WAV with samples in it (an empty data chunk will not decode in Firefox)', () => {
  const b64 = readFileSync(wwwroot + 'music.js', 'utf8').match(/const SILENT = 'data:audio\/wav;base64,([^']+)'/)[1];
  const wav = Buffer.from(b64, 'base64');
  assert.equal(wav.toString('latin1', 0, 4), 'RIFF');
  assert.equal(wav.readUInt32LE(WAV_RIFF_SIZE_AT), wav.length - WAV_RIFF_PREAMBLE_BYTES);
  const formatTag = 'WAVEfmt ';
  assert.equal(wav.toString('latin1', WAV_RIFF_PREAMBLE_BYTES, WAV_RIFF_PREAMBLE_BYTES + formatTag.length), formatTag);
  assert.equal(wav.readUInt16LE(WAV_FORMAT_AT), 1, 'PCM');
  assert.equal(wav.toString('latin1', WAV_DATA_TAG_AT, WAV_DATA_SIZE_AT), 'data');
  const samples = wav.readUInt32LE(WAV_DATA_SIZE_AT);
  assert.ok(samples > 0);
  assert.equal(wav.length, WAV_HEADER_BYTES + samples);
  assert.ok(wav.subarray(WAV_HEADER_BYTES).every((v) => v === 0x80), 'silence in 8-bit PCM');
});

test('the rotation plays each track once, never the same one twice in a row', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  const seen = await playThrough(c, 7);
  assert.equal(seen.length, 7, String(seen));
  for (let i = 1; i < seen.length; i++) assert.notEqual(seen[i], seen[i - 1], `back to back at ${i}: ${seen}`);
  assert.deepEqual(new Set(seen.slice(0, 3)).size, 3, 'the first cycle plays every track once');
});

/** A `random` that hands out `values` in turn, then HIGH: HIGH leaves a shuffle in the order the tracks were listed, 0 reverses its first swap. */
const HIGH = 0.99;
const dice = (...values) => () => values.shift() ?? HIGH;
const trackNames = (n) => Array.from({ length: n }, (_, i) => `fr-g${i + 1}.mp3`);

test('a new run never opens on the track played last, even when the dice would pick it', async () => {
  const c = await start({ gameplay: 3, random: () => HIGH });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-g1.mp3'], 'the first run opens on the first listed track');
  c.music.cue('start');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-g2.mp3']);
});

test('the first cycle of a run plays every track of its order from the first', async () => {
  const c = await start({ gameplay: 3, random: () => HIGH });
  c.music.cue('gameplay');
  assert.deepEqual(await playThrough(c, 3), trackNames(3));
});

test('after the last track of the order the rotation starts a new order from its first track', async () => {
  const c = await start({ gameplay: 3, random: () => HIGH });
  c.music.cue('gameplay');
  assert.deepEqual(await playThrough(c, 4), [...trackNames(3), 'fr-g1.mp3']);
});

test('a reshuffle at the end of the order does not open on the track just played', async () => {
  // the first order is the listed one (two HIGH draws); the second draws 0 then HIGH, which would open on the third track and
  // lay the order out as g3 g2 g1. Playing the same track again is a no-op on the player, so only the tracks after it tell.
  const c = await start({ gameplay: 3, random: dice(HIGH, HIGH, 0, HIGH) });
  c.music.cue('gameplay');
  assert.deepEqual(await playThrough(c, 6), [...trackNames(3), 'fr-g2.mp3', 'fr-g3.mp3', 'fr-g1.mp3']);
});

test('the next track fades in about 2 s before the end of the current one', async () => {
  const c = await start({ gameplay: 2 });
  c.music.cue('gameplay');
  const crossfadeSec = 2, beforeCrossfadeSec = 3, insideCrossfadeSec = 1.5; // the latter two count back from the track's end
  await c.advance(CUE_SETTLE_SEC);
  const first = c.names();
  assert.equal(first.length, 1);
  await c.advance(DURATION - CUE_SETTLE_SEC - beforeCrossfadeSec);
  assert.equal(c.names().length, 1);
  await c.advance(beforeCrossfadeSec - insideCrossfadeSec);
  assert.equal(c.names().length, 2, `crossfading: ${c.names()}`);
  const incoming = c.els.find((e) => !e.paused && e.currentTime < DURATION / 2);
  assert.ok(incoming.currentTime < crossfadeSec, 'the new track has just started');
  await c.advance(CUE_SETTLE_SEC);
  assert.equal(c.names().length, 1);
  assert.notEqual(c.names()[0], first[0]);
});

test('a boss interrupts and the rotation resumes with the next track, not the interrupted one', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(INTO_TRACK_SEC);
  const [interrupted] = c.names();
  c.music.cue('boss:gunship');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-boss.mp3']);
  assert.ok(c.els.find((e) => e.src.endsWith('fr-boss.mp3')).loop, 'the boss track loops');
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  const [resumed] = c.names();
  assert.notEqual(resumed, interrupted);
  assert.ok(c.els.find((e) => e.src.endsWith(resumed)).currentTime <= CUE_SETTLE_SEC, 'the next track starts from 0');
  assert.ok(!c.els.some((e) => !e.paused && e.src.endsWith(interrupted)), 'the interrupted track is not restarted');
});

test('game over and the start page stop the rotation; a new run starts a fresh one', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(INTO_TRACK_SEC);
  const [before] = c.names();
  c.music.cue('gameover');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-gameover.mp3']);
  await c.advance(DURATION * 2);                     // far longer than any track: nothing rotates in
  assert.deepEqual(c.names(), ['fr-gameover.mp3']);
  c.music.cue('start');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-start.mp3']);
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  const [after] = c.names();
  assert.ok(after.startsWith('fr-g'));
  assert.notEqual(after, before, 'a new run does not start on the track played last');
});

test('a new run starts at a random rotation track', async () => {
  const firsts = new Set();
  for (const r of [0, 0.34, 0.67, 0.99]) {
    const c = await start({ gameplay: 3, random: () => r });
    c.music.cue('gameplay');
    await c.advance(CUE_SETTLE_SEC);
    firsts.add(c.names()[0]);
  }
  assert.ok(firsts.size >= 2, `starts vary with the dice: ${[...firsts]}`);
});

test('with one gameplay track it loops', async () => {
  const c = await start({ gameplay: 1 });
  c.music.cue('gameplay');
  const marginSec = 5; // well past the fade-in, and later well past the loop point
  await c.advance(marginSec);
  const el = c.els.find((e) => !e.paused && isBlob(e.src));
  assert.ok(el.loop);
  await c.advance(DURATION + marginSec);
  assert.equal(c.names().length, 1);
});

const mp3s = (c) => c.fetched.filter((u) => u.endsWith('.mp3')).map((u) => lastSegment(u));

test('the start page cue fetches the start page track and no gameplay track ahead of time', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('start');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(mp3s(c), ['fr-start.mp3']);
});

test('no track is fetched before the first gesture or the game\'s load, and the start page track once after the load', async () => {
  const c = await setup({ gameplay: 2 });
  c.music.init({ manifestUrl: 'music/tracks.json', thin: thinConnection });
  c.music.cue('start');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(mp3s(c), [], 'nothing while the game is still downloading');
  c.music.allowFetchAhead();
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(mp3s(c), ['fr-start.mp3']);
  c.music.allowFetchAhead();
  await c.advance(1);
  assert.deepEqual(mp3s(c), ['fr-start.mp3'], 'asked once');
});

test('the first gesture fetches the start page track and starts it at once, when it comes before the game\'s load', async () => {
  let unlock;
  const c = await setup({ gameplay: 2 });
  window.addEventListener = (ev, fn) => { if (fn.name === 'unlock') unlock ??= fn; };
  c.music.init({ manifestUrl: 'music/tracks.json', thin: thinConnection });
  c.music.cue('start');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(mp3s(c), []);
  unlock();
  await c.flush();
  assert.deepEqual(mp3s(c), ['fr-start.mp3']);
  assert.deepEqual(c.starts, ['fr-start.mp3'], 'playing without waiting on the clock');
  c.music.allowFetchAhead();
  await c.advance(1);
  assert.deepEqual(mp3s(c), ['fr-start.mp3'], 'the load asks for nothing more');
});

test('a start page cue that comes after the game\'s load, with the manifest still on its way, is fetched once the manifest is in', async () => {
  const c = await setup({ gameplay: 2 });
  c.music.init({ manifestUrl: 'music/tracks.json', thin: thinConnection });
  c.music.allowFetchAhead();
  c.music.cue('start');
  await c.advance(1);
  assert.deepEqual(mp3s(c), ['fr-start.mp3']);
});

test('the page lets the music fetch ahead only once the game has loaded', () => {
  const main = readFileSync(wwwroot + 'main.js', 'utf8');
  const loaded = main.indexOf('= await runtimeReady;');
  const allowed = main.indexOf('music?.allowFetchAhead()');
  assert.ok(loaded > 0 && allowed > loaded, 'allowFetchAhead is called after the runtime is ready');
  assert.equal(main.split('allowFetchAhead').length, 2, 'and nowhere else');
});

test('on a data-saver connection nothing is fetched before the first gesture, only the track list', async () => {
  const c = await setup({ gameplay: 2, connection: { saveData: true } });
  c.music.init({ manifestUrl: 'music/tracks.json', thin: thinConnection });
  c.music.cue('start');
  c.music.allowFetchAhead();
  await c.advance(1);
  assert.deepEqual(mp3s(c), []);
});

test('gameplay fetches its own track and the next one only, not the boss or game over tracks', async () => {
  const c = await start({ gameplay: 4 });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  const got = mp3s(c).filter((x) => x !== 'fr-start.mp3');
  assert.equal(got.length, 2, `current and next only: ${got}`);
  assert.ok(got.every((x) => x.startsWith('fr-g')), String(got));
});

test('the boss and game over tracks are fetched when they are cued', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  assert.ok(!mp3s(c).includes('fr-boss.mp3'));
  c.music.cue('boss:gunship');
  await c.advance(CUE_SETTLE_SEC);
  assert.ok(mp3s(c).includes('fr-boss.mp3'));
  assert.ok(!mp3s(c).includes('fr-gameover.mp3'));
  c.music.cue('gameover');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-gameover.mp3']);
});

for (const connection of [{ saveData: true }, { effectiveType: 'slow-2g' }, { effectiveType: '2g' }, { effectiveType: '3g' }]) {
  test(`nothing is fetched ahead on ${JSON.stringify(connection)}, but a cued track still plays`, async () => {
    const c = await start({ gameplay: 3, connection });
    assert.deepEqual(mp3s(c), [], 'no start page track up front');
    c.music.cue('gameplay');
    await c.advance(CUE_SETTLE_SEC);
    assert.equal(mp3s(c).length, 1, `only the playing track: ${mp3s(c)}`);
    assert.equal(c.names().length, 1);
  });
}

test('a 4g connection without data saver still fetches the next track', async () => {
  const c = await start({ gameplay: 3, connection: { saveData: false, effectiveType: '4g' } });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  assert.equal(mp3s(c).filter((x) => x.startsWith('fr-g')).length, 2);
});

test('a rotation track that has been played through has its blob URL revoked', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  const [first] = c.names();
  assert.deepEqual(c.revoked, []);
  await c.advance(DURATION);                         // past the crossfade, which is when the outgoing track is released
  assert.deepEqual(c.revoked, [first]);
});

test('a start page, boss or game over track that has faded out has its blob URL revoked, and is fetched again when cued again', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('start');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.revoked, ['fr-start.mp3'], 'the start page track went with its fade');
  c.music.cue('boss:gunship');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('gameover');
  await c.advance(CUE_SETTLE_SEC);
  assert.ok(c.revoked.includes('fr-boss.mp3'), String(c.revoked));
  c.music.cue('start');
  await c.advance(CUE_SETTLE_SEC);
  assert.ok(c.revoked.includes('fr-gameover.mp3'), String(c.revoked));
  assert.deepEqual(c.names(), ['fr-start.mp3']);
  assert.equal(mp3s(c).filter((x) => x === 'fr-start.mp3').length, 2, 'the start page track was fetched afresh');
});

test('a muted start page track keeps its blob, so unmuting plays it without a second download', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('start');
  await c.advance(CUE_SETTLE_SEC);
  c.music.setMuted(true);
  await c.advance(1);
  assert.deepEqual(c.names(), []);
  assert.deepEqual(c.revoked, []);
  c.music.setMuted(false);
  await c.advance(1);
  assert.deepEqual(c.names(), ['fr-start.mp3']);
  assert.deepEqual(mp3s(c), ['fr-start.mp3']);
});

test('a hidden tab pauses the music where it is and a return carries on, without rotating in between', async () => {
  const c = await start({ gameplay: 3 });
  c.music.cue('gameplay');
  await c.advance(INTO_TRACK_SEC);
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

// ---------------------------------------------------------------- a track for every boss

/** A manifest written in the test: the shared boss track, two keyed ones, and the other roles. */
const keyed = (extra = []) => [
  { file: 'fr-start.mp3', role: 'start' },
  { file: 'fr-g1.mp3', role: 'gameplay' }, { file: 'fr-g2.mp3', role: 'gameplay' }, { file: 'fr-g3.mp3', role: 'gameplay' },
  { file: 'fr-boss.mp3', role: 'boss' },
  { file: 'fr-boss-bulwark.mp3', role: 'boss', boss: 'bulwark' },
  { file: 'fr-boss-thread.mp3', role: 'boss', boss: 'thread' },
  { file: 'fr-gameover.mp3', role: 'gameover' },
  ...extra,
];

test('a boss cue plays the track whose boss field is the cued key', async () => {
  const c = await start({ manifest: keyed() });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('boss:bulwark');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-boss-bulwark.mp3']);
  assert.ok(c.els.find((e) => e.src.endsWith('fr-boss-bulwark.mp3')).loop, 'the boss track loops');
  c.music.cue('boss:thread');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-boss-thread.mp3'], 'a different boss swaps the track');
});

test('a boss with no keyed track plays the shared track, not a keyed boss track', async () => {
  const draws = 4; // dice values spread evenly inside (0, 1)
  for (let seed = 1; seed <= draws; seed++) {
    const c = await start({ manifest: keyed(), random: () => seed / (draws + 1) });
    c.music.cue('gameplay');
    await c.advance(CUE_SETTLE_SEC);
    c.music.cue('boss:gunship');
    await c.advance(CUE_SETTLE_SEC);
    assert.deepEqual(c.names(), ['fr-boss.mp3'], `seed ${seed}`);
  }
});

test('with several shared tracks only a shared track plays for an unkeyed boss', async () => {
  for (const r of [0, 0.4, 0.99]) {
    const c = await start({ manifest: keyed([{ file: 'fr-boss-two.mp3', role: 'boss' }]), random: () => r });
    c.music.cue('boss:doomscroll');
    await c.advance(CUE_SETTLE_SEC);
    assert.ok(['fr-boss.mp3', 'fr-boss-two.mp3'].includes(c.names()[0]), c.names());
  }
});

test('with no shared track a boss with no keyed track plays any boss track, never a gameplay one', async () => {
  const manifest = keyed().filter((t) => t.file !== 'fr-boss.mp3');
  for (const r of [0, 0.3, 0.6, 0.99]) {
    const c = await start({ manifest, random: () => r });
    c.music.cue('gameplay');
    await c.advance(CUE_SETTLE_SEC);
    c.music.cue('boss:mirror');
    await c.advance(CUE_SETTLE_SEC);
    assert.equal(c.names().length, 1);
    assert.ok(['fr-boss-bulwark.mp3', 'fr-boss-thread.mp3'].includes(c.names()[0]), c.names());
  }
});

test('with no boss track at all a boss cue leaves the music as it was, and never plays a gameplay track for it', async () => {
  const manifest = keyed().filter((t) => t.role !== 'boss');
  const c = await start({ manifest });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  const [playing] = c.names();
  c.music.cue('boss:bulwark');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), [playing]);
  assert.equal(c.starts.length, 1);
});

test('after a boss cue the gameplay rotation goes on where it was', async () => {
  const c = await start({ manifest: keyed() });
  c.music.cue('gameplay');
  await c.advance(INTO_TRACK_SEC);
  const [interrupted] = c.names();
  c.music.cue('boss:bulwark');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('boss:thread');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  const [resumed] = c.names();
  assert.ok(resumed.startsWith('fr-g'));
  assert.notEqual(resumed, interrupted);
  assert.ok(c.els.find((e) => e.src.endsWith(resumed)).currentTime <= CUE_SETTLE_SEC, 'the next track starts from 0');
  assert.ok(!c.els.some((e) => !e.paused && e.src.endsWith(interrupted)));
});

test('a boss returning on a later lap cues its own track again', async () => {
  const c = await start({ manifest: keyed() });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('boss:bulwark');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('boss:bulwark');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-boss-bulwark.mp3']);
});

test('the bare boss cue still means a shared track', async () => {
  const c = await start({ manifest: keyed() });
  c.music.cue('boss');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-boss.mp3']);
});

test('the hint fetches the named boss track without playing it, and the cue then plays that same track', async () => {
  const c = await start({ manifest: keyed() });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  const playing = c.names();
  c.music.cue('ahead:boss:bulwark');
  await c.advance(CUE_SETTLE_SEC);
  assert.ok(mp3s(c).includes('fr-boss-bulwark.mp3'), 'fetched');
  assert.deepEqual(c.names(), playing, 'but nothing changed');
  assert.ok(!c.starts.includes('fr-boss-bulwark.mp3'));
  const before = mp3s(c).filter((x) => x === 'fr-boss-bulwark.mp3').length;
  c.music.cue('boss:bulwark');
  await c.advance(CUE_SETTLE_SEC);
  assert.deepEqual(c.names(), ['fr-boss-bulwark.mp3']);
  assert.equal(mp3s(c).filter((x) => x === 'fr-boss-bulwark.mp3').length, before, 'not fetched twice');
});

test('the hint for a boss with no keyed track fetches the shared track', async () => {
  const c = await start({ manifest: keyed() });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('ahead:boss:gunship');
  await c.advance(CUE_SETTLE_SEC);
  assert.ok(mp3s(c).includes('fr-boss.mp3'));
  assert.ok(!mp3s(c).includes('fr-boss-bulwark.mp3'));
  assert.ok(!c.starts.includes('fr-boss.mp3'));
});

test('the hint is not a state: it neither replaces the wanted cue nor stops a rotation hand-over', async () => {
  const c = await start({ manifest: keyed() });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('ahead:boss:thread');
  await c.advance(DURATION);
  assert.ok(c.starts.filter((x) => x.startsWith('fr-g')).length >= 2);
  assert.ok(!c.starts.some((x) => x.includes('boss')));
});

test('a hint that comes before the manifest is fetched once it has loaded', async () => {
  let unlock;
  const c = await setup({ manifest: keyed() });
  window.addEventListener = (ev, fn) => { if (fn.name === 'unlock') unlock ??= fn; };
  c.music.init({ manifestUrl: 'music/tracks.json', thin: thinConnection });
  c.music.cue('ahead:boss:thread');
  c.music.cue('gameplay');
  unlock();
  await c.advance(CUE_SETTLE_SEC);
  assert.ok(mp3s(c).includes('fr-boss-thread.mp3'));
  assert.ok(c.names().every((x) => x.startsWith('fr-g')));
});

for (const connection of [{ saveData: true }, { effectiveType: '3g' }]) {
  test(`the hint fetches nothing on ${JSON.stringify(connection)}, and the cue still plays the track`, async () => {
    const c = await start({ manifest: keyed(), connection });
    c.music.cue('gameplay');
    await c.advance(CUE_SETTLE_SEC);
    c.music.cue('ahead:boss:thread');
    await c.advance(CUE_SETTLE_SEC);
    assert.ok(!mp3s(c).includes('fr-boss-thread.mp3'));
    c.music.cue('boss:thread');
    await c.advance(CUE_SETTLE_SEC);
    assert.deepEqual(c.names(), ['fr-boss-thread.mp3']);
  });
}

test('a hint for a boss with nothing to play is ignored', async () => {
  const c = await start({ manifest: keyed().filter((t) => t.role !== 'boss') });
  c.music.cue('gameplay');
  await c.advance(CUE_SETTLE_SEC);
  c.music.cue('ahead:boss:thread');
  c.music.cue('ahead:');
  c.music.cue('ahead:boss:');
  await c.advance(CUE_SETTLE_SEC);
  assert.equal(c.names().length, 1);
});

test('the manifest parses with or without boss fields, in the page and in the bundle', async () => {
  const { parseManifest } = (await setup()).music;
  const plain = [{ file: 'a.mp3', role: 'boss' }, { file: 'b.mp3', role: 'gameplay' }];
  assert.deepEqual(parseManifest(plain, 'https://x.test/m/tracks.json').map((t) => t.boss ?? null), [null, null]);
  const withKeys = parseManifest(keyed(), 'https://x.test/m/tracks.json');
  assert.deepEqual(withKeys.filter((t) => t.role === 'boss').map((t) => t.boss ?? null), [null, 'bulwark', 'thread']);
  assert.equal(parseManifest([{ file: 'a.mp3', role: 'boss', boss: 7 }, { file: 'b.mp3', role: 'gameplay', boss: 'thread' }])
    .filter((t) => t.boss).length, 0, 'a boss field that is not a name, or on another role, is ignored');
  const bundle = readMusicManifest([
    { role: 'start', url: 'https://x.test/a.mp3' }, { role: 'gameplay', url: 'https://x.test/b.mp3' },
    { role: 'boss', url: 'https://x.test/c.mp3' }, { role: 'boss', url: 'https://x.test/d.mp3', boss: 'thread' },
    { role: 'gameover', url: 'https://x.test/e.mp3' },
  ]);
  assert.deepEqual(bundle.filter((t) => t.role === 'boss').map((t) => t.boss ?? null), [null, 'thread']);
  assert.throws(() => readMusicManifest([{ role: 'boss', url: 'https://x.test/c.mp3', boss: 5 }]), /boss/);
});

test('the shipped tracks.json parses in the page and has only well-formed boss fields', async () => {
  const { parseManifest } = (await setup()).music;
  const list = JSON.parse(readFileSync(wwwroot + 'music/tracks.json', 'utf8'));
  const tracks = parseManifest(list, 'https://example.test/raider/music/tracks.json');
  assert.equal(tracks.length, list.length);
  for (const t of list) if ('boss' in t) assert.ok(t.role === 'boss' && /^[a-z]+$/.test(t.boss), JSON.stringify(t));
  assert.ok(tracks.some((t) => t.role === 'boss' && !t.boss), 'a shared boss track');
});
