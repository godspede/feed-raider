// Smoke test for wwwroot/sfx.js with a stubbed AudioContext: every sound builds, every node it makes is connected on, every
// source is started and stopped, and no sound outlasts its budget. Not part of `npm test`: a manual check, run from the repo root with `node --test web/*.test.mjs`.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';
import { measure } from './offline-render.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const sfx = await import(pathToFileURL(join(here, '..', 'src', 'FeedRaider.Web', 'wwwroot', 'sfx.js')).href);

// the sound names the C# side emits (SfxNames in SfxBus.cs)
const NAMES = ['shot', 'hit', 'kill', 'boom', 'hurt', 'death', 'bomb', 'comm', 'like', 'nolike', 'darkness', 'clear', 'pickup_shield', 'pickup_bomb', 'pickup_power', 'shield_hit'];
const LONG = { kill: 1.0, boom: 2.4, bomb: 2.0, darkness: 2.6 };   // everything else stays under 0.6 s
// the loudness each sound should have over its audible span (RMS dBFS at the default level, unity master), measured offline by offline-render.mjs
const RMS = { shot: -27, hit: -24, kill: -20, hurt: -18, death: -18, bomb: -14, boom: -14, comm: -20, like: -20, clear: -20, nolike: -24, darkness: -18,
  pickup_shield: -20, pickup_bomb: -20, pickup_power: -20, shield_hit: -20 };
const NEW_PICKUPS = ['pickup_shield', 'pickup_bomb', 'pickup_power', 'shield_hit'];   // these also keep their peaks under -4 dBFS

function makeCtx({ panner = true } = {}) {
  const ctx = {
    currentTime: 10, sampleRate: 8000, state: 'running', nodes: [],
    destination: { kind: 'destination', connect() {}, disconnect() {} },
  };
  const param = (value = 0) => ({
    value,
    setValueAtTime() { return this; }, linearRampToValueAtTime() { return this; }, exponentialRampToValueAtTime() { return this; },
    setTargetAtTime() { return this; }, cancelScheduledValues() { return this; },
  });
  const node = (kind, extra = {}) => {
    const n = { kind, outputs: [], inputs: 0, disconnected: false, ...extra };
    n.connect = (to) => { n.outputs.push(to); if (to) to.inputs = (to.inputs ?? 0) + 1; return to; };
    n.disconnect = () => { n.disconnected = true; };
    ctx.nodes.push(n);
    return n;
  };
  ctx.createGain = () => node('gain', { gain: param(1) });
  ctx.createOscillator = () => node('osc', {
    frequency: param(440), detune: param(0), type: 'sine', started: false, stopAt: null,
    start(t) { assert.ok(Number.isFinite(t)); this.started = true; },
    stop(t) { assert.ok(this.started, 'stop before start'); this.stopAt = t; },
  });
  ctx.createBufferSource = () => node('src', {
    buffer: null, loop: false, started: false, stopAt: null,
    start(t, off) { assert.ok(this.buffer, 'noise buffer missing'); assert.ok(Number.isFinite(t) && Number.isFinite(off)); this.started = true; },
    stop(t) { assert.ok(this.started, 'stop before start'); this.stopAt = t; },
  });
  ctx.createBiquadFilter = () => node('filter', { type: 'lowpass', frequency: param(350), Q: param(1) });
  ctx.createWaveShaper = () => node('shaper', { curve: null });
  if (panner) ctx.createStereoPanner = () => node('pan', { pan: param(0) });
  let buffers = 0;
  ctx.createBuffer = (ch, n, rate) => { buffers++; assert.ok(n > 0 && rate > 0); const d = new Float32Array(n); return { getChannelData: () => d }; };
  ctx.buffers = () => buffers;
  return ctx;
}

function setup(opts) {
  const ctx = makeCtx(opts);
  const masterNode = ctx.createGain();
  const master = { context: ctx, node: masterNode, gain: 0.5, muted: false, volume: 0.5 };
  const ducks = [];
  assert.equal(sfx.init({ master, duck: (db, ms) => ducks.push([db, ms]) }), true);
  return { ctx, master, masterNode, ducks };
}

// everything a sound created must lead to the master node, and every source must be started and stopped
function checkGraph(ctx, masterNode, name) {
  const reaches = (n, seen = new Set()) => n === masterNode || (!seen.has(n) && seen.add(n) && n.outputs.some((o) => o && reaches(o, seen)));
  for (const n of ctx.nodes) {
    if (n === masterNode) continue;
    assert.ok(n.outputs.length > 0, `${name}: a ${n.kind} node is not connected on`);
    assert.ok(reaches(n), `${name}: a ${n.kind} node does not reach the master gain`);
    if (n.kind === 'osc' || n.kind === 'src') {
      assert.ok(n.started && n.stopAt !== null, `${name}: a ${n.kind} was not started and stopped`);
      const len = n.stopAt - ctx.currentTime;
      assert.ok(len > 0 && len <= (LONG[name] ?? 0.6) + 0.05, `${name}: a source runs ${len.toFixed(2)} s`);
    }
  }
}

test('the sound table is the list the game emits', () => {
  assert.deepEqual(Object.keys(sfx.sounds).sort(), [...NAMES].sort());
});

for (const panner of [true, false]) {
  for (const name of NAMES) {
    test(`${name} builds and connects (${panner ? 'stereo panner' : 'no panner'})`, async () => {
      const { ctx, masterNode } = setup({ panner });
      const before = ctx.nodes.length;
      const warn = console.warn;
      const warnings = [];
      console.warn = (...a) => warnings.push(a.join(' '));
      try { sfx.play(name, -0.7); } finally { console.warn = warn; }
      assert.deepEqual(warnings, []);
      assert.ok(ctx.nodes.length > before + 3, 'nothing was built');
      assert.equal(ctx.buffers(), 2, 'noise buffers are built once, white and brown');
      checkGraph(ctx, masterNode, name);
    });
  }
}

test('nothing plays before the unlock, when muted, off, or for an unknown name', async () => {
  const { ctx, master } = setup();
  const n = ctx.nodes.length;
  ctx.state = 'suspended'; sfx.play('boom'); assert.equal(ctx.nodes.length, n);
  ctx.state = 'running'; master.gain = 0; sfx.play('boom'); assert.equal(ctx.nodes.length, n);
  master.gain = 0.5; sfx.play('nope'); assert.equal(ctx.nodes.length, n);
  master.node = null; sfx.play('boom'); assert.equal(ctx.nodes.length, n);
  assert.equal(sfx.init({ master: { ...master, node: ctx.createGain() }, off: true }), false);
  const m = ctx.nodes.length; sfx.play('boom'); assert.equal(ctx.nodes.length, m);
  assert.equal(sfx.init({}), false);
});

test('a shot inside the gap is dropped, and the voices are capped at six (the oldest is stolen)', async () => {
  const { ctx } = setup();
  const oscs = () => ctx.nodes.filter((n) => n.kind === 'osc').length;
  sfx.play('shot'); const one = oscs();
  sfx.play('shot'); assert.equal(oscs(), one, 'second shot inside 60 ms was played');
  const sources = () => ctx.nodes.filter((n) => n.kind === 'osc' || n.kind === 'src');
  for (const n of ['kill', 'boom', 'hurt', 'death', 'bomb', 'comm', 'like', 'nolike']) sfx.play(n);
  const stopped = sources().filter((s) => s.stopAt !== null && s.stopAt < ctx.currentTime + 0.1);
  assert.ok(stopped.length > 0, 'the oldest voice was not stolen');
  const first = ctx.nodes.find((n) => n.kind === 'osc');
  assert.ok(first.stopAt < ctx.currentTime + 0.1, 'the first shot voice should have been cut short');
});

test('a failing build logs once and stays silent', async () => {
  const { ctx } = setup();
  ctx.createOscillator = () => { throw new Error('boom'); };
  const warn = console.warn; const seen = [];
  console.warn = (m) => seen.push(m);
  try { sfx.play('kill'); sfx.play('hurt'); sfx.play('death'); } finally { console.warn = warn; }
  assert.equal(seen.length, 1);
  assert.match(seen[0], /staying silent/);
});

test('the big sounds duck the music, the small ones do not, and a failing duck is harmless', async () => {
  const { ducks } = setup();
  for (const n of NAMES) sfx.play(n);
  assert.deepEqual(ducks.map(([db]) => db), [-6, -6, -6]);
  const { ctx } = setup();
  sfx.init({ master: { context: ctx, node: ctx.createGain(), gain: 0.5 }, duck: () => { throw new Error('no'); } });
  const warn = console.warn; const seen = [];
  console.warn = (m) => seen.push(m);
  try { sfx.play('bomb'); } finally { console.warn = warn; }
  assert.deepEqual(seen, []);
});

test('the combo step raises the kill and hit pitch by semitones, capped at +7', async () => {
  const freqs = (name, step) => {
    const { ctx } = setup();
    const seen = [];
    const make = ctx.createOscillator;
    ctx.createOscillator = () => { const o = make(); const f = o.frequency; f.setValueAtTime = (v) => { seen.push(v); return f; }; return o; };
    sfx.play(name, 0, step, 0);
    return seen;
  };
  for (const name of ['kill', 'hit']) {
    const base = freqs(name, 0)[0];
    assert.ok(Math.abs(freqs(name, 3)[0] / base - 2 ** (3 / 12)) < 1e-9, name + ' +3');
    assert.ok(Math.abs(freqs(name, 99)[0] / base - 2 ** (7 / 12)) < 1e-9, name + ' is capped at +7');
  }
  const shot = freqs('shot', 0).length;
  assert.ok(shot > 0);
});

test('every sound lands near its target loudness and leaves headroom (offline render)', async () => {
  for (const [name, target] of Object.entries(RMS)) {
    const m = await measure(sfx, name);
    assert.ok(Math.abs(m.rms - target) <= 2.5, `${name}: RMS ${m.rms.toFixed(1)} dBFS, wanted ${target} +-2.5`);
    assert.ok(m.peak <= -1, `${name}: peak ${m.peak.toFixed(1)} dBFS`);
  }
  for (const name of NEW_PICKUPS) {
    const m = await measure(sfx, name);
    assert.ok(m.peak <= -4, `${name}: peak ${m.peak.toFixed(1)} dBFS, wanted <= -4`);
    assert.ok(m.seconds >= 0.1 && m.seconds <= 0.6, `${name}: audible ${m.seconds.toFixed(2)} s`);
  }
  for (const name of ['bomb', 'boom']) {
    const m = await measure(sfx, name);
    assert.ok(m.peak >= -4 && m.seconds >= 1, `${name}: peak ${m.peak.toFixed(1)}, audible ${m.seconds.toFixed(2)} s`);
  }
  const kill = await measure(sfx, 'kill', { size: 1 });
  assert.ok(kill.seconds >= 0.3 && kill.seconds <= 0.6, `kill is audible for ${kill.seconds.toFixed(2)} s`);
});

test('the pickups are short rising arpeggios, power is the brightest, and the shield hit is a ping over a thud', async () => {
  const notes = (name) => {
    const { ctx } = setup();
    const seen = [];
    const make = ctx.createOscillator;
    ctx.createOscillator = () => { const o = make(); const f = o.frequency; const set = f.setValueAtTime; f.setValueAtTime = (v, t) => { seen.push([t, v, o.type]); return set.call(f, v, t); }; return o; };
    sfx.play(name, 0);
    return seen;
  };
  const lead = (name) => {
    const n = notes(name);
    const main = n.filter(([, , type]) => type === 'square' || (name === 'pickup_shield' && type === 'sine'));
    return main.sort((a, b) => a[0] - b[0]).map(([, f]) => f).filter((f) => f < 2100);
  };
  for (const name of ['pickup_shield', 'pickup_bomb', 'pickup_power']) {
    const l = lead(name);
    assert.ok(l.length >= 4, `${name} has ${l.length} notes`);
    const firsts = l.filter((f, i) => i === 0 || f > l[0]);
    assert.ok(firsts.some((f) => f >= l[0] * 1.5), `${name} does not rise`);
  }
  const mean = (name) => { const n = notes(name).map(([, f]) => f); return n.reduce((a, b) => a + b, 0) / n.length; };
  assert.ok(mean('pickup_power') > mean('pickup_shield') && mean('pickup_power') > mean('pickup_bomb'), 'power should be the brightest');
  const hit = notes('shield_hit').map(([, f]) => f);
  assert.ok(hit.some((f) => f > 1500) && hit.some((f) => f < 300), 'shield_hit wants a high ping and a low thud');
});
