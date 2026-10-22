// Background music: four states (menu, gameplay, boss, gameover), one track at a time; a change crossfades over FADE_MS between
// two <audio> elements. Tracks come from a manifest of {file, role}. Gameplay plays a shuffled rotation: each track once through,
// crossfading into the next CROSSFADE_MS before its end; a boss interrupts it and, when the boss is gone, the rotation goes on
// with the next track. Each track is fetched whole into a Blob and played from a blob: URL (the host serves no byte ranges, and
// Safari will not stream from such a server). Fetching is lazy, one at a time: a cue is fetched when it is wanted, and only the next gameplay track is fetched ahead
// (never on a data-saver or slow connection). A gameplay track that has been played through is dropped and its blob URL revoked. Nothing here may break the game: any failure
// leaves the page silent and logs once.
// With Web Audio available the elements play through a shared master GainNode (`master.node`, which sfx.js plays through too).
// iOS ignores element.volume, so without it volume would not work there. The page can run the controls and the master with
// the music itself off (init without tracks, or off: true) so that the sound effects keep the speaker button, M and the slider.

const FADE_MS = 1500, CROSSFADE_MS = 2000, WATCH_MS = 250;
const VOLUME_KEY = 'feed-raider.music.volume', MUTED_KEY = 'feed-raider.music.muted';
const SILENT = 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=';
const ROLES = ['menu', 'gameplay', 'boss', 'gameover'];

const store = {
  get(k) { try { return localStorage.getItem(k); } catch { return null; } },
  set(k, v) { try { localStorage.setItem(k, v); } catch { /* private mode: not persisted */ } },
};
const storedVolume = Number.parseFloat(store.get(VOLUME_KEY));
let volume = Number.isFinite(storedVolume) ? Math.min(1, Math.max(0, storedVolume)) : 0.5;
let muted = store.get(MUTED_KEY) === '1';

let off = true, musicOn = false, unlocked = false, warned = false;   // off: no music and no sound effects either
let tracks = null;       // [{id, role, url}] once the manifest is in
let requested = null;    // the last state the game asked for, kept until the manifest arrives
let want = null;         // the state in force
let target = null;       // the track that should be playing
let rot = { order: [], i: -1, last: null }; // the gameplay rotation; `last` is the track played most recently
let advancing = false, random = Math.random;
let tabHidden = false;   // the page is in a background tab: nothing plays or rotates until it is back
let ctx = null, masterNode = null, musicBus = null;   // musicBus: the tracks only, so duck() leaves the effects alone
const listeners = new Set();

/** The volume every sound shares. `gain` is the effective 0..1 (0 when muted); `node` is the Web Audio master GainNode once
 *  the first gesture has made the context (null before, or without Web Audio). onChange(fn) returns an unsubscribe. */
export const master = {
  get volume() { return volume; },
  get muted() { return muted; },
  get gain() { return muted ? 0 : volume; },
  get context() { return ctx; },
  get node() { return masterNode; },
  onChange(fn) { listeners.add(fn); return () => listeners.delete(fn); },
};

function warnOnce(what, e) {
  if (warned) return;
  warned = true;
  console.warn(`music: ${what}${e ? ': ' + (e.message || e) : ''}; staying silent`);
}

// ---------------------------------------------------------------- manifest and rotation

/** Turns a manifest ([{file, role}] or {tracks: [...]}) into tracks. `file` resolves against `base`; an entry with an absolute
 *  `url` (the single-file bundle) keeps it. Entries with an unknown role or no location are dropped. */
export function parseManifest(json, base) {
  const list = Array.isArray(json) ? json : json?.tracks;
  if (!Array.isArray(list)) throw new Error('manifest is not a list of tracks');
  const out = [];
  for (const e of list) {
    if (!e || !ROLES.includes(e.role)) continue;
    const where = typeof e.url === 'string' ? e.url : e.file;
    if (typeof where !== 'string' || !where) continue;
    out.push({ id: typeof e.file === 'string' ? e.file : where, role: e.role, url: base ? new URL(where, base).href : where });
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

const pick = (role) => { const l = ofRole(role); return l[Math.floor(random() * l.length)] ?? null; };

function applyCue(name) {
  if (!ROLES.includes(name) || name === want) return;
  let next;
  if (name === 'gameplay') next = want === 'boss' && rot.order.length ? advance() : newRun();
  else {
    next = pick(name);
    if (next && name !== 'boss') rot = { order: [], i: -1, last: rot.last }; // menu and gameover end the rotation
  }
  if (!next) return; // no track for this role: the music stays as it was
  want = name;
  target = next;
  positions.delete(next.id);
  reconcile();
  if (!muted) prefetchAround(name);
}

// Only the next gameplay track is worth fetching ahead; the menu, boss and gameover tracks are fetched when they are cued.
function prefetchAround(name) {
  if (name === 'gameplay') prefetch(...rot.order.slice(rot.i + 1, rot.i + 2));
}

/** True on a data-saver or slow connection (where the Network Information API exists), so nothing is fetched ahead. */
function thinConnection() {
  const c = globalThis.navigator?.connection;
  return !!c && (c.saveData === true || ['slow-2g', '2g', '3g'].includes(c.effectiveType));
}

// ---------------------------------------------------------------- fetching

const blobs = new Map(); // track id -> Promise<string | null>
function fetchCue(track) {
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
  url?.then((u) => { if (u) { try { URL.revokeObjectURL(u); } catch { /* already gone */ } } });
}

// background fetching, one track at a time, never while muted
const queue = [];
let pumping = false;
function prefetch(...list) {
  if (thinConnection()) return;
  for (const t of list) if (!blobs.has(t.id) && !queue.includes(t)) queue.push(t);
  pump();
}
async function pump() {
  if (pumping) return;
  pumping = true;
  while (queue.length && !muted && musicOn) await fetchCue(queue.shift());
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
  ticker ??= setInterval(tick, 40);
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
  if (s.track && spent.has(s.track.id)) evict(s.track.id);
  s.track = null;
}
const positions = new Map(); // where a gameplay track was faded out (muted), so unmuting carries on

function prepare() {
  if (musicOn) {
    for (const s of slots) {
      s.el = new Audio();
      s.el.preload = 'auto';
    }
  }
  try {
    const AC = window.AudioContext || window.webkitAudioContext;
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
    // no Web Audio: the elements play on their own
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
    if (!ctx && !slots[0].el) prepare();
    ctx?.resume();
    for (const s of musicOn ? slots : []) {
      if (s.track) continue;
      s.el.src = SILENT;
      s.el.play().then(() => { if (!s.track) s.el.pause(); }, () => {});
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
    for (const s of slots) if (s.el && s.target !== 0) fadeTo(s, 0, 200);
    return;
  }
  const active = slots.find((s) => s.target === 1);
  if (active?.track === target) return;
  const track = target;
  fetchCue(track).then((url) => {
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
  if (!Number.isFinite(d) || d <= 0 || d - a.el.currentTime > CROSSFADE_MS / 1000) return;
  const next = advance();
  if (!next) return;
  target = next;
  positions.delete(a.track.id);
  spent.add(a.track.id);
  advancing = true;
  fetchCue(next).then((url) => {
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

function changed() {
  if (masterNode) masterNode.gain.setTargetAtTime(master.gain, ctx.currentTime, 0.03);
  else for (const s of slots) if (s.el) setLevel(s, s.level);
  for (const fn of listeners) { try { fn(master); } catch { /* a listener's problem */ } }
  syncUi();
}

export function setVolume(v) {
  volume = Math.min(1, Math.max(0, v));
  store.set(VOLUME_KEY, String(volume));
  changed();
}

export function setMuted(m) {
  muted = !!m;
  store.set(MUTED_KEY, muted ? '1' : '0');
  changed();
  reconcile();
  if (!muted && want) prefetchAround(want);
}

export const toggleMute = () => setMuted(!muted);

/** Dips the music (not the effects) by `db` for a big sound: 40 ms down, held `holdMs`, then 400 ms back up. */
export function duck(db = -6, holdMs = 200) {
  if (!musicBus || !ctx) return;
  try {
    const g = musicBus.gain, t = ctx.currentTime, low = 10 ** (Math.min(0, db) / 20);
    g.cancelScheduledValues(t);
    g.setValueAtTime(g.value, t);
    g.linearRampToValueAtTime(low, t + 0.04);
    g.setValueAtTime(low, t + 0.04 + holdMs / 1000);
    g.linearRampToValueAtTime(1, t + 0.04 + holdMs / 1000 + 0.4);
  } catch { /* a missed duck is not worth a warning */ }
}

let button = null, slider = null;
function syncUi() {
  if (button) {
    button.setAttribute('aria-pressed', String(muted));
    button.title = muted ? 'Sound is off (M)' : 'Sound is on (M)';
    button.classList.toggle('muted', muted);
  }
  if (slider) slider.value = String(Math.round(volume * 100));
}

function installControls() {
  button = document.getElementById('mute');
  slider = document.getElementById('vol');
  for (const el of [button, slider]) {
    if (!el) continue;
    el.hidden = false;
    el.tabIndex = -1;
    // a focused control would eat the game's Space and arrow keys (the page ignores keys while an input has focus)
    el.addEventListener('mousedown', () => setTimeout(() => el.blur(), 0));
    el.addEventListener('change', () => el.blur());
  }
  button?.addEventListener('click', () => { toggleMute(); button.blur(); });
  slider?.addEventListener('input', () => { setVolume(Number(slider.value) / 100); if (muted && Number(slider.value) > 0) setMuted(false); });
  window.addEventListener('keydown', (e) => {
    if (e.code !== 'KeyM' || e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
    if (['INPUT', 'TEXTAREA'].includes(document.activeElement?.tagName) && document.activeElement !== slider) return;
    toggleMute();
  });
  syncUi();
}

document.addEventListener('visibilitychange', () => { if (!document.hidden && ctx?.state === 'suspended') ctx.resume().catch(() => {}); });

// ---------------------------------------------------------------- the page's calls

/** tracks: [{role, url}] with absolute URLs (the single-file bundle), or manifestUrl: the tracks.json of {file, role}, relative
 *  to the page. Neither, or off, leaves the music off; sfx: true keeps the controls and the master gain even then.
 *  Returns whether the music is on. */
export function init({ tracks: given, manifestUrl, off: disabled, sfx = false } = {}) {
  musicOn = !disabled && !!(given || manifestUrl);
  off = !musicOn && !sfx;
  if (off) return false;
  installControls();
  for (const ev of GESTURES) window.addEventListener(ev, unlock, true);
  if (!musicOn) return false;
  const ready = (list) => {
    tracks = list;
    if (!muted) prefetch(...ofRole('menu'));
    if (requested) applyCue(requested);
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
    else if (!muted) s.el.play()?.catch(() => {});
  }
  if (!hidden) reconcile();
}

/** The game says which state it is in: menu, gameplay, boss or gameover. Never throws. */
export function cue(name) {
  try {
    if (!musicOn || typeof name !== 'string') return;
    requested = name;
    if (tracks) applyCue(name);
  } catch (e) {
    warnOnce('cue failed', e);
  }
}
