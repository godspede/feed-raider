import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { startView, renderStart, normalizeHandle, guestTarget, signInTarget, loadPercent, createTypeahead, stepActive, signIn, SignInCancelled, signInFailed, isOAuthCallback, popupKey, handOff, POPUP_STATE, watchSaved, completeCallback, noteSignIn, readSignIn, clearSignIn, SIGNIN_REPORT, REPORT_MS, restoreSaved, signInLost, callbackNote, CALLBACK_WAIT_NOTE } from '../../src/FeedRaider.Web/wwwroot/page.js';

const el = (extra = {}) => ({ hidden: true, disabled: false, textContent: '', className: '', style: {}, attrs: {}, setAttribute(k, v) { this.attrs[k] = v; }, removeAttribute(k) { delete this.attrs[k]; }, ...extra });
const fakeEls = () => ({ form: el(), load: el(), loadText: el(), retry: el(), bar: el(), barFill: el(), cardSignIn: el(), signIn: el(), guest: el(), random: el(), hint: el(), signed: el(), play: el(), signOut: el() });
const draw = (state) => { const els = fakeEls(); renderStart(els, startView(state)); return els; };
const LOADING = { status: 'loading', percent: 63 };

test('loading, signed out: bar at 63%, all three choices offered and usable', () => {
  const els = draw({ loginAvailable: true, handle: null, load: LOADING });
  assert.equal(els.load.hidden, false);
  assert.equal(els.loadText.textContent, 'Loading the game… 63%');
  assert.equal(els.barFill.style.width, '63%');
  assert.equal(els.bar.attrs['aria-valuenow'], '63');
  assert.equal(els.retry.hidden, true);
  assert.equal(els.cardSignIn.hidden, false);
  assert.equal(els.signIn.hidden, false);
  assert.equal(els.signIn.disabled, false, 'sign-in works during the load');
  assert.equal(els.guest.textContent, 'Play as someone');
  assert.equal(els.guest.disabled, false);
  assert.equal(els.random.textContent, 'Play random');
  assert.equal(els.random.disabled, false);
  assert.equal(els.signed.hidden, true);
  assert.equal(els.hint.textContent, 'Without signing in, liking is off.');
});

test('loading with no progress numbers: the bar is indeterminate, not stuck at 0', () => {
  const els = draw({ loginAvailable: true, handle: null, load: { status: 'loading', percent: null } });
  assert.equal(els.bar.className, 'indeterminate');
  assert.equal('aria-valuenow' in els.bar.attrs, false);
  assert.equal(els.loadText.textContent, 'Loading the game…');
});

test('ready: the load block is gone and every start is enabled', () => {
  const els = draw({ loginAvailable: true, handle: null, load: { status: 'ready', percent: null } });
  assert.equal(els.load.hidden, true);
  assert.equal(els.guest.disabled, false);
  assert.equal(els.random.disabled, false);
});

test('queued start: the pressed button reads "loading 63%" and nothing else can start', () => {
  const guest = draw({ loginAvailable: true, handle: null, load: LOADING, queued: 'guest' });
  assert.equal(guest.guest.textContent, 'loading 63%');
  assert.equal(guest.random.textContent, 'Play random');
  assert.equal(guest.guest.disabled, true);
  assert.equal(guest.random.disabled, true);
  assert.equal(guest.signIn.disabled, true);
  const random = draw({ loginAvailable: true, handle: null, load: LOADING, queued: 'random' });
  assert.equal(random.random.textContent, 'loading 63%');
  assert.equal(random.guest.textContent, 'Play as someone');
  assert.equal(random.random.disabled, true);
  assert.equal(random.guest.disabled, true);
  assert.equal(random.signIn.disabled, true);
  const play = draw({ loginAvailable: true, handle: 'alice.bsky.social', load: LOADING, queued: 'play' });
  assert.equal(play.play.textContent, 'loading 63%');
  assert.equal(play.play.disabled, true);
  assert.equal(play.random.disabled, true);
  const blind = draw({ loginAvailable: true, handle: null, load: { status: 'loading', percent: null }, queued: 'random' });
  assert.equal(blind.random.textContent, 'loading…');
});

test('load failure: a plain message and a retry button; starting is off, sign-in is not', () => {
  const els = draw({ loginAvailable: true, handle: 'alice.bsky.social', load: { status: 'failed', percent: null }, queued: null });
  assert.equal(els.load.hidden, false);
  assert.equal(els.load.className, 'failed');
  assert.match(els.loadText.textContent, /could not load/);
  assert.equal(els.retry.hidden, false);
  assert.equal(els.bar.hidden, true);
  assert.equal(els.play.disabled, true);
  const out = draw({ loginAvailable: true, handle: null, load: { status: 'failed', percent: null } });
  assert.equal(out.guest.disabled, true);
  assert.equal(out.random.disabled, true);
  assert.equal(out.signIn.disabled, false);
});

test('signed in: the first choice becomes Play as @handle with sign out; the other two stay', () => {
  const v = startView({ loginAvailable: true, handle: 'alice.bsky.social' });
  assert.equal(v.mode, 'signed-in');
  const els = draw({ loginAvailable: true, handle: 'alice.bsky.social' });
  assert.equal(els.play.textContent, 'Play as @alice.bsky.social');
  assert.equal(els.cardSignIn.hidden, false);
  assert.equal(els.signed.hidden, false);
  assert.equal(els.signOut.hidden, false);
  assert.equal(els.signIn.hidden, true);
  assert.equal(els.guest.disabled, false);
  assert.equal(els.random.disabled, false);
});

test('login unavailable: no sign-in choice, the other two stay, and a stale handle is ignored', () => {
  const els = draw({ loginAvailable: false, handle: 'alice.bsky.social' });
  assert.equal(els.cardSignIn.hidden, true);
  assert.equal(els.signIn.hidden, true);
  assert.equal(els.guest.disabled, false);
  assert.equal(els.random.disabled, false);
  assert.equal(els.signed.hidden, true);
  assert.equal(els.signOut.hidden, true);
  assert.match(els.hint.textContent, /not available/);
});

test('OAuth client still starting: sign-in is shown but waits; a handle check in flight locks every start', () => {
  assert.equal(draw({ loginAvailable: true, handle: null, authPending: true }).signIn.disabled, true);
  const busy = draw({ loginAvailable: true, handle: null, busy: true });
  assert.equal(busy.guest.disabled, true);
  assert.equal(busy.random.disabled, true);
  assert.equal(busy.signIn.disabled, true);
});

test('guestTarget: an empty box asks for a handle, not Discover; a DID or URL is refused; a name is normalized', () => {
  assert.match(guestTarget('').error, /Type a handle/);
  assert.match(guestTarget('   ').error, /Type a handle/);
  assert.match(guestTarget('@').error, /Type a handle/);
  assert.match(guestTarget('did:plc:abc').error, /Type a handle/);
  assert.match(guestTarget('https://example.com').error, /Type a handle/);
  assert.deepEqual(guestTarget(' @Alice '), { handle: 'alice.bsky.social' });
  assert.deepEqual(guestTarget('bob.example.com'), { handle: 'bob.example.com' });
});

test('loadPercent: whole percent, never 100 before the runtime says so, null without a total', () => {
  assert.equal(loadPercent(5, 8), 62);
  assert.equal(loadPercent(8, 8), 99);
  assert.equal(loadPercent(0, 8), 0);
  assert.equal(loadPercent(1, 0), null);
  assert.equal(loadPercent(undefined, undefined), null);
});

test('normalizeHandle: @ and bare names, .bsky.social only when no dot is typed', () => {
  assert.equal(normalizeHandle('alice'), 'alice.bsky.social');
  assert.equal(normalizeHandle('@alice'), 'alice.bsky.social');
  assert.equal(normalizeHandle('  @Alice  '), 'alice.bsky.social');
  assert.equal(normalizeHandle('bsky.app'), 'bsky.app');
  assert.equal(normalizeHandle('@Bsky.App'), 'bsky.app');
  assert.equal(normalizeHandle('alice.example.com'), 'alice.example.com');
  assert.equal(normalizeHandle('did:plc:abc123'), 'did:plc:abc123');
  assert.equal(normalizeHandle('https://example.com'), 'https://example.com');
  assert.equal(normalizeHandle(''), '');
  assert.equal(normalizeHandle('  @ '), '');
  assert.equal(normalizeHandle(null), '');
});

test('sign-in target: an empty box is the bsky.social entryway', () => {
  assert.equal(signInTarget(''), 'https://bsky.social');
  assert.equal(signInTarget('  '), 'https://bsky.social');
  assert.equal(signInTarget('@bob'), 'bob.bsky.social');
});

// A fake clock: timers run only when `tick` is called, so nothing in these tests sleeps.
function clock() {
  let now = 0, next = 1;
  const timers = new Map();
  return {
    setTimer: (fn, ms) => { timers.set(next, { fn, at: now + ms }); return next++; },
    clearTimer: (id) => { timers.delete(id); },
    tick(ms) {
      now += ms;
      for (const [id, t] of [...timers]) if (t.at <= now) { timers.delete(id); t.fn(); }
    },
  };
}
const actorsBody = (n) => ({ actors: Array.from({ length: n }, (_, i) => ({ handle: `a${i}.bsky.social`, displayName: `A ${i}`, avatar: `https://cdn/${i}.jpg` })) });
const flush = () => new Promise((r) => setImmediate(r));

function typeaheadRig(body = actorsBody(8)) {
  const c = clock(), calls = [], shown = [];
  const fetchImpl = async (url) => { calls.push(url); return { ok: true, json: async () => body }; };
  const ta = createTypeahead({ fetchImpl, onResults: (r) => shown.push(r), setTimer: c.setTimer, clearTimer: c.clearTimer });
  return { c, calls, shown, ta };
}

test('typeahead: nothing is asked under 2 characters, and the list is cleared', async () => {
  const { c, calls, shown, ta } = typeaheadRig();
  ta.input('a');
  ta.input('@a');
  ta.input('  ');
  c.tick(1000);
  await flush();
  assert.deepEqual(calls, []);
  assert.deepEqual(shown, [[], [], []]);
});

test('typeahead: debounced 200 ms, one request for a burst of typing, limit 6, @ stripped', async () => {
  const { c, calls, shown, ta } = typeaheadRig();
  ta.input('al');
  c.tick(150);
  ta.input('ali');
  c.tick(150);
  ta.input('@alic');
  c.tick(199);
  await flush();
  assert.equal(calls.length, 0, 'still inside the quiet window');
  c.tick(1);
  await flush();
  assert.equal(calls.length, 1);
  assert.equal(calls[0], 'https://public.api.bsky.app/xrpc/app.bsky.actor.searchActorsTypeahead?q=alic&limit=6');
  const last = shown.at(-1);
  assert.equal(last.length, 6, 'at most 6 even if the server sends more');
  assert.deepEqual(last[0], { handle: 'a0.bsky.social', displayName: 'A 0', avatar: 'https://cdn/0.jpg' });
});

test('typeahead: an answer that arrives after newer typing is dropped', async () => {
  const c = clock(), shown = [], pending = [];
  const fetchImpl = (url) => new Promise((resolve) => pending.push({ url, resolve }));
  const ta = createTypeahead({ fetchImpl, onResults: (r) => shown.push(r.map((a) => a.handle)), setTimer: c.setTimer, clearTimer: c.clearTimer });
  ta.input('ab');
  c.tick(200);
  ta.input('abc');
  c.tick(200);
  assert.equal(pending.length, 2);
  pending[1].resolve({ ok: true, json: async () => ({ actors: [{ handle: 'new.bsky.social' }] }) });
  await flush();
  pending[0].resolve({ ok: true, json: async () => ({ actors: [{ handle: 'old.bsky.social' }] }) });
  await flush();
  assert.deepEqual(shown.filter((r) => r.length), [['new.bsky.social']]);
});

test('typeahead: a failed request shows no list and reports the error', async () => {
  const c = clock(), shown = [], errors = [];
  const ta = createTypeahead({ fetchImpl: async () => ({ ok: false, status: 502 }), onResults: (r) => shown.push(r), onError: (e) => errors.push(e.message), setTimer: c.setTimer, clearTimer: c.clearTimer });
  ta.input('alice');
  c.tick(200);
  await flush();
  assert.deepEqual(shown.at(-1), []);
  assert.match(errors[0], /502/);
});

test('stepActive: Down/Up walk the rows and fall back to none at the ends', () => {
  assert.equal(stepActive(-1, 3, 'ArrowDown'), 0);
  assert.equal(stepActive(0, 3, 'ArrowDown'), 1);
  assert.equal(stepActive(2, 3, 'ArrowDown'), -1);
  assert.equal(stepActive(-1, 3, 'ArrowUp'), 2);
  assert.equal(stepActive(0, 3, 'ArrowUp'), -1);
  assert.equal(stepActive(-1, 0, 'ArrowDown'), -1);
});

// --- popup, then redirect ---
function fakeClient({ authorize = 'ok' } = {}) {
  const calls = [];
  return {
    calls,
    async signInRedirect(target, opts) { calls.push(['redirect', target, opts]); },
    async authorize(target, opts) {
      calls.push(['authorize', target, opts]);
      if (authorize === 'fail') throw new Error('handle did not resolve');
      return new URL('https://auth.example/oauth/authorize?request_uri=x');
    },
  };
}
const fakeWindow = (closed = false) => ({ closed, location: { href: 'about:blank' }, close() { this.closed = true; } });
// An in-memory BroadcastChannel: what one end posts, every other open end of the bus hears.
function fakeBus() {
  const ends = new Set(), posted = [];
  const channel = () => {
    const end = {
      listeners: new Set(),
      postMessage(data) { posted.push(data); for (const o of ends) if (o !== end) for (const l of o.listeners) queueMicrotask(() => l({ data })); },
      addEventListener(_, l) { end.listeners.add(l); },
      removeEventListener(_, l) { end.listeners.delete(l); },
      close() { ends.delete(end); },
    };
    ends.add(end);
    return end;
  };
  return { channel, posted, open: () => ends.size };
}
// The page-wake hook, fired by hand: what focus or a tab turning visible does in the browser.
function fakeWake() {
  const wakes = new Set();
  return { watch: (fn) => { wakes.add(fn); return () => wakes.delete(fn); }, fire: () => { for (const fn of wakes) fn(); }, count: () => wakes.size };
}
const tick = (ms = 0) => new Promise((r) => setTimeout(r, ms));
// The key the popup's sign-in put in the authorization request's state, as the callback page reads it back.
const keyOf = (client) => popupKey(client.calls.find((c) => c[0] === 'authorize')[2].state);

test('sign-in: the popup is opened in the click, then pointed at the authorization page; a hand-off returns the saved session', async () => {
  const client = fakeClient(), win = fakeWindow(), opened = [], bus = fakeBus(), wake = fakeWake();
  let saved = null, waiting = 0;
  const run = signIn(client, '@Alice', { scope: 's', state: '?x', open: (...a) => { opened.push(a); return win; }, restore: async () => saved, onWaiting: () => waiting++, channel: bus.channel, watch: wake.watch, pollMs: 5 });
  await tick(10);
  assert.equal(opened[0][0], 'about:blank');
  assert.deepEqual(client.calls.map((c) => c[0]), ['authorize']);
  assert.equal(client.calls[0][1], 'alice.bsky.social');
  assert.equal(client.calls[0][2].scope, 's');
  assert.equal(client.calls[0][2].display, 'popup');
  assert.ok(client.calls[0][2].state.startsWith(POPUP_STATE));
  assert.equal(win.location.href, 'https://auth.example/oauth/authorize?request_uri=x');
  assert.equal(waiting, 1);
  saved = { sub: 'did:plc:me' };
  assert.equal(await handOff(keyOf(client), { sub: 'did:plc:me' }, { channel: bus.channel }), true, 'the opener answers the hand-off');
  const out = await run;
  assert.equal(out.via, 'popup');
  assert.deepEqual(out.session, { sub: 'did:plc:me' });
  assert.equal(bus.open(), 0, 'the channel is closed');
  assert.equal(wake.count(), 0, 'focus and visibility are no longer watched');
});

test('sign-in: the authorization server cuts the popup off (closed reads true at once), which is no cancel', async (t) => {
  // on a mocked clock: with real timers a stall of the machine longer than the 200ms timeout made the run settle before the check
  t.mock.timers.enable({ apis: ['setTimeout', 'setInterval'] });
  const flush = () => new Promise((r) => setImmediate(r));
  const pass = async (ms) => { await flush(); t.mock.timers.tick(ms); await flush(); };
  const client = fakeClient(), win = fakeWindow();
  let settled = false, restores = 0;
  const run = signIn(client, 'carol', { open: () => win, restore: async () => { restores++; return null; }, channel: fakeBus().channel, watch: fakeWake().watch, pollMs: 5, timeoutMs: 200 });
  run.then(() => { settled = true; }, () => { settled = true; });
  await pass(10);
  win.closed = true; // what Cross-Origin-Opener-Policy: same-origin on the authorization page does to the opener's handle
  await pass(60);
  assert.equal(settled, false, 'still waiting for the callback');
  assert.equal(restores, 1, 'a closed popup asks for a saved session once');
  await pass(129);
  assert.equal(settled, false, 'one millisecond short of the timeout');
  await pass(1);
  await assert.rejects(run, (e) => e instanceof SignInCancelled, 'only the timeout gives up');
});

test('sign-in: the popup closes with no hand-off, and the session it saved is restored', async () => {
  const win = fakeWindow();
  let saved = null;
  const run = signIn(fakeClient(), 'dave', { open: () => win, restore: async () => saved, channel: fakeBus().channel, watch: fakeWake().watch, pollMs: 5 });
  await tick(10);
  saved = { sub: 'did:plc:dave' }; // the callback saved it, but its message never came
  win.closed = true;
  assert.deepEqual((await run).session, { sub: 'did:plc:dave' });
});

test('sign-in: back on a phone tab after the callback finished in another tab, focus or visibility restores the session', async () => {
  const wake = fakeWake();
  let saved = null;
  const run = signIn(fakeClient(), 'erin', { open: () => fakeWindow(), restore: async () => saved, channel: fakeBus().channel, watch: wake.watch, pollMs: 5 });
  await tick(10);
  wake.fire(); // back too soon: nothing saved yet, keep waiting
  await tick(5);
  saved = { sub: 'did:plc:erin' };
  wake.fire();
  assert.deepEqual((await run).session, { sub: 'did:plc:erin' });
});

test('sign-in: a wake during a slow restore asks again, so a session saved meanwhile is not missed', async () => {
  const wake = fakeWake();
  let saved = null, first = true, release;
  const restore = async () => { if (first) { first = false; await new Promise((r) => { release = r; }); return null; } return saved; };
  const run = signIn(fakeClient(), 'fay', { open: () => fakeWindow(), restore, channel: fakeBus().channel, watch: wake.watch, pollMs: 5 });
  await tick(10);
  wake.fire();
  await tick(1);
  saved = { sub: 'did:plc:fay' };
  wake.fire();
  release();
  assert.deepEqual((await run).session, { sub: 'did:plc:fay' });
});

test('sign-in: a callback that failed in the popup rejects with its reason, which the start page shows as one line', async () => {
  const client = fakeClient(), bus = fakeBus();
  const run = signIn(client, 'gus', { open: () => fakeWindow(), channel: bus.channel, watch: fakeWake().watch, pollMs: 5 });
  await tick(10);
  assert.equal(await handOff(keyOf(client), { error: 'Issuer mismatch' }, { channel: bus.channel }), true);
  await assert.rejects(run, (e) => signInFailed(e) === "Sign-in didn't finish: something went wrong. Try again.");
});

test('sign-in: a restore that throws rejects with its error', async () => {
  const win = fakeWindow();
  const run = signIn(fakeClient(), 'hal', { open: () => win, restore: async () => { throw new Error('session was deleted'); }, channel: fakeBus().channel, watch: fakeWake().watch, pollMs: 5 });
  await tick(10);
  win.closed = true;
  await assert.rejects(run, /session was deleted/);
});

test('sign-in: an empty box signs in at the bsky.social entryway; pressing again gives up on the old one quietly', async () => {
  const client = fakeClient(), ac = new AbortController();
  const run = signIn(client, '', { open: () => fakeWindow(), channel: fakeBus().channel, watch: fakeWake().watch, signal: ac.signal });
  await tick(5);
  assert.equal(client.calls[0][1], 'https://bsky.social');
  ac.abort();
  await assert.rejects(run, (e) => e instanceof SignInCancelled);
});

test('sign-in: a blocked popup falls back to the redirect with the same target, scope and state', async () => {
  const client = fakeClient();
  const out = await signIn(client, 'bob', { scope: 'atproto', state: '?handle=x', open: () => null });
  assert.equal(out.via, 'redirect');
  assert.deepEqual(client.calls, [['redirect', 'bob.bsky.social', { scope: 'atproto', state: '?handle=x' }]]);
});

test('sign-in: window.open throwing, iOS standalone, a client without authorize and no window.open all use the redirect', async () => {
  const redirected = async (client, opts) => { await signIn(client, '', opts); return client.calls.map((c) => c[0]); };
  assert.deepEqual(await redirected(fakeClient(), { open: () => { throw new Error('denied'); } }), ['redirect']);
  const opened = [];
  assert.deepEqual(await redirected(fakeClient(), { standalone: true, open: (...a) => { opened.push(a); return fakeWindow(); } }), ['redirect']);
  assert.equal(opened.length, 0, 'standalone never opens a window');
  const noPopup = fakeClient();
  delete noPopup.authorize;
  assert.deepEqual(await redirected(noPopup, { open: () => fakeWindow() }), ['redirect']);
  assert.deepEqual(await redirected(fakeClient(), { open: undefined }), ['redirect']);
});

test('sign-in: a failed authorization request closes the popup and the error surfaces (no redirect retry)', async () => {
  const client = fakeClient({ authorize: 'fail' }), win = fakeWindow();
  await assert.rejects(signIn(client, 'nobody', { open: () => win }), /did not resolve/);
  assert.equal(win.closed, true);
  assert.deepEqual(client.calls.map((c) => c[0]), ['authorize']);
});

test('sign-in: a popup closed before the authorization page loads is a cancel', async () => {
  await assert.rejects(signIn(fakeClient(), 'ivy', { open: () => fakeWindow(true) }), (e) => e instanceof SignInCancelled && e.name === 'SignInCancelled');
});

test('sign-in: no hand-off, no wake and a popup that never reads closed: asking every so often still finds the saved session', async () => {
  let saved = null;
  const run = signIn(fakeClient(), 'kim', { open: () => fakeWindow(), restore: async () => saved, channel: fakeBus().channel, watch: fakeWake().watch, pollMs: 5, restoreMs: 20, timeoutMs: 500 });
  await tick(10);
  saved = { sub: 'did:plc:kim' }; // the callback saved it; its message was lost and the page never lost focus
  assert.deepEqual((await run).session, { sub: 'did:plc:kim' });
});

test('sign-in: a session found before the hand-off arrives still answers the late hand-off, so the popup closes', async () => {
  const bus = fakeBus(), client = fakeClient();
  let saved = null;
  const run = signIn(client, 'kim', { open: () => fakeWindow(), restore: async () => saved, channel: bus.channel, watch: fakeWake().watch, pollMs: 5, restoreMs: 10, ackMs: 100, timeoutMs: 500 });
  await tick(5);
  saved = { sub: 'did:plc:kim' }; // the callback restored it (storage changed) before it got to hand off
  assert.deepEqual((await run).session, { sub: 'did:plc:kim' });
  assert.equal(await handOff(keyOf(client), { sub: 'did:plc:kim' }, { channel: bus.channel, waitMs: 50 }), true);
  await tick(150);
  assert.equal(bus.open(), 0, 'the channel closes once no hand-off can still come');
});

test('sign-in: a signal aborted as soon as the popup shows the authorization page still cancels', async () => {
  const bus = fakeBus(), stop = new AbortController();
  const run = signIn(fakeClient(), 'kim', { open: () => fakeWindow(), restore: async () => null, onWaiting: () => stop.abort(), signal: stop.signal, channel: bus.channel, watch: fakeWake().watch, pollMs: 5, timeoutMs: 60e3 });
  await assert.rejects(Promise.race([run, tick(200).then(() => { throw new Error('still waiting'); })]), (e) => e instanceof SignInCancelled);
  assert.equal(bus.open(), 0);
});

// A window and document for the default page-wake hook, put on globalThis for one test.
async function withFakePage(fn) {
  const window = new EventTarget(), document = Object.assign(new EventTarget(), { visibilityState: 'visible' });
  Object.assign(globalThis, { window, document });
  try { return await fn(window); } finally { delete globalThis.window; delete globalThis.document; }
}

test('sign-in: another page of the site writing storage (the callback saving the account) wakes the check', async () => {
  await withFakePage(async (window) => {
    let saved = null;
    const run = signIn(fakeClient(), 'lou', { open: () => fakeWindow(), restore: async () => saved, channel: fakeBus().channel, pollMs: 5, timeoutMs: 300 });
    await tick(10);
    saved = { sub: 'did:plc:lou' };
    window.dispatchEvent(new Event('storage'));
    assert.deepEqual((await run).session, { sub: 'did:plc:lou' });
  });
});

test('watchSaved: a session saved by another page while this one is signed out signs it in; signed in, nothing is asked', async () => {
  const target = new EventTarget(), got = [];
  let saved = null, signedIn = false, restores = 0;
  const stop = watchSaved({ restore: async () => { restores++; return saved; }, onSession: (s) => got.push(s), signedIn: () => signedIn, target });
  target.dispatchEvent(new Event('storage')); // another tab saved its handle: no session yet
  await tick(1);
  saved = { sub: 'did:plc:mo' }; // the popup whose sign-in this page stopped waiting for saved one
  target.dispatchEvent(new Event('storage'));
  await tick(1);
  assert.deepEqual(got, [{ sub: 'did:plc:mo' }]);
  signedIn = true;
  target.dispatchEvent(new Event('storage'));
  await tick(1);
  assert.equal(restores, 2);
  stop();
  signedIn = false;
  target.dispatchEvent(new Event('storage'));
  await tick(1);
  assert.equal(restores, 2, 'stopped');
});

test('watchSaved: a broken saved session leaves the page signed out; an event during a restore asks again', async () => {
  const target = new EventTarget(), got = [];
  let release, first = true, saved = null;
  const restore = async () => {
    if (first) { first = false; await new Promise((r) => { release = r; }); throw new Error('session was deleted'); }
    return saved;
  };
  watchSaved({ restore, onSession: (s) => got.push(s), target });
  target.dispatchEvent(new Event('storage'));
  await tick(1);
  saved = { sub: 'did:plc:ned' };
  target.dispatchEvent(new Event('storage'));
  release();
  await tick(5);
  assert.deepEqual(got, [{ sub: 'did:plc:ned' }]);
});

test('completeCallback: a popup callback hands the account back and closes once the opener answers', async () => {
  const sent = [], closed = [];
  const hand = async (key, result) => { sent.push([key, result]); return true; };
  const result = await completeCallback({ callback: async () => ({ state: POPUP_STATE + 'k1' }), adopt: async () => 'did:plc:me', handOff: hand, close: () => closed.push(1) });
  assert.deepEqual(result, { state: POPUP_STATE + 'k1' });
  assert.deepEqual(sent, [['k1', { sub: 'did:plc:me' }]]);
  assert.equal(closed.length, 1);
});

test('completeCallback: a restore that fails after the code exchange still tells the opener why, instead of leaving it waiting', async () => {
  const sent = [], closed = [];
  const hand = async (key, result) => { sent.push([key, result]); return true; };
  await assert.rejects(completeCallback({ callback: async () => ({ state: POPUP_STATE + 'k2' }), adopt: async () => { throw new Error('session was deleted'); }, handOff: hand, close: () => closed.push(1) }), /session was deleted/);
  assert.deepEqual(sent, [['k2', { error: 'session was deleted' }]]);
  assert.equal(closed.length, 1);
});

test('completeCallback: a failed exchange hands back its reason; a redirect callback hands nothing back; no answer, no close', async () => {
  const sent = [], closed = [];
  const hand = async (key, result) => { sent.push([key, result]); return false; };
  const failing = async () => { throw Object.assign(new Error('Issuer mismatch'), { state: POPUP_STATE + 'k3' }); };
  await assert.rejects(completeCallback({ callback: failing, adopt: async () => 'x', handOff: hand, close: () => closed.push(1) }), /Issuer mismatch/);
  assert.deepEqual(sent, [['k3', { error: 'Issuer mismatch' }]]);
  const result = await completeCallback({ callback: async () => ({ state: '?handle=x' }), adopt: async () => 'did:plc:me', handOff: hand, close: () => closed.push(1) });
  assert.equal(result.state, '?handle=x');
  assert.equal(sent.length, 1, 'a redirect callback has no opener to tell');
  assert.equal(closed.length, 0);
});

test('completeCallback: each step reached goes in the sign-in report, a failure with its reason', async () => {
  const steps = [];
  const report = (step, extra = {}) => steps.push({ step, ...extra });
  await completeCallback({ callback: async () => ({ state: '?', session: { sub: 'did:plc:me' } }), adopt: async () => 'did:plc:me', report, handOff: async () => false });
  assert.deepEqual(steps, [{ step: 'answered' }, { step: 'saved', sub: 'did:plc:me' }, { step: 'restored', sub: 'did:plc:me' }]);
  steps.length = 0;
  await assert.rejects(completeCallback({ callback: async () => ({ state: '?', session: { sub: 'did:plc:me' } }), adopt: async () => { throw new Error('session was deleted'); }, report, handOff: async () => false }));
  assert.deepEqual(steps, [{ step: 'answered' }, { step: 'saved', sub: 'did:plc:me' }, { step: 'failed', error: 'session was deleted' }]);
});

const fakeStorage = () => {
  const m = new Map();
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: (k) => m.delete(k), map: m };
};

test('sign-in report: written with its step and time, read back while recent, gone once stale, garbled or cleared; no storage is no error', () => {
  const s = fakeStorage();
  noteSignIn(s, 'saved', { sub: 'did:plc:me' }, 1000);
  assert.deepEqual(readSignIn(s, 1000 + 60e3), { sub: 'did:plc:me', step: 'saved', at: 1000 });
  assert.equal(readSignIn(s, 1000 + REPORT_MS), null, 'a report from an old sign-in says nothing about this one');
  assert.equal(readSignIn(s, 500), null, 'a clock that went back');
  s.setItem(SIGNIN_REPORT, '{not json');
  assert.equal(readSignIn(s, 1000), null);
  noteSignIn(s, 'started', {}, 1000);
  clearSignIn(s);
  assert.equal(readSignIn(s, 1000), null);
  noteSignIn(null, 'started');
  assert.equal(readSignIn(null), null);
  const broken = { getItem() { throw new Error('denied'); }, setItem() { throw new Error('denied'); }, removeItem() { throw new Error('denied'); } };
  noteSignIn(broken, 'started');
  clearSignIn(broken);
  assert.equal(readSignIn(broken), null);
});

test('restoreSaved: the library\'s current account first; the report is not asked when it has one', async () => {
  const s = fakeStorage();
  noteSignIn(s, 'saved', { sub: 'did:plc:other' }, 1000);
  const asked = [];
  const out = await restoreSaved({ initRestore: async () => ({ session: { did: 'did:plc:me' } }), restore: async (sub) => asked.push(sub), storage: s, now: 2000 });
  assert.deepEqual(out, { session: { did: 'did:plc:me' }, report: null, error: null });
  assert.deepEqual(asked, []);
});

test('restoreSaved: the library forgot its current account but a callback saved one; that account is restored by its sub', async () => {
  // the operator's case: the popup saved the session, the opener never heard, and the reload found the library's pointer gone
  const s = fakeStorage();
  noteSignIn(s, 'saved', { sub: 'did:plc:me' }, 1000);
  const asked = [];
  const out = await restoreSaved({ initRestore: async () => undefined, restore: async (sub) => { asked.push(sub); return { did: sub }; }, storage: s, now: 2000 });
  assert.deepEqual(asked, ['did:plc:me']);
  assert.deepEqual(out.session, { did: 'did:plc:me' });
  assert.equal(out.report.step, 'saved');
  noteSignIn(s, 'restored', { sub: 'did:plc:me' }, 1000);
  assert.deepEqual((await restoreSaved({ initRestore: async () => undefined, restore: async (sub) => ({ did: sub }), storage: s, now: 2000 })).session, { did: 'did:plc:me' });
});

test('restoreSaved: a restore of the reported account that fails is given back as the error, not thrown; no saved step, no restore', async () => {
  const s = fakeStorage();
  noteSignIn(s, 'saved', { sub: 'did:plc:me' }, 1000);
  const out = await restoreSaved({ initRestore: async () => undefined, restore: async () => { throw new Error('The session was deleted by another process'); }, storage: s, now: 2000 });
  assert.equal(out.session, null);
  assert.match(out.error.message, /deleted by another process/);
  for (const step of ['started', 'answered', 'failed']) {
    noteSignIn(s, step, {}, 1000);
    const asked = [];
    const r = await restoreSaved({ initRestore: async () => undefined, restore: async (sub) => asked.push(sub), storage: s, now: 2000 });
    assert.deepEqual([r.session, r.report.step, r.error, asked], [null, step, null, []]);
  }
  clearSignIn(s);
  assert.deepEqual(await restoreSaved({ initRestore: async () => undefined, restore: async () => { throw new Error('no'); }, storage: s, now: 2000 }), { session: null, report: null, error: null });
  await assert.rejects(restoreSaved({ initRestore: async () => { throw new Error('IndexedDB is off'); }, restore: async () => null, storage: s }), /IndexedDB is off/);
});

test('signInLost: the start page says how far the last sign-in got, as one line; nothing without a report', () => {
  assert.equal(signInLost(null), null);
  assert.match(signInLost({ step: 'started' }), /^Sign-in didn't finish: no answer from Bluesky reached this browser\..*Try again\.$/);
  assert.match(signInLost({ step: 'answered' }), /closed before it saved your session/);
  assert.equal(signInLost({ step: 'failed', error: 'Issuer mismatch' }), "Sign-in didn't finish: something went wrong. Try again.");
  assert.match(signInLost({ step: 'saved', sub: 'did:plc:me' }), /no saved session was found in this browser/);
  assert.equal(signInLost({ step: 'saved', sub: 'did:plc:me' }, new Error('The session was deleted.')),
    "Sign-in didn't finish: the sign-in window saved your session, but restoring it here failed (The session was deleted). Try again.");
});

test('callbackNote: a popup that saved the session says so and that it may be closed; a redirect or no account says nothing', () => {
  assert.match(callbackNote({ popup: true, handle: 'me.bsky.social' }), /^Signed in as @me\.bsky\.social and saved in this browser\. You can close this window/);
  assert.equal(callbackNote({ popup: false, handle: 'me.bsky.social' }), null);
  assert.equal(callbackNote({ popup: true, handle: null }), null);
  assert.match(CALLBACK_WAIT_NOTE, /keep this window open/);
});

test('handOff: false when no opener answers in time, so the callback page stays as a signed-in page', async () => {
  const bus = fakeBus();
  assert.equal(await handOff('k', { sub: 'did:plc:me' }, { channel: bus.channel, waitMs: 20 }), false);
  assert.deepEqual(bus.posted, [{ key: 'k', sub: 'did:plc:me' }]);
  assert.equal(bus.open(), 0);
});

test('popupKey: only a popup sign-in state carries a key; a redirect state is the query string we left with', () => {
  assert.equal(popupKey(POPUP_STATE + 'abc'), 'abc');
  assert.equal(popupKey('?handle=x'), null);
  assert.equal(popupKey(POPUP_STATE), null);
  assert.equal(popupKey(null), null);
  assert.equal(popupKey(undefined), null);
});

test('isOAuthCallback: the answer comes in the fragment (the library default) and the query is accepted too', () => {
  assert.equal(isOAuthCallback('https://site.example/#state=s&iss=https%3A%2F%2Fbsky.social&code=c'), true);
  assert.equal(isOAuthCallback('https://site.example/?debug=feed#state=s&error=access_denied'), true);
  assert.equal(isOAuthCallback('https://site.example/?state=s&code=c'), true);
  assert.equal(isOAuthCallback('https://site.example/?handle=x'), false);
  assert.equal(isOAuthCallback('https://site.example/#state=s'), false);
});

test('signInFailed: plain words for the known library failures, a generic line otherwise, no library text', () => {
  const unreach = "Sign-in didn't finish: couldn't reach your Bluesky account's server. Check the handle and your connection, then try again.";
  assert.equal(signInFailed(new Error('Failed to resolve OAuth server metadata for resource "https://x"')), unreach);
  assert.equal(signInFailed(new SignInCancelled()), "Sign-in didn't finish: the sign-in window was closed. Try again.");
  assert.equal(signInFailed('access_denied'), "Sign-in didn't finish: Bluesky did not allow it. Try again when you are ready to approve.");
  assert.equal(signInFailed(new Error('Unknown authorization session "x".')), "Sign-in didn't finish: something went wrong. Try again.");
  assert.equal(signInFailed(new Error('')), "Sign-in didn't finish: something went wrong. Try again.");
});

test('typeahead: onSearching fires when the request goes out, not on each keystroke', async () => {
  const c = clock(), events = [];
  const fetchImpl = async () => ({ ok: true, json: async () => actorsBody(1) });
  const ta = createTypeahead({ fetchImpl, onResults: () => events.push('results'), onSearching: () => events.push('searching'), setTimer: c.setTimer, clearTimer: c.clearTimer });
  ta.input('al'); ta.input('ali');
  assert.deepEqual(events, []);
  c.tick(200);
  assert.deepEqual(events, ['searching']);
  await flush();
  assert.deepEqual(events, ['searching', 'results']);
});

test('main.js: the handle search says Searching… in the status note while it runs', () => {
  const js = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
  assert.match(js, /onSearching: \(\) => showSuggestNote\('Searching…'\)/);
});

test('main.js: the callback is told by the fragment, and a failed sign-in or restore shows the line, not only a warning', () => {
  const js = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
  assert.match(js, /const oauthCallback = page\.isOAuthCallback\(location\.href\)/);
  assert.doesNotMatch(js, /params\.has\('state'\)/);
  assert.match(js, /startFail\(page\.signInFailed\(e\)\)/);
  assert.match(js, /auth\?\.initError\(\)/);
});

test('atproto.js: the callback is finished here, not by the library popup hand-off that revokes a session no opener answered', () => {
  const src = readFileSync(new URL('../src/atproto.js', import.meta.url), 'utf8');
  assert.doesNotMatch(src, /signInPopup|client\.init\(/);
  assert.match(src, /client\.callback\(params/);
  assert.match(src, /client\.restore\(sub, false\)/);
  assert.match(src, /completeCallback\(\{/, 'a restore that fails after the exchange is handed back too');
  assert.match(src, /export function initError/);
});

test('atproto.js: every restore goes through restoreSaved, and each sign-in step is noted in the report', () => {
  const src = readFileSync(new URL('../src/atproto.js', import.meta.url), 'utf8');
  assert.doesNotMatch(src, /\?\.session \?\? null|const restored = await client\.initRestore/, 'no bare initRestore that misses a forgotten account');
  assert.match(src, /restoreSaved\(\{ initRestore: \(\) => client\.initRestore\(false\), restore: \(sub\) => client\.restore\(sub, false\)/);
  assert.match(src, /noteSignIn\(storage\(\), 'started'\)/);
  assert.match(src, /did = session\.did;[^]*?noteSignIn\(storage\(\), 'restored', \{ sub: did \}\)/, 'a page showing the account keeps the report fresh');
  assert.match(src, /export async function logout\(\) \{\s*clearSignIn\(storage\(\)\)/, 'a sign-out is not undone by the next load');
  assert.match(src, /report: \(step, extra\) => noteSignIn\(storage\(\), step, extra\)/);
  assert.match(src, /failure = signInLost\(out\.report, out\.error\)/);
  assert.match(src, /export function initNote/);
});

test('main.js and index.html: the callback page shows the wait line, then its own note; the note is a status, not an alert', () => {
  const js = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
  const html = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/index.html', import.meta.url), 'utf8');
  assert.match(js, /if \(oauthCallback && needsStart\) showNote\(page\.CALLBACK_WAIT_NOTE\)/);
  assert.match(js, /showNote\(auth\?\.initNote\(\)\)/);
  assert.match(html, /<p id="start-note" role="status" hidden><\/p>/);
});

test('main.js: a signed-out start page takes a session another page saved, so a popup it stopped waiting for still signs it in', () => {
  const js = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
  assert.match(js, /page\.watchSaved\(\{\s*restore: \(\) => auth\.adoptSaved\(\)/);
});

test('index.html: three equal choices, one handle box, the old guest and Discover wording is gone, nothing needs JS to paint', () => {
  const html = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/index.html', import.meta.url), 'utf8');
  assert.equal((html.match(/id="start-handle"/g) || []).length, 1);
  assert.doesNotMatch(html, /start-input|start-discover|Just the Discover feed|Start as guest|Play as guest|public Discover feed/);
  assert.equal((html.match(/<section class="choice"/g) || []).length, 3);
  assert.match(html, /<h2 id="start-h-signin">Sign in with Bluesky<\/h2>/);
  assert.match(html, /<h2 id="start-h-guest">Play as someone<\/h2>/);
  assert.match(html, /<h2 id="start-h-random">Random<\/h2>/);
  for (const id of ['start-login', 'start-guest', 'start-random']) assert.match(html, new RegExp(`id="${id}" class="primary"`), `${id} is a full-weight button`);
  assert.match(html, /role="combobox"/);
  assert.match(html, /inputmode="email"/);
  assert.doesNotMatch(html, /<input[^>]*\bautofocus\b/, 'no keyboard on load');
  assert.match(html, /href="\?mode=spectator"/);
});

test('main.js: Random is ?feed=random with no handle and skips the panel on reload; Play as someone needs a handle', () => {
  const js = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
  assert.match(js, /pick\('', 'random', 'random'\)/);
  assert.match(js, /needsStart = !spectatorMode && !handle && !randomFromUrl/);
  assert.match(js, /page\.guestTarget\(startHandle\.value\)/);
  assert.doesNotMatch(js, /pick\('', 'guest'\)/, 'an empty box is no longer Discover');
});

test('busy handle check: Play as someone reads "checking…", the form is aria-busy and the note says so', () => {
  const view = startView({ loginAvailable: true, handle: null, busy: 'check' });
  const els = fakeEls();
  renderStart(els, view);
  assert.equal(els.guest.textContent, 'checking…');
  assert.equal(els.signIn.textContent, 'Sign in with Bluesky');
  assert.equal(els.form.attrs['aria-busy'], 'true');
  assert.equal(view.busyNote, 'Checking the handle…');
  assert.equal(startView({ loginAvailable: true, handle: null, busy: true }).guestLabel, 'checking…');
});

test('busy sign-in: the button reads "signing in…" and the note waits for Bluesky; idle shows the plain labels', () => {
  const view = startView({ loginAvailable: true, handle: null, busy: 'signin' });
  const els = fakeEls();
  renderStart(els, view);
  assert.equal(els.signIn.textContent, 'signing in…');
  assert.equal(els.guest.textContent, 'Play as someone');
  assert.equal(els.form.attrs['aria-busy'], 'true');
  assert.equal(view.busyNote, 'Waiting for Bluesky…');
  const idle = draw({ loginAvailable: true, handle: null });
  assert.equal(idle.signIn.textContent, 'Sign in with Bluesky');
  assert.equal(idle.form.attrs['aria-busy'], 'false');
  assert.equal(startView({ loginAvailable: true, handle: null }).busyNote, '');
});

test('typeahead: an empty answer is marked as answered, a cleared box and a failure are not', async () => {
  const c = clock(), seen = [];
  const ok = createTypeahead({ fetchImpl: async () => ({ ok: true, json: async () => ({ actors: [] }) }), onResults: (r, info) => seen.push([r.length, info?.answered === true]), setTimer: c.setTimer, clearTimer: c.clearTimer });
  ok.input('nobody');
  c.tick(200);
  await flush();
  ok.input('n');
  assert.deepEqual(seen, [[0, true], [0, false]]);
  const failed = [];
  const bad = createTypeahead({ fetchImpl: async () => { throw new Error('offline'); }, onResults: (r, info) => failed.push(['results', info?.answered === true]), onError: () => failed.push(['error']), setTimer: c.setTimer, clearTimer: c.clearTimer });
  bad.input('alice');
  c.tick(200);
  await flush();
  assert.deepEqual(failed, [['results', false], ['error']]);
});
