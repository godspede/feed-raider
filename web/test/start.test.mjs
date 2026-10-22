import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { startView, renderStart, normalizeHandle, signInTarget, loadPercent, createTypeahead, stepActive, signIn, SignInCancelled } from '../../src/FeedRaider.Web/wwwroot/page.js';

const el = (extra = {}) => ({ hidden: true, disabled: false, textContent: '', className: '', style: {}, attrs: {}, setAttribute(k, v) { this.attrs[k] = v; }, removeAttribute(k) { delete this.attrs[k]; }, ...extra });
const fakeEls = () => ({ load: el(), loadText: el(), retry: el(), bar: el(), barFill: el(), form: el(), signIn: el(), guest: el(), hint: el(), signed: el(), play: el(), signOut: el() });
const draw = (state) => { const els = fakeEls(); renderStart(els, startView(state)); return els; };
const LOADING = { status: 'loading', percent: 63 };

test('loading, signed out: bar at 63%, sign-in usable, guest start offered', () => {
  const els = draw({ loginAvailable: true, handle: null, load: LOADING });
  assert.equal(els.load.hidden, false);
  assert.equal(els.loadText.textContent, 'Loading the game… 63%');
  assert.equal(els.barFill.style.width, '63%');
  assert.equal(els.bar.attrs['aria-valuenow'], '63');
  assert.equal(els.retry.hidden, true);
  assert.equal(els.form.hidden, false);
  assert.equal(els.signIn.hidden, false);
  assert.equal(els.signIn.disabled, false, 'sign-in works during the load');
  assert.equal(els.guest.textContent, 'Play as guest');
  assert.equal(els.guest.disabled, false);
  assert.equal(els.signed.hidden, true);
});

test('loading with no progress numbers: the bar is indeterminate, not stuck at 0', () => {
  const els = draw({ loginAvailable: true, handle: null, load: { status: 'loading', percent: null } });
  assert.equal(els.bar.className, 'indeterminate');
  assert.equal('aria-valuenow' in els.bar.attrs, false);
  assert.equal(els.loadText.textContent, 'Loading the game…');
});

test('ready: the load block is gone and play is enabled', () => {
  const els = draw({ loginAvailable: true, handle: null, load: { status: 'ready', percent: null } });
  assert.equal(els.load.hidden, true);
  assert.equal(els.guest.disabled, false);
});

test('queued start: the pressed button reads "loading 63%" and nothing else can start', () => {
  const guest = draw({ loginAvailable: true, handle: null, load: LOADING, queued: 'guest' });
  assert.equal(guest.guest.textContent, 'loading 63%');
  assert.equal(guest.guest.disabled, true);
  assert.equal(guest.signIn.disabled, true);
  const play = draw({ loginAvailable: true, handle: 'alice.bsky.social', load: LOADING, queued: 'play' });
  assert.equal(play.play.textContent, 'loading 63%');
  assert.equal(play.play.disabled, true);
  const blind = draw({ loginAvailable: true, handle: null, load: { status: 'loading', percent: null }, queued: 'guest' });
  assert.equal(blind.guest.textContent, 'loading…');
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
  assert.equal(out.signIn.disabled, false);
});

test('signed in: Play as @handle leads, sign out below, no handle box', () => {
  const v = startView({ loginAvailable: true, handle: 'alice.bsky.social' });
  assert.equal(v.mode, 'signed-in');
  const els = draw({ loginAvailable: true, handle: 'alice.bsky.social' });
  assert.equal(els.play.textContent, 'Play as @alice.bsky.social');
  assert.equal(els.signed.hidden, false);
  assert.equal(els.signOut.hidden, false);
  assert.equal(els.form.hidden, true);
  assert.equal(els.signIn.hidden, true);
  assert.equal(els.guest.hidden, true);
});

test('login unavailable: no sign-in button, guest is the primary action, and a stale handle is ignored', () => {
  const els = draw({ loginAvailable: false, handle: 'alice.bsky.social' });
  assert.equal(els.form.hidden, false);
  assert.equal(els.signIn.hidden, true);
  assert.equal(els.guest.hidden, false);
  assert.equal(els.guest.className, 'primary secondary');
  assert.equal(els.signed.hidden, true);
  assert.equal(els.signOut.hidden, true);
  assert.match(els.hint.textContent, /not available/);
});

test('OAuth client still starting: sign-in is shown but waits; a handle check in flight locks both starts', () => {
  assert.equal(draw({ loginAvailable: true, handle: null, authPending: true }).signIn.disabled, true);
  const busy = draw({ loginAvailable: true, handle: null, busy: true });
  assert.equal(busy.guest.disabled, true);
  assert.equal(busy.signIn.disabled, true);
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
function fakeClient({ popup = 'ok' } = {}) {
  const calls = [];
  return {
    calls,
    async signInRedirect(target, opts) { calls.push(['redirect', target, opts]); },
    signInPopup(target, opts) {
      calls.push(['popup', target, opts]);
      if (popup === 'ok') return Promise.resolve({ sub: 'did:plc:me' });
      if (popup === 'fail') return Promise.reject(new Error('handle did not resolve'));
      return new Promise(() => {}); // the user never finishes
    },
  };
}
const fakeWindow = (closed = false) => ({ closed, close() { this.closed = true; } });

test('sign-in: the popup is tried first and its session returned; the redirect is not used', async () => {
  const client = fakeClient(), win = fakeWindow(), opened = [];
  const out = await signIn(client, '@Alice', { scope: 's', state: '?x', open: (...a) => { opened.push(a); return win; } });
  assert.equal(out.via, 'popup');
  assert.deepEqual(out.session, { sub: 'did:plc:me' });
  assert.deepEqual(client.calls.map((c) => c[0]), ['popup']);
  assert.equal(client.calls[0][1], 'alice.bsky.social');
  assert.equal(opened[0][0], 'about:blank');
  assert.equal(client.calls[0][2].popupName, opened[0][1], 'the library reuses the window opened in the click');
});

test('sign-in: an empty box signs in at the bsky.social entryway', async () => {
  const client = fakeClient();
  await signIn(client, '', { open: () => fakeWindow() });
  assert.equal(client.calls[0][1], 'https://bsky.social');
});

test('sign-in: a blocked popup falls back to the redirect with the same target, scope and state', async () => {
  const client = fakeClient();
  const out = await signIn(client, 'bob', { scope: 'atproto', state: '?handle=x', open: () => null });
  assert.equal(out.via, 'redirect');
  assert.deepEqual(client.calls, [['redirect', 'bob.bsky.social', { scope: 'atproto', state: '?handle=x' }]]);
});

test('sign-in: window.open throwing, iOS standalone, a client without signInPopup and no window.open all use the redirect', async () => {
  const redirected = async (client, opts) => { await signIn(client, '', opts); return client.calls.map((c) => c[0]); };
  assert.deepEqual(await redirected(fakeClient(), { open: () => { throw new Error('denied'); } }), ['redirect']);
  const opened = [];
  assert.deepEqual(await redirected(fakeClient(), { standalone: true, open: (...a) => { opened.push(a); return fakeWindow(); } }), ['redirect']);
  assert.equal(opened.length, 0, 'standalone never opens a window');
  const noPopup = fakeClient();
  delete noPopup.signInPopup;
  assert.deepEqual(await redirected(noPopup, { open: () => fakeWindow() }), ['redirect']);
  assert.deepEqual(await redirected(fakeClient(), { open: undefined }), ['redirect']);
});

test('sign-in: a popup that fails is closed and the error surfaces (no redirect retry)', async () => {
  const client = fakeClient({ popup: 'fail' }), win = fakeWindow();
  await assert.rejects(signIn(client, 'nobody', { open: () => win }), /did not resolve/);
  assert.equal(win.closed, true);
  assert.deepEqual(client.calls.map((c) => c[0]), ['popup']);
});

test('sign-in: a popup the user closes rejects with SignInCancelled after the grace period', async () => {
  const client = fakeClient({ popup: 'hang' }), win = fakeWindow();
  const run = signIn(client, 'carol', { open: () => win, pollMs: 5, graceMs: 20 });
  setTimeout(() => { win.closed = true; }, 15);
  await assert.rejects(run, (e) => e instanceof SignInCancelled && e.name === 'SignInCancelled');
});

test('index.html: one handle box, the old guest box and Discover button are gone, and nothing needs JS to paint', () => {
  const html = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/index.html', import.meta.url), 'utf8');
  assert.equal((html.match(/id="start-handle"/g) || []).length, 1);
  assert.doesNotMatch(html, /start-input|start-discover|Just the Discover feed|Start as guest/);
  assert.match(html, /Sign in with Bluesky/);
  assert.match(html, /Play as guest/);
  assert.match(html, /role="combobox"/);
  assert.match(html, /inputmode="email"/);
  assert.doesNotMatch(html, /<input[^>]*\bautofocus\b/, 'no keyboard on load');
  assert.match(html, /href="\?mode=spectator"/);
});
