// Page logic that needs no DOM or network of its own, so node can test it. main.js and the atproto.js bundle both import it.

export const ENTRYWAY = 'https://bsky.social';
export const TYPEAHEAD_URL = 'https://public.api.bsky.app/xrpc/app.bsky.actor.searchActorsTypeahead';

// What a typed handle means: "@Alice", " alice " and "alice" all become alice.bsky.social; a dotted name, a DID or a URL stays
// as typed (bar the @, spaces and case of a handle). Empty stays empty.
export function normalizeHandle(raw) {
  const h = String(raw ?? '').trim().replace(/^@+/, '').trim();
  if (!h) return '';
  if (/^(did:|https?:\/\/)/i.test(h)) return h;
  const lower = h.toLowerCase();
  return lower.includes('.') ? lower : `${lower}.bsky.social`;
}

// Where sign-in goes: the typed account, or with an empty box the bsky.social entryway, whose own page offers the account picker.
export const signInTarget = (raw) => normalizeHandle(raw) || ENTRYWAY;

// Whole-number percent of the boot resources downloaded. Held below 100 until the runtime itself says it is ready.
export function loadPercent(loaded, total) {
  if (!(total > 0) || !(loaded >= 0)) return null;
  return Math.min(99, Math.floor((loaded / total) * 100));
}

const NO_LIKES_NOTE = 'Without signing in, liking is off.';

// What the start page shows: three equal choices. Sign in with Bluesky (or, once signed in, Play as @handle), Play as someone (a
// typed handle; its follows are the wingmen) and Random (posts from public comedy feeds). `load` is { status: 'loading' | 'ready' |
// 'failed', percent: number | null }; `queued` is 'play', 'guest' or 'random' once a start was pressed before the runtime was ready.
// `busy` is a handle check in flight; `authPending` is the OAuth client still starting; `handle` is the signed-in account (restored
// session or back from the redirect), or null.
export function startView({ loginAvailable, handle, load = { status: 'ready', percent: null }, queued = null, busy = false, authPending = false }) {
  const mode = !loginAvailable ? 'guest-only' : handle ? 'signed-in' : 'signed-out';
  const ready = load.status === 'ready', failed = load.status === 'failed';
  const pct = load.percent == null ? null : load.percent;
  const loadingLabel = pct == null ? 'loading…' : `loading ${pct}%`;
  const startDisabled = busy || !!queued || failed;
  return {
    mode,
    load: {
      hidden: ready,
      failed,
      text: failed ? 'The game could not load. Check your connection, then retry.' : ready ? 'Ready.' : pct == null ? 'Loading the game…' : `Loading the game… ${pct}%`,
      percent: ready ? 100 : failed ? 0 : pct, // null: the bar has no numbers to show and moves on its own
      retry: failed,
    },
    cardSignIn: mode !== 'guest-only',
    signIn: mode === 'signed-out',
    signInDisabled: authPending || busy || !!queued,
    guestLabel: queued === 'guest' ? loadingLabel : 'Play as someone',
    guestDisabled: startDisabled,
    randomLabel: queued === 'random' ? loadingLabel : 'Play random',
    randomDisabled: startDisabled,
    signedIn: mode === 'signed-in',
    signOut: mode === 'signed-in',
    playLabel: handle ? (queued === 'play' ? loadingLabel : `Play as @${handle}`) : null,
    playDisabled: startDisabled,
    hint: mode === 'guest-only' ? `Sign-in is not available on this page (it needs HTTPS). ${NO_LIKES_NOTE}` : NO_LIKES_NOTE,
  };
}

// What pressing "Play as someone" means: the handle to check, or an error to show. An empty box asks for a handle; a DID or a URL is not one.
export function guestTarget(raw) {
  const h = normalizeHandle(raw);
  if (!h) return { error: 'Type a handle to play as, e.g. someone.bsky.social.' };
  if (/^(did:|https?:)/i.test(h)) return { error: 'Type a handle, e.g. someone.bsky.social.' };
  return { handle: h };
}

// els: the start page's elements. Each takes `hidden`; load and bar take `className`; the bar `setAttribute`/`removeAttribute`
// and its fill `style`; the buttons `disabled` and `textContent`.
export function renderStart(els, view) {
  els.load.hidden = view.load.hidden;
  els.load.className = view.load.failed ? 'failed' : '';
  els.loadText.textContent = view.load.text;
  els.retry.hidden = !view.load.retry;
  els.bar.hidden = view.load.failed;
  els.bar.className = view.load.percent == null ? 'indeterminate' : '';
  els.barFill.style.width = view.load.percent == null ? '' : `${view.load.percent}%`;
  if (view.load.percent == null) els.bar.removeAttribute('aria-valuenow');
  else els.bar.setAttribute('aria-valuenow', String(view.load.percent));
  els.cardSignIn.hidden = !view.cardSignIn;
  els.signIn.hidden = !view.signIn;
  els.signIn.disabled = view.signInDisabled;
  els.guest.textContent = view.guestLabel;
  els.guest.disabled = view.guestDisabled;
  els.random.textContent = view.randomLabel;
  els.random.disabled = view.randomDisabled;
  els.hint.textContent = view.hint;
  els.signed.hidden = !view.signedIn;
  els.signOut.hidden = !view.signOut;
  if (view.playLabel) els.play.textContent = view.playLabel;
  els.play.disabled = view.playDisabled;
}

// Typeahead over the public actor search. input(text) waits `delay` ms of quiet, then asks once; text under `minLength`
// characters asks nothing and clears. A slower, older answer never replaces a newer one. Results are
// { handle, displayName, avatar } and arrive through onResults (an empty list when there is nothing to show).
export function createTypeahead({ fetchImpl = (...a) => fetch(...a), onResults, onError = () => {}, delay = 200, limit = 6, minLength = 2, setTimer = setTimeout, clearTimer = clearTimeout, url = TYPEAHEAD_URL }) {
  let timer = null, seq = 0, ctl = null;
  const cancel = () => {
    if (timer !== null) { clearTimer(timer); timer = null; }
    if (ctl) { ctl.abort(); ctl = null; }
    seq++;
  };
  async function search(q, mine) {
    ctl = typeof AbortController === 'function' ? new AbortController() : null;
    try {
      const res = await fetchImpl(`${url}?q=${encodeURIComponent(q)}&limit=${limit}`, ctl ? { signal: ctl.signal } : undefined);
      if (!res.ok) throw new Error(`typeahead answered ${res.status}`);
      const body = await res.json();
      if (mine !== seq) return;
      onResults((body.actors || []).slice(0, limit).map((a) => ({ handle: a.handle, displayName: a.displayName || '', avatar: a.avatar || '' })));
    } catch (e) {
      if (mine === seq && e?.name !== 'AbortError') { onResults([]); onError(e); }
    }
  }
  return {
    input(text) {
      cancel();
      const q = String(text ?? '').trim().replace(/^@+/, '');
      if (q.length < minLength) { onResults([]); return; }
      const mine = seq;
      timer = setTimer(() => { timer = null; search(q, mine); }, delay);
    },
    cancel,
  };
}

// Arrow keys over a suggestion list of `count` rows; -1 is none. Down from the last row and up from the first go back to none.
export function stepActive(active, count, key) {
  if (!count) return -1;
  if (key === 'ArrowDown') return active + 1 >= count ? -1 : active + 1;
  if (key === 'ArrowUp') return active <= -1 ? count - 1 : active - 1;
  return active;
}

export class SignInCancelled extends Error {
  constructor() { super('The sign-in window was closed.'); this.name = 'SignInCancelled'; }
}

// The start page's line for a sign-in that failed: the callback, the token exchange or the session restore.
export function signInFailed(e) {
  const reason = String((e instanceof Error ? e.message : e) || 'unknown error').trim().replace(/[.\s]+$/, '') || 'unknown error';
  return `Sign-in didn't finish: ${reason}. Try again.`;
}

// True when the page URL carries an OAuth answer (state with code or error). The library asks for it in the fragment; the
// query is accepted too.
export function isOAuthCallback(href) {
  const u = new URL(href);
  return [new URLSearchParams(u.hash.slice(1)), u.searchParams].some((p) => p.has('state') && (p.has('code') || p.has('error')));
}

// The popup's sign-in carries this prefix and a random key in its OAuth app state; the page that receives the callback hands
// the result back over a BroadcastChannel of this name.
export const POPUP_STATE = 'feed-raider-popup:';
export const POPUP_CHANNEL = 'feed-raider-signin';
const makeChannel = (name) => new BroadcastChannel(name);

// The popup key in a callback's app state, or null when the sign-in was not a popup's.
export const popupKey = (appState) => (typeof appState === 'string' && appState.startsWith(POPUP_STATE) ? appState.slice(POPUP_STATE.length) || null : null);

// The callback page tells the opener how it went: { sub } once the session is saved, or { error }. Resolves true when the opener
// answered within `waitMs`; false when no page was listening (an opener frozen in a background tab, or gone).
export function handOff(key, result, { channel = makeChannel, waitMs = 500, setTimer = setTimeout, clearTimer = clearTimeout } = {}) {
  const ch = channel(POPUP_CHANNEL);
  return new Promise((resolve) => {
    const done = (acked) => {
      clearTimer(timer);
      ch.removeEventListener('message', onMessage);
      ch.close();
      resolve(acked);
    };
    const onMessage = ({ data }) => { if (data?.key === key && data.ack) done(true); };
    ch.addEventListener('message', onMessage);
    const timer = setTimer(() => done(false), waitMs);
    ch.postMessage({ key, ...result });
  });
}

// The callback page's work once the authorization server has answered: `callback()` exchanges the code and saves the session
// (resolving { state }), `adopt()` restores and shows it (resolving the account's DID). A popup's callback hands the result, or
// the reason it failed at any step, back to the page that opened it and closes once that page answers; without the reason
// that page would wait on a session that never comes. A redirect's callback hands nothing back. Resolves callback()'s result, or
// rejects with the step's error, after handing it back.
export async function completeCallback({ callback, adopt, handOff: hand = handOff, close = () => globalThis.window?.close() }) {
  let key = null;
  try {
    const result = await callback();
    key = popupKey(result?.state);
    const sub = await adopt();
    if (key && await hand(key, { sub })) close();
    return result;
  } catch (e) {
    key ??= popupKey(e?.state);
    if (key && await hand(key, { error: e instanceof Error ? e.message : String(e) })) close();
    throw e;
  }
}

// Calls wake() when the page comes back to the front (window focus, or the document turning visible: a phone tab switch) or
// another page of the site writes storage, as the callback page does when it restores the session it saved.
function watchWake(wake) {
  const w = globalThis.window, d = globalThis.document;
  if (!w || !d) return () => {};
  const onVisible = () => { if (d.visibilityState === 'visible') wake(); };
  w.addEventListener('focus', wake);
  w.addEventListener('storage', wake);
  d.addEventListener('visibilitychange', onVisible);
  return () => { w.removeEventListener('focus', wake); w.removeEventListener('storage', wake); d.removeEventListener('visibilitychange', onVisible); };
}

// While this page is signed out, a session another page of the site saved (a popup whose sign-in this page stopped waiting for,
// or a sign-in in another tab) is adopted when that page writes storage: `restore()` resolves it or null, `onSession` shows it.
// A restore that fails leaves the page as it is. Returns a function that stops watching.
export function watchSaved({ restore, onSession, signedIn = () => false, target = globalThis.window }) {
  if (!target) return () => {};
  let checking = false, again = false, stopped = false;
  const check = async () => {
    if (stopped || signedIn()) return;
    if (checking) { again = true; return; }
    checking = true;
    try {
      do {
        again = false;
        try {
          const s = await restore();
          if (s && !stopped) return onSession(s);
        } catch { /* nothing usable was saved */ }
      } while (again && !stopped && !signedIn());
    } finally {
      checking = false;
    }
  };
  target.addEventListener('storage', check);
  return () => { stopped = true; target.removeEventListener('storage', check); };
}

// Signs in with a popup, so the page (and the runtime download) stays loaded, and falls back to the full-page redirect where a
// popup cannot work: iOS standalone, no window.open, or a blocked popup. The window is opened here, in the click, before the
// authorization request is made, so a popup blocker sees the user's gesture.
// The authorization server sends Cross-Origin-Opener-Policy: same-origin, which cuts the popup off from this page: `closed`
// reads true a moment after it loads and the popup loses its opener. So closed is no sign of a cancel. The callback page
// hands back over a BroadcastChannel instead, and since a phone's popup is a tab that may come back to a frozen page,
// `restore()` (the session the callback saved, or null) is asked whenever the popup reads closed, a hand-off arrives, this
// page regains focus or visibility or sees storage change, and every `restoreMs` besides, so a lost hand-off is no hang.
// Resolves { via: 'popup', session }; { via: 'redirect' } is what a test sees, for in a browser the redirect leaves the page. `onWaiting` runs once the popup shows the authorization page.
// Rejects with the callback's error, or SignInCancelled after `timeoutMs` or on `signal`.
export async function signIn(client, raw, { scope, state, open = globalThis.window?.open?.bind(globalThis.window), standalone = false, restore = async () => null, onWaiting = () => {}, signal, channel = makeChannel, watch = watchWake, popupName = 'feed-raider-signin', popupFeatures = 'width=500,height=700,menubar=no,toolbar=no', pollMs = 500, restoreMs = 2000, ackMs = 10e3, timeoutMs = 5 * 60e3 } = {}) {
  const target = signInTarget(raw);
  let probe = null;
  if (!standalone && typeof client.authorize === 'function' && typeof open === 'function') {
    try { probe = open('about:blank', popupName, popupFeatures) || null; } catch { probe = null; }
  }
  if (!probe) {
    await client.signInRedirect(target, { scope, state });
    return { via: 'redirect' };
  }
  const key = Math.random().toString(36).slice(2);
  try {
    const url = await client.authorize(target, { scope, state: POPUP_STATE + key, display: 'popup' });
    signal?.throwIfAborted();
    if (probe.closed) throw new SignInCancelled();
    probe.location.href = url.href;
    probe.focus?.();
  } catch (e) {
    try { if (!probe.closed) probe.close(); } catch { /* already gone */ }
    throw signal?.aborted ? new SignInCancelled() : e;
  }
  onWaiting();
  const session = await new Promise((resolve, reject) => {
    const ch = channel(POPUP_CHANNEL);
    let finished = false, checking = false, again = false, sawClosed = false, handedOff = false;
    const finish = (settle, value) => {
      if (finished) return;
      finished = true;
      clearInterval(poll);
      clearInterval(asking);
      clearTimeout(timer);
      const hangUp = () => { ch.removeEventListener('message', onMessage); ch.close(); };
      // a session found before its hand-off came: the popup is still to hand off, and closes only once answered
      if (settle === resolve && !handedOff) setTimeout(hangUp, ackMs); else hangUp();
      unwatch();
      signal?.removeEventListener('abort', onAbort);
      settle(value);
    };
    // one restore at a time; a wake during one asks again once it is done, since the session may have landed meanwhile
    const check = async () => {
      if (finished) return;
      if (checking) { again = true; return; }
      checking = true;
      try {
        do {
          again = false;
          const s = await restore();
          if (s) return finish(resolve, s);
        } while (again && !finished);
      } catch (e) {
        finish(reject, e);
      } finally {
        checking = false;
      }
    };
    const onMessage = ({ data }) => {
      if (data?.key !== key || data.ack) return;
      handedOff = true;
      ch.postMessage({ key, ack: true });
      if (finished) return;
      if (data.error) finish(reject, new Error(data.error)); else check();
    };
    const onAbort = () => finish(reject, new SignInCancelled());
    ch.addEventListener('message', onMessage);
    signal?.addEventListener('abort', onAbort);
    const unwatch = watch(check);
    const poll = setInterval(() => { if (probe.closed && !sawClosed) { sawClosed = true; check(); } }, pollMs);
    const asking = setInterval(check, restoreMs);
    const timer = setTimeout(onAbort, timeoutMs);
    if (signal?.aborted) onAbort(); // given up while the popup was being shown
  });
  return { via: 'popup', session };
}

// The OAuth client metadata document sits beside index.html, whatever the page URL's file name, query or fragment.
export const metadataUrl = (pageHref) => new URL('./client-metadata.json', pageHref);

// --- avatars: the Bluesky CDN sends no CORS headers, so a page cannot read its pixels; the author's own PDS can serve the blob ---

// Where an author's DID document lives (plc.directory, or a did:web host's well-known file), or null for a DID we cannot resolve.
export function didDocUrl(did) {
  if (typeof did !== 'string') return null;
  if (did.startsWith('did:plc:')) return 'https://plc.directory/' + did;
  const host = did.startsWith('did:web:') ? decodeURIComponent(did.slice('did:web:'.length)) : '';
  return host && !host.includes(':') && !host.includes('/') ? `https://${host}/.well-known/did.json` : null;
}

// The blob CID a CDN avatar URL (.../img/avatar/plain/<did>/<cid>@jpeg) already names, so the PDS can be asked for it directly.
export function avatarCid(url) {
  const m = /\/img\/avatar[^/]*\/[^/]+\/did:[^/]+\/([a-z0-9]+)(?:@[a-z]+)?$/i.exec(typeof url === 'string' ? url : '');
  return m ? m[1] : null;
}

// Without an inlined manifest the music is music/fr-<cue>.mp3 beside the page.

// --- game input, kept free of the DOM so node can test it ---

export const GAME_KEYS = new Set(['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Space', 'KeyF', 'KeyB', 'KeyE']);

// The game key a keyboard event means, or null. event.code is the physical key; when a browser or input method leaves it empty
// or odd (code "Unidentified", an IME), the typed letter still names the key.
export function gameKey(e) {
  if (GAME_KEYS.has(e.code)) return e.code;
  const k = typeof e.key === 'string' ? e.key : '';
  if (k === ' ') return 'Space';
  if (/^[a-z]$/i.test(k) && GAME_KEYS.has('Key' + k.toUpperCase())) return 'Key' + k.toUpperCase();
  return GAME_KEYS.has(k) ? k : null;
}

// True when the focused element takes typed text, so game keys must leave it alone. A button, the volume slider or xterm's
// own helper textarea does not count: with one of those focused the game still has to hear B.
export function isTextEntry(el) {
  if (!el) return false;
  if (el.tagName === 'TEXTAREA') return !String(el.className || '').includes('xterm-helper-textarea');
  if (el.tagName !== 'INPUT') return !!el.isContentEditable;
  return !['button', 'submit', 'reset', 'checkbox', 'radio', 'range', 'color', 'file', 'image'].includes((el.type || 'text').toLowerCase());
}

export const TAP_MS = 350;      // a press shorter than this that barely moved is a tap
export const SLOP = 12;         // pixels of travel before a touch is a drag, not a press

// Turns mouse and touch events into the host calls. A press only starts a hold; the game fills the charge (450 ms) and likes
// when it is full, so a tap or click never likes. host: { holdStart(cell), holdEnd(), tap(cell), bomb() }, cells are [col, row].
export function createGestures(host, now = () => performance.now()) {
  let t = null; // the touch in progress: { at, x, y, cell, moved, fingers }
  return {
    mouseDown(button, cell) {
      if (button === 0) { host.holdStart(cell); host.tap(cell); } else if (button === 2) host.bomb();
    },
    mouseUp(button) { if (button === 0) host.holdEnd(); },
    mouseLeave() { host.holdEnd(); },
    touchStart(count, x, y, cell) {
      if (count === 1) { t = { at: now(), x, y, cell, moved: 0, fingers: 1 }; host.holdStart(cell); return; }
      if (t) t.fingers = Math.max(t.fingers, count);
      host.holdEnd();
    },
    // dx, dy: how far the finger went since the last move event, in pixels
    touchMove(count, dx, dy) {
      if (!t) return;
      if (count !== 1) { t.moved += 99; host.holdEnd(); return; }
      t.moved += Math.abs(dx) + Math.abs(dy);
      if (t.moved >= SLOP) host.holdEnd();
    },
    touchEnd(remaining) {
      host.holdEnd();
      if (remaining > 0 || !t) return;
      const quick = now() - t.at < TAP_MS && t.moved < SLOP;
      if (quick && t.fingers >= 2) host.bomb(); else if (quick) host.tap(t.cell);
      t = null;
    },
    touchCancel() { host.holdEnd(); t = null; },
  };
}

// --- the pause menu: Esc or the gear opens it during a run; it pauses the game and can end the run ---

// What a key means for the pause menu: 'open', 'close' or null. Only during a run, so the start page keeps Esc for its list.
export function menuKey(e, { playing, open }) {
  if (!playing || e.repeat || e.ctrlKey || e.metaKey || e.altKey) return null;
  if (e.key !== 'Escape' && e.key !== 'Esc' && e.code !== 'Escape') return null;
  return open ? 'close' : 'open';
}

// The menu's state, free of the DOM. host: { show(), hide(), setPaused(on), releaseKeys(), quit(), toStart() }. closed() is the
// dialog's own close event, however it closed (the browser's Esc, the Resume button, close()); it resumes a run that is still on.
export function createMenu(host) {
  let playing = false, open = false;
  return {
    isOpen: () => open,
    isPlaying: () => playing,
    started() { playing = true; open = false; },
    open() {
      if (!playing || open) return;
      open = true;
      host.releaseKeys();
      host.setPaused(true);
      host.show();
    },
    close() {
      if (!open) return;
      host.hide();
      this.closed();
    },
    closed() {
      if (!open) return;
      open = false;
      if (playing) host.setPaused(false);
    },
    // ends the run (the host's quit lets go of the pause too) and shows the start page
    backToMenu() {
      if (!playing) return;
      playing = false;
      open = false;
      host.hide();
      host.quit();
      host.toStart();
    },
  };
}

// The WebGL addon (0.18) keeps every glyph it has drawn, per glyph, foreground and background, on texture pages. At its page
// cap (the GPU's texture units, at least 16 in WebGL2) it merges four pages into one and shifts the rest down a slot, and it
// re-uploads a slot only when the page there has a different version number: a page that lands in a slot whose old page had
// the same number keeps the old texture, and cells draw pieces of other glyphs until the page is cleared. The game's
// true-colour cells (avatars, the dimmed log, the backdrop) fill the cap in two to four minutes of play. watchAtlas counts
// the pages from the addon's events and calls renew once, at ATLAS_PAGE_LIMIT pages or at the first merge; the page then
// swaps in a fresh addon, which starts with a one-page atlas.
export const ATLAS_PAGE_LIMIT = 14;
export function watchAtlas(addon, renew, limit = ATLAS_PAGE_LIMIT) {
  let pages = 1, asked = false;
  const ask = () => { if (!asked) { asked = true; renew(); } };
  addon.onChangeTextureAtlas(() => { pages = 1; });
  addon.onAddTextureAtlasCanvas(() => { if (++pages >= limit) ask(); });
  addon.onRemoveTextureAtlasCanvas(() => { pages--; ask(); });
}

export const FIELD_MAX_COLS = 64; // the engine's Tuning.FieldMaxCols: the playfield is centred, at most this many columns
export const GEAR_PX = 44;        // the gear's touch target, in CSS pixels
const SIDE_MIN_PX = 260;          // the narrowest right margin that takes the how-to-play panel

// Where the gear and the widescreen side panel go, in page pixels, from the terminal's grid: cols x rows cells of cellW x cellH
// from (left, top), in a window `width` wide. The field sits as the engine puts it (FieldLayout). With room right of the field
// the gear sits there beside the Stage readout and, with more room, the panel below it; otherwise the gear goes into the HUD
// just left of the longest readout ("STAGE 10") and there is no panel.
export function chromeLayout({ cols, rows, cellW, cellH, left, top, width }) {
  const pc = Math.min(cols, FIELD_MAX_COLS) & ~1;
  const c0 = Math.floor((cols - pc) / 2);
  const fieldRight = left + (c0 + pc) * cellW;
  const room = width - fieldRight;
  let gear;
  if (room >= GEAR_PX + 8) {
    const s = Math.max(28, Math.min(40, Math.round(2 * cellH)));
    gear = { x: fieldRight + 4, y: top, w: s, h: s, size: Math.max(GEAR_PX, s), inField: false };
  } else {
    const stageLeft = left + (c0 + pc - 1 - 'STAGE 10'.length) * cellW;
    const h = Math.round(cellH), w = Math.round(cellH * 1.6);
    gear = { x: stageLeft - cellW - w, y: top, w, h, size: GEAR_PX, inField: true };
  }
  let side = null;
  if (room >= SIDE_MIN_PX) {
    const y = top + gear.h + 16;
    side = { x: fieldRight + 16, y, w: Math.min(room - 32, 440), h: top + rows * cellH - y - 16 };
  }
  return { fieldRight, gear, side };
}
