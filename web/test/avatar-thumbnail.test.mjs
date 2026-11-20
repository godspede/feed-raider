import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const main = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');

// main.js is a module with top-level await and cannot be imported, so the avatar helpers are cut out of the source and run here.
function loadHelpers() {
  const from = main.indexOf('const CDN_AVATAR = ');
  assert.notEqual(from, -1, 'main.js defines CDN_AVATAR');
  const end = main.indexOf('\n}\n', from);
  assert.notEqual(end, -1, 'main.js defines loadAvatar after CDN_AVATAR');
  return new Function(`${main.slice(from, end + 3)}\nreturn { avatarThumbnail, loadAvatar };`)();
}

const PLAIN = 'https://cdn.bsky.app/img/avatar/plain/did:plc:abc/bafkreiabc@jpeg';
const THUMB = 'https://cdn.bsky.app/img/avatar_thumbnail/plain/did:plc:abc/bafkreiabc@jpeg';

test('a Bluesky CDN avatar address becomes its thumbnail; any other address is used as it is', () => {
  const { avatarThumbnail } = loadHelpers();
  assert.equal(avatarThumbnail(PLAIN), THUMB);
  for (const other of [THUMB, 'https://example.com/img/avatar/plain/x@jpeg', 'https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:abc/c@jpeg', '']) {
    assert.equal(avatarThumbnail(other), other);
  }
});

// an img that only records what is set on it and the listeners it is given
function fakeImg() {
  const listeners = {};
  return { listeners, addEventListener: (type, fn, opts) => { listeners[type] = { fn, opts }; } };
}

test('an avatar loads as the thumbnail, decoded asynchronously, and falls back to full size once if the thumbnail fails', () => {
  const { loadAvatar } = loadHelpers();
  const img = fakeImg();
  loadAvatar(img, PLAIN);
  assert.equal(img.src, THUMB);
  assert.equal(img.decoding, 'async');
  assert.deepEqual(img.listeners.error.opts, { once: true });
  img.listeners.error.fn();
  assert.equal(img.src, PLAIN);
});

test('an avatar from elsewhere loads as given, with no fallback to fail into', () => {
  const { loadAvatar } = loadHelpers();
  const img = fakeImg();
  loadAvatar(img, 'https://example.com/me.png');
  assert.equal(img.src, 'https://example.com/me.png');
  assert.equal(img.listeners.error, undefined);
});
