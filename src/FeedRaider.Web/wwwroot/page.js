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

const SIGN_IN_NOTE = 'Signing in lets you like posts. Nothing else is ever posted.';
const GUEST_NOTE = 'As a guest, your wingmen come from the people you follow; leave the box empty for the public Discover feed.';

// What the start page shows. `load` is { status: 'loading' | 'ready' | 'failed', percent: number | null }; `queued` is 'play' or
// 'guest' once a start was pressed before the runtime was ready; `busy` is a handle check in flight; `authPending` is the
// OAuth client still starting; `handle` is the signed-in account (restored session or back from the redirect), or null.
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
    form: mode !== 'signed-in',
    signIn: mode === 'signed-out',
    signInDisabled: authPending || busy || !!queued,
    guest: mode !== 'signed-in',
    guestPrimary: mode === 'guest-only',
    guestLabel: queued === 'guest' ? loadingLabel : 'Play as guest',
    guestDisabled: startDisabled,
    signedIn: mode === 'signed-in',
    signOut: mode === 'signed-in',
    playLabel: handle ? (queued === 'play' ? loadingLabel : `Play as @${handle}`) : null,
    playDisabled: startDisabled,
    hint: mode === 'guest-only' ? `Sign-in is not available on this page (it needs HTTPS). ${GUEST_NOTE}` : `${SIGN_IN_NOTE} ${GUEST_NOTE}`,
  };
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
  els.form.hidden = !view.form;
  els.signIn.hidden = !view.signIn;
  els.signIn.disabled = view.signInDisabled;
  els.guest.hidden = !view.guest;
  els.guest.className = view.guestPrimary ? 'primary secondary' : 'secondary';
  els.guest.textContent = view.guestLabel;
  els.guest.disabled = view.guestDisabled;
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

// Signs in with the popup, so the page (and the runtime download) stays loaded, and falls back to the full-page redirect where a
// popup cannot work: iOS standalone, no window.open, or a blocked popup. The library opens its popup by name without ever
// reporting a block (it then waits forever), so one is opened here first, in the click, and handed over by name.
// Resolves { via: 'popup', session }; { via: 'redirect' } is what a test sees, for in a browser the redirect leaves the page.
// A popup the user closes rejects with SignInCancelled after `graceMs`, in case the result is still on its way.
export async function signIn(client, raw, { scope, state, open = globalThis.window?.open?.bind(globalThis.window), standalone = false, popupName = 'feed-raider-signin', popupFeatures = 'width=500,height=700,menubar=no,toolbar=no', pollMs = 500, graceMs = 1500 } = {}) {
  const target = signInTarget(raw);
  let probe = null;
  if (!standalone && typeof client.signInPopup === 'function' && typeof open === 'function') {
    try { probe = open('about:blank', popupName, popupFeatures) || null; } catch { probe = null; }
  }
  if (!probe) {
    await client.signInRedirect(target, { scope, state });
    return { via: 'redirect' };
  }
  let poll = null, grace = null;
  try {
    const popup = client.signInPopup(target, { scope, popupName, popupFeatures });
    popup.catch(() => {}); // a popup the user closed never settles; its late failure is nobody's
    const closed = new Promise((_, reject) => {
      poll = setInterval(() => {
        if (probe.closed && grace === null) grace = setTimeout(() => reject(new SignInCancelled()), graceMs);
      }, pollMs);
    });
    return { via: 'popup', session: await Promise.race([popup, closed]) };
  } catch (e) {
    try { if (!probe.closed) probe.close(); } catch { /* already gone */ }
    throw e;
  } finally {
    clearInterval(poll);
    clearTimeout(grace);
  }
}

// The OAuth client metadata document sits beside index.html, whatever the page URL's file name, query or fragment.
export const metadataUrl = (pageHref) => new URL('./client-metadata.json', pageHref);

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
