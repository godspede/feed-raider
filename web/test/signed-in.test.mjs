import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { gameKey, isTextEntry, createGestures, signInAgain, grants, CLICK_MS, SLOP } from '../../src/FeedRaider.Web/wwwroot/page.js';

const scope = JSON.parse(readFileSync(new URL('../scope.json', import.meta.url), 'utf8'));
const bundle = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/atproto.js', import.meta.url), 'utf8');

test('the granular scope reads the timeline with the same aud as the feed, and creates no posts', () => {
  const parts = scope.granular.split(' ');
  const aud = 'aud=did:web:api.bsky.app%23bsky_appview';
  assert.ok(parts.includes(`rpc:app.bsky.feed.getTimeline?${aud}`));
  assert.ok(parts.includes(`rpc:app.bsky.feed.getFeed?${aud}`));
  assert.ok(parts.includes(`rpc:app.bsky.feed.getFeedSkeleton?${aud}`));
  assert.ok(parts.includes(`rpc:app.bsky.feed.getPostThread?${aud}`));
  assert.ok(!/app\.bsky\.feed\.post/.test(scope.granular));
  assert.ok(!/action=(create|update|delete)/.test(scope.granular.replace(/repo:app\.bsky\.feed\.(like|repost)\?action=create&action=delete/g, '')));
});

test('the granular scope creates and deletes likes and reposts and names no other collection', () => {
  const parts = scope.granular.split(' ');
  assert.ok(parts.includes('atproto'));
  assert.ok(parts.includes('repo:app.bsky.feed.like?action=create&action=delete'));
  assert.ok(parts.includes('repo:app.bsky.feed.repost?action=create&action=delete'));
  const repos = parts.filter((p) => p.startsWith('repo'));
  assert.equal(repos.length, 2, 'only the like and the repost');
  assert.deepEqual(parts.filter((p) => p.startsWith('rpc:')).map((p) => p.split('?')[0]),
    ['rpc:app.bsky.feed.getFeed', 'rpc:app.bsky.feed.getFeedSkeleton', 'rpc:app.bsky.feed.getTimeline', 'rpc:app.bsky.feed.getPostThread']);
  assert.equal(parts.length, 7);
});

test('the built atproto.js names getTimeline', () => {
  assert.match(bundle, /getTimeline/);
});

test('B, Space, F and E are game keys, by code or by the typed letter', () => {
  for (const c of ['KeyB', 'Space', 'KeyF', 'KeyE']) assert.equal(gameKey({ code: c, key: '' }), c);
  assert.equal(gameKey({ code: 'Unidentified', key: 'b' }), 'KeyB');
  assert.equal(gameKey({ code: '', key: 'B' }), 'KeyB');
  assert.equal(gameKey({ code: '', key: ' ' }), 'Space');
  assert.equal(gameKey({ code: 'KeyQ', key: 'q' }), null);
});

test('Z and X, which like and repost the post under the ship, are game keys by code or by the typed letter', () => {
  for (const c of ['KeyZ', 'KeyX']) assert.equal(gameKey({ code: c, key: '' }), c);
  assert.equal(gameKey({ code: 'Unidentified', key: 'z' }), 'KeyZ');
  assert.equal(gameKey({ code: '', key: 'X' }), 'KeyX');
});

test('text fields keep their keys; buttons, sliders and the xterm helper do not', () => {
  assert.equal(isTextEntry({ tagName: 'INPUT', type: 'text' }), true);
  assert.equal(isTextEntry({ tagName: 'TEXTAREA', className: '' }), true);
  assert.equal(isTextEntry({ tagName: 'INPUT', type: 'range' }), false);
  assert.equal(isTextEntry({ tagName: 'BUTTON' }), false);
  assert.equal(isTextEntry({ tagName: 'TEXTAREA', className: 'xterm-helper-textarea' }), false);
  assert.equal(isTextEntry(null), false);
});

function rig() {
  const log = []; let t = 0;
  const g = createGestures({ click: () => log.push('click'), bomb: () => log.push('bomb') }, () => t);
  return { g, log, at: (ms) => { t = ms; } };
}

test('a left button press clicks; a right one bombs', () => {
  const { g, log } = rig();
  g.mouseDown(0, [1, 1]);
  assert.deepEqual(log, ['click']);
  g.mouseDown(2, [1, 1]);
  assert.equal(log.at(-1), 'bomb');
});

test('a quick touch is a click; a long still touch is not', () => {
  const { g, log, at } = rig();
  g.touchStart(1, 10, 10, [2, 2]); at(100); g.touchEnd(0);
  assert.deepEqual(log, ['click']);
  log.length = 0; at(1000);
  g.touchStart(1, 10, 10, [2, 2]); at(1000 + CLICK_MS + 200);
  g.touchEnd(0);
  assert.deepEqual(log, []);
});

test('a dragged touch is not a click; a quick two-finger touch bombs', () => {
  const { g, log, at } = rig();
  g.touchStart(1, 0, 0, [2, 2]); g.touchMove(1, SLOP + 1, 0);
  g.touchEnd(0);
  assert.ok(!log.includes('click') && !log.includes('bomb'));
  log.length = 0; at(5000);
  g.touchStart(1, 0, 0, [2, 2]); g.touchStart(2, 0, 0, [2, 2]); at(5100); g.touchEnd(1); g.touchEnd(0);
  assert.ok(log.includes('bomb'));
});

// A feed generator's posts (Discover, For You, any at:// feed) are read with app.bsky.feed.getFeed. Bluesky's servers answer
// that call only when the sign-in also allows app.bsky.feed.getFeedSkeleton for the same service: without it the feed fails
// with 'Missing required scope "rpc:app.bsky.feed.getFeedSkeleton?aud=..."'.
test('the sign-in asks for both permissions a feed generator read needs', () => {
  const asked = scope.granular.split(' ');
  const aud = '?aud=did:web:api.bsky.app%23bsky_appview';
  assert.ok(asked.includes('rpc:app.bsky.feed.getFeed' + aud), 'getFeed');
  assert.ok(asked.includes('rpc:app.bsky.feed.getFeedSkeleton' + aud), 'getFeedSkeleton, which getFeed needs');
});

test('Sign in again asks for the new scope for the same handle, and afterwards the game is given the new grants', async () => {
  const asked = [], given = [];
  let saved = { did: 'did:plc:a', handle: 'ada.test', scope: 'atproto repo:app.bsky.feed.like?action=create' };
  const auth = {
    session: () => JSON.stringify(saved),
    signIn: async (input, opts) => {
      asked.push({ input, opts });
      saved = { ...saved, scope: scope.granular };
      return JSON.stringify(saved);
    },
  };
  const out = await signInAgain(auth, { setGrants: (g) => given.push(g) });
  assert.equal(asked.length, 1);
  assert.equal(asked[0].input, 'ada.test', 'the same handle, so Bluesky does not ask which account');
  assert.deepEqual(given, [{ like: true, unlike: true, repost: true, unrepost: true }]);
  assert.deepEqual(out, { like: true, unlike: true, repost: true, unrepost: true });
  assert.deepEqual(grants(scope.granular), { like: true, unlike: true, repost: true, unrepost: true }, 'the scope the page asks for unlocks all four');
});

test('Sign in again: a sign-in that failed or was given the old scope again leaves the game as it was', async () => {
  const given = [];
  const older = { did: 'did:plc:a', handle: 'ada.test', scope: 'atproto repo:app.bsky.feed.like?action=create' };
  // the authorization server still had the old client metadata: the old scope again
  const same = { session: () => JSON.stringify(older), signIn: async () => JSON.stringify(older) };
  assert.deepEqual(await signInAgain(same, { setGrants: (g) => given.push(g) }), { like: true, unlike: false, repost: false, unrepost: false });
  assert.deepEqual(given, [{ like: true, unlike: false, repost: false, unrepost: false }]);
  // a refusal is the caller's to show; the game is told nothing
  const failing = { session: () => JSON.stringify(older), signIn: async () => { throw new Error('access_denied'); } };
  await assert.rejects(signInAgain(failing, { setGrants: (g) => given.push(g) }), /access_denied/);
  assert.equal(given.length, 1);
  // signed out there is no one to sign in again as
  assert.equal(await signInAgain({ session: () => null, signIn: async () => assert.fail('no sign-in') }, { setGrants: (g) => given.push(g) }), null);
  // another account picked in the popup: the run is not theirs, so nothing is unlocked
  const other = { session: () => JSON.stringify(older), signIn: async () => JSON.stringify({ did: 'did:plc:b', handle: 'bob.test', scope: scope.granular }) };
  await assert.rejects(signInAgain(other, { setGrants: (g) => given.push(g) }), /another account/);
  assert.equal(given.length, 1);
});
