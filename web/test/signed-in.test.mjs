import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { gameKey, isTextEntry, createGestures, TAP_MS, SLOP } from '../../src/FeedRaider.Web/wwwroot/page.js';

const scope = JSON.parse(readFileSync(new URL('../scope.json', import.meta.url), 'utf8'));
const bundle = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/atproto.js', import.meta.url), 'utf8');

test('the granular scope reads the timeline with the same aud as the feed, and creates no posts', () => {
  const parts = scope.granular.split(' ');
  const aud = 'aud=did:web:api.bsky.app%23bsky_appview';
  assert.ok(parts.includes(`rpc:app.bsky.feed.getTimeline?${aud}`));
  assert.ok(parts.includes(`rpc:app.bsky.feed.getFeed?${aud}`));
  assert.ok(scope.granular.includes('getTimeline'));
  assert.ok(!/app\.bsky\.feed\.post/.test(scope.granular));
  assert.ok(!/action=(create|update|delete)/.test(scope.granular.replace('app.bsky.feed.like?action=create', '')));
});

test('the bundle exposes getTimeline and the rebuilt file carries it', () => {
  assert.match(bundle, /getTimeline/);
});

test('B, Space, F and E are game keys, by code or by the typed letter', () => {
  for (const c of ['KeyB', 'Space', 'KeyF', 'KeyE']) assert.equal(gameKey({ code: c, key: '' }), c);
  assert.equal(gameKey({ code: 'Unidentified', key: 'b' }), 'KeyB');
  assert.equal(gameKey({ code: '', key: 'B' }), 'KeyB');
  assert.equal(gameKey({ code: '', key: ' ' }), 'Space');
  assert.equal(gameKey({ code: 'KeyQ', key: 'q' }), null);
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
  const g = createGestures({ holdStart: (c) => log.push('hold'), holdEnd: () => log.push('end'), tap: () => log.push('tap'), bomb: () => log.push('bomb') }, () => t);
  return { g, log, at: (ms) => { t = ms; } };
}

test('a left click only starts and ends a hold (the game decides: a quick one never likes); right click bombs', () => {
  const { g, log } = rig();
  g.mouseDown(0, [1, 1]); g.mouseUp(0);
  assert.ok(!log.includes('like'));
  assert.deepEqual(log, ['hold', 'tap', 'end']);
  g.mouseDown(2, [1, 1]);
  assert.equal(log.at(-1), 'bomb');
});

test('a touch tap starts a hold then ends it; a long still touch keeps the hold until release', () => {
  const { g, log, at } = rig();
  g.touchStart(1, 10, 10, [2, 2]); at(100); g.touchEnd(0);
  assert.deepEqual(log, ['hold', 'end', 'tap']);
  log.length = 0; at(1000);
  g.touchStart(1, 10, 10, [2, 2]); at(1000 + TAP_MS + 200);
  assert.deepEqual(log, ['hold']);       // still holding: the game fills the charge
  g.touchEnd(0);
  assert.deepEqual(log, ['hold', 'end']); // too long to be a tap
});

test('dragging cancels the hold; a two-finger tap bombs', () => {
  const { g, log, at } = rig();
  g.touchStart(1, 0, 0, [2, 2]); g.touchMove(1, SLOP + 1, 0);
  assert.equal(log.at(-1), 'end');
  g.touchEnd(0);
  assert.ok(!log.includes('tap') && !log.includes('bomb'));
  log.length = 0; at(5000);
  g.touchStart(1, 0, 0, [2, 2]); g.touchStart(2, 0, 0, [2, 2]); at(5100); g.touchEnd(1); g.touchEnd(0);
  assert.ok(log.includes('bomb'));
});
