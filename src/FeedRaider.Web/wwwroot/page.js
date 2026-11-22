// Page logic that needs no DOM or network of its own, so node can test it. main.js and the atproto.js bundle both import it.

export const ENTRYWAY = 'https://bsky.social';
export const PUBLIC_API = 'https://public.api.bsky.app';
const TYPEAHEAD_URL = `${PUBLIC_API}/xrpc/app.bsky.actor.searchActorsTypeahead`;
const TYPEAHEAD_QUIET_MS = 200;   // quiet after the last keystroke before a search is sent
const TYPEAHEAD_LIMIT = 6;        // accounts asked for, and the most shown
const TYPEAHEAD_MIN_CHARS = 2;    // shorter text asks nothing
const MINUTE_MS = 60e3;
const SIGNIN_POLL_MS = 500;              // reading the popup's `closed` costs nothing, so the page looks often and asks soon after it closes
const SIGNIN_RESTORE_MS = 2000;          // each ask restores from storage, so it comes less often; it is what makes a lost hand-off no hang
const SIGNIN_ACK_MS = 10e3;              // a session found before its hand-off keeps the channel open this long, so the popup is answered and closes
const SIGNIN_TIMEOUT_MS = 5 * MINUTE_MS; // a sign-in still unanswered after this is cancelled
const HANDOFF_WAIT_MS = 500;             // how long the callback page waits for the opener's answer; a listening opener answers at once

// What a typed handle means: "@Alice", " alice " and "alice" all become alice.bsky.social; a dotted name, a DID or a URL stays
// as typed (bar the @, spaces and case of a handle). Empty stays empty.
export function normaliseHandle(raw) {
  const h = String(raw ?? '').trim().replace(/^@+/, '').trim();
  if (!h) return '';
  if (/^(did:|https?:\/\/)/i.test(h)) return h;
  const lower = h.toLowerCase();
  return lower.includes('.') ? lower : `${lower}.bsky.social`;
}

// Where sign-in goes: the typed account, or with an empty box the bsky.social entryway, whose own page offers the account picker.
export const signInTarget = (raw) => normaliseHandle(raw) || ENTRYWAY;

// Whole-number percent of the boot resources downloaded. Held below 100 until the runtime itself says it is ready.
const PERCENT_BEFORE_READY = 99, FULL_PERCENT = 100;
export function loadPercent(loaded, total) {
  if (!(total > 0) || !(loaded >= 0)) return null;
  return Math.min(PERCENT_BEFORE_READY, Math.floor((loaded / total) * FULL_PERCENT));
}

const NO_LIKES_NOTE = 'Sign in to like and repost for real.';

// What the start page shows: three equal choices. Sign in with Bluesky (or, once signed in, Play), Play as someone (a
// typed handle; its follows' posts are the feed) and Random (posts from public comedy feeds). `load` is { status: 'loading' | 'ready' |
// 'failed', percent: number | null }; `queued` is 'play', 'guest' or 'random' once a start was pressed before the runtime was ready.
// `busy` is false, or what is in flight: 'signin' (the sign-in started) or any other truthy value (a handle check); `authPending` is the OAuth client still starting; `handle` is the signed-in account (restored
// session or back from the redirect), or null.
export function startView({ signInAvailable, handle, load = { status: 'ready', percent: null }, queued = null, busy = false, authPending = false }) {
  const guestOnly = !signInAvailable, signedIn = !guestOnly && !!handle;
  const mode = guestOnly ? 'guest-only' : signedIn ? 'signed-in' : 'signed-out';
  const ready = load.status === 'ready', failed = load.status === 'failed';
  const pct = load.percent ?? null;
  const loadingLabel = pct == null ? 'loading…' : `loading ${pct}%`;
  const startDisabled = busy || !!queued || failed;
  return {
    mode,
    load: {
      hidden: ready,
      failed,
      text: failed ? 'The game could not load. Check your connection, then retry.' : ready ? 'Ready.' : pct == null ? 'Loading the game…' : `Loading the game… ${pct}%`,
      percent: ready ? FULL_PERCENT : failed ? 0 : pct, // null: the bar has no numbers to show and moves on its own
      retry: failed,
    },
    cardSignIn: !guestOnly,
    signIn: !guestOnly && !signedIn,
    signInDisabled: authPending || busy || !!queued,
    signInLabel: busy === 'signin' ? 'signing in…' : 'Sign in with Bluesky',
    guestLabel: queued === 'guest' ? loadingLabel : busy && busy !== 'signin' ? 'checking…' : 'Play as someone',
    formBusy: !!busy,
    busyNote: busy === 'signin' ? 'Waiting for Bluesky…' : busy ? 'Checking the handle…' : '',
    guestDisabled: startDisabled,
    randomLabel: queued === 'random' ? loadingLabel : 'Play random',
    randomDisabled: startDisabled,
    signedIn,
    signOut: signedIn,
    playLabel: handle ? (queued === 'play' ? loadingLabel : 'Play') : null, // who it plays as is in the small line at the foot
    playDisabled: startDisabled,
    hint: guestOnly ? 'Sign-in is not available on this page (it needs HTTPS or localhost), so likes and reposts here send nothing to Bluesky.' : signedIn ? '' : NO_LIKES_NOTE,
  };
}

// What pressing "Play as someone" means: the handle to check, or an error to show. An empty box asks for a handle; a DID or a URL is not one.
export function guestTarget(raw) {
  const h = normaliseHandle(raw);
  if (!h) return { error: 'Type a handle to play as, e.g. someone.bsky.social.' };
  if (/^(did:|https?:)/i.test(h)) return { error: 'Type a handle, e.g. someone.bsky.social.' };
  return { handle: h };
}

// els: the start page's elements. Each takes `hidden`; load and bar take `className`; the bar `setAttribute`/`removeAttribute`
// and its fill `style`; the optional form takes `setAttribute`; the buttons `disabled` and `textContent`.
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
  els.signIn.textContent = view.signInLabel;
  if (els.form) els.form.setAttribute('aria-busy', String(view.formBusy));
  els.guest.textContent = view.guestLabel;
  els.guest.disabled = view.guestDisabled;
  els.random.textContent = view.randomLabel;
  els.random.disabled = view.randomDisabled;
  els.hint.textContent = view.hint;
  els.signed.hidden = !view.signedIn;
  els.signOut.hidden = !view.signOut;
  els.play.hidden = !view.signedIn;
  if (view.playLabel) els.play.textContent = view.playLabel;
  els.play.disabled = view.playDisabled;
}

// Typeahead over the public actor search. input(text) waits TYPEAHEAD_QUIET_MS of quiet, then asks once; text under TYPEAHEAD_MIN_CHARS
// characters asks nothing and clears. A slower, older answer never replaces a newer one. Results are
// { handle, displayName, avatar } and arrive through onResults (an empty list when there is nothing to show). A list that came from a search is
// passed with a second argument { answered: true }, so an empty answer can be told from a cleared box; a failed search calls onResults([]) and then onError.
export function createTypeahead({ fetchImpl = (...a) => fetch(...a), onResults, onError = () => {}, onSearching = () => {}, setTimer = setTimeout, clearTimer = clearTimeout }) {
  let timer = null, seq = 0, ctl = null;
  const cancel = () => {
    if (timer !== null) { clearTimer(timer); timer = null; }
    if (ctl) { ctl.abort(); ctl = null; }
    seq++;
  };
  async function search(q, mine) {
    onSearching();
    ctl = new AbortController();
    try {
      const res = await fetchImpl(`${TYPEAHEAD_URL}?q=${encodeURIComponent(q)}&limit=${TYPEAHEAD_LIMIT}`, { signal: ctl.signal });
      if (!res.ok) throw new Error(`typeahead answered ${res.status}`);
      const body = await res.json();
      if (mine !== seq) return;
      onResults((body.actors || []).slice(0, TYPEAHEAD_LIMIT).map((a) => ({ handle: a.handle, displayName: a.displayName || '', avatar: a.avatar || '' })), { answered: true });
    } catch (e) {
      if (mine === seq && e?.name !== 'AbortError') { onResults([]); onError(e); }
    }
  }
  return {
    input(text) {
      cancel();
      const q = String(text ?? '').trim().replace(/^@+/, '');
      if (q.length < TYPEAHEAD_MIN_CHARS) { onResults([]); return; }
      const mine = seq;
      timer = setTimer(() => { timer = null; search(q, mine); }, TYPEAHEAD_QUIET_MS);
    },
    cancel,
  };
}

// How long a notice stays when the run goes on after it. A message with no run behind it (an unknown feed, a failed sign-in) is
// shown with sticky() and stays until the player acts.
export const NOTICE_MS = 8000;

// The page's error bar. notice() is for trouble the run survives: it leaves by itself NOTICE_MS later, and a newer message
// replaces it and restarts the wait. sticky() never leaves by itself. Both are spoken once through announce.
export function createErrorBar({ el, announce = () => {}, setTimer = setTimeout, clearTimer = clearTimeout }) {
  let timer = null;
  const cancel = () => { if (timer !== null) { clearTimer(timer); timer = null; } };
  const show = (message) => {
    cancel();
    el.textContent = message;
    el.style.display = 'block';
    announce(message);
  };
  return {
    sticky: show,
    notice(message) {
      show(message);
      timer = setTimer(() => { timer = null; el.style.display = 'none'; }, NOTICE_MS);
    },
    hide() { cancel(); el.style.display = 'none'; },
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

// The line for a sign-in that did not finish because of `reason`, one sentence with its full stop trimmed.
const signInLine = (reason) => `Sign-in did not finish: ${String(reason).trim().replace(/[.\s]+$/, '')}. Try again.`;
// The start page's line for a sign-in that failed: the callback, the token exchange or the session restore. A known library
// failure is put in plain words; any other carries the error's own message, so its cause is never hidden behind a generic line.
export function signInFailed(e) {
  const raw = String((e instanceof Error ? e.message : e) || '');
  const has = (re) => re.test(raw) || re.test(e?.name || '');
  if (has(/resolve|metadata|fetch|network|timed? ?out/i)) return "Sign-in did not finish: could not reach your Bluesky account's server. Check the handle and your connection, then try again.";
  if (has(/popup|window|cancel|closed/i)) return signInLine('the sign-in window was closed');
  if (has(/access_denied|denied|declin/i)) return 'Sign-in did not finish: Bluesky did not allow it. Try again when you are ready to approve.';
  return signInLine(raw || `${e?.name || 'an error'} with no message`);
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
const POPUP_CHANNEL = 'feed-raider-signin';
const makeChannel = (name) => new BroadcastChannel(name);

// The popup key in a callback's app state, or null when the sign-in was not a popup's.
export const popupKey = (appState) => (typeof appState === 'string' && appState.startsWith(POPUP_STATE) ? appState.slice(POPUP_STATE.length) || null : null);

// The callback page tells the opener how it went: { sub } once the session is saved, or { error }. Resolves true when the opener
// answered within `waitMs`; false when no page was listening (an opener frozen in a background tab, or gone).
export function handOff(key, result, { channel = makeChannel, waitMs = HANDOFF_WAIT_MS } = {}) {
  const popupChannel = channel(POPUP_CHANNEL);
  return new Promise((resolve) => {
    const done = (acked) => {
      clearTimeout(timer);
      popupChannel.removeEventListener('message', onMessage);
      popupChannel.close();
      resolve(acked);
    };
    const onMessage = ({ data }) => { if (data?.key === key && data.ack) done(true); };
    popupChannel.addEventListener('message', onMessage);
    const timer = setTimeout(() => done(false), waitMs);
    popupChannel.postMessage({ key, ...result });
  });
}

// The callback page's work once the authorization server has answered: `callback()` exchanges the code and saves the session
// (resolving { state, session: { sub } }), `adopt()` restores and shows it (resolving the account's DID). A popup's callback hands
// the result, or the reason it failed at any step, back to the page that opened it and closes once that page answers; without the
// reason that page would wait on a session that never comes. A redirect's callback hands nothing back. `report(step, extra)` notes
// each step reached in the sign-in report. Resolves callback()'s result, or rejects with the step's error, after handing it back.
export async function completeCallback({ callback, adopt, report = () => {}, handOff: hand = handOff, close = () => globalThis.window?.close() }) {
  let key = null;
  try {
    report('answered');
    const result = await callback();
    key = popupKey(result?.state);
    report('saved', { sub: result?.session?.sub });
    const sub = await adopt();
    report('restored', { sub });
    if (key && await hand(key, { sub })) close();
    return result;
  } catch (e) {
    key ??= popupKey(e?.state);
    const error = e instanceof Error ? e.message : String(e);
    report('failed', { error });
    if (key && await hand(key, { error })) close();
    throw e;
  }
}

// --- the sign-in report: how far the last sign-in got, in this browser's storage, so a page that finds no session can say why ---

// The page that opens the popup notes 'started'. The callback page notes 'answered' once the authorization server's answer
// arrives, 'saved' once the code is exchanged and the session stored with its sub, 'restored' once it shows as signed in, or
// 'failed' with the reason. Every page that shows the account notes 'restored' again, so a load soon after still knows which
// account to restore; a sign-out clears it. A report older than REPORT_MS is no longer about this sign-in.
export const SIGNIN_REPORT = 'feed-raider(signin-report)';
export const REPORT_MS = 30 * MINUTE_MS;
// storage is null where the browser blocks it, and its calls throw a DOMException where it blocks or is full: the report then
// goes unkept, as the sign-in works without it and a stale one ages out. Any other error is a bug and is thrown.
export function noteSignIn(storage, step, extra = {}, now = Date.now()) {
  try { storage?.setItem(SIGNIN_REPORT, JSON.stringify({ ...extra, step, at: now })); } catch (e) { if (!(e instanceof DOMException)) throw e; }
}
// A report that is not JSON (SyntaxError) or cannot be read (DOMException) is the same as none: it only helps a page that loads
// soon after a sign-in find the account.
export function readSignIn(storage, now = Date.now()) {
  try {
    const r = JSON.parse(storage?.getItem(SIGNIN_REPORT) || 'null');
    return r && typeof r.step === 'string' && now - r.at >= 0 && now - r.at < REPORT_MS ? r : null;
  } catch (e) {
    if (!(e instanceof SyntaxError || e instanceof DOMException)) throw e;
    return null;
  }
}
export function clearSignIn(storage) {
  try { storage?.removeItem(SIGNIN_REPORT); } catch (e) { if (!(e instanceof DOMException)) throw e; }
}

const SAVED_STEPS = ['saved', 'restored'];

// The saved session, as a page load or a waiting page asks for it: `initRestore()` (the library's: the account this browser
// signed in with last, or undefined when it keeps none) and, when it keeps none but the sign-in report names an account a
// callback saved, `restore(sub)`. The library forgets which account is current when a callback page is closed between saving
// the session and restoring it, and whenever any restore of that account fails, even one that raced the callback saving a fresh
// session; the session itself stays saved. Resolves { session, report, error }: session null when none was found, report the
// recent sign-in report (or null), error why the reported account's restore failed. Rejects with initRestore's own error.
export async function restoreSaved({ initRestore, restore, storage, now = Date.now() }) {
  const restored = await initRestore();
  if (restored?.session) return { session: restored.session, report: null, error: null };
  const report = readSignIn(storage, now);
  if (!report?.sub || !SAVED_STEPS.includes(report.step)) return { session: null, report, error: null };
  try {
    return { session: await restore(report.sub), report, error: null };
  } catch (e) {
    return { session: null, report, error: e };
  }
}

// The start page's line for a page load that found no session while a sign-in report was recent: how far that sign-in got.
// Null when there is nothing to say (no report).
export function signInLost(report, error) {
  if (!report) return null;
  if (error) return signInLine(`the sign-in window saved your session, but restoring it here failed (${(error instanceof Error ? error.message : String(error)).replace(/[.\s]+$/, '')})`);
  switch (report.step) {
    case 'started': return signInLine('no answer from Bluesky reached this browser. The sign-in window was closed before Bluesky sent it back, or it opened where this page cannot see its storage');
    case 'answered': return signInLine('the sign-in window was closed before it saved your session. Leave it open until it says you are signed in');
    case 'failed': return signInFailed(report.error);
    default: return signInLine('no saved session was found in this browser');
  }
}

// What the callback page says once it is done: the popup that saved the session and stays open (no opener answered) says so.
export function callbackNote({ popup, handle }) {
  return popup && handle ? `Signed in as @${handle} and saved in this browser. You can close this window; reload the game page if it still shows Sign in.` : null;
}
export const CALLBACK_WAIT_NOTE = 'Finishing sign-in… keep this window open until it says you are signed in.';

// Calls back() when the page comes back to the front (window focus, or the document turning visible: a phone tab switch) or
// another page of the site writes storage, as the callback page does when it restores the session it saved.
function watchReturn(back) {
  const w = globalThis.window, d = globalThis.document;
  if (!w || !d) return () => {};
  const onVisible = () => { if (d.visibilityState === 'visible') back(); };
  w.addEventListener('focus', back);
  w.addEventListener('storage', back);
  d.addEventListener('visibilitychange', onVisible);
  return () => { w.removeEventListener('focus', back); w.removeEventListener('storage', back); d.removeEventListener('visibilitychange', onVisible); };
}

// While this page is signed out, a session another page of the site saved (a popup whose sign-in this page stopped waiting for,
// or a sign-in in another tab) is adopted when that page writes storage: `restore()` resolves it or null, `onSession` shows it.
// A restore that fails leaves the page signed out and hands its error to `onFailure`, which tells the player. Returns a function
// that stops watching.
export function watchSaved({ restore, onSession, onFailure, signedIn = () => false, target = globalThis.window }) {
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
        } catch (e) {
          if (!stopped) onFailure(e);
        }
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
const POPUP_NAME = POPUP_CHANNEL;
const POPUP_FEATURES = 'width=500,height=700,menubar=no,toolbar=no';
const KEY_RADIX = 36, KEY_PREFIX_CHARS = 2;   // a random fraction in base 36 is "0." and its digits; the digits are the key
export async function signIn(client, raw, { scope, state, open = globalThis.window?.open?.bind(globalThis.window), standalone = false, restore = async () => null, onWaiting = () => {}, signal, channel = makeChannel, watch = watchReturn, pollMs = SIGNIN_POLL_MS, restoreMs = SIGNIN_RESTORE_MS, ackMs = SIGNIN_ACK_MS, timeoutMs = SIGNIN_TIMEOUT_MS } = {}) {
  const target = signInTarget(raw);
  let popup = null;
  if (!standalone && typeof client.authorize === 'function' && typeof open === 'function') {
    // a browser that refuses the window throws a DOMException; with no popup the sign-in goes by the redirect below
    try { popup = open('about:blank', POPUP_NAME, POPUP_FEATURES); } catch (e) { if (!(e instanceof DOMException)) throw e; }
  }
  if (!popup) {
    await client.signInRedirect(target, { scope, state });
    return { via: 'redirect' };
  }
  const key = Math.random().toString(KEY_RADIX).slice(KEY_PREFIX_CHARS);
  try {
    const url = await client.authorize(target, { scope, state: POPUP_STATE + key, display: 'popup' });
    signal?.throwIfAborted();
    if (popup.closed) throw new SignInCancelled();
    popup.location.href = url.href;
    popup.focus?.();
  } catch (e) {
    if (!popup.closed) popup.close();
    throw signal?.aborted ? new SignInCancelled() : e;
  }
  onWaiting();
  const session = await new Promise((resolve, reject) => {
    const popupChannel = channel(POPUP_CHANNEL);
    let finished = false, checking = false, again = false, sawClosed = false, handedOff = false;
    const finish = (settle, value) => {
      if (finished) return;
      finished = true;
      clearInterval(poll);
      clearInterval(asking);
      clearTimeout(timer);
      const hangUp = () => { popupChannel.removeEventListener('message', onMessage); popupChannel.close(); };
      // a session found before its hand-off came: the popup is still to hand off, and closes only once answered
      if (settle === resolve && !handedOff) setTimeout(hangUp, ackMs); else hangUp();
      unwatch();
      signal?.removeEventListener('abort', onAbort);
      settle(value);
    };
    // one restore at a time; a return to the page during one asks again once it is done, since the session may have landed meanwhile
    const check = async () => {
      if (finished) return;
      if (checking) { again = true; return; }
      checking = true;
      try {
        do {
          again = false;
          const saved = await restore();
          if (saved) return finish(resolve, saved);
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
      popupChannel.postMessage({ key, ack: true });
      if (finished) return;
      if (data.error) finish(reject, new Error(data.error)); else check();
    };
    const onAbort = () => finish(reject, new SignInCancelled());
    popupChannel.addEventListener('message', onMessage);
    signal?.addEventListener('abort', onAbort);
    const unwatch = watch(check);
    const poll = setInterval(() => { if (popup.closed && !sawClosed) { sawClosed = true; check(); } }, pollMs);
    const asking = setInterval(check, restoreMs);
    const timer = setTimeout(onAbort, timeoutMs);
    if (signal?.aborted) onAbort(); // given up while the popup was being shown
  });
  return { via: 'popup', session };
}

// The OAuth client metadata document sits beside index.html, whatever the page URL's file name, query or fragment.
export const metadataUrl = (pageHref) => new URL('./client-metadata.json', pageHref);

// --- avatars: the Bluesky CDN sends no CORS headers, so a page cannot read its pixels; the author's own PDS can serve the blob ---

// Endings that name a machine on the player's own network rather than a public server.
const LOCAL_NAME_ENDINGS = ['.localhost', '.local', '.localdomain', '.internal', '.intranet', '.lan', '.home', '.home.arpa', '.corp',
  '.private'];

// True for a public DNS name: letters, digits and hyphens in two or more labels, not an IP literal and not a local name. An author
// controls the address their DID document names, so anything else could point every player's browser at their own network.
// Only the name is judged: a public name that resolves to a private address is left to the browser's own rules.
function isPublicHostName(host) {
  const h = host.toLowerCase();
  // a last label that is a number, in decimal or in hex (0x1, or 0x alone), is how a URL spells an IPv4 address
  return /^[a-z0-9-]+(?:\.[a-z0-9-]+)+$/.test(h) && !/\.(?:\d+|0x[0-9a-f]*)$/.test(h) && !LOCAL_NAME_ENDINGS.some((end) => h.endsWith(end));
}

// The origin of an author's PDS (https://host) from the serviceEndpoint in their DID document, or null unless it is a plain
// https host name: no user, no port, no path, query or fragment.
export function pdsOrigin(endpoint) {
  let u;
  try {
    u = new URL(endpoint);
  } catch (e) {
    if (e instanceof TypeError) return null;   // not a URL at all
    throw e;
  }
  const plain = u.protocol === 'https:' && !u.username && !u.password && !u.port && u.pathname === '/' && !u.search && !u.hash;
  return plain && isPublicHostName(u.hostname) ? u.origin : null;
}

// Where an author's DID document lives (plc.directory, or a did:web host's well-known file), or null for a DID we cannot resolve.
// A did:web with a port (%3A) or a path (a further colon) is refused, as is any host that is not a public name.
const DID_WEB = 'did:web:';
const PLC_DIRECTORY = 'https://plc.directory';
export function didDocUrl(did) {
  if (typeof did !== 'string') return null;
  if (did.startsWith('did:plc:')) return `${PLC_DIRECTORY}/${did}`;
  const host = did.startsWith(DID_WEB) ? did.slice(DID_WEB.length) : '';
  return isPublicHostName(host) ? `https://${host.toLowerCase()}/.well-known/did.json` : null;
}

// The blob CID a CDN avatar URL (.../img/avatar/plain/<did>/<cid>@jpeg) already names, so the PDS can be asked for it directly.
export function avatarCid(url) {
  const m = /\/img\/avatar[^/]*\/[^/]+\/did:[^/]+\/([a-z0-9]+)(?:@[a-z]+)?$/i.exec(typeof url === 'string' ? url : '');
  return m ? m[1] : null;
}

// --- a post's pictures: read the same way as an avatar, from the author's own PDS, and from nowhere else ---

// The blob CID a post's picture URL (.../img/feed_thumbnail/plain/<did>/<cid>@jpeg) names, when the URL is the author's own.
export function pictureCid(url, did) {
  const m = /\/img\/feed_(?:thumbnail|fullsize)\/[^/]+\/(did:[^/]+)\/([a-z0-9]+)(?:@[a-z]+)?$/i.exec(typeof url === 'string' ? url : '');
  return m && m[1] === did ? m[2] : null;
}

// The host a video's thumbnail is read from. The address a post gives is on video.bsky.app, which answers with a redirect to this
// one; the reader builds it here from the DID and CID, so it follows no redirect and reads from nowhere else.
export const VIDEO_THUMB_ORIGIN = 'https://video.cdn.bsky.app';

// The thumbnail address of the video `cid` that `did` posted, or null when either is not the shape the game reads.
export function videoThumbUrl(did, cid) {
  const didOk = typeof did === 'string' && /^did:(?:plc:[a-z2-7]{24}|web:[a-z0-9.-]+)$/.test(did);
  const cidOk = typeof cid === 'string' && /^[a-z0-9]{8,128}$/i.test(cid);
  return didOk && cidOk ? `${VIDEO_THUMB_ORIGIN}/hls/${did}/${cid}/thumbnail.jpg` : null;
}

const fitsPicture = (w, h) => Number.isInteger(w) && Number.isInteger(h) && w >= 1 && h >= 1 && w <= PICTURE_MAX_SIDE && h <= PICTURE_MAX_SIDE;

// The value `cache` holds under `key`, made by make() the first time. A cache at `cap` entries drops its oldest first, so a
// long session does not grow it without end.
export function rememberRecent(cache, key, make, cap) {
  if (!cache.has(key)) {
    if (cache.size >= cap) cache.delete(cache.keys().next().value);
    cache.set(key, make());
  }
  return cache.get(key);
}

// True on a data-saver, slow or mobile connection, so nothing optional is downloaded ahead and the pictures start off. Only
// where the Network Information API exists: Safari and Firefox have none, and there the Pictures setting is the way out.
export function thinConnection() {
  const c = globalThis.navigator?.connection;
  return !!c && (c.saveData === true || ['slow-2g', '2g', '3g'].includes(c.effectiveType) || c.type === 'cellular');
}

const AUTHORS_KEPT = 300;   // authors whose PDS or avatar is remembered at once
const PDS_SERVICE_ID = '#atproto_pds';   // a DID document's PDS entry, written bare or after the DID

// Finds the origin of an author's PDS from their DID document, once per author (the last `kept` authors are remembered): see
// pdsOrigin for what is accepted; anything else is null. A request that fails rejects, and the rejection is remembered too. A
// redirect fails the same way: the host rule judges only the address asked, and a did:web host could send the browser on anywhere.
export function pdsFinder({ fetch, kept = AUTHORS_KEPT }) {
  const found = new Map();
  const find = async (did) => {
    const docUrl = didDocUrl(did);
    if (!docUrl) return null;
    const r = await fetch(docUrl, { redirect: 'error' });
    if (!r.ok) return null;
    const doc = await r.json();
    const services = Array.isArray(doc?.service) ? doc.service : [];
    return pdsOrigin(services.find((s) => s?.id === PDS_SERVICE_ID || s?.id === did + PDS_SERVICE_ID)?.serviceEndpoint);
  };
  return (did) => rememberRecent(found, did, () => find(did), kept);
}

export const PICTURE_MAX_SIDE = 64;
const PICTURE_MAX_BYTES = 2_000_000, PICTURE_TIMEOUT_MS = 8000, PICTURES_AT_ONCE = 3, PICTURES_KEPT = 96;
export const AVATAR_SIDE = 8;
// An ask that has waited this long for a turn is skipped, for its card has left the field even at the slowest feed speed. There a
// card moves a row a second (FeedSpeed's 30 frames a row at Tuning.Fps 30) and crosses the field's rows and its own: about 55 rows
// in a full-height window at 14 px type plus about 15 for a card with one picture is 70 rows, so 70 seconds.
export const PICTURE_WAIT_MS = 70_000;
// Reads in a row that run out of time before the reader calls tooSlow: on a link that slow each one is a part-downloaded blob
// thrown away, and the feed's own requests wait behind them.
const PICTURE_TIMEOUTS_TOO_SLOW = 3;

const RED_SHIFT = 16, GREEN_SHIFT = 8, CHANNEL_MASK = 0xff;   // a pixel is 0xRRGGBB

// A side x side picture (0xRRGGBB values, row by row) shrunk to w x h, each pixel the mean of the source pixels it covers.
export function shrinkPixels(px, side, w, h) {
  const out = new Array(w * h);
  const span = (i, n) => { const a = Math.floor((i * side) / n); return [a, Math.max(a + 1, Math.floor(((i + 1) * side) / n))]; };
  for (let y = 0; y < h; y++) {
    const [y0, y1] = span(y, h);
    for (let x = 0; x < w; x++) {
      const [x0, x1] = span(x, w);
      let r = 0, g = 0, b = 0;
      for (let sy = y0; sy < y1; sy++) {
        for (let sx = x0; sx < x1; sx++) {
          const p = px[sy * side + sx];
          r += (p >> RED_SHIFT) & CHANNEL_MASK; g += (p >> GREEN_SHIFT) & CHANNEL_MASK; b += p & CHANNEL_MASK;
        }
      }
      const n = (y1 - y0) * (x1 - x0);
      out[y * w + x] = (Math.round(r / n) << RED_SHIFT) | (Math.round(g / n) << GREEN_SHIFT) | Math.round(b / n);
    }
  }
  return out;
}

// What a picture may meet on the way, all of which leave its box empty: a refused, dropped or unreadable request (TypeError), a
// DID document that is not JSON (SyntaxError), and a request given up on or a blob that is not a picture (DOMException).
const lostOnTheWay = (e) => e instanceof TypeError || e instanceof SyntaxError || e instanceof DOMException;

// A wait for a turn that was dropped or waited too long, so its answer is not kept and a later ask fetches it.
const NOT_FETCHED = Symbol('not fetched');

// The page's reader of avatars and post pictures, the one way either is fetched. Each blob comes from the author's PDS (pdsOf) by
// the CID the CDN URL names, decode(blob, w, h) gives it as w*h 0xRRGGBB values. No more than atOnce are fetched at a time and the
// rest wait their turn, each for no longer than PICTURE_WAIT_MS by now(); a blob larger than PICTURE_MAX_BYTES, an answer slower than
// timeoutMs, a refusal, a blob that is not a picture and an ask that waited too long are all null, which the game shows as no
// avatar or an empty box. While wanted() is false nothing is fetched and the answer is null.
// tooSlow() is called once PICTURE_TIMEOUTS_TOO_SLOW reads in a row have run out of time; a read that ends any other way starts the
// count again, and an ask that never started its fetch leaves it as it is.
// picture(did, url, w, h) resolves to w*h comma-joined values, or null. A post picture is kept by its CID at PICTURE_MAX_SIDE
// square (the last picturesKept of them), so another size of it is shrunk from that copy and never fetched again.
// avatar(did, url) resolves to AVATAR_SIDE squared values, or null; avatars are kept for the last AUTHORS_KEPT CIDs.
// videoThumb(did, cid, w, h) reads a video's thumbnail from videoThumbUrl instead of a PDS, under the same limits, turns and count,
// and resolves as picture() does; the thumbnails are kept by CID apart from the pictures.
export function pictureReader({ fetch, pdsOf, decode, wanted, tooSlow = () => {}, atOnce = PICTURES_AT_ONCE, timeoutMs = PICTURE_TIMEOUT_MS, picturesKept = PICTURES_KEPT, now = () => performance.now() }) {
  const pictures = new Map(), avatars = new Map(), videoThumbs = new Map();
  const waiting = [];
  let busy = 0;
  let timeoutsInARow = 0;
  const turn = () => (busy < atOnce ? (busy++, Promise.resolve(true)) : new Promise((go) => waiting.push({ go, since: now() })));
  // the turn goes to the oldest ask that has not waited too long; the older ones are told they will not be fetched
  const releaseTurn = () => {
    for (let next = waiting.shift(); next; next = waiting.shift()) {
      if (now() - next.since <= PICTURE_WAIT_MS) return next.go(true);
      next.go(false);
    }
    busy--;
  };
  // The body is read as it arrives and given up on past PICTURE_MAX_BYTES, so a reply with no content-length is not downloaded to the end.
  const readCapped = async (r) => {
    const reader = r.body.getReader();
    const chunks = [];
    let total = 0;
    for (;;) {
      const { done, value } = await reader.read();
      if (done) return new Blob(chunks);
      total += value.length;
      if (total > PICTURE_MAX_BYTES) { await reader.cancel(); return null; }
      chunks.push(value);
    }
  };
  const blobAddress = async (did, cid) => {
    const pds = await pdsOf(did);
    return pds ? `${pds}/xrpc/com.atproto.sync.getBlob?did=${encodeURIComponent(did)}&cid=${encodeURIComponent(cid)}` : null;
  };
  const read = async (address, did, cid, side, signal) => {
    const url = await address(did, cid);
    if (!url || signal.aborted) return null;
    // a redirect is refused as pdsFinder refuses one: the author's PDS could send every player's browser to any host
    const r = await fetch(url, { signal, redirect: 'error' });
    if (!r.ok || Number(r.headers.get('content-length')) > PICTURE_MAX_BYTES) return null;
    const blob = await readCapped(r);
    if (!blob || signal.aborted) return null;
    const px = await decode(blob, side, side);
    return Array.isArray(px) && px.length === side * side ? px : null;
  };
  const emptyIfLost = (e) => { if (lostOnTheWay(e)) return null; throw e; };
  // A read that ends with the pictures switched off is not counted, so they come back on with the count at none.
  const countEnd = (ranOut) => {
    if (!wanted()) return;
    if (!ranOut) { timeoutsInARow = 0; return; }
    if (++timeoutsInARow === PICTURE_TIMEOUTS_TOO_SLOW) tooSlow();
  };
  const load = async (address, did, cid, side) => {
    if (!(await turn())) return NOT_FETCHED;
    const stop = new AbortController();
    let timer, ranOut = false;
    const late = new Promise((give) => { timer = setTimeout(() => { ranOut = true; stop.abort(); give(null); }, timeoutMs); });
    try {
      return await Promise.race([read(address, did, cid, side, stop.signal).catch(emptyIfLost), late]);
    } finally {
      clearTimeout(timer);
      stop.abort(); // a blob refused for its size is not downloaded to the end
      countEnd(ranOut);
      releaseTurn();
    }
  };
  // The cached answer for cid, null too; a turn that was dropped is forgotten, so the next ask fetches it.
  const cachedOrFetch = (cache, cap, did, cid, side, address = blobAddress) => {
    if (!wanted()) return Promise.resolve(null);
    return rememberRecent(cache, cid, () => {
      const answer = load(address, did, cid, side).then((px) => {
        if (px !== NOT_FETCHED) return px;
        if (cache.get(cid) === answer) cache.delete(cid);
        return null;
      });
      return answer;
    }, cap);
  };
  // the asks still waiting for a turn get null and are never started: the pictures were switched off, or the run they were for is over
  const drop = () => { for (const { go } of waiting.splice(0)) go(false); };
  return {
    picture(did, url, w, h) {
      const cid = pictureCid(url, did);
      if (!cid || !fitsPicture(w, h)) return Promise.resolve(null);
      return cachedOrFetch(pictures, picturesKept, did, cid, PICTURE_MAX_SIDE).then((px) => px && shrinkPixels(px, PICTURE_MAX_SIDE, w, h).join(','));
    },
    avatar(did, url) {
      const cid = avatarCid(url);
      if (!cid) return Promise.resolve(null);
      return cachedOrFetch(avatars, AUTHORS_KEPT, did, cid, AVATAR_SIDE).then((px) => px && px.join(','));
    },
    videoThumb(did, cid, w, h) {
      const url = videoThumbUrl(did, cid);
      if (!url || !fitsPicture(w, h)) return Promise.resolve(null);
      return cachedOrFetch(videoThumbs, picturesKept, did, cid, PICTURE_MAX_SIDE, async () => url).then((px) => px && shrinkPixels(px, PICTURE_MAX_SIDE, w, h).join(','));
    },
    drop,
    // the pictures were switched off: the asks still waiting are dropped, and the timeouts in a row are counted from none
    switchedOff() {
      drop();
      timeoutsInARow = 0;
    },
  };
}

// --- game input, kept free of the DOM so node can test it ---

export const SLOWER_FEED_KEY = 'Minus', FASTER_FEED_KEY = 'Equal';
export const GAME_KEYS = new Set(['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Space', 'KeyF', 'KeyB', 'KeyE', 'KeyR', 'KeyZ', 'KeyX', 'BracketLeft', 'BracketRight', SLOWER_FEED_KEY, FASTER_FEED_KEY]);

// The game key a keyboard event means, or null. event.code is the physical key; when a browser or input method leaves it empty
// or odd (code "Unidentified", an IME), the typed letter still names the key.
export function gameKey(e) {
  const code = gameKeyOf(e);
  // - and + are the feed speed keys; with Ctrl or Cmd held they are the browser's zoom and stay the browser's
  if ((code === SLOWER_FEED_KEY || code === FASTER_FEED_KEY) && (e.ctrlKey || e.metaKey)) return null;
  return code;
}

function gameKeyOf(e) {
  if ((e.code === 'BracketLeft' || e.code === 'BracketRight') && (e.key === '+' || e.key === '-' || e.key === '=')) return e.key === '-' ? SLOWER_FEED_KEY : FASTER_FEED_KEY;   // a layout with the speed keys there
  if (GAME_KEYS.has(e.code)) return e.code;
  const k = typeof e.key === 'string' ? e.key : '';
  if (k === ' ') return 'Space';
  if (k === '-') return SLOWER_FEED_KEY;
  if (k === '+' || k === '=') return FASTER_FEED_KEY;
  if (k === '[') return 'BracketLeft';
  if (k === ']') return 'BracketRight';
  if (/^[a-z]$/i.test(k) && GAME_KEYS.has('Key' + k.toUpperCase())) return 'Key' + k.toUpperCase();
  return GAME_KEYS.has(k) ? k : null;
}

// --- the feed-speed option: five steps, kept in localStorage, ?feed-speed=1 to 5 for one visit ---

export const FEED_SPEED_KEY = 'feed-raider.feed-speed';
export const FEED_SPEED_MIN = 1, FEED_SPEED_MAX = 5, FEED_SPEED_DEFAULT = 4;
export const FEED_SPEED_NAMES = ['Slowest', 'Slower', 'Slow', 'Normal', 'Fast'];   // the engine's FeedSpeed steps 1 to 5; the default is Normal

const speedStep = (v) => (typeof v === 'string' && new RegExp(`^[${FEED_SPEED_MIN}-${FEED_SPEED_MAX}]$`).test(v) ? Number(v) : null);

// The step to start at: ?feed-speed= when it is a whole number 1 to 5, else the stored step, else the default. Anything else is ignored.
export function feedSpeed(search, stored) {
  return speedStep(new URLSearchParams(search).get('feed-speed')) ?? speedStep(stored == null ? null : String(stored)) ?? FEED_SPEED_DEFAULT;
}

// The step the player has chosen, free of the DOM. storage: { get(key), set(key, value) } (it may throw); tell(step, say) hands the step
// to the game, and say is false for the start-up call, which must not show the toast. The address option wins for the visit and
// writes nothing; only a change by the player is stored.
export function createFeedSpeed({ search = '', storage, tell }) {
  let onChange = () => {};
  let stored = null;
  try { stored = storage.get(FEED_SPEED_KEY); } catch (e) { if (!(e instanceof DOMException)) throw e; /* storage blocked: the default stands */ }
  let step = feedSpeed(search, stored), pending = false;
  const keep = () => { try { storage.set(FEED_SPEED_KEY, String(step)); } catch (e) { if (!(e instanceof DOMException)) throw e; /* storage blocked or full: not kept */ } };
  const clamp = (n) => Math.min(FEED_SPEED_MAX, Math.max(FEED_SPEED_MIN, Math.round(n)));
  return {
    get step() { return step; },
    onChange(fn) { onChange = fn; },
    // the run has started: the game takes the step with no toast
    begin() { pending = false; tell(step, false); },
    // the slider moved: kept now, told to the game when the pause menu closes
    move(n) {
      const was = step;
      step = clamp(n);
      keep();
      pending = true;
      if (step !== was) onChange(step);
    },
    // a key: the game hears of it at once and shows the toast, which also says so at the ends
    bump(delta) {
      const was = step;
      step = clamp(step + delta);
      if (step !== was) { keep(); onChange(step); }
      pending = false;
      tell(step, true);
    },
    // the pause menu is closing
    flush() {
      if (!pending) return;
      pending = false;
      tell(step, true);
    },
  };
}

// The pause menu's slider: it opens on the current step, a move is kept, and a key moves it too.
export function bindFeedSlider(slider, speed, name = null) {
  const show = () => {
    slider.value = String(speed.step);
    const stepName = FEED_SPEED_NAMES[speed.step - FEED_SPEED_MIN];
    if (name) name.textContent = stepName;
    if (slider.setAttribute) slider.setAttribute('aria-valuetext', `${stepName}, ${speed.step} of ${FEED_SPEED_MAX}`);
  };
  slider.addEventListener('input', () => { speed.move(Number(slider.value)); show(); });
  speed.onChange(show);
  return { sync: show };
}

// --- the Pictures setting: avatars and post pictures on or off, kept in localStorage ---

export const PICTURES_KEY = 'feed-raider.pictures';

// Whether the pictures start on: a stored 'on' or 'off' always wins; with nothing stored they start off only on a thin connection.
export function picturesStartOn(stored, thin) {
  return stored === 'on' || (stored !== 'off' && !thin);
}

// The Pictures setting, free of the DOM. storage: { get(key), set(key, value) }, which throws a DOMException where the browser
// blocks storage; thin: thinConnection() at page load; switchedOff() runs whenever the pictures go off.
export function createPictures({ storage, thin, switchedOff }) {
  let stored = null;
  try {
    stored = storage.get(PICTURES_KEY);
  } catch (e) {
    if (!(e instanceof DOMException)) throw e;   // storage blocked: the default stands
  }
  let on = picturesStartOn(stored, thin);
  return {
    get on() { return on; },
    set(next) {
      if (next === on) return;
      on = next;
      try {
        storage.set(PICTURES_KEY, on ? 'on' : 'off');
      } catch (e) {
        if (!(e instanceof DOMException)) throw e;   // storage blocked or full: the choice holds for this visit only
      }
      if (!on) switchedOff();
    },
    // Off for this visit only: the stored choice is not written, so the next visit starts as it would have.
    offForVisit() {
      if (!on) return;
      on = false;
      switchedOff();
    },
  };
}

// The pause menu's Pictures switch: it opens on the current setting, and a change is kept at once.
export function bindPicturesSwitch(box, pictures, name) {
  const show = () => {
    box.checked = pictures.on;
    name.textContent = pictures.on ? 'On' : 'Off';
  };
  box.addEventListener('change', () => { pictures.set(box.checked); show(); });
  return { sync: show };
}

// True when the focused element takes typed text, so game keys must leave it alone. A button, the volume slider or xterm's
// own helper textarea does not count: with one of those focused the game still has to hear B.
export function isTextEntry(el) {
  if (!el) return false;
  if (el.tagName === 'TEXTAREA') return !String(el.className || '').includes('xterm-helper-textarea');
  if (el.tagName !== 'INPUT') return !!el.isContentEditable;
  return !['button', 'submit', 'reset', 'checkbox', 'radio', 'range', 'color', 'file', 'image'].includes((el.type || 'text').toLowerCase());
}

export const CLICK_MS = 350;    // a touch shorter than this that barely moved is a click
export const SLOP = 12;   // pixels of travel before a touch is a drag, not a press
const SECOND_FINGER_MOVED = 99;   // a second finger down counts as travel far past SLOP: the touch is never a click
const ONE_FINGER = 1, BOMB_FINGERS = 2;   // one finger steers and clicks; a quick touch with two or more drops a bomb
const MAIN_BUTTON = 0, SECONDARY_BUTTON = 2;   // MouseEvent.button: the left button clicks, the right drops a bomb

// Turns mouse and touch events into the host calls. A click (the mouse's left button, or a quick touch of one finger) on a heart
// or repost likes or reposts; anywhere else it does nothing. host: { click(cell, touch), bomb(), inPanel(cell), logClick(cell) },
// cells are [col, row]; touch is true for a finger, whose press the game forgives a little past a button's edge. A finger that
// travels SLOP steers and never clicks, and one held still for CLICK_MS does nothing, so a resting thumb never likes.
// A press on the feed log (host.inPanel(cell)) is a log click on its buttons, never a steering drag or a click on the field:
// host.logClick(cell) comes on the press for a mouse and on a quick lift for a finger.
export function createGestures(host, now = () => performance.now()) {
  let touch = null; // { at, x, y, cell, moved, fingers, onPanel } while a finger is down
  const inPanel = (cell) => !!host.inPanel?.(cell);
  return {
    mouseDown(button, cell) {
      if (button === MAIN_BUTTON && inPanel(cell)) host.logClick(cell);
      else if (button === MAIN_BUTTON) host.click(cell, false); else if (button === SECONDARY_BUTTON) host.bomb();
    },
    touchStart(count, x, y, cell) {
      if (count === ONE_FINGER) {
        const onPanel = inPanel(cell);
        touch = { at: now(), x, y, cell, moved: 0, fingers: ONE_FINGER, onPanel };
        return;
      }
      if (touch) touch.fingers = Math.max(touch.fingers, count);
    },
    // dx, dy: how far the finger went since the last move event, in pixels. True when the move should steer the ship: not a finger
    // that began on the feed log.
    touchMove(count, dx, dy) {
      if (!touch) return false;
      if (count !== ONE_FINGER) { touch.moved += SECOND_FINGER_MOVED; return false; }
      touch.moved += Math.abs(dx) + Math.abs(dy);
      return !touch.onPanel;
    },
    touchEnd(remaining) {
      if (remaining > 0 || !touch) return;
      const quick = now() - touch.at < CLICK_MS && touch.moved < SLOP;
      if (quick && touch.fingers >= BOMB_FINGERS) host.bomb(); else if (quick && touch.onPanel) host.logClick(touch.cell); else if (quick) host.click(touch.cell, true);
      touch = null;
    },
    touchCancel() { touch = null; },
  };
}

// --- the pause menu: Esc or the gear opens it during a run; it pauses the game and can end the run ---

// What a key means for the pause menu: 'open', 'close' or null. Only during a run, so the start page keeps Esc for its list.
export function menuKey(e, { playing, open }) {
  if (!playing || e.repeat || e.ctrlKey || e.metaKey || e.altKey) return null;
  if (e.key !== 'Escape' && e.code !== 'Escape') return null;
  return open ? 'close' : 'open';
}

// The pause menu's state, free of the DOM. host: { show(), hide(), setPaused(on), releaseKeys(), quit(), toStart() }. closed() is the
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
      host.refreshLog?.();
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
      if (playing) { host.resuming?.(); host.setPaused(false); }
    },
    // ends the run (the host's quit lets go of the pause too) and shows the start page
    backToStartPage() {
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
// true-colour cells (avatars, the dimmed log, the sky) fill the cap in two to four minutes of play. watchAtlas counts
// the pages from the addon's events and calls renew once, at ATLAS_PAGE_LIMIT pages or at the first merge. renew starts a
// fresh one-page atlas in the same addon (no new GL context); its change event re-arms the watch.
export const ATLAS_PAGE_LIMIT = 14;
const FRESH_ATLAS_PAGES = 1;
export function watchAtlas(addon, renew) {
  let pages = FRESH_ATLAS_PAGES, asked = false;
  const ask = () => { if (!asked) { asked = true; renew(); } };
  addon.onChangeTextureAtlas(() => { pages = FRESH_ATLAS_PAGES; asked = false; });
  addon.onAddTextureAtlasCanvas(() => { if (++pages >= ATLAS_PAGE_LIMIT) ask(); });
  addon.onRemoveTextureAtlasCanvas(() => { pages--; ask(); });
}

// These four are the engine's Tuning constants of the same meaning; web/test/side-panel.test.mjs fails when one differs.
export const FIELD_MAX_COLS = 64; // Tuning.FieldMaxCols: the field is centred, at most this many columns
export const SIDE_MAX_COLS = 44;  // Tuning.SidePanelMaxCols: the widest a side panel gets
export const SIDE_MIN_COLS = 30;  // Tuning.SidePanelMinCols: the narrowest, below which neither panel shows
export const SIDE_GAP_COLS = 2;   // Tuning.SideGapCols: the gap between a side panel and the field
export const GEAR_PX = 44;        // the gear's touch target, in CSS pixels
const GEAR_GAP_COLS = 2;          // cells between the gear and the Stage readout
const PANEL_TOP_ROWS = 2;         // the side panel starts this many rows below the field's top
const PANEL_EDGE_COLS = 1;        // FieldLayout.PanelCols: a side panel keeps this many columns clear at the window's edge
const GEAR_ASPECT = 1.6;          // the gear button is this much wider than a cell is tall
const LONGEST_READOUT = 'STAGE 10';   // the widest HUD text on the field row; the gear sits left of it

// The engine's FieldLayout for a window `cols` wide: the field's first column (Col0) and width (PCols), and the width of each side
// panel (PanelCols), 0 when the margin cannot hold the narrowest. Everything the page places beside the field starts here.
function fieldLayout(cols) {
  // even, as FieldLayout.Compute rounds it: a cell is two pixels wide, so only an even width puts the field's middle, where the
  // ship starts, on the line between two cells
  const fieldCols = Math.min(cols, FIELD_MAX_COLS) & ~1;
  const fieldCol = Math.floor((cols - fieldCols) / 2);
  const room = Math.min(SIDE_MAX_COLS, fieldCol - SIDE_GAP_COLS - PANEL_EDGE_COLS);
  return { fieldCol, fieldCols, panelCols: room >= SIDE_MIN_COLS ? room : 0 };
}

// Where the gear and the how-to-play panel go, in page pixels, from the terminal's grid: cols x rows cells of cellW x cellH
// from (left, top). The field sits as the engine puts it (FieldLayout). The gear is always on the field, in the HUD row just
// left of the longest readout ("STAGE 10"). The panel mirrors the engine's feed log (FieldLayout.PanelCols): the same cell
// width, the same gap from the field, the same rows (2 to rows - 2); with too little margin both go and there is no panel.
export function chromeLayout({ cols, rows, cellW, cellH, left, top }) {
  const { fieldCol, fieldCols, panelCols } = fieldLayout(cols);
  const fieldRight = left + (fieldCol + fieldCols) * cellW;
  const stageLeft = left + (fieldCol + fieldCols - 1 - LONGEST_READOUT.length) * cellW;
  const h = Math.round(cellH), w = Math.round(cellH * GEAR_ASPECT);
  const gear = { x: stageLeft - GEAR_GAP_COLS * cellW - w, y: top, w, h, size: GEAR_PX };
  let side = null;
  if (panelCols > 0) {
    const y = top + PANEL_TOP_ROWS * cellH;
    side = { x: fieldRight + SIDE_GAP_COLS * cellW, y, w: panelCols * cellW, h: top + (rows - 1) * cellH - y, cols: panelCols };
  }
  return { fieldRight, gear, side };
}

// --- the start page's backdrop: the game playing itself under its buttons ---

// When the tour runs: only while the start page is showing (`show`/`hide`) and the runtime is up (`ready`). Starting is idempotent, so a
// second show() never restarts a tour that is already running, and hide() always ends it. start(seed, slow) begins the engine's
// tour (slow: a quarter speed, for reduced motion); stop() ends it. A running tour sleeps once TOUR_SLEEP_MS pass with nothing done on the
// page (visitorActed): sleep() keeps it still on its last frame and the next visitorActed() calls wake(), which carries on from there.
// All four are the host's; setTimer and clearTimer are setTimeout and clearTimeout unless a test passes its own.
const MAX_TOUR_SEED = 0x7fffffff;   // the largest signed 32-bit integer, so the seed fits an int on the .NET side
// a backdrop must not run a phone's battery down while nobody is looking, so after two idle minutes the tour stops costing frames
export const TOUR_SLEEP_MS = 2 * MINUTE_MS;
export function createTour({ start, stop, sleep, wake, slow = false, seed = () => Math.floor(Math.random() * MAX_TOUR_SEED),
  setTimer = setTimeout, clearTimer = clearTimeout }) {
  let ready = false, wanted = false, running = false, asleep = false, timer;
  const arm = () => { clearTimer(timer); timer = setTimer(() => { asleep = true; sleep(); }, TOUR_SLEEP_MS); };
  const sync = () => {
    if (wanted && ready && !running) { running = true; start(seed(), slow); arm(); }
    else if (!(wanted && ready) && running) {
      running = false;
      clearTimer(timer);
      stop();
      // the sleep is a hold of its own on the host, so it is let go with the tour or the next tour would begin held
      if (asleep) { asleep = false; wake(); }
    }
  };
  return {
    ready() { ready = true; sync(); },
    show() { wanted = true; sync(); },
    hide() { wanted = false; sync(); },
    visitorActed() {
      if (!running) return;
      if (asleep) { asleep = false; wake(); }
      arm();
    },
  };
}

const WRITES = ['like', 'unlike', 'repost', 'unrepost'];
const everyWrite = (granted) => Object.fromEntries(WRITES.map((write) => [write, granted]));

// What the game is told a session may write. A session whose granted scope could not be read is taken to allow everything: the
// write is tried and the server's refusal, if any, is shown. Signed out nothing is written, so nothing is locked.
export function sessionGrants(session) {
  if (!session || session.scope == null) return everyWrite(true);
  return grants(session.scope);
}

// The pause menu's line and button for a sign-in made before the game could repost and undo, or null when the session may do
// all four (or there is none). Without a popup the sign-in leaves the page, so the button says the run ends.
export function menuGrants(session, { popup = true } = {}) {
  if (!session) return null;
  const granted = sessionGrants(session);
  if (granted.like && granted.unlike && granted.repost && granted.unrepost) return null;
  const note = !granted.like ? 'This sign-in cannot like or repost.'
    : granted.repost ? 'This sign-in can like and repost, but not undo.'
    : 'This sign-in can like, but not repost or undo.';
  return { note, button: popup ? 'Sign in again' : 'Sign in again (ends this run)' };
}

class AccountChanged extends Error {
  constructor() { super('another account signed in'); this.name = 'AccountChanged'; }
}

// Sign in again, from the pause menu: the normal sign-in for the handle already signed in, which asks for the scope the page
// asks of every new sign-in. Once it lands the game is given what the new session may write, so locked buttons unlock in the
// run that is paused behind it. Resolves the grants, or null when there was no session to renew. A sign-in that fails rejects
// and tells the game nothing; one that comes back as a different account rejects with AccountChanged, for the run on screen
// was not theirs.
export async function signInAgain(auth, { setGrants, signal }) {
  const before = JSON.parse(auth.session() || 'null');
  if (!before) return null;
  const after = JSON.parse((await auth.signIn(before.handle, { signal })) || 'null');
  if (!after) return null;
  if (after.did !== before.did) throw new AccountChanged();
  const granted = sessionGrants(after);
  setGrants(granted);
  return granted;
}

// What a granted scope string lets the session do: { like, unlike, repost, unrepost }. Unknown or empty grants nothing.
// transition:generic is the broad legacy scope and allows all four. A repo permission is `repo:<collection>` (every action) or
// `repo:<collection>?action=a&action=b`; the `repo?collection=` form and `*` are read too. See https://atproto.com/specs/permission
export function grants(scope) {
  const out = everyWrite(false);
  const rules = { 'app.bsky.feed.like': ['like', 'unlike'], 'app.bsky.feed.repost': ['repost', 'unrepost'] };
  for (const token of String(scope ?? '').split(/\s+/).filter(Boolean)) {
    if (token === 'transition:generic') return everyWrite(true);
    const m = /^repo(?::([^?]*))?(?:\?(.*))?$/.exec(token);
    if (!m) continue;
    const query = new URLSearchParams(m[2] ?? '');
    const collections = m[1] !== undefined ? [m[1]] : query.getAll('collection');
    const actions = query.getAll('action');
    for (const collection of collections) {
      const pair = collection === '*' ? WRITES : rules[collection];
      if (!pair) continue;
      for (const n of pair) {
        const verb = n.startsWith('un') ? 'delete' : 'create';
        if (actions.length === 0 || actions.includes(verb)) out[n] = true;
      }
    }
  }
  return out;
}

// --- the feed log: the posts that scrolled off the field, with their like and repost buttons ---

// The terminal cell under a pointer position, { col, row }, or null outside the terminal (or before it is laid out).
// m: { left, top, cellW, cellH, cols, rows }, the screen's box in pixels and its size in cells.
export function cellAt(x, y, m) {
  if (!(m.cellW > 0) || !(m.cellH > 0)) return null;
  const col = Math.floor((x - m.left) / m.cellW), row = Math.floor((y - m.top) / m.cellH);
  if (!Number.isFinite(col) || !Number.isFinite(row) || col < 0 || row < 0 || col >= m.cols || row >= m.rows) return null;
  return { col, row };
}

// The feed log's side panel in a window `cols` wide, as { col, cols }, by the same sums as the game's FieldLayout, or null when it does not show.
export function leftPanel(cols) {
  const { fieldCol, panelCols } = fieldLayout(cols);
  return panelCols > 0 ? { col: fieldCol - SIDE_GAP_COLS - panelCols, cols: panelCols } : null;
}

const LOG_EMPTY = 'No posts have gone by yet.';
const LOCKED_WHY = 'To allow this, press Sign in again in this menu.';

// Fills the pause menu's list: one item per entry, newest first as given. els: { list, empty }; press(uri, 'like' | 'repost').
// An entry is { uri, name, handle, text, like, repost, likeCount, repostCount, likeLocked, repostLocked }, a state being
// 'off', 'marked', 'pending' or 'lit'.
export function renderLog(els, entries, { doc = document, press }) {
  const items = entries.map((e) => {
    const li = doc.createElement('li');
    const head = doc.createElement('p');
    head.className = 'log-who';
    const name = doc.createElement('strong');
    name.textContent = e.name || '';
    const handle = doc.createElement('span');
    handle.textContent = `@${e.handle}`;
    head.append(name, handle);
    const text = doc.createElement('p');
    text.className = 'log-text';
    text.textContent = e.text;
    const row = doc.createElement('div');
    row.className = 'log-buttons';
    row.append(logButton(doc, e, 'like', press), logButton(doc, e, 'repost', press));
    li.append(head, text, row);
    return li;
  });
  els.list.replaceChildren(...items);
  els.list.hidden = items.length === 0;
  els.empty.hidden = items.length > 0;
  els.empty.textContent = LOG_EMPTY;
}

function logButton(doc, e, action, press) {
  const like = action === 'like';
  const state = like ? e.like : e.repost, count = like ? e.likeCount : e.repostCount, locked = like ? e.likeLocked : e.repostLocked;
  const on = state === 'lit', pending = state === 'pending';
  const button = doc.createElement('button');
  button.setAttribute('type', 'button');
  button.className = `log-${action}${on ? ' lit' : ''}${state === 'marked' ? ' marked' : ''}`;
  button.textContent = `${like ? (on || pending ? '\u2665' : '\u2661') : '\u21bb'} ${count}`;
  button.setAttribute('aria-pressed', on ? 'true' : 'false');
  if (pending) button.setAttribute('aria-busy', 'true');
  const what = like ? (on ? 'Undo like' : 'Like') : (on ? 'Undo repost' : 'Repost');
  button.setAttribute('aria-label', `${what}, ${count}${locked ? `. ${LOCKED_WHY}` : ''}`);
  if (locked) { button.disabled = true; button.title = LOCKED_WHY; }
  button.addEventListener('click', () => press(e.uri, action));
  return button;
}

// The pause menu's Feed log: read() is the game's FeedLog() (JSON text), toggle(uri, action) its Toggle. refresh() rebuilds the
// list (the pause menu opening); said(message) is what the game just announced: the status line speaks it and the list is rebuilt,
// since a write's answer changes a button. FeedLog() answers a JSON list even before a run ("[]"), so a throw from it or from
// the parse is a fault and is thrown.
export function createLogList({ els, doc = globalThis.document, read, toggle }) {
  const refresh = () => renderLog(els, JSON.parse(read()), { doc, press });
  const press = (uri, action) => { toggle(uri, action); refresh(); };
  return {
    refresh,
    said(message) { els.status.textContent = message; refresh(); },
    clearStatus() { els.status.textContent = ''; },
  };
}
