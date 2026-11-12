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
  readCallbackParams() { return new URLSearchParams('code=c&state=s'); }
  findRedirectUrl() { return 'http://127.0.0.1/'; }
  async callback() { return { session: { sub: 'did:plc:me' }, state: globalThis.fakeCallbackState }; }
  async restore(sub) { return { did: sub, fetchHandler: async () => new Response('{}') }; }
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
