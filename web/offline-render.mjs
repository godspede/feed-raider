// A small offline renderer for the Web Audio subset sfx.js uses (oscillators, buffer sources, gain, biquad, wave shaper, stereo
// panner), so node can measure how loud each sound is. It is an approximation of a browser (naive oscillators, no
// oversampling), good for a couple of dB. Usage: const m = await measure(sfxModule, 'kill', { size: 0 }).
import assert from 'node:assert/strict';

const SR = 44100;
const AUDIBLE = 0.003;   // -50 dBFS: the "audible span" runs from the first to the last sample above this
const COEFF_INTERVAL = 16;   // samples between recomputing a filter's coefficients from its (possibly ramping) frequency
const MAX_FILTER_HZ_FRACTION = 0.45;   // a filter's frequency is held under this share of the sample rate, short of Nyquist where the formulas blow up
const RAMP_FLOOR = 1e-9;   // an exponential ramp cannot start or end at 0, so both ends are held at least this high
const MIN_Q = 0.05;   // the lowest Q used, so the bandwidth term never reaches zero
const CENTS_PER_OCTAVE = 1200;   // an oscillator's detune is in cents
const RENDER_SECONDS = 3.2;   // the length of the buffer each sound is rendered into
const CLOCK_START_S = 10;   // the fake clock starts above 0, as a running browser context does, so a sound that schedules from time 0 instead of the current time renders silent

// a deterministic Math.random for the noise buffers
function seeded(seed = 1) {
  const LCG_MUL = 1664525, LCG_ADD = 1013904223, UINT32 = 4294967296;   // a linear congruential generator (Numerical Recipes)
  let s = seed >>> 0;
  return () => { s = (Math.imul(s, LCG_MUL) + LCG_ADD) >>> 0; return s / UINT32; };
}

class Param {
  constructor(value = 0) { this.value = value; this.events = []; }
  setValueAtTime(v, t) { this.events.push({ k: 'set', v, t }); return this; }
  linearRampToValueAtTime(v, t) { this.events.push({ k: 'lin', v, t }); return this; }
  exponentialRampToValueAtTime(v, t) { this.events.push({ k: 'exp', v, t }); return this; }
  cancelScheduledValues() { this.events = []; return this; }
  // the parameter's value at every sample from time t0
  render(n, t0) {
    const out = new Float32Array(n), ev = this.events.map((e, i) => ({ ...e, i })).sort((a, b) => a.t - b.t || a.i - b.i);
    let cur = this.value, prevT = -Infinity, j = 0, from = cur;
    for (let i = 0; i < n; i++) {
      const t = t0 + i / SR;
      while (j < ev.length && ev[j].k === 'set' && ev[j].t <= t) { cur = from = ev[j].v; prevT = ev[j].t; j++; }
      if (j < ev.length && ev[j].k !== 'set') {
        const e = ev[j];
        if (t >= e.t) { cur = from = e.v; prevT = e.t; j++; }
        else {
          const p = (t - prevT) / (e.t - prevT);
          if (!Number.isFinite(p)) cur = from;
          else cur = e.k === 'lin' ? from + (e.v - from) * p : from * Math.pow(Math.max(e.v, RAMP_FLOOR) / Math.max(from, RAMP_FLOOR), p);
        }
      }
      out[i] = cur;
    }
    return out;
  }
}

function makeCtx() {
  const N = Math.ceil(RENDER_SECONDS * SR);
  const ctx = { currentTime: CLOCK_START_S, sampleRate: SR, state: 'running' };
  const memo = new Map();
  const mk = (kind, extra) => {
    const n = { kind, inputs: [], ...extra };
    n.connect = (to) => { to.inputs.push(n); return to; };
    n.disconnect = () => {};
    return n;
  };
  const sum = (n) => {
    const out = new Float32Array(N);
    for (const src of n.inputs) { const x = render(src); for (let i = 0; i < N; i++) out[i] += x[i]; }
    return out;
  };
  const active = (n) => [Math.max(0, Math.round((n.t0 - ctx.currentTime) * SR)), Math.min(N, Math.round((n.t1 - ctx.currentTime) * SR))];
  function render(n) {
    if (memo.has(n)) return memo.get(n);
    let out;
    switch (n.kind) {
      case 'gain': { const x = sum(n), g = n.gain.render(N, ctx.currentTime); out = x.map((v, i) => v * g[i]); break; }
      case 'pan': out = sum(n).map((v) => v * Math.SQRT1_2); break;   // pan 0, equal power: each channel carries 0.707
      case 'shaper': {
        const c = n.curve, x = sum(n);
        out = x.map((v) => { if (!c) return v; const p = (Math.max(-1, Math.min(1, v)) + 1) / 2 * (c.length - 1), i = Math.floor(p); return c[i] + (c[Math.min(c.length - 1, i + 1)] - c[i]) * (p - i); });
        break;
      }
      case 'osc': {
        out = new Float32Array(N);
        const [a, b] = active(n), f = n.frequency.render(N, ctx.currentTime), dt = Math.pow(2, n.detune.value / CENTS_PER_OCTAVE);
        let ph = 0;
        for (let i = a; i < b; i++) {
          ph += f[i] * dt / SR; ph -= Math.floor(ph);
          out[i] = n.type === 'square' ? (ph < 0.5 ? 1 : -1) : n.type === 'sawtooth' ? 2 * ph - 1 : n.type === 'triangle' ? 4 * Math.abs(ph - 0.5) - 1 : Math.sin(2 * Math.PI * ph);
        }
        break;
      }
      case 'src': {
        out = new Float32Array(N);
        const [a, b] = active(n), d = n.buffer.data;
        let k = Math.floor(n.offset * SR) % d.length;
        for (let i = a; i < b; i++) { out[i] = d[k]; k = (k + 1) % d.length; }
        break;
      }
      case 'filter': {
        const x = sum(n), f = n.frequency.render(N, ctx.currentTime), q = n.Q.value;
        out = new Float32Array(N);
        let x1 = 0, x2 = 0, y1 = 0, y2 = 0, b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
        for (let i = 0; i < N; i++) {
          if (i % COEFF_INTERVAL === 0) {
            const w = 2 * Math.PI * Math.min(f[i], SR * MAX_FILTER_HZ_FRACTION) / SR, c = Math.cos(w), al = Math.sin(w) / (2 * Math.max(q, MIN_Q)), a0 = 1 + al;
            if (n.type === 'lowpass') { b0 = (1 - c) / 2 / a0; b1 = (1 - c) / a0; b2 = b0; }
            else if (n.type === 'highpass') { b0 = (1 + c) / 2 / a0; b1 = -(1 + c) / a0; b2 = b0; }
            else { b0 = al / a0; b1 = 0; b2 = -b0; }   // bandpass
            a1 = -2 * c / a0; a2 = (1 - al) / a0;
          }
          const y = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
          x2 = x1; x1 = x[i]; y2 = y1; y1 = y; out[i] = y;
        }
        break;
      }
      default: throw new Error('cannot render ' + n.kind);
    }
    memo.set(n, out);
    return out;
  }
  ctx.render = render;
  ctx.createGain = () => mk('gain', { gain: new Param(1) });
  ctx.createStereoPanner = () => mk('pan', { pan: new Param(0) });
  ctx.createWaveShaper = () => mk('shaper', { curve: null, oversample: 'none' });
  ctx.createBiquadFilter = () => mk('filter', { type: 'lowpass', frequency: new Param(350), Q: new Param(1) });
  ctx.createOscillator = () => mk('osc', {
    type: 'sine', frequency: new Param(440), detune: new Param(0), t0: 0, t1: 0,
    start(t) { this.t0 = t; }, stop(t) { this.t1 = t; },
  });
  ctx.createBufferSource = () => mk('src', {
    buffer: null, loop: false, offset: 0, t0: 0, t1: 0,
    start(t, off = 0) { this.t0 = t; this.offset = off; }, stop(t) { this.t1 = t; },
  });
  ctx.createBuffer = (ch, n) => { const d = new Float32Array(n); return { getChannelData: () => d, data: d, length: n }; };
  return ctx;
}

/** Plays one sound through `sfx` (an imported sfx.js) into a unity master gain and measures what comes out. */
export async function measure(sfx, name, { size = 0 } = {}) {
  const realRandom = Math.random;
  Math.random = seeded(7);
  try {
    const ctx = makeCtx(), node = ctx.createGain();
    sfx.init({ master: { context: ctx, node, gain: 1 } });
    sfx.play(name, 0, 0, size);
    const x = ctx.render(node);
    let first = -1, last = -1, peak = 0;
    for (let i = 0; i < x.length; i++) {
      const a = Math.abs(x[i]);
      if (a > peak) peak = a;
      if (a >= AUDIBLE) { if (first < 0) first = i; last = i; }
    }
    assert.ok(first >= 0, `${name}: silent`);
    let e = 0;
    for (let i = first; i <= last; i++) e += x[i] * x[i];
    const db = (v) => 20 * Math.log10(v);
    return { peak: db(peak), rms: db(Math.sqrt(e / (last - first + 1))), seconds: (last - first + 1) / SR };
  } finally {
    Math.random = realRandom;
  }
}
