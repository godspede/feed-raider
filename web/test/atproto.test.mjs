// node --test web/test: src/atproto.js bundled against a fake OAuth client, so init() runs the callback path with no network.
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const FAKE_CLIENT = `
export class BrowserOAuthClient {
  constructor(options) { this.clientMetadata = options.clientMetadata; }
  async signInRedirect(target, options) { (globalThis.fakeRedirects ??= []).push({ target, options }); }
  async authorize() { throw new Error('the popup path is not under test here'); }
  readCallbackParams() { return new URLSearchParams('code=c&state=s'); }
  findRedirectUrl() { return 'http://127.0.0.1/'; }
  async callback() { return { session: { sub: 'did:plc:me' }, state: globalThis.fakeCallbackState }; }
  async restore(sub) {
    return {
      did: sub,
      getTokenInfo: async () => ({ scope: globalThis.fakeScope }),
      fetchHandler: async (path, init) => {
        (globalThis.fakeCalls ??= []).push({ path, body: init?.body ? JSON.parse(typeof init.body === 'string' ? init.body : new TextDecoder().decode(init.body)) : null });
        if (globalThis.fakeRefusal) return new Response(JSON.stringify(globalThis.fakeRefusal), { status: globalThis.fakeRefusal.error === 'RateLimitExceeded' ? 429 : 403, headers: { 'content-type': 'application/json' } });
        return new Response(JSON.stringify({ uri: 'at://did:plc:me/app.bsky.feed.repost/new', cid: 'bafyreigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi' }), { headers: { 'content-type': 'application/json' } });
      },
    };
  }
}`;
const fakeOAuthClient = {
  name: 'fake-oauth-client',
  setup(b) {
    b.onResolve({ filter: /^@atproto\/oauth-client-browser$/ }, () => ({ path: 'fake', namespace: 'fake' }));
    b.onLoad({ filter: /.*/, namespace: 'fake' }, () => ({ contents: FAKE_CLIENT, loader: 'js' }));
  },
};

async function loadAtproto() {
  const outfile = join(mkdtempSync(join(tmpdir(), 'fr-atproto-')), 'atproto.mjs');
  await build({ entryPoints: [fileURLToPath(new URL('../src/atproto.js', import.meta.url))], outfile, bundle: true, format: 'esm', platform: 'node', plugins: [fakeOAuthClient], logLevel: 'silent' });
  return import(pathToFileURL(outfile).href);
}

test('initNote: null before sign-in, then the note a popup shows once its callback saved the session', async () => {
  globalThis.location = new URL('http://127.0.0.1:8000/?x=1');
  globalThis.history = { replaceState() {} };
  globalThis.fetch = async () => new Response(JSON.stringify({ handle: 'me.test' }));
  globalThis.fakeCallbackState = 'feed-raider-popup:key1';
  const atproto = await loadAtproto();
  assert.equal(atproto.initNote(), null, 'before any sign-in there is no note');
  await atproto.init();
  assert.equal(atproto.initNote(), 'Signed in as @me.test and saved in this browser. You can close this window; reload the game page if it still shows Sign in.');
});

async function signedIn() {
  globalThis.location = new URL('http://127.0.0.1:8000/');
  globalThis.history = { replaceState() {} };
  globalThis.fetch = async () => new Response(JSON.stringify({ handle: 'me.test' }));
  globalThis.fakeCallbackState = undefined;
  globalThis.fakeCalls = [];
  globalThis.fakeRefusal = null;
  globalThis.fakeScope = 'atproto repo:app.bsky.feed.like?action=create';
  const atproto = await loadAtproto();
  await atproto.init();
  return atproto;
}

test('session carries the scope the sign-in was granted', async () => {
  const atproto = await signedIn();
  assert.deepEqual(JSON.parse(atproto.session()), { did: 'did:plc:me', handle: 'me.test', scope: 'atproto repo:app.bsky.feed.like?action=create' });
});

test('a repost passes the post uri and cid and returns the new record address', async () => {
  const atproto = await signedIn();
  const out = JSON.parse(await atproto.repost('at://did:plc:x/app.bsky.feed.post/1', 'bafypost'));
  assert.deepEqual(out, { uri: 'at://did:plc:me/app.bsky.feed.repost/new', cid: 'bafyreigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi' });
  const [call] = globalThis.fakeCalls;
  assert.match(call.path, /com\.atproto\.repo\.createRecord$/);
  assert.equal(call.body.repo, 'did:plc:me');
  assert.equal(call.body.collection, 'app.bsky.feed.repost');
  assert.deepEqual(call.body.record.subject, { uri: 'at://did:plc:x/app.bsky.feed.post/1', cid: 'bafypost' });
  assert.ok(call.body.record.createdAt);
});

test('a delete passes the record address and nothing else', async () => {
  const atproto = await signedIn();
  await atproto.remove('at://did:plc:me/app.bsky.feed.like/3kabc');
  const [call] = globalThis.fakeCalls;
  assert.match(call.path, /com\.atproto\.repo\.deleteRecord$/);
  assert.deepEqual(call.body, { repo: 'did:plc:me', collection: 'app.bsky.feed.like', rkey: '3kabc' });
});

test('a delete of another account or another collection is refused before it is sent', async () => {
  const atproto = await signedIn();
  await assert.rejects(atproto.remove('at://did:plc:other/app.bsky.feed.like/1'));
  await assert.rejects(atproto.remove('at://did:plc:me/app.bsky.feed.post/1'));
  assert.equal(globalThis.fakeCalls.length, 0);
});

test('a refusal for missing scope comes back as sign in again, not as the raw error', async () => {
  const atproto = await signedIn();
  globalThis.fakeRefusal = { error: 'InvalidToken', message: 'Missing required scope "repo:app.bsky.feed.repost?action=create"' };
  await assert.rejects(atproto.repost('at://did:plc:x/app.bsky.feed.post/1', 'c'), (e) => /sign in again/i.test(e.message) && !/InvalidToken|Missing required/.test(e.message));
  globalThis.fakeRefusal = { error: 'Forbidden', message: 'insufficient scope' };
  await assert.rejects(atproto.remove('at://did:plc:me/app.bsky.feed.like/1'), /sign in again/i);
  globalThis.fakeRefusal = { error: 'RateLimitExceeded', message: 'slow down' };
  await assert.rejects(atproto.remove('at://did:plc:me/app.bsky.feed.like/1'), (e) => !/sign in again/i.test(e.message));
});

test('a sign-in from an installed home-screen page goes by redirect without opening a popup, and notes that it started', async () => {
  const atproto = await signedIn();
  const opened = [], saved = new Map();
  globalThis.fakeRedirects = [];
  globalThis.window = { open: (...args) => { opened.push(args); return null; } };
  Object.defineProperty(globalThis, 'navigator', { value: { standalone: true }, configurable: true });
  Object.defineProperty(globalThis, 'localStorage', { value: { setItem: (k, v) => saved.set(k, v), getItem: (k) => saved.get(k) ?? null, removeItem: (k) => saved.delete(k) }, configurable: true });
  try {
    const out = JSON.parse(await atproto.signIn('me.test'));
    assert.equal(out.did, 'did:plc:me', 'a redirect leaves the signed-in session as it was');
    assert.equal(opened.length, 0, 'no popup is opened');
    assert.equal(globalThis.fakeRedirects.length, 1);
    assert.equal(globalThis.fakeRedirects[0].target, 'me.test');
    const [report] = [...saved.values()].map((v) => JSON.parse(v));
    assert.equal(report.step, 'started');
  } finally {
    delete globalThis.window;
    delete globalThis.navigator;
    delete globalThis.localStorage;
  }
});
