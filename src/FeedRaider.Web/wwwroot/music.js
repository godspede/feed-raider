// Background music: four states (start, gameplay, boss, gameover), one track at a time; a change crossfades over FADE_MS between
// two <audio> elements. Tracks come from a manifest of {file, role}; a boss track may add `boss`, the lower-case name of the one
// boss it belongs to. The game cues a boss as `boss:<key>`: that boss's own track, else a shared boss track (one with no `boss`),
// else any boss track, never a gameplay one. `ahead:boss:<key>` is a hint, no state: it fetches that pick without playing it. Gameplay plays a shuffled rotation: each track once through,
// crossfading into the next CROSSFADE_MS before its end; a boss interrupts it and, when the boss is gone, the rotation goes on
// with the next track. Each track is fetched whole into a Blob and played from a blob: URL, never streamed from its address.
// Fetching is lazy, one at a time: no track before the first gesture or the page's
// allowFetchAhead(), whichever comes first; then a cue is fetched when it is wanted, and only the next gameplay track is fetched ahead
// (never on a data-saver or slow connection). A gameplay track that has been played through, and a start page, boss or game over track
// once it has faded out and is not wanted, is dropped and its blob URL revoked. Nothing here may break the game: any failure
// leaves the page silent and logs once.
// With Web Audio available the elements play through a shared master GainNode (`master.node`, which sfx.js plays through too).
// iOS ignores element.volume, so without it volume would not work there. The page can run the controls and the master with
// the music itself off (init without tracks) so that the sound effects keep the speaker button, M and the slider.

const FADE_MS = 1500, CROSSFADE_MS = 2000, WATCH_MS = 250;
const CUT_FADE_MS = 200;          // the quick fade-out when the music is muted or nothing is wanted
const FADE_TICK_MS = 40;          // how often a running fade is stepped
const MS_PER_S = 1000;            // the element clock and the Web Audio clock count seconds
const MASTER_SMOOTH_S = 0.03;     // time constant that smooths a volume change on the master gain
const DUCK_IN_S = 0.04, DUCK_OUT_S = 0.4;   // a duck dips this fast and recovers this slowly
const DB_PER_TENFOLD_AMPLITUDE = 20;          // turns a duck's depth in dB into a gain factor
const VOLUME_KEY = 'feed-raider.music.volume', MUTED_KEY = 'feed-raider.music.muted';
// the unlock clip: 10 ms of 8 kHz 8-bit mono silence (80 samples of 0x80); a WAV with no samples will not decode in Firefox
const SILENT = 'data:audio/wav;base64,UklGRnQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YVAAAACAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgA==';
const DEFAULT_VOLUME = 0.5;        // the volume before the player sets one
const SLIDER_STEPS = 100;          // the volume slider runs 0 to this
const DUCK_HOLD_MS = 200;          // how long a duck stays down by default
const ROLES = ['start', 'gameplay', 'boss', 'gameover'];

const store = {
  get(k) { try { return localStorage.getItem(k); } catch { return null; /* storage unavailable: the defaults stand */ } },
  set(k, v) { try { localStorage.setItem(k, v); } catch { /* private mode: not persisted */ } },
};
const storedVolume = Number.parseFloat(store.get(VOLUME_KEY));
let volume = Number.isFinite(storedVolume) ? Math.min(1, Math.max(0, storedVolume)) : DEFAULT_VOLUME;
let muted = store.get(MUTED_KEY) === '1';

let off = true, musicOn = false, unlocked = false, warned = false;   // off: no music and no sound effects either
let tracks = null;       // [{id, role, url}] once the manifest is in
let requested = null;    // the last state the game asked for, kept until the manifest arrives
let aheadRequested = null; // the last fetch-ahead hint, kept until the manifest arrives
let want = null;         // the cue in force: a state, or boss:<key>
let target = null;       // the track that should be playing
let rot = { order: [], i: -1, last: null }; // the gameplay rotation; `last` is the track played most recently
let advancing = false, random = Math.random;
let tabHidden = false;   // the page is in a background tab: nothing plays or rotates until it is back
let ctx = null, masterNode = null, musicBus = null;   // musicBus: the tracks only, so duck() leaves the effects alone

/** The volume every sound shares. `gain` is the effective 0..1 (0 when muted); `node` is the Web Audio master GainNode once
 *  the first gesture has made the context (null before, or without Web Audio). */
export const master = {
  get gain() { return muted ? 0 : volume; },
  get context() { return ctx; },
  get node() { return masterNode; },
};

function warnOnce(what, e) {
  if (warned) return;
  warned = true;
  console.warn(`music: ${what}${e ? ': ' + (e.message || e) : ''}; staying silent`);
}

// ---------------------------------------------------------------- manifest and rotation

/** Turns a manifest ([{file, role}]) into tracks. `file` resolves against `base`; an entry with an absolute
 *  `url` (the single-file bundle) keeps it. Entries with an unknown role or no location are dropped. */
export function parseManifest(json, base) {
  if (!Array.isArray(json)) throw new Error('manifest is not a list of tracks');
  const out = [];
  for (const e of json) {
    if (!e || !ROLES.includes(e.role)) continue;
    const where = typeof e.url === 'string' ? e.url : e.file;
    if (typeof where !== 'string' || !where) continue;
    const t = { id: typeof e.file === 'string' ? e.file : where, role: e.role, url: base ? new URL(where, base).href : where };
    if (e.role === 'boss' && typeof e.boss === 'string' && e.boss) t.boss = e.boss;   // a boss field on another role, or not a name, is ignored
    out.push(t);
  }
  return out;
}

const ofRole = (role) => (tracks ?? []).filter((t) => t.role === role);

function shuffled(list, avoidFirst) {
  const a = list.slice();
  for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(random() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; }
  if (a.length > 1 && a[0] === avoidFirst) [a[0], a[1]] = [a[1], a[0]];
  return a;
}

/** A new run: a fresh shuffle, never starting on the track played last. */
function newRun() {
  rot.order = shuffled(ofRole('gameplay'), rot.last);
  rot.i = 0;
  return (rot.last = rot.order[0] ?? null);
}

/** The next rotation track; at the end of the order, a reshuffle that does not repeat the track just played. */
function advance() {
  if (!rot.order.length) return newRun();
  if (++rot.i >= rot.order.length) { rot.order = shuffled(ofRole('gameplay'), rot.last); rot.i = 0; }
  return (rot.last = rot.order[rot.i]);
}

const randomTrackOfRole = (role) => { const l = ofRole(role); return l[Math.floor(random() * l.length)] ?? null; };

/** A cue name as the game says it: a state (start, gameplay, boss, gameover), `boss:<key>` (the boss state, for one boss), or
 *  `ahead:boss:<key>` (a hint, no state). Null for anything else. */
function parseCue(name) {
  if (ROLES.includes(name)) return { state: name, key: null, ahead: false };
  const m = /^(ahead:)?boss:(.+)$/.exec(name);
  return m ? { state: 'boss', key: m[2], ahead: !!m[1] } : null;
}
const isBossCue = (name) => typeof name === 'string' && parseCue(name)?.state === 'boss';

// The track picked for a boss key, kept from the hint to the cue so that both name the same file.
const bossPicks = new Map();

/** A boss's track: its own, else a shared track (a boss track with no boss field), else any boss track; never a gameplay one. */
function pickBoss(key) {
  const kept = bossPicks.get(key ?? '');
  if (kept && tracks?.includes(kept)) return kept;
  const boss = ofRole('boss');
  let pool = key ? boss.filter((t) => t.boss === key) : [];
  if (!pool.length) pool = boss.filter((t) => !t.boss);
  if (!pool.length) pool = boss;
  const t = pool[Math.floor(random() * pool.length)] ?? null;
  if (t) bossPicks.set(key ?? '', t);
  return t;
}

function applyCue(name) {
  const c = parseCue(name);
  if (!c || c.ahead || name === want) return;
  let next;
  if (c.state === 'gameplay') next = isBossCue(want) && rot.order.length ? advance() : newRun();
  else if (c.state === 'boss') { next = pickBoss(c.key); bossPicks.delete(c.key ?? ''); }
  else {
    next = randomTrackOfRole(c.state);
    if (next) rot = { order: [], i: -1, last: rot.last }; // start and gameover end the rotation
  }
  if (!next) return; // no track for this role: the music stays as it was
  want = name;
  target = next;
  positions.delete(next.id);
  reconcile();
  if (aheadAllowed) prefetchTarget();
  if (!muted) prefetchAround(name);
}

/** The hint: the stage ahead ends with this boss. Fetches the track the cue will play; plays and changes nothing. */
function applyAhead(name) {
  const c = parseCue(name);
  if (!c?.ahead || muted) return;
  const t = pickBoss(c.key);
  if (t) prefetch(t);
}

// Only the next gameplay track is worth fetching ahead; the start page, boss and game over tracks are fetched when they are cued.
function prefetchAround(name) {
  if (name === 'gameplay') prefetch(...rot.order.slice(rot.i + 1, rot.i + 2));
}

let isThin;   // init sets it: true on a data-saver or slow connection, where nothing is fetched ahead

// ---------------------------------------------------------------- fetching

const blobs = new Map(); // track id -> Promise<string | null>
function fetchTrackBlob(track) {
  if (!blobs.has(track.id)) {
    blobs.set(track.id, (async () => {
      try {
        const r = await fetch(track.url);
        if (!r.ok) throw new Error(`${track.id}: HTTP ${r.status}`);
        return URL.createObjectURL(new Blob([await r.arrayBuffer()], { type: 'audio/mpeg' }));
      } catch (e) {
        warnOnce('could not fetch a track', e);
        return null;
      }
    })());
  }
  return blobs.get(track.id);
}

// A track played through in the rotation will not be wanted again soon: drop it once its element has let go of it.
const spent = new Set();
function evict(id) {
  spent.delete(id);
  const url = blobs.get(id);
  blobs.delete(id);
  url?.then((u) => { if (u) URL.revokeObjectURL(u); });
}

// background fetching, one track at a time, never while muted
const queue = [];
let pumping = false;
function prefetch(...list) {
  if (isThin()) return;
  for (const t of list) if (!blobs.has(t.id) && !queue.includes(t)) queue.push(t);
  pump();
}
async function pump() {
  if (pumping) return;
  pumping = true;
  while (queue.length && !muted && musicOn) await fetchTrackBlob(queue.shift());
  pumping = false;
}

// ---------------------------------------------------------------- playing

const slots = [makeSlot(), makeSlot()];
function makeSlot() {
  return { el: null, node: null, track: null, level: 0, from: 0, target: 0, t0: 0 };
}

function setLevel(s, v) {
  s.level = v;
  if (s.node) s.node.gain.value = v;
  else s.el.volume = Math.min(1, Math.max(0, v * master.gain)); // no Web Audio: the element carries the master volume too
}

let ticker = null;
function fadeTo(s, target, ms = FADE_MS) {
  s.from = s.level; s.target = target; s.t0 = performance.now(); s.ms = ms;
  ticker ??= setInterval(tick, FADE_TICK_MS);
}
function tick() {
  const now = performance.now();
  let busy = false;
  for (const s of slots) {
    if (!s.el || s.level === s.target) continue;
    const p = Math.min(1, (now - s.t0) / s.ms);
    setLevel(s, s.from + (s.target - s.from) * p);
    if (p < 1) busy = true;
    else if (s.target === 0) release(s);
  }
  if (!busy) { clearInterval(ticker); ticker = null; }
}
function release(s) {
  if (s.track) positions.set(s.track.id, s.el.currentTime);
  s.el.pause();
  // a start page, boss or game over track comes round again only after a whole run, so its blob is not held meanwhile; while it is
  // still the wanted track (faded out by a mute) it is kept for the unmute
  const unwanted = s.track && s.track.role !== 'gameplay' && s.track !== target;
  if (s.track && (spent.has(s.track.id) || unwanted)) evict(s.track.id);
  s.track = null;
}
const positions = new Map(); // where a gameplay track was faded out (muted), so unmuting carries on

function createAudio() {
  if (musicOn) {
    for (const s of slots) {
      s.el = new Audio();
      s.el.preload = 'auto';
    }
  }
  const AC = window.AudioContext || window.webkitAudioContext;
  if (!AC) {
    console.info('music: Web Audio unavailable, using element volume');
    return;
  }
  try {
    ctx = new AC();
    masterNode = ctx.createGain();
    masterNode.gain.value = master.gain;
    masterNode.connect(ctx.destination);
    if (musicOn) {
      musicBus = ctx.createGain();
      musicBus.connect(masterNode);
      for (const s of slots) {
        s.node = ctx.createGain();
        s.node.gain.value = 0;
        ctx.createMediaElementSource(s.el).connect(s.node);
        s.node.connect(musicBus);
      }
    }
  } catch (e) {
    // a browser that refuses a context or a media element source (a DOMException such as NotSupportedError) still plays the
    // elements on their own, at element volume; any other error is a fault and goes to unlock's warning
    if (!(e instanceof DOMException)) throw e;
    ctx = masterNode = musicBus = null;
    for (const s of slots) s.node = null;
    console.info('music: Web Audio unavailable, using element volume');
  }
}

/** Runs inside a user gesture: makes the context, wakes both elements (iOS lets an element play later only after it played
 *  once from a gesture) and starts whatever track is wanted. Stays armed until playback really works. */
function unlock() {
  if (off || unlocked) return;
  try {
    if (!ctx && !slots[0].el) createAudio();
    ctx?.resume();
    for (const s of musicOn ? slots : []) {
      if (s.track) continue;
      s.el.src = SILENT;
      s.el.play().then(() => { if (!s.track) s.el.pause(); }, () => { /* best effort: the silent clip only primes iOS, and a track's own play() reports its own failure */ });
    }
    unlocked = true;
    for (const ev of GESTURES) window.removeEventListener(ev, unlock, true);
  } catch (e) {
    warnOnce('audio would not start', e);
    return;
  }
  reconcile();
  watcher ??= setInterval(watch, WATCH_MS);
}
const GESTURES = ['keydown', 'pointerup', 'touchend', 'click', 'mousedown'];

function reconcile() {
  if (!musicOn || !unlocked) return;
  if (muted || !target) {
    for (const s of slots) if (s.el && s.target !== 0) fadeTo(s, 0, CUT_FADE_MS);
    return;
  }
  const active = slots.find((s) => s.target === 1);
  if (active?.track === target) return;
  const track = target;
  fetchTrackBlob(track).then((url) => {
    if (url && target === track && !muted && !tabHidden) startTrack(track, url);
  });
}

function startTrack(track, url, ms = FADE_MS) {
  const active = slots.find((s) => s.target === 1);
  if (active?.track === track) return;
  const incoming = slots.find((s) => s.target === 0 && !s.track) ?? slots.find((s) => s.target === 0);
  try {
    incoming.el.src = url;
    incoming.track = track;
    // only the gameplay rotation plays once through (a lone gameplay track loops); the rest loop until the state changes
    incoming.el.loop = track.role !== 'gameplay' || ofRole('gameplay').length < 2;
    incoming.el.currentTime = track.role === 'gameplay' ? positions.get(track.id) ?? 0 : 0;
    setLevel(incoming, 0);
    const started = incoming.el.play();
    started?.catch((e) => {
      // a play() refused for lack of a gesture: wait for the next one
      if (e?.name === 'NotAllowedError') rearm(); else warnOnce('playback failed', e);
    });
    fadeTo(incoming, 1, ms);
    if (active) fadeTo(active, 0, ms);
  } catch (e) {
    warnOnce('playback failed', e);
  }
}

// Polls the playing rotation track: CROSSFADE_MS before its end the next track fades in as it fades out.
let watcher = null;
function watch() {
  if (advancing || muted || tabHidden || !musicOn || want !== 'gameplay') return;
  const a = slots.find((s) => s.target === 1);
  if (!a?.track || a.track !== target || a.el.loop) return;
  const d = a.el.duration;
  if (!Number.isFinite(d) || d <= 0 || d - a.el.currentTime > CROSSFADE_MS / MS_PER_S) return;
  const next = advance();
  if (!next) return;
  target = next;
  positions.delete(a.track.id);
  spent.add(a.track.id);
  advancing = true;
  fetchTrackBlob(next).then((url) => {
    advancing = false;
    if (url && target === next && !muted && !tabHidden) startTrack(next, url, CROSSFADE_MS);
  });
  prefetch(...rot.order.slice(rot.i + 1, rot.i + 2));
}

function rearm() {
  unlocked = false;
  for (const s of slots) { s.target = 0; s.track = null; }
  for (const ev of GESTURES) window.addEventListener(ev, unlock, true);
}

// ---------------------------------------------------------------- controls

function applyMasterChange() {
  if (masterNode) masterNode.gain.setTargetAtTime(master.gain, ctx.currentTime, MASTER_SMOOTH_S);
  else for (const s of slots) if (s.el) setLevel(s, s.level);
  syncUi();
}

function setVolume(v) {
  volume = Math.min(1, Math.max(0, v));
  store.set(VOLUME_KEY, String(volume));
  applyMasterChange();
}

export function setMuted(m) {
  muted = !!m;
  store.set(MUTED_KEY, muted ? '1' : '0');
  applyMasterChange();
  reconcile();
  if (!muted && want) prefetchAround(want);
}

const toggleMute = () => setMuted(!muted);

/** Dips the music (not the effects) by `db` for a big sound: DUCK_IN_S down, held `holdMs`, then DUCK_OUT_S back up. */
export function duck(db, holdMs = DUCK_HOLD_MS) {
  if (!musicBus || !ctx) return;
  try {
    const g = musicBus.gain, t = ctx.currentTime, low = 10 ** (Math.min(0, db) / DB_PER_TENFOLD_AMPLITUDE);
    g.cancelScheduledValues(t);
    g.setValueAtTime(g.value, t);
    g.linearRampToValueAtTime(low, t + DUCK_IN_S);
    g.setValueAtTime(low, t + DUCK_IN_S + holdMs / MS_PER_S);
    g.linearRampToValueAtTime(1, t + DUCK_IN_S + holdMs / MS_PER_S + DUCK_OUT_S);
  } catch { /* best effort: a missed duck leaves the music at full volume, which harms nothing */ }
}

let button = null, slider = null;
function syncUi() {
  if (button) {
    button.setAttribute('aria-pressed', String(muted));
    button.title = muted ? 'Sound is off (M)' : 'Sound is on (M)';
    button.classList.toggle('muted', muted);
  }
  if (slider) slider.value = String(Math.round(volume * SLIDER_STEPS));
}

function installControls() {
  button = document.getElementById('mute');
  slider = document.getElementById('vol');
  for (const el of [button, slider]) {
    if (!el) continue;
    el.hidden = false;
    // a focused control would eat the game's Space and arrow keys (the page ignores keys while an input has focus); in the
    // pause menu the game is paused and the controls keep focus like any dialog's
    // in the spectator view they sit in the header with no game to disturb, so they stay in the tab order
    if (el.closest('dialog') || el.closest('header')) continue;
    el.tabIndex = -1;
    el.addEventListener('mousedown', () => setTimeout(() => el.blur(), 0));
    el.addEventListener('change', () => el.blur());
  }
  button?.addEventListener('click', () => { toggleMute(); if (!button.closest('dialog') && !button.closest('header')) button.blur(); });
  slider?.addEventListener('input', () => { setVolume(Number(slider.value) / SLIDER_STEPS); if (muted && Number(slider.value) > 0) setMuted(false); });
  window.addEventListener('keydown', (e) => {
    if (e.code !== 'KeyM' || e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
    if (['INPUT', 'TEXTAREA'].includes(document.activeElement?.tagName) && document.activeElement !== slider) return;
    toggleMute();
  });
  syncUi();
}

document.addEventListener('visibilitychange', () => { if (!document.hidden && ctx?.state === 'suspended') ctx.resume().catch(() => { /* best effort: a suspended context is resumed by the next gesture or the next track start */ }); });

// ---------------------------------------------------------------- the page's calls

/** tracks: [{role, url}] with absolute URLs (the single-file bundle), or manifestUrl: the tracks.json of {file, role}, relative
 *  to the page. Neither leaves the music off; thin() says whether the connection is data-saver or slow; sfx: true keeps the controls and the master gain even then.
 *  Returns whether the music is on. */
export function init({ tracks: given, manifestUrl, sfx = false, thin = () => false } = {}) {
  isThin = thin;
  musicOn = !!(given || manifestUrl);
  off = !musicOn && !sfx;
  if (off) return false;
  installControls();
  for (const ev of GESTURES) window.addEventListener(ev, unlock, true);
  if (!musicOn) return false;
  const ready = (list) => {
    tracks = list;
    if (requested) applyCue(requested);
    if (aheadRequested) applyAhead(aheadRequested);
  };
  try {
    if (given) ready(parseManifest(given));
    else {
      const url = new URL(manifestUrl, document.baseURI).href;
      fetch(url).then((r) => { if (!r.ok) throw new Error(`tracks.json: HTTP ${r.status}`); return r.json(); })
        .then((j) => ready(parseManifest(j, url)))
        .catch((e) => warnOnce('could not load the track list', e));
    }
  } catch (e) {
    warnOnce('bad track list', e);
  }
  return true;
}

// Before the first gesture no track is downloaded until the page says the game has loaded, so the music never competes with
// the game's own download; from then the cued track is fetched ahead and starts at once on the gesture.
let aheadAllowed = false;
function prefetchTarget() {
  if (target && !muted) prefetch(target);
}

/** The game has loaded: the cued track may be fetched ahead of the first gesture (never on a data-saver or slow connection). */
export function allowFetchAhead() {
  aheadAllowed = true;
  if (musicOn) prefetchTarget();
}

/** Test hook: the random source for shuffling and picking. */
export function setRandom(fn) { random = fn; }

/** The page's tab went to the background (true) or came back (false): the tracks pause where they are and carry on from there. */
export function setPaused(hidden) {
  hidden = !!hidden;
  if (hidden === tabHidden) return;
  tabHidden = hidden;
  if (!musicOn) return;
  for (const s of slots) {
    if (!s.el || !s.track) continue;
    if (hidden) s.el.pause();
    else if (!muted) s.el.play()?.catch(() => { /* best effort: a refused resume leaves the music paused, which harms nothing */ });
  }
  if (!hidden) reconcile();
}

/** The game says which state it is in: start, gameplay, boss (or boss:<key> for one boss) or gameover; `ahead:boss:<key>` is a hint
 *  to fetch that boss's track, not a state. Never throws. */
export function cue(name) {
  try {
    if (!musicOn || typeof name !== 'string') return;
    if (parseCue(name)?.ahead) {
      aheadRequested = name;
      if (tracks) applyAhead(name);
      return;
    }
    if (!parseCue(name)) return;
    requested = name;
    if (tracks) applyCue(name);
  } catch (e) {
    warnOnce('cue failed', e);
  }
}
