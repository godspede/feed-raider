// Sound effects, synthesised at runtime with Web Audio (oscillators, filtered noise, envelopes): no audio files.
// They play through music.js's master gain, so mute and volume apply, and nothing sounds before the first gesture (the master
// node and a running context only exist after music.js unlocked). An sfx bus (a gain into a soft limiter, a tanh wave shaper that
// tops out near -3 dBFS so a pile-up of voices cannot clip) sits under the master.
// At most MAX_VOICES sounds at once (the oldest is stolen) and a per-sound minimum gap. A sound the browser refuses turns the
// effects off, and play() returns the line the page shows the player.

const MAX_VOICES = 6;
const BUS_LEVEL = 0.8;                    // the sfx bus gain under the master
const DUCK_DB = -6;                       // how far a big sound dips the music
// per-sound level trim, a multiplier on every tone and noise in the sound, set so each lands near its target loudness
const TRIM = { shot: 0.4, hit: 0.52, kill: 0.92, boom: 1.51, hurt: 1.37, death: 1, bomb: 1.67, comm: 2.68, like: 1.56, nolike: 1.34, darkness: 2.2, clear: 1.58, pickup_shield: 1.48, pickup_bomb: 1.4, pickup_weapon: 1.85, shield_hit: 0.97,
  warn: 0.5, ping: 0.9, open: 1.4, shut: 1.4, charge: 1, beam: 0.6, lob: 1, blast: 1.2, hatch: 1.3, tick: 1, part: 1.3, boss_down: 1.3, sting: 2.9, repost: 1.5, undo: 1.34,
  burst: 1.9, burst_end: 1.35,
  shot_heavy: 0.62, shot_twin: 0.56, shot_missile: 1.78, shot_twin_heavy: 0.55, shot_spread: 1.08, shot_machine_gun: 0.35, shot_gatling: 0.54, shot_swarm: 1.76, shot_storm: 0.8 };
const MAX_STEP = 7;                       // the combo pitch rise tops out at +7 semitones
const SEMITONES_PER_OCTAVE = 12;
const MAX_TICK_STEP = SEMITONES_PER_OCTAVE;   // a fuse's tick rises up to an octave
// the sounds that duck the music under them by DUCK_DB, with the hold in ms (music.js owns how fast the dip goes and comes back)
const DUCKS = { bomb: 250, boom: 500, death: 400, boss_down: 1500, sting: 500 };
// the fewest ms between two plays of a sound (the game rate-limits too; this protects the page from any caller)
export const GAP_MS = { shot: 60, hit: 45, kill: 40, hurt: 90, like: 80, nolike: 150, bomb: 150, comm: 250, clear: 400, darkness: 1500, pickup_shield: 100, pickup_bomb: 100, pickup_weapon: 100, shield_hit: 90,
  warn: 600, ping: 133, open: 100, shut: 100, charge: 0, beam: 600, lob: 100, blast: 66, hatch: 66, tick: 100, part: 66, boss_down: 1500, sting: 500, repost: 80, undo: 150,
  burst: 333, burst_end: 333,
  // the weapons fire from 100 to 333 ms apart; the machine gun and the gatling are heard on every second volley
  shot_heavy: 60, shot_twin: 60, shot_missile: 60, shot_twin_heavy: 60, shot_spread: 60, shot_machine_gun: 160, shot_gatling: 160, shot_swarm: 60, shot_storm: 60 };

const SILENCE = 0.0001;                   // the floor of an exponential ramp, which cannot reach 0
const STEAL_FADE_S = 0.012;               // a stolen voice fades out this fast, to avoid a click
const STEAL_STOP_S = 0.015;               // and its sources stop just after the fade ends
const STOP_TAIL_S = 0.02;                 // a tone or noise stops this long after its envelope ends
const MIN_FREQ_HZ = 20;                   // the lowest frequency a filter or oscillator is asked for
const GRIT_DRIVE = 4;                     // gain into the saturator that makes a noise gritty
const NOISE_SECONDS = 1.5;                // each noise buffer is this long, and loops
const BROWN_STEP = 0.02, BROWN_LEAK = 1.02;   // brown noise integrates white in small steps, leaking so it does not drift
const BROWN_GAIN = 3.5;                   // makes up the level that integrating takes off

let master = null, duck = null, clock = null, off = true;
let bus = null, busCtx = null, whiteBuf = null, brownBuf = null;
// soft saturation, slope 1 at zero: also the limiter curve on the bus (it tops out at about -2.8 dBFS)
const CURVE_POINTS = 1025;           // one more than a power of two, so the middle point sits at exactly 0
const CURVE_CEILING = 0.9;           // also the width of the knee
const SATURATE = Float32Array.from({ length: CURVE_POINTS }, (_, i) => CURVE_CEILING * Math.tanh((i / ((CURVE_POINTS - 1) / 2) - 1) / CURVE_CEILING));
const voices = [];
const lastAt = new Map();


// ---------------------------------------------------------------- the bus and the noise (built once per context)

function noiseBuffers(ctx) {
  const n = Math.floor(ctx.sampleRate * NOISE_SECONDS);
  const white = ctx.createBuffer(1, n, ctx.sampleRate), brown = ctx.createBuffer(1, n, ctx.sampleRate);
  const w = white.getChannelData(0), b = brown.getChannelData(0);
  let last = 0;
  for (let i = 0; i < n; i++) {
    const x = Math.random() * 2 - 1;
    w[i] = x;
    last = (last + BROWN_STEP * x) / BROWN_LEAK;
    b[i] = last * BROWN_GAIN;
  }
  return [white, brown];
}

function ensureBusAndNoise(ctx) {
  if (bus && busCtx === ctx) return bus;
  busCtx = ctx;
  [whiteBuf, brownBuf] = noiseBuffers(ctx);
  const gain = ctx.createGain();
  gain.gain.value = BUS_LEVEL;
  // a limiter so several loud voices together do not clip; unlike a compressor it adds no unknown makeup gain
  const lim = ctx.createWaveShaper();
  lim.curve = SATURATE;
  gain.connect(lim);
  lim.connect(master.node);
  bus = gain;
  return bus;
}

// ---------------------------------------------------------------- voices and building blocks

// A voice can be released twice (stolen, then its sources end) and a source stopped after it ended. Some browsers throw
// for a node already disconnected or a source already stopped; the voice is gone either way, so only those two pass.
function ignoreGone(e) {
  if (!(e instanceof DOMException && (e.name === 'InvalidAccessError' || e.name === 'InvalidStateError'))) throw e;
}

/** One playing sound: its own gain (for stealing), pan, and the sources to stop. */
function makeVoice(ctx, pan, k, trim) {
  const out = ctx.createGain(), head = ctx.createStereoPanner();
  head.pan.value = Math.max(-1, Math.min(1, Number.isFinite(pan) ? pan : 0));
  out.connect(head);
  head.connect(ensureBusAndNoise(ctx));
  const v = {
    ctx, out, head, k, trim, sources: [], live: 0,
    track(src) {
      this.sources.push(src);
      this.live++;
      src.onended = () => { if (--this.live <= 0) this.release(); };
    },
    release() {
      const i = voices.indexOf(this);
      if (i >= 0) voices.splice(i, 1);
      try { this.head.disconnect(); this.out.disconnect(); } catch (e) { ignoreGone(e); }
    },
    steal() {
      const t = ctx.currentTime;
      this.out.gain.cancelScheduledValues(t);
      this.out.gain.setValueAtTime(this.out.gain.value, t);
      this.out.gain.linearRampToValueAtTime(0, t + STEAL_FADE_S);
      for (const s of this.sources) { try { s.stop(t + STEAL_STOP_S); } catch (e) { ignoreGone(e); } }
      this.release();
    },
  };
  return v;
}

const clampFrequency = (f) => Math.max(MIN_FREQ_HZ, f);

function tone(v, { type = 'square', f0, f1 = f0, at = 0, dur = 0.1, vol = 0.2, attack = 0.004, hold = 0, lp = 0, detune = 0 }) {
  const { ctx } = v, t = ctx.currentTime + at, stop = t + dur + STOP_TAIL_S;
  const o = ctx.createOscillator(), g = ctx.createGain();
  o.type = type;
  o.frequency.setValueAtTime(clampFrequency(f0 * v.k), t);
  if (f1 !== f0) o.frequency.exponentialRampToValueAtTime(clampFrequency(f1 * v.k), t + dur);
  if (detune) o.detune.value = detune;
  g.gain.setValueAtTime(SILENCE, t);
  g.gain.linearRampToValueAtTime(vol * v.trim, t + attack);
  if (hold) g.gain.setValueAtTime(vol * v.trim, t + attack + hold);
  g.gain.exponentialRampToValueAtTime(SILENCE, t + dur);
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
  v.track(o);
}

function noise(v, { buf = 'white', type = 'lowpass', f0 = 2000, f1 = f0, q = 0.7, at = 0, dur = 0.1, vol = 0.2, attack = 0.002, hold = 0, shape = false }) {
  const { ctx } = v, t = ctx.currentTime + at, stop = t + dur + STOP_TAIL_S;
  const s = ctx.createBufferSource(), f = ctx.createBiquadFilter(), g = ctx.createGain();
  s.buffer = buf === 'brown' ? brownBuf : whiteBuf;
  s.loop = true;
  f.type = type; f.Q.value = q;
  f.frequency.setValueAtTime(clampFrequency(f0 * v.k), t);
  if (f1 !== f0) f.frequency.exponentialRampToValueAtTime(clampFrequency(f1 * v.k), t + dur);
  g.gain.setValueAtTime(SILENCE, t);
  g.gain.linearRampToValueAtTime(vol * v.trim, t + attack);
  if (hold) g.gain.setValueAtTime(vol * v.trim, t + attack + hold);
  g.gain.exponentialRampToValueAtTime(SILENCE, t + dur);
  s.connect(f);
  if (shape) {
    // a little grit: overdrive the filtered noise before its envelope
    const pre = ctx.createGain(), w = ctx.createWaveShaper();
    pre.gain.value = GRIT_DRIVE;
    w.curve = SATURATE;
    f.connect(pre); pre.connect(w); w.connect(g);
  } else {
    f.connect(g);
  }
  g.connect(v.out);
  s.start(t, Math.random());
  s.stop(stop);
  v.track(s);
}

function swell(v, peak, peakAt, dur) {
  const t = v.ctx.currentTime;
  v.out.gain.setValueAtTime(SILENCE, t);
  v.out.gain.linearRampToValueAtTime(peak, t + peakAt);
  v.out.gain.linearRampToValueAtTime(SILENCE, t + dur);
}

// ---------------------------------------------------------------- the sounds (A minor, saw and square, a little grit)

const SHOT_DETUNE_SPAN = 0.08;            // a shot's pitch varies by up to +-4 % from one to the next
const BAR_PARTIAL = 2.76;                 // a struck bar's first overtone, inharmonic, over its fundamental: the ring of glass and metal
const BAR_SECOND_PARTIAL = 5.4;           // the same bar's second overtone over its fundamental
const GLASS_PARTIAL = 3.01;               // a third harmonic pulled a hair sharp, so a bell note shimmers
const BRIGHT_OCTAVE = 2.005;              // an octave pulled a hair sharp, so the doubled note beats against the first
const BEEP_DROOP = 0.985;                 // a radio beep sags in pitch by this ratio as it sounds
const CHARGE_MIN_S = 0.4, CHARGE_SPAN_S = 1.2;   // a wind-up lasts CHARGE_MIN_S plus up to CHARGE_SPAN_S, by its size
const CHARGE_ATTACK_SHARE = 0.8;          // a wind-up swells for this share of its length
const detuneRand = () => 1 + (Math.random() - 0.5) * SHOT_DETUNE_SPAN;

export const sounds = {
  // a short square zap that falls in pitch, a little different every time, over a soft low triangle for body
  shot(v) {
    const k = detuneRand();
    tone(v, { type: 'square', f0: 1250 * k, f1: 320 * k, dur: 0.07, vol: 0.5, lp: 4200 });
    tone(v, { type: 'triangle', f0: 190 * k, f1: 85, dur: 0.1, vol: 0.5 });
    noise(v, { type: 'highpass', f0: 5000, dur: 0.012, vol: 0.2 });
  },
  // the heavy laser: a lower, fatter zap with a buzzing saw under it and a dull puff
  shot_heavy(v) {
    tone(v, { type: 'square', f0: 700, f1: 180, dur: 0.1, vol: 0.45, lp: 2600 });
    tone(v, { type: 'sawtooth', f0: 140, f1: 60, dur: 0.13, vol: 0.5, lp: 900 });
    noise(v, { type: 'lowpass', f0: 1800, dur: 0.03, vol: 0.3 });
  },
  // the twin laser: two thin zaps a hair apart, one for each gun
  shot_twin(v) {
    tone(v, { type: 'square', f0: 1500, f1: 420, dur: 0.05, vol: 0.4, lp: 5000 });
    tone(v, { type: 'square', f0: 1400, f1: 380, at: 0.022, dur: 0.05, vol: 0.4, lp: 5000 });
    noise(v, { type: 'highpass', f0: 6000, dur: 0.01, vol: 0.15 });
  },
  // a missile launch: a pop, then a rising hiss of exhaust over a soft rising saw
  shot_missile(v) {
    noise(v, { type: 'highpass', f0: 4000, dur: 0.015, vol: 0.2 });
    noise(v, { type: 'bandpass', f0: 900, f1: 2600, q: 1.5, dur: 0.18, vol: 0.5 });
    tone(v, { type: 'sawtooth', f0: 220, f1: 330, dur: 0.12, vol: 0.25, lp: 1200 });
  },
  // the twin heavy: two low zaps a beat apart over one deep thump
  shot_twin_heavy(v) {
    tone(v, { type: 'square', f0: 650, f1: 170, dur: 0.09, vol: 0.45, lp: 2400 });
    tone(v, { type: 'square', f0: 620, f1: 160, at: 0.03, dur: 0.09, vol: 0.45, lp: 2400 });
    tone(v, { type: 'triangle', f0: 110, f1: 55, dur: 0.14, vol: 0.6 });
  },
  // the spread: a falling chord of soft bells, one note after another like the fan opening, and a bright hiss
  shot_spread(v) {
    tone(v, { type: 'triangle', f0: 1800, f1: 900, dur: 0.08, vol: 0.3 });
    tone(v, { type: 'triangle', f0: 1350, f1: 675, at: 0.01, dur: 0.08, vol: 0.3 });
    tone(v, { type: 'sine', f0: 2400, f1: 1200, at: 0.02, dur: 0.06, vol: 0.2 });
    noise(v, { type: 'highpass', f0: 7000, dur: 0.02, vol: 0.15 });
  },
  // the machine gun: a short hard clack, quick enough to chatter
  shot_machine_gun(v) {
    tone(v, { type: 'square', f0: 900, f1: 600, dur: 0.025, vol: 0.45, lp: 3000 });
    noise(v, { type: 'bandpass', f0: 2500, q: 2, dur: 0.02, vol: 0.5 });
  },
  // the gatling: a lower burr of barrels, a buzz, grit and a small thump
  shot_gatling(v) {
    tone(v, { type: 'sawtooth', f0: 520, f1: 380, dur: 0.04, vol: 0.35, lp: 2200 });
    noise(v, { type: 'bandpass', f0: 1600, q: 1.5, dur: 0.03, vol: 0.5 });
    tone(v, { type: 'triangle', f0: 95, f1: 70, dur: 0.05, vol: 0.4 });
  },
  // the swarm: a wide rising hiss and two buzzing rockets a moment apart
  shot_swarm(v) {
    noise(v, { type: 'bandpass', f0: 1400, f1: 4000, q: 2, dur: 0.22, vol: 0.45 });
    tone(v, { type: 'square', f0: 300, f1: 600, dur: 0.1, vol: 0.15, lp: 1500 });
    tone(v, { type: 'square', f0: 310, f1: 620, at: 0.04, dur: 0.1, vol: 0.15, lp: 1500 });
  },
  // the storm: a heavy falling saw on a sub thump, with the missiles' hiss rising through it
  shot_storm(v) {
    tone(v, { type: 'sawtooth', f0: 500, f1: 120, dur: 0.12, vol: 0.4, lp: 2000 });
    tone(v, { type: 'sine', f0: 80, f1: 45, dur: 0.16, vol: 0.7 });
    noise(v, { type: 'bandpass', f0: 1000, f1: 3000, q: 1.2, dur: 0.15, vol: 0.35 });
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
      const at = i * 0.2;
      noise(v, { type: 'lowpass', f0: 3400 - i * 600, f1: 200, q: 1, at, dur: 0.5, vol: 0.9, shape: true });
      tone(v, { type: 'square', f0: 360 - i * 60, f1: 50, at, dur: 0.4, vol: 0.25, lp: 1500 });
    }
    const click = 0.02, rumble = { at: 0.3, dur: 2 };
    noise(v, { type: 'highpass', f0: 3500, dur: click, vol: 0.5 });
    tone(v, { type: 'sine', f0: 95, f1: 32, at: click, dur: 2.2, vol: 0.95 });
    noise(v, { ...rumble, buf: 'brown', type: 'lowpass', f0: 1700, f1: 60, vol: 1, attack: 0.03, shape: true });
    tone(v, { ...rumble, type: 'sawtooth', f0: 120, f1: 26, vol: 0.45, lp: 400 });
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
  // a speech box opens: a squelch, then three bandpassed radio beeps (low, high, mid), Star Fox style
  comm(v) {
    noise(v, { type: 'bandpass', f0: 2400, q: 2, dur: 0.035, vol: 0.4 });
    const beepsAt = 0.045, beepGap = 0.02;
    let at = beepsAt;
    for (const [f, d] of [[1320, 0.04], [1760, 0.04], [1480, 0.07]]) {
      tone(v, { type: 'square', f0: f, f1: f * BEEP_DROOP, at, dur: d, vol: 0.35, attack: 0.002, lp: 2600 });
      at += d + beepGap;
    }
    noise(v, { type: 'bandpass', f0: 2000, q: 2, at: 0.25, dur: 0.03, vol: 0.25 });
  },
  // like pressed: a bright bell arpeggio up an open A chord with a glassy upper partial
  like(v) {
    [880, 1319, 1760, 2637].forEach((f, i, notes) => {
      const at = i * 0.055, last = i === notes.length - 1;
      tone(v, { type: 'triangle', f0: f, at, dur: last ? 0.42 : 0.2, vol: last ? 0.55 : 0.45 });
      tone(v, { type: 'sine', f0: f * BAR_PARTIAL, at, dur: last ? 0.28 : 0.1, vol: 0.14 });
    });
  },
  // a repost made: two round notes, each sweeping up, the second higher, like something handed on and away; no bell in it,
  // so it is never taken for the like
  repost(v) {
    tone(v, { type: 'triangle', f0: 392, f1: 523, dur: 0.12, vol: 0.5, lp: 2400 });
    tone(v, { type: 'sine', f0: 784, f1: 1046, dur: 0.1, vol: 0.1 });
    tone(v, { type: 'triangle', f0: 587, f1: 880, at: 0.12, dur: 0.22, vol: 0.55, lp: 3200 });
    tone(v, { type: 'sine', f0: 1174, f1: 1760, at: 0.12, dur: 0.16, vol: 0.12 });
  },
  // like refused: two soft, dull, falling blips
  nolike(v) {
    tone(v, { type: 'triangle', f0: 247, f1: 233, dur: 0.09, vol: 0.45, lp: 700 });
    tone(v, { type: 'triangle', f0: 185, f1: 165, at: 0.09, dur: 0.12, vol: 0.45, lp: 600 });
  },
  // a like or repost undone: one short, low tone falling away, below nolike so it never reads as a refusal
  undo(v) {
    tone(v, { type: 'triangle', f0: 220, f1: 147, dur: 0.11, vol: 0.4, lp: 650 });
  },
  // a live wave is coming: 2.4 s of slow, low swell: detuned saws and a tritone under an opening filter, with breath
  darkness(v) {
    const dur = 2.4;
    swell(v, 1, 1.5, dur);
    tone(v, { type: 'sawtooth', f0: 55, f1: 49, dur, vol: 0.55, attack: 0.5, lp: 260 });
    tone(v, { type: 'sawtooth', f0: 55.8, f1: 49.6, dur, vol: 0.55, attack: 0.5, lp: 380, detune: 9 });
    tone(v, { type: 'sine', f0: 77.8, f1: 69, dur, vol: 0.5, attack: 0.9 });
    tone(v, { type: 'square', f0: 110, f1: 98, at: 0.5, dur: 1.8, vol: 0.1, attack: 0.8, lp: 500 });
    noise(v, { buf: 'brown', type: 'bandpass', f0: 140, f1: 900, q: 1.5, dur, vol: 0.9, attack: 1 });
  },
  // the wave is cleared: a quick rising A minor run that lands on a held top note
  clear(v) {
    [440, 523, 659, 880].forEach((f, i, notes) => {
      const last = i === notes.length - 1, at = i * 0.075;
      tone(v, { type: 'square', f0: f, at, dur: last ? 0.27 : 0.1, vol: last ? 0.4 : 0.32, lp: 3200 });
      if (last) tone(v, { type: 'triangle', f0: f * 2, at, dur: 0.27, vol: 0.25 });
    });
  },
  // shield collected (cyan): a soft glassy bell run up a major triad, each note with a high partial, ending on a held shimmer
  pickup_shield(v) {
    const notes = [523, 659, 784, 1047], gap = 0.07, top = notes.length - 1;
    notes.forEach((f, i) => {
      const at = i * gap, last = i === top;
      tone(v, { type: 'sine', f0: f, at, dur: last ? 0.4 : 0.16, vol: 0.5 });
      tone(v, { type: 'sine', f0: f * GLASS_PARTIAL, at, dur: last ? 0.3 : 0.08, vol: 0.14 });
    });
    tone(v, { type: 'triangle', f0: notes[top], at: top * gap, dur: 0.4, vol: 0.2, detune: 12 });
  },
  // bomb collected (orange): a warm, punchy square run up a minor-seventh shape over a low thump, ending on a short held octave
  pickup_bomb(v) {
    tone(v, { type: 'sine', f0: 150, f1: 70, dur: 0.14, vol: 0.3 });
    [220, 262, 330, 440].forEach((f, i, notes) => {
      const at = i * 0.065, last = i === notes.length - 1;
      tone(v, { type: 'square', f0: f, at, dur: last ? 0.3 : 0.1, vol: last ? 0.34 : 0.28, lp: 1800 });
      tone(v, { type: 'triangle', f0: f / 2, at, dur: last ? 0.3 : 0.1, vol: 0.3 });
    });
  },
  // weapon collected (gold, the brightest): five fast saw/square notes up an open chord into a high ringing top with a sparkle
  pickup_weapon(v) {
    [659, 880, 1109, 1319, 1760].forEach((f, i, notes) => {
      const at = i * 0.05, last = i === notes.length - 1;
      tone(v, { type: 'square', f0: f, at, dur: last ? 0.42 : 0.1, vol: last ? 0.3 : 0.24, lp: 5000 });
      tone(v, { type: 'sawtooth', f0: f * BRIGHT_OCTAVE, at, dur: last ? 0.3 : 0.08, vol: 0.12, lp: 6000 });
    });
    tone(v, { type: 'sine', f0: 3520, at: 0.25, dur: 0.35, vol: 0.2 });
    noise(v, { type: 'highpass', f0: 6500, at: 0.25, dur: 0.1, vol: 0.12 });
  },
  // the shield takes a hit: a glassy ping (a sine with an inharmonic partial) over a dull thud and a tick of noise
  shield_hit(v) {
    const ping = 2200;
    noise(v, { type: 'bandpass', f0: 4200, q: 2, dur: 0.02, vol: 0.4 });
    tone(v, { type: 'sine', f0: ping, f1: 1900, dur: 0.22, vol: 0.5 });
    tone(v, { type: 'sine', f0: ping * BAR_PARTIAL, f1: 5200, dur: 0.1, vol: 0.18 });
    tone(v, { type: 'sine', f0: 170, f1: 70, dur: 0.14, vol: 0.7 });
  },
  // a warning: two low klaxon pulses (a saw and a detuned square a half step apart, held, then cut), 0.6 s
  warn(v) {
    for (let i = 0; i < 2; i++) {
      const pulse = { at: i * 0.3, dur: 0.28, attack: 0.01, hold: 0.17 };
      tone(v, { ...pulse, type: 'sawtooth', f0: 196, f1: 185, vol: 0.5, lp: 1100 });
      tone(v, { ...pulse, type: 'square', f0: 208, f1: 196, vol: 0.3, lp: 800 });
      tone(v, { ...pulse, type: 'sine', f0: 98, f1: 92, vol: 0.5 });
    }
  },
  // a shot stopped by armour: a dry, dull tick, a short muted knock under a click
  ping(v) {
    noise(v, { type: 'bandpass', f0: 2600, q: 3, dur: 0.025, vol: 0.45 });
    tone(v, { type: 'triangle', f0: 1500, f1: 1100, dur: 0.05, vol: 0.4, lp: 3000 });
    tone(v, { type: 'sine', f0: 420, f1: 300, dur: 0.06, vol: 0.5 });
  },
  // a part opens: a bright chirp rising, ending on a held note that asks to be shot
  open(v) {
    tone(v, { type: 'sine', f0: 500, f1: 1500, dur: 0.16, vol: 0.5 });
    tone(v, { type: 'triangle', f0: 1000, f1: 3000, dur: 0.16, vol: 0.2 });
    tone(v, { type: 'sine', f0: 1500, at: 0.14, dur: 0.12, vol: 0.4 });
  },
  // a part closes: the same chirp falling, ending on a low dull knock
  shut(v) {
    tone(v, { type: 'sine', f0: 1500, f1: 500, dur: 0.16, vol: 0.5 });
    tone(v, { type: 'triangle', f0: 3000, f1: 1000, dur: 0.16, vol: 0.2 });
    tone(v, { type: 'sine', f0: 240, f1: 150, at: 0.14, dur: 0.12, vol: 0.4 });
  },
  // a wind-up: a saw and a sine rising together under an opening noise band, swelling to the end; the length is `size`, 0.4 to 1.6 s
  charge(v, size) {
    const dur = CHARGE_MIN_S + CHARGE_SPAN_S * size, a = dur * CHARGE_ATTACK_SHARE;
    tone(v, { type: 'sawtooth', f0: 90, f1: 520, dur, vol: 0.5, attack: a, lp: 1300 });
    tone(v, { type: 'sine', f0: 180, f1: 1040, dur, vol: 0.4, attack: a });
    noise(v, { type: 'bandpass', f0: 300, f1: 3000, q: 2, dur, vol: 0.3, attack: a });
  },
  // a harsh buzz held for 0.6 s: a square and a saw a hair apart beat against each other, over gritty noise
  beam(v) {
    const held = { dur: 0.58, attack: 0.02, hold: 0.45 };
    tone(v, { ...held, type: 'square', f0: 120, f1: 118, vol: 0.4, lp: 2600 });
    tone(v, { ...held, type: 'sawtooth', f0: 126, f1: 124, vol: 0.35, lp: 3000 });
    tone(v, { ...held, type: 'square', f0: 480, f1: 470, vol: 0.12, lp: 2400 });
    noise(v, { ...held, type: 'highpass', f0: 2500, vol: 0.3, shape: true });
  },
  // a launch: a hollow thump, a sine falling with a muted knock and a puff of brown noise
  lob(v) {
    tone(v, { type: 'sine', f0: 220, f1: 70, dur: 0.22, vol: 0.8 });
    tone(v, { type: 'triangle', f0: 440, f1: 140, dur: 0.12, vol: 0.3, lp: 600 });
    noise(v, { buf: 'brown', type: 'lowpass', f0: 500, dur: 0.1, vol: 0.3 });
  },
  // a shell lands or a fuse goes off: a short boom, lighter than the boss's
  blast(v) {
    noise(v, { type: 'lowpass', f0: 2800, f1: 200, q: 1, dur: 0.45, vol: 0.6, shape: true });
    tone(v, { type: 'sine', f0: 120, f1: 40, dur: 0.5, vol: 0.7 });
    tone(v, { type: 'square', f0: 300, f1: 60, dur: 0.25, vol: 0.2, lp: 1200 });
  },
  // gnats released: a wet pop, a bubble rising and bursting with a squelch of band noise
  hatch(v) {
    tone(v, { type: 'sine', f0: 300, f1: 900, dur: 0.06, vol: 0.6 });
    noise(v, { type: 'bandpass', f0: 1200, f1: 2400, q: 4, dur: 0.04, vol: 0.35 });
    tone(v, { type: 'sine', f0: 700, f1: 250, at: 0.05, dur: 0.08, vol: 0.3 });
  },
  // a clock tick: a click and a short wooden note
  tick(v) {
    noise(v, { type: 'bandpass', f0: 4000, q: 3, dur: 0.015, vol: 0.4 });
    tone(v, { type: 'triangle', f0: 1200, f1: 1000, dur: 0.04, vol: 0.5 });
  },
  // a part destroyed: a mid boom, a dull thud and a crunch, with a metal ring (inharmonic partials) left behind
  part(v) {
    noise(v, { type: 'lowpass', f0: 2500, f1: 200, q: 1, dur: 0.3, vol: 0.7, shape: true });
    tone(v, { type: 'sine', f0: 140, f1: 50, dur: 0.35, vol: 0.8 });
    const ring = 1250, ringAt = 0.02;
    tone(v, { type: 'sine', f0: ring, f1: 1230, at: ringAt, dur: 0.6, vol: 0.2 });
    tone(v, { type: 'sine', f0: ring * BAR_PARTIAL, f1: 3400, at: ringAt, dur: 0.4, vol: 0.12 });
    tone(v, { type: 'sine', f0: ring * BAR_SECOND_PARTIAL, at: ringAt, dur: 0.2, vol: 0.06 });
  },
  // a boss goes down: three booms, each lower than the last, the last the longest, 1.5 s
  boss_down(v) {
    const booms = 3;
    for (let i = 0; i < booms; i++) {
      const at = i * 0.4, dur = i === booms - 1 ? 0.68 : 0.5;
      noise(v, { type: 'lowpass', f0: 3200 - i * 900, f1: 150, q: 1, at, dur, vol: 0.9, shape: true });
      tone(v, { type: 'sine', f0: 120 - i * 25, f1: 30, at, dur, vol: 0.9 });
      tone(v, { type: 'sawtooth', f0: 160 - i * 40, f1: 28, at, dur: dur - 0.1, vol: 0.35, lp: 500 });
    }
  },
  // a visitor arrives (a lesser gunship or a mini-boss): a low brass call, deep and slow so it reads as a warning and never as a hit
  sting(v) {
    const attack = 0.012, first = { dur: 0.2, attack }, second = { at: first.dur, dur: 0.28, attack };
    tone(v, { ...first, type: 'sawtooth', f0: 110, f1: 108, vol: 0.5, lp: 900 });
    tone(v, { ...first, type: 'square', f0: 110.5, f1: 108.5, vol: 0.2, lp: 700 });
    tone(v, { ...second, type: 'sawtooth', f0: 82.4, f1: 80, vol: 0.55, lp: 800 });
    tone(v, { ...second, type: 'square', f0: 82.9, f1: 80.5, vol: 0.2, lp: 600 });
    tone(v, { type: 'sine', f0: 41.2, f1: 40, at: second.at, dur: second.dur, vol: 0.4 });
  },
  // the speed burst taken: a rising whoosh, so the player hears the page gather speed
  burst(v) {
    noise(v, { type: 'bandpass', f0: 400, f1: 3000, q: 1.2, dur: 0.35, vol: 0.9, attack: 0.12, hold: 0.12 });
    tone(v, { type: 'sine', f0: 600, f1: 1800, dur: 0.3, vol: 0.12, attack: 0.03 });
  },
  // the speed burst spent: the burst's rise turned down, so its end is heard before the ship can be hurt again
  burst_end(v) {
    tone(v, { type: 'sine', f0: 880, f1: 330, dur: 0.3, vol: 0.3, attack: 0.02 });
    noise(v, { type: 'bandpass', f0: 1800, f1: 400, q: 2, dur: 0.25, vol: 0.25, attack: 0.02 });
  },
};

// ---------------------------------------------------------------- the page's calls

/** master: music.js's `master` (volume, mute, context, node); duck: its duck(db, holdMs); clock: the ms that GAP_MS is measured in.
 *  Without master the effects are disabled. */
export function init({ master: m, duck: d, clock: c = () => performance.now() } = {}) {
  duck = typeof d === 'function' ? d : null;
  clock = c;
  off = !m;
  master = m ?? null;
  voices.length = 0; lastAt.clear(); bus = busCtx = null;
  return !off;
}

/** Plays one effect. name is one of Object.keys(sounds); pan is -1 (left) to 1 (right); step (kill, hit) is the
 *  combo pitch rise in semitones, 0..7, and (tick) the fuse's count, 0..12; size (kill) is the enemy's size, 0..1 and (charge)
 *  the wind-up's length, 0.4 s to 1.6 s. A sound the browser cannot make turns the effects off for the visit and returns the line
 *  that tells the player so; any other call returns undefined, and a fault throws, a name that is no sound's with a RangeError. */
export function play(name, pan = 0, step = 0, size = 0) {
  if (!Object.hasOwn(sounds, name)) throw new RangeError(`no sound effect is named ${JSON.stringify(name)}`);
  try {
    if (off) return;
    const ctx = master.context;
    if (!master.node || !ctx || ctx.state !== 'running' || master.gain <= 0) return;
    const now = clock(), gap = GAP_MS[name] ?? 0;
    if (now - (lastAt.get(name) ?? -Infinity) < gap) return;
    lastAt.set(name, now);
    while (voices.length >= MAX_VOICES) voices[0].steal();
    const cap = name === 'tick' ? MAX_TICK_STEP : name === 'kill' || name === 'hit' ? MAX_STEP : 0;
    const rise = Math.min(cap, Math.max(0, Math.round(step) || 0));
    const v = makeVoice(ctx, pan, 2 ** (rise / SEMITONES_PER_OCTAVE), TRIM[name]);
    voices.push(v);
    try {
      sounds[name](v, Math.min(1, Math.max(0, Number(size) || 0)));
    } catch (e) {
      v.steal();
      throw e;
    }
    if (!v.live) v.release();
    if (duck && name in DUCKS) duck(DUCK_DB, DUCKS[name]);
  } catch (e) {
    // Web Audio refuses a node or a schedule with a DOMException (NotSupportedError, InvalidStateError) where the browser
    // cannot make that sound. Any other error is a fault and is thrown.
    if (!(e instanceof DOMException)) throw e;
    off = true;
    return `Sound effects are off: this browser could not play the ${name} sound (${e.name}).`;
  }
}
