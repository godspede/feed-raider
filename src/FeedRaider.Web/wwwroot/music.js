// Background music: four states (start, gameplay, boss, gameover), one track at a time; a change crossfades over FADE_MS between
// two <audio> elements. Tracks come from a manifest of {file, role}; a boss track may add `boss`, the lower-case name of the one
// boss it belongs to. The game cues a boss as `boss:<key>`: that boss's own track, else a shared boss track (one with no `boss`),
// else any boss track, never a gameplay one. `ahead:boss:<key>` is a hint, no state: it fetches that pick without playing it. Gameplay plays a shuffled rotation: each track once through,
// crossfading into the next CROSSFADE_MS before its end; a boss interrupts it and, when the boss is gone, the rotation goes on
// with the next track. Each track is fetched whole into a Blob and played from a blob: URL, never streamed from its address.
// Fetching is lazy, one at a time: no track before the first gesture or the page's
// allowFetchAhead(), whichever comes first; then a cue is fetched when it is wanted, and only the next gameplay track is fetched ahead
// (never on a data-saver or slow connection). A gameplay track that has been played through, and a start page, boss or game over track
// once it has faded out and is not wanted, is dropped and its blob URL revoked. A track or list that cannot be fetched or
// played leaves the page silent and tells the player once; any other error is a fault and is thrown.
// With Web Audio available the elements play through a shared master GainNode (`master.node`, which sfx.js plays through too).
// iOS ignores element.volume, so without it volume would not work there; a browser without Web Audio, or one that refuses
// it, plays the music at element volume and no sound effects, and the player is told so. The page can run the controls and the master with
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

// Where there is no localStorage (a worker, a test), or it throws a DOMException because the browser blocks it (or, on a save,
// because it is full), the defaults stand and a setting holds for this visit only. Any other error is a bug and is thrown.
const store = {
  get(k) { try { return globalThis.localStorage?.getItem(k) ?? null; } catch (e) { if (!(e instanceof DOMException)) throw e; return null; } },
  set(k, v) { try { globalThis.localStorage?.setItem(k, v); } catch (e) { if (!(e instanceof DOMException)) throw e; } },
};
const storedVolume = Number.parseFloat(store.get(VOLUME_KEY));
let volume = Number.isFinite(storedVolume) ? Math.min(1, Math.max(0, storedVolume)) : DEFAULT_VOLUME;
let muted = store.get(MUTED_KEY) === '1';

let off = true, musicOn = false, unlocked = false;   // off: no music and no sound effects either
let onTrouble = null, told = false;   // onTrouble: init's trouble; told: it has been called, so a run of failures shows one line
let tracks = null;       // [{id, role, url}] once the manifest is in
let requested = null;    // the last state the game asked for, kept until the manifest arrives
let aheadRequested = null; // the last fetch-ahead hint, kept until the manifest arrives
let want = null;         // the cue in force: a state, or boss:<key>
let wantedTrack = null;  // the track that should be playing
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

function tellOnce(what, e) {
  if (told) return;
  told = true;
  onTrouble(`Music: ${what}${e ? ` (${e.message || e})` : ''}. The game plays on without it.`);
}

// Without Web Audio the effects have no context to play in and the volume may not reach the music (iOS), so the player is
// told.
function tellNoWebAudio(why) {
  onTrouble(`Sound: ${why}, so the game plays without sound effects and the volume may not change the music.`);
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
  wantedTrack = next;
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

// A track's blob: URL, fetched once: a promise per track id of the URL, or of null where the track could not be fetched.
const trackUrls = new Map();
function trackUrl(track) {
  if (!trackUrls.has(track.id)) {
    trackUrls.set(track.id, (async () => {
      // null is a track that could not be fetched: the caller plays nothing for it. fetch and the body's read reject with a
      // TypeError when the network is gone; any other error is a fault and is thrown.
      try {
        const r = await fetch(track.url);
        if (r.ok) return URL.createObjectURL(new Blob([await r.arrayBuffer()], { type: 'audio/mpeg' }));
        tellOnce(`could not fetch a track (${track.id}: HTTP ${r.status})`);
        return null;
      } catch (e) {
        if (!(e instanceof TypeError)) throw e;
        tellOnce('could not fetch a track', e);
        return null;
      }
    })());
  }
  return trackUrls.get(track.id);
}

// A track played through in the rotation will not be wanted again soon: drop it once its element has let go of it.
const spent = new Set();
function evict(id) {
  spent.delete(id);
  const url = trackUrls.get(id);
  trackUrls.delete(id);
  url?.then((u) => { if (u) URL.revokeObjectURL(u); });
}

// background fetching, one track at a time, never while muted
const queue = [];
let pumping = false;
function prefetch(...list) {
  if (isThin()) return;
  for (const t of list) if (!trackUrls.has(t.id) && !queue.includes(t)) queue.push(t);
  pump();
}
async function pump() {
  if (pumping) return;
  pumping = true;
  while (queue.length && !muted && musicOn) await trackUrl(queue.shift());
  pumping = false;
}

// ---------------------------------------------------------------- playing

const slots = [makeSlot(), makeSlot()];
function makeSlot() {
  return { el: null, node: null, track: null, level: 0, from: 0, wantedLevel: 0, t0: 0 };
}

function setLevel(s, v) {
  s.level = v;
  if (s.node) s.node.gain.value = v;
  else s.el.volume = Math.min(1, Math.max(0, v * master.gain)); // no Web Audio: the element carries the master volume too
}

let ticker = null;
function fadeTo(s, level, ms = FADE_MS) {
  s.from = s.level; s.wantedLevel = level; s.t0 = performance.now(); s.ms = ms;
  ticker ??= setInterval(tick, FADE_TICK_MS);
}
function tick() {
  const now = performance.now();
  let busy = false;
  for (const s of slots) {
    if (!s.el || s.level === s.wantedLevel) continue;
    const p = Math.min(1, (now - s.t0) / s.ms);
    setLevel(s, s.from + (s.wantedLevel - s.from) * p);
    if (p < 1) busy = true;
    else if (s.wantedLevel === 0) release(s);
  }
  if (!busy) { clearInterval(ticker); ticker = null; }
}
function release(s) {
  if (s.track) positions.set(s.track.id, s.el.currentTime);
  s.el.pause();
  // a start page, boss or game over track comes round again only after a whole run, so its blob is not held meanwhile; while it is
  // still the wanted track (faded out by a mute) it is kept for the unmute
  const unwanted = s.track && s.track.role !== 'gameplay' && s.track !== wantedTrack;
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
    tellNoWebAudio('this browser has no Web Audio');
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
    // elements on their own, at element volume; any other error is a fault and is thrown
    if (!(e instanceof DOMException)) throw e;
    ctx = masterNode = musicBus = null;
    for (const s of slots) s.node = null;
    tellNoWebAudio(`this browser refused Web Audio (${e.message || e.name})`);
  }
}

/** Runs inside a user gesture: makes the context, wakes both elements (iOS lets an element play later only after it played
 *  once from a gesture) and starts whatever track is wanted. Stays armed until playback really works. */
function unlock() {
  if (off || unlocked) return;
  if (!ctx && !slots[0].el) createAudio();
  ctx?.resume();
  for (const s of musicOn ? slots : []) {
    if (s.track) continue;
    s.el.src = SILENT;
    // the silent clip only primes iOS: a refused or cut-off prime leaves a track's own play() to start it or rearm
    s.el.play().then(() => { if (!s.track) s.el.pause(); }, playRefused);
  }
  unlocked = true;
  for (const ev of GESTURES) window.removeEventListener(ev, unlock, true);
  reconcile();
  watcher ??= setInterval(watch, WATCH_MS);
}
const GESTURES = ['keydown', 'pointerup', 'touchend', 'click', 'mousedown'];

// A media element's play() rejects with NotAllowedError where the browser wants a gesture first, and with AbortError when a
// newer src or a pause() cut it off; both are the browser's normal answers and are returned by name. Any other rejection is
// a fault and is thrown on, so the console shows it as an unhandled rejection.
function playRefused(e) {
  if (e?.name === 'NotAllowedError' || e?.name === 'AbortError') return e.name;
  throw e;
}

function reconcile() {
  if (!musicOn || !unlocked) return;
  if (muted || !wantedTrack) {
    for (const s of slots) if (s.el && s.wantedLevel !== 0) fadeTo(s, 0, CUT_FADE_MS);
    return;
  }
  const active = slots.find((s) => s.wantedLevel === 1);
  if (active?.track === wantedTrack) return;
  const track = wantedTrack;
  trackUrl(track).then((url) => {
    if (url && wantedTrack === track && !muted && !tabHidden) startTrack(track, url);
  });
}

function startTrack(track, url, ms = FADE_MS) {
  const active = slots.find((s) => s.wantedLevel === 1);
  if (active?.track === track) return;
  const incoming = slots.find((s) => s.wantedLevel === 0 && !s.track) ?? slots.find((s) => s.wantedLevel === 0);
  incoming.el.src = url;
  incoming.track = track;
  // only the gameplay rotation plays once through (a lone gameplay track loops); the rest loop until the state changes
  incoming.el.loop = track.role !== 'gameplay' || ofRole('gameplay').length < 2;
  incoming.el.currentTime = track.role === 'gameplay' ? positions.get(track.id) ?? 0 : 0;
  setLevel(incoming, 0);
  incoming.el.play().catch((e) => {
    // a track this browser cannot decode (NotSupportedError) stays silent while the rest play; refused for lack of a
    // gesture, the music waits for the next one; cut off by a newer track, that one owns the slot
    if (e?.name === 'NotSupportedError') tellOnce(`a track would not play (${track.id})`, e);
    else if (playRefused(e) === 'NotAllowedError') rearm();
  });
  fadeTo(incoming, 1, ms);
  if (active) fadeTo(active, 0, ms);
}

// Polls the playing rotation track: CROSSFADE_MS before its end the next track fades in as it fades out.
let watcher = null;
function watch() {
  if (advancing || muted || tabHidden || !musicOn || want !== 'gameplay') return;
  const a = slots.find((s) => s.wantedLevel === 1);
  if (!a?.track || a.track !== wantedTrack || a.el.loop) return;
  const d = a.el.duration;
  if (!Number.isFinite(d) || d <= 0 || d - a.el.currentTime > CROSSFADE_MS / MS_PER_S) return;
  const next = advance();
  if (!next) return;
  wantedTrack = next;
  positions.delete(a.track.id);
  spent.add(a.track.id);
  advancing = true;
  trackUrl(next).then((url) => {
    advancing = false;
    if (url && wantedTrack === next && !muted && !tabHidden) startTrack(next, url, CROSSFADE_MS);
  });
  prefetch(...rot.order.slice(rot.i + 1, rot.i + 2));
}

function rearm() {
  unlocked = false;
  for (const s of slots) { s.wantedLevel = 0; s.track = null; }
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
  if (!musicBus) return;
  const g = musicBus.gain, t = ctx.currentTime, low = 10 ** (Math.min(0, db) / DB_PER_TENFOLD_AMPLITUDE);
  g.cancelScheduledValues(t);
  g.setValueAtTime(g.value, t);
  g.linearRampToValueAtTime(low, t + DUCK_IN_S);
  g.setValueAtTime(low, t + DUCK_IN_S + holdMs / MS_PER_S);
  g.linearRampToValueAtTime(1, t + DUCK_IN_S + holdMs / MS_PER_S + DUCK_OUT_S);
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
    // out of the tab order, only a click focuses the control, and its focus event is the moment the browser has given it focus
    el.addEventListener('focus', () => el.blur());
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

// resume() rejects only for a closed context, and this one is never closed; a resume the browser holds back stays pending and
// goes through on the next gesture
document.addEventListener('visibilitychange', () => { if (!document.hidden && ctx?.state === 'suspended') ctx.resume(); });

// ---------------------------------------------------------------- the page's calls

/** tracks: [{role, url}] with absolute URLs (the single-file bundle), or manifestUrl: the tracks.json of {file, role}, relative
 *  to the page. Neither leaves the music off; thin() says whether the connection is data-saver or slow; sfx: true keeps the controls and the master gain even then.
 *  trouble(line) shows the player why the music is silent, the first time a track or the track list fails, and that the
 *  browser has no Web Audio where it has none. trouble is required: every caller must say where those lines go, so init
 *  throws without it rather than fail later at the first trouble. Returns whether the music is on. */
export function init({ tracks: given, manifestUrl, sfx = false, thin = () => false, trouble } = {}) {
  if (typeof trouble !== 'function') throw new TypeError('music init needs a trouble(line) callback');
  onTrouble = trouble;
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
  // The bundle's list is built with the page, so one parseManifest refuses is a fault and is thrown. The fetched one can fail
  // on the way: no network (fetch's TypeError), a refusal (its status) or a body that is not JSON (SyntaxError). Each leaves
  // the music silent and tells the player, and a list that parses but is not a list of tracks is a fault, thrown as well.
  if (given) ready(parseManifest(given));
  else {
    const url = new URL(manifestUrl, document.baseURI).href;
    fetch(url).then(async (r) => {
      if (!r.ok) return tellOnce(`could not load the track list (tracks.json: HTTP ${r.status})`);
      let json;
      try { json = await r.json(); } catch (e) { if (!(e instanceof SyntaxError || e instanceof TypeError)) throw e; return tellOnce('could not load the track list', e); }
      ready(parseManifest(json, url));
    }, (e) => {
      if (!(e instanceof TypeError)) throw e;
      tellOnce('could not load the track list', e);
    });
  }
  return true;
}

// Before the first gesture no track is downloaded until the page says the game has loaded, so the music never competes with
// the game's own download; from then the cued track is fetched ahead and starts at once on the gesture.
let aheadAllowed = false;
function prefetchTarget() {
  if (wantedTrack && !muted) prefetch(wantedTrack);
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
    // a resume the browser refuses without a gesture waits for the next one; one cut off by a newer track leaves that one
    else if (!muted) s.el.play().catch((e) => { if (playRefused(e) === 'NotAllowedError') rearm(); });
  }
  if (!hidden) reconcile();
}

/** The game says which state it is in: start, gameplay, boss (or boss:<key> for one boss) or gameover; `ahead:boss:<key>` is a hint
 *  to fetch that boss's track, not a state. A name it does not know is ignored. */
export function cue(name) {
  if (!musicOn || typeof name !== 'string') return;
  if (parseCue(name)?.ahead) {
    aheadRequested = name;
    if (tracks) applyAhead(name);
    return;
  }
  if (!parseCue(name)) return;
  requested = name;
  if (tracks) applyCue(name);
}
