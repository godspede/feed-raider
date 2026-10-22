// Sound effects, synthesised at runtime with Web Audio (oscillators, filtered noise, envelopes): no audio files.
// They play through music.js's master gain, so mute and volume apply, and nothing sounds before the first gesture (the master
// node and a running context only exist after music.js unlocked). An sfx bus (a gain into a soft limiter, a tanh wave shaper that
// tops out near -3 dBFS so a pile-up of voices cannot clip) sits under the master.
// At most MAX_VOICES sounds at once (the oldest is stolen) and a per-sound minimum gap. Nothing here may break the game: any
// failure leaves the effects silent and logs once.

const MAX_VOICES = 6;
const DEFAULT_LEVEL = 0.8;
// per-sound level trim, a multiplier on every tone and noise in the sound, set so each lands near its target loudness
const TRIM = { shot: 0.4, hit: 0.52, kill: 0.92, boom: 1.51, hurt: 1.37, death: 1, bomb: 1.67, comm: 2.68, like: 1.56, nolike: 1.34, darkness: 2.2, clear: 1.58, pickup_shield: 1.48, pickup_bomb: 1.4, pickup_power: 1.85, shield_hit: 0.97 };
const MAX_STEP = 7;                       // the combo pitch rise tops out at +7 semitones
// the sounds that duck the music under them, with the hold in ms (music.js ducks -6 dB, 40 ms in, 400 ms out)
const DUCKS = { bomb: 250, boom: 500, death: 400 };
// the fewest ms between two plays of a sound (the game rate-limits too; this protects the page from any caller)
const GAP_MS = { shot: 60, hit: 45, kill: 40, hurt: 90, like: 80, nolike: 150, bomb: 150, comm: 250, clear: 400, darkness: 1500, pickup_shield: 100, pickup_bomb: 100, pickup_power: 100, shield_hit: 90 };

let master = null, duck = null, off = true, warned = false, level = DEFAULT_LEVEL;
let bus = null, busCtx = null, whiteBuf = null, brownBuf = null;
// soft saturation, slope 1 at zero: also the limiter curve on the bus (it tops out at about -2.8 dBFS)
const SATURATE = Float32Array.from({ length: 1025 }, (_, i) => 0.9 * Math.tanh((i / 512 - 1) / 0.9));
const voices = [];
const lastAt = new Map();

function warnOnce(what, e) {
  if (warned) return;
  warned = true;
  console.warn(`sfx: ${what}${e ? ': ' + (e.message || e) : ''}; staying silent`);
}

// ---------------------------------------------------------------- the bus and the noise (built once per context)

function noiseBuffers(ctx) {
  const n = Math.floor(ctx.sampleRate * 1.5);
  const white = ctx.createBuffer(1, n, ctx.sampleRate), brown = ctx.createBuffer(1, n, ctx.sampleRate);
  const w = white.getChannelData(0), b = brown.getChannelData(0);
  let last = 0;
  for (let i = 0; i < n; i++) {
    const x = Math.random() * 2 - 1;
    w[i] = x;
    last = (last + 0.02 * x) / 1.02;
    b[i] = last * 3.5;
  }
  return [white, brown];
}

function getBus(ctx) {
  if (bus && busCtx === ctx) return bus;
  busCtx = ctx;
  [whiteBuf, brownBuf] = noiseBuffers(ctx);
  const gain = ctx.createGain();
  gain.gain.value = level;
  if (ctx.createWaveShaper) {
    // a limiter so several loud voices together do not clip; unlike a compressor it adds no unknown makeup gain
    const lim = ctx.createWaveShaper();
    lim.curve = SATURATE;
    gain.connect(lim);
    lim.connect(master.node);
  } else {
    gain.connect(master.node);
  }
  bus = gain;
  return bus;
}

// ---------------------------------------------------------------- voices and building blocks

/** One playing sound: its own gain (for stealing), pan, and the sources to stop. */
function makeVoice(ctx, pan, k = 1, trim = 1) {
  const out = ctx.createGain();
  let head = out;
  if (ctx.createStereoPanner) {
    const p = ctx.createStereoPanner();
    p.pan.value = Math.max(-1, Math.min(1, Number.isFinite(pan) ? pan : 0));
    out.connect(p);
    head = p;
  }
  head.connect(getBus(ctx));
  const v = {
    ctx, out, head, k, trim, sources: [], live: 0, end: 0,
    track(src, stopAt) {
      this.sources.push(src);
      this.live++;
      this.end = Math.max(this.end, stopAt);
      src.onended = () => { if (--this.live <= 0) this.release(); };
    },
    release() {
      const i = voices.indexOf(this);
      if (i >= 0) voices.splice(i, 1);
      try { this.head.disconnect(); this.out.disconnect(); } catch { /* already gone */ }
    },
    steal() {
      const t = ctx.currentTime;
      try {
        this.out.gain.cancelScheduledValues(t);
        this.out.gain.setValueAtTime(this.out.gain.value, t);
        this.out.gain.linearRampToValueAtTime(0, t + 0.012);
        for (const s of this.sources) { try { s.stop(t + 0.015); } catch { /* not started or already stopped */ } }
      } catch { /* the voice was already finished */ }
      this.release();
    },
  };
  return v;
}

const clampF = (f) => Math.max(20, f);

/** A pitched oscillator: frequency f0 -> f1 (exponential) over dur, peak volume vol, linear attack, exponential decay. */
function tone(v, { type = 'square', f0, f1 = f0, at = 0, dur = 0.1, vol = 0.2, attack = 0.004, lp = 0, detune = 0 }) {
  const { ctx } = v, t = ctx.currentTime + at, stop = t + dur + 0.02;
  const o = ctx.createOscillator(), g = ctx.createGain();
  o.type = type;
  o.frequency.setValueAtTime(clampF(f0 * v.k), t);
  if (f1 !== f0) o.frequency.exponentialRampToValueAtTime(clampF(f1 * v.k), t + dur);
  if (detune) o.detune.value = detune;
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(vol * v.trim, t + attack);
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  let head = o;
  if (lp) {
    const f = ctx.createBiquadFilter();
    f.type = 'lowpass'; f.frequency.value = lp;
    o.connect(f);
    head = f;
  }
  head.connect(g);
  g.connect(v.out);
  o.start(t);
  o.stop(stop);
  v.track(o, stop - ctx.currentTime);
}

/** Filtered noise: the filter sweeps f0 -> f1 over dur. buf is 'white' or 'brown'. */
function noise(v, { buf = 'white', type = 'lowpass', f0 = 2000, f1 = f0, q = 0.7, at = 0, dur = 0.1, vol = 0.2, attack = 0.002, shape = false }) {
  const { ctx } = v, t = ctx.currentTime + at, stop = t + dur + 0.02;
  const s = ctx.createBufferSource(), f = ctx.createBiquadFilter(), g = ctx.createGain();
  s.buffer = buf === 'brown' ? brownBuf : whiteBuf;
  s.loop = true;
  f.type = type; f.Q.value = q;
  f.frequency.setValueAtTime(clampF(f0 * v.k), t);
  if (f1 !== f0) f.frequency.exponentialRampToValueAtTime(clampF(f1 * v.k), t + dur);
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(vol * v.trim, t + attack);
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  s.connect(f);
  if (shape && ctx.createWaveShaper) {
    // a little grit: overdrive the filtered noise before its envelope
    const pre = ctx.createGain(), w = ctx.createWaveShaper();
    pre.gain.value = 4;
    w.curve = SATURATE;
    f.connect(pre); pre.connect(w); w.connect(g);
  } else {
    f.connect(g);
  }
  g.connect(v.out);
  s.start(t, Math.random());
  s.stop(stop);
  v.track(s, stop - ctx.currentTime);
}

/** A slow swell envelope on the whole voice: silence -> peak at `peakAt` -> silence at `dur`. */
function swell(v, peak, peakAt, dur) {
  const t = v.ctx.currentTime;
  v.out.gain.setValueAtTime(0.0001, t);
  v.out.gain.linearRampToValueAtTime(peak, t + peakAt);
  v.out.gain.linearRampToValueAtTime(0.0001, t + dur);
}

// ---------------------------------------------------------------- the sounds (A minor, saw and square, a little grit)

const detuneRand = () => 1 + (Math.random() - 0.5) * 0.08;

export const sounds = {
  // a short square zap that falls in pitch, a little different every time, over a soft low triangle for body
  shot(v) {
    const k = detuneRand();
    tone(v, { type: 'square', f0: 1250 * k, f1: 320 * k, dur: 0.07, vol: 0.5, lp: 4200 });
    tone(v, { type: 'triangle', f0: 190 * k, f1: 85, dur: 0.1, vol: 0.5 });
    noise(v, { type: 'highpass', f0: 5000, dur: 0.012, vol: 0.2 });
  },
  // a dry tick: a noise click over a short low thump
  hit(v) {
    noise(v, { type: 'bandpass', f0: 3200, q: 1.2, dur: 0.05, vol: 0.7 });
    tone(v, { type: 'triangle', f0: 260, f1: 110, dur: 0.07, vol: 0.7 });
    tone(v, { type: 'sine', f0: 140, f1: 80, dur: 0.06, vol: 0.5 });
  },
  // an explosion with weight: a sharp click, a sub thump falling 90 -> 48 Hz (longer and bigger for a bigger enemy), then a
  // saturated noise crunch sweeping down and a falling square
  kill(v, size) {
    noise(v, { type: 'highpass', f0: 3500, dur: 0.014, vol: 0.5 });
    tone(v, { type: 'sine', f0: 90, f1: 48, dur: 0.4 + 0.2 * size, vol: 0.8 + 0.2 * size, attack: 0.002 });
    noise(v, { type: 'lowpass', f0: 3800, f1: 220, q: 1, at: 0.01, dur: 0.7 + 0.2 * size, vol: 0.55 + 0.1 * size, shape: true });
    tone(v, { type: 'square', f0: 420, f1: 55, at: 0.01, dur: 0.45, vol: 0.3, lp: 1800 });
  },
  // the boss goes: three saturated crunches in a row, a sub drop you feel, a long rumble and a falling shimmer, about 2 s
  boom(v) {
    for (let i = 0; i < 3; i++) {
      noise(v, { type: 'lowpass', f0: 3400 - i * 600, f1: 200, q: 1, at: i * 0.2, dur: 0.5, vol: 0.9, shape: true });
      tone(v, { type: 'square', f0: 360 - i * 60, f1: 50, at: i * 0.2, dur: 0.4, vol: 0.25, lp: 1500 });
    }
    noise(v, { type: 'highpass', f0: 3500, dur: 0.02, vol: 0.5 });
    tone(v, { type: 'sine', f0: 95, f1: 32, at: 0.02, dur: 2.2, vol: 0.95 });
    noise(v, { buf: 'brown', type: 'lowpass', f0: 1700, f1: 60, at: 0.3, dur: 2, vol: 1, attack: 0.03, shape: true });
    tone(v, { type: 'sawtooth', f0: 120, f1: 26, at: 0.3, dur: 2, vol: 0.45, lp: 400 });
    tone(v, { type: 'square', f0: 1760, f1: 220, at: 0.45, dur: 1.4, vol: 0.1, lp: 3000 });
  },
  // the ship is hit: a harsh falling tritone buzz
  hurt(v) {
    tone(v, { type: 'sawtooth', f0: 330, f1: 90, dur: 0.3, vol: 0.55, lp: 1800 });
    tone(v, { type: 'sawtooth', f0: 466, f1: 128, dur: 0.3, vol: 0.35, lp: 1800 });
    tone(v, { type: 'sine', f0: 110, f1: 45, dur: 0.25, vol: 0.6 });
    noise(v, { type: 'bandpass', f0: 1400, f1: 400, q: 1, dur: 0.22, vol: 0.5, shape: true });
  },
  // the ship dies: a long square slide down with a wobble, a sub drop, under a saturated noise crash
  death(v) {
    tone(v, { type: 'square', f0: 640, f1: 38, dur: 0.56, vol: 0.45, lp: 2400 });
    tone(v, { type: 'sawtooth', f0: 320, f1: 30, dur: 0.56, vol: 0.4, lp: 900, detune: 40 });
    tone(v, { type: 'sine', f0: 100, f1: 34, dur: 0.58, vol: 0.7 });
    noise(v, { type: 'lowpass', f0: 4200, f1: 150, q: 1, dur: 0.57, vol: 0.75, shape: true });
  },
  // the bomb: a crack, a sub drop you feel, a noise wash that opens and then closes over a long tail, about 1.5 s
  bomb(v) {
    noise(v, { type: 'highpass', f0: 3000, dur: 0.02, vol: 0.5 });
    tone(v, { type: 'sine', f0: 170, f1: 34, dur: 1.9, vol: 1 });
    tone(v, { type: 'sawtooth', f0: 90, f1: 300, dur: 0.14, vol: 0.3, lp: 900 });
    noise(v, { buf: 'brown', type: 'lowpass', f0: 500, f1: 3600, dur: 0.2, vol: 0.9 });
    noise(v, { type: 'lowpass', f0: 3600, f1: 140, q: 0.9, at: 0.2, dur: 1.6, vol: 0.8, shape: true });
  },
  // the wingman's comm opens: a squelch, then three bandpassed radio beeps (low, high, mid), Star Fox style
  comm(v) {
    noise(v, { type: 'bandpass', f0: 2400, q: 2, dur: 0.035, vol: 0.4 });
    [[1320, 0.04], [1760, 0.04], [1480, 0.07]].reduce((at, [f, d]) => {
      tone(v, { type: 'square', f0: f, f1: f * 0.985, at, dur: d, vol: 0.35, attack: 0.002, lp: 2600 });
      return at + d + 0.02;
    }, 0.045);
    noise(v, { type: 'bandpass', f0: 2000, q: 2, at: 0.25, dur: 0.03, vol: 0.25 });
  },
  // like pressed: a bright bell arpeggio up an open A chord with a glassy upper partial
  like(v) {
    [880, 1319, 1760, 2637].forEach((f, i) => {
      const at = i * 0.055, last = i === 3;
      tone(v, { type: 'triangle', f0: f, at, dur: last ? 0.42 : 0.2, vol: last ? 0.55 : 0.45 });
      tone(v, { type: 'sine', f0: f * 2.76, at, dur: last ? 0.28 : 0.1, vol: 0.14 });
    });
  },
  // like refused: two soft, dull, falling blips
  nolike(v) {
    tone(v, { type: 'triangle', f0: 247, f1: 233, dur: 0.09, vol: 0.45, lp: 700 });
    tone(v, { type: 'triangle', f0: 185, f1: 165, at: 0.09, dur: 0.12, vol: 0.45, lp: 600 });
  },
  // a darkness wave is coming: 2.4 s of slow, low swell: detuned saws and a tritone under an opening filter, with breath
  darkness(v) {
    swell(v, 1, 1.5, 2.4);
    tone(v, { type: 'sawtooth', f0: 55, f1: 49, dur: 2.4, vol: 0.55, attack: 0.5, lp: 260 });
    tone(v, { type: 'sawtooth', f0: 55.8, f1: 49.6, dur: 2.4, vol: 0.55, attack: 0.5, lp: 380, detune: 9 });
    tone(v, { type: 'sine', f0: 77.8, f1: 69, dur: 2.4, vol: 0.5, attack: 0.9 });
    tone(v, { type: 'square', f0: 110, f1: 98, at: 0.5, dur: 1.8, vol: 0.1, attack: 0.8, lp: 500 });
    noise(v, { buf: 'brown', type: 'bandpass', f0: 140, f1: 900, q: 1.5, dur: 2.4, vol: 0.9, attack: 1 });
  },
  // the wave is cleared: a quick rising A minor run that lands on a held top note
  clear(v) {
    [440, 523, 659, 880].forEach((f, i) => {
      const last = i === 3, at = i * 0.075;
      tone(v, { type: 'square', f0: f, at, dur: last ? 0.27 : 0.1, vol: last ? 0.4 : 0.32, lp: 3200 });
      if (last) tone(v, { type: 'triangle', f0: f * 2, at, dur: 0.27, vol: 0.25 });
    });
  },
  // shield collected (cyan): a soft glassy bell run up a major triad, each note with a high partial, ending on a held shimmer
  pickup_shield(v) {
    [523, 659, 784, 1047].forEach((f, i) => {
      const at = i * 0.07, last = i === 3;
      tone(v, { type: 'sine', f0: f, at, dur: last ? 0.4 : 0.16, vol: 0.5 });
      tone(v, { type: 'sine', f0: f * 3.01, at, dur: last ? 0.3 : 0.08, vol: 0.14 });
    });
    tone(v, { type: 'triangle', f0: 1047, at: 0.21, dur: 0.4, vol: 0.2, detune: 12 });
  },
  // bomb collected (orange): a warm, punchy square run up a minor-seventh shape over a low thump, ending on a short held octave
  pickup_bomb(v) {
    tone(v, { type: 'sine', f0: 150, f1: 70, dur: 0.14, vol: 0.3 });
    [220, 262, 330, 440].forEach((f, i) => {
      const at = i * 0.065, last = i === 3;
      tone(v, { type: 'square', f0: f, at, dur: last ? 0.3 : 0.1, vol: last ? 0.34 : 0.28, lp: 1800 });
      tone(v, { type: 'triangle', f0: f / 2, at, dur: last ? 0.3 : 0.1, vol: 0.3 });
    });
  },
  // power collected (gold, the brightest): five fast saw/square notes up an open chord into a high ringing top with a sparkle
  pickup_power(v) {
    [659, 880, 1109, 1319, 1760].forEach((f, i) => {
      const at = i * 0.05, last = i === 4;
      tone(v, { type: 'square', f0: f, at, dur: last ? 0.42 : 0.1, vol: last ? 0.3 : 0.24, lp: 5000 });
      tone(v, { type: 'sawtooth', f0: f * 2.005, at, dur: last ? 0.3 : 0.08, vol: 0.12, lp: 6000 });
    });
    tone(v, { type: 'sine', f0: 3520, at: 0.25, dur: 0.35, vol: 0.2 });
    noise(v, { type: 'highpass', f0: 6500, at: 0.25, dur: 0.1, vol: 0.12 });
  },
  // the shield takes a hit: a glassy ping (a sine with an inharmonic partial) over a dull thud and a tick of noise
  shield_hit(v) {
    noise(v, { type: 'bandpass', f0: 4200, q: 2, dur: 0.02, vol: 0.4 });
    tone(v, { type: 'sine', f0: 2200, f1: 1900, dur: 0.22, vol: 0.5 });
    tone(v, { type: 'sine', f0: 2200 * 2.76, f1: 5200, dur: 0.1, vol: 0.18 });
    tone(v, { type: 'sine', f0: 170, f1: 70, dur: 0.14, vol: 0.7 });
  },
};

// ---------------------------------------------------------------- the page's calls

/** master: music.js's `master` (volume, mute, context, node); duck: its duck(db, holdMs). Without master, or with off, the effects are disabled. */
export function init({ master: m, duck: d, off: disabled } = {}) {
  duck = typeof d === 'function' ? d : null;
  off = !!disabled || !m;
  master = m ?? null;
  voices.length = 0; lastAt.clear(); bus = busCtx = null;
  return !off;
}

/** The sfx level (0..1) under the master gain. */
export function setLevel(v) {
  level = Math.min(1, Math.max(0, Number(v) || 0));
  if (bus) bus.gain.value = level;
}
export const getLevel = () => level;

/** Plays one effect. name is one of Object.keys(sounds); pan is -1 (left) to 1 (right); step (kill, hit) is the
 *  combo pitch rise in semitones, 0..7; size (kill) is the enemy's size, 0..1. Never throws. */
export function play(name, pan = 0, step = 0, size = 0) {
  try {
    if (off || !Object.hasOwn(sounds, name)) return;
    const ctx = master.context;
    if (!master.node || !ctx || ctx.state !== 'running' || master.gain <= 0) return;
    const now = performance.now(), gap = GAP_MS[name] ?? 0;
    if (now - (lastAt.get(name) ?? -Infinity) < gap) return;
    lastAt.set(name, now);
    while (voices.length >= MAX_VOICES) voices[0].steal();
    const rise = name === 'kill' || name === 'hit' ? Math.min(MAX_STEP, Math.max(0, Math.round(step) || 0)) : 0;
    const v = makeVoice(ctx, pan, 2 ** (rise / 12), TRIM[name] ?? 1);
    voices.push(v);
    try {
      sounds[name](v, Math.min(1, Math.max(0, Number(size) || 0)));
      if (duck && name in DUCKS) { try { duck(-6, DUCKS[name]); } catch { /* the music is not our problem */ } }
    } catch (e) {
      v.steal();
      throw e;
    }
    if (!v.live) v.release();
  } catch (e) {
    warnOnce(`could not play ${name}`, e);
  }
}
