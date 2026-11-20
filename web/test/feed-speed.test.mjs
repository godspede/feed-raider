import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  feedSpeed, createFeedSpeed, bindFeedSlider, createMenu, gameKey, GAME_KEYS, FEED_SPEED_KEY, FEED_SPEED_DEFAULT,
  FEED_SPEED_MIN, FEED_SPEED_MAX, FEED_SPEED_NAMES,
} from '../../src/FeedRaider.Web/wwwroot/page.js';

// The feed-speed option page side: reading the step, keeping it, the slider, the keys.

const memory = (start = {}) => {
  const data = { ...start };
  return { data, get: (k) => (k in data ? data[k] : null), set: (k, v) => { data[k] = String(v); } };
};

function fakeSlider() {
  const on = {};
  return { value: '3', addEventListener: (type, fn) => { on[type] = fn; }, fire: (type) => on[type]() };
}

test('feedSpeed: ?feed-speed=4 is step 4; 0, 6 and abc are ignored and fall back to the stored step, and with neither it is 3', () => {
  assert.equal(feedSpeed('?feed-speed=4', null), 4);
  assert.equal(feedSpeed('?feed-speed=4', '2'), 4);
  for (const bad of ['0', '6', 'abc', '', '2.5', '-1', '12']) {
    assert.equal(feedSpeed(`?feed-speed=${bad}`, '2'), 2, `feed-speed=${bad} with a stored 2`);
    assert.equal(feedSpeed(`?feed-speed=${bad}`, null), 3, `feed-speed=${bad} with nothing stored`);
  }
  assert.equal(feedSpeed('', '5'), 5);
  assert.equal(feedSpeed('?feed=random', null), 3);
  assert.equal(feedSpeed('', 'junk'), 3);
  assert.equal(feedSpeed('', '9'), 3);
  assert.equal(FEED_SPEED_DEFAULT, 3);
});

test('feedSpeed: the address wins for the visit and writes nothing; a later change is stored', () => {
  const store = memory({ [FEED_SPEED_KEY]: '2' });
  const speed = createFeedSpeed({ search: '?feed-speed=5', storage: store, tell: () => {} });
  assert.equal(speed.step, 5);
  assert.deepEqual(store.data, { [FEED_SPEED_KEY]: '2' }, 'visiting with ?feed-speed= leaves the stored step alone');
  speed.bump(-1);
  assert.equal(speed.step, 4);
  assert.equal(store.data[FEED_SPEED_KEY], '4');
});

test('feedSpeed: the step survives a reload', () => {
  const store = memory();
  const first = createFeedSpeed({ search: '', storage: store, tell: () => {} });
  first.bump(1);
  first.bump(1);
  assert.equal(createFeedSpeed({ search: '', storage: store, tell: () => {} }).step, 5);
});

test('feedSpeed: - lowers and + raises the step, and at 1 and 5 it stays; the game is told each time, with the toast', () => {
  const told = [];
  const speed = createFeedSpeed({ search: '?feed-speed=2', storage: memory(), tell: (s, say) => told.push([s, say]) });
  speed.bump(-1);
  speed.bump(-1);
  assert.equal(speed.step, 1);
  for (let i = 0; i < 6; i++) speed.bump(1);
  assert.equal(speed.step, 5);
  assert.deepEqual(told.slice(0, 2), [[1, true], [1, true]]);
  assert.deepEqual(told.at(-1), [5, true]);
  assert.equal(told.length, 8);
});

test('feedSpeed: the start-up call tells the game the step without a toast', () => {
  const told = [];
  const speed = createFeedSpeed({ search: '?feed-speed=4', storage: memory(), tell: (s, say) => told.push([s, say]) });
  speed.begin();
  assert.deepEqual(told, [[4, false]]);
});

test('feedSpeed: with storage blocked the step still starts and changes; any other failure surfaces', () => {
  const blocked = { get() { throw new DOMException('denied', 'SecurityError'); }, set() { throw new DOMException('full', 'QuotaExceededError'); } };
  const speed = createFeedSpeed({ search: '', storage: blocked, tell: () => {} });
  assert.equal(speed.step, 3);
  speed.bump(1);
  assert.equal(speed.step, 4);
  const broken = { get() { throw new RangeError('a bug'); }, set() {} };
  assert.throws(() => createFeedSpeed({ search: '', storage: broken, tell: () => {} }), RangeError);
});

test('feedSpeed: the slider opens on the current step; moving it stores the step and the game hears of it when the pause menu closes', () => {
  const store = memory();
  const told = [];
  const speed = createFeedSpeed({ search: '?feed-speed=2', storage: store, tell: (s, say) => told.push([s, say]) });
  const slider = fakeSlider();
  const bound = bindFeedSlider(slider, speed);
  bound.sync();
  assert.equal(slider.value, '2');
  slider.value = '4';
  slider.fire('input');
  assert.equal(store.data[FEED_SPEED_KEY], '4');
  assert.equal(speed.step, 4);
  assert.deepEqual(told, [], 'not told while the pause menu is open');
  speed.flush();
  assert.deepEqual(told, [[4, true]]);
  speed.flush();
  assert.equal(told.length, 1, 'told once');
  speed.bump(1);
  assert.equal(slider.value, '5', 'a key moves the slider too');
});

test('menu: closing resumes through the host\'s resuming hook first, so a changed feed speed reaches the game before it runs on', () => {
  const calls = [];
  const speed = createFeedSpeed({ search: '', storage: memory(), tell: (s, say) => calls.push(`speed ${s} ${say}`) });
  const menu = createMenu({
    show: () => calls.push('show'), hide: () => calls.push('hide'), setPaused: (on) => calls.push(on ? 'pause' : 'resume'),
    releaseKeys: () => {}, quit: () => {}, toStart: () => {}, resuming: () => speed.flush(),
  });
  menu.started();
  menu.open();
  speed.move(5);
  menu.close();
  menu.closed();
  assert.deepEqual(calls, ['pause', 'show', 'hide', 'speed 5 true', 'resume']);
  menu.open();
  menu.close();
  assert.equal(calls.filter((c) => c.startsWith('speed')).length, 1, 'a pause menu opened and closed with no change tells the game nothing');
});

test('gameKey: - and + or = are the feed speed keys by code or by what was typed, but not with Ctrl or Cmd held', () => {
  assert.equal(gameKey({ code: 'Minus', key: '-' }), 'Minus');
  assert.equal(gameKey({ code: 'Equal', key: '=' }), 'Equal');
  assert.equal(gameKey({ code: 'Equal', key: '+', shiftKey: true }), 'Equal');
  assert.equal(gameKey({ code: 'NumpadAdd', key: '+' }), 'Equal');
  assert.equal(gameKey({ code: 'NumpadSubtract', key: '-' }), 'Minus');
  assert.equal(gameKey({ code: 'BracketRight', key: '+' }), 'Equal', 'a layout with + on another key');
  assert.equal(gameKey({ code: 'Minus', key: '-', ctrlKey: true }), null, 'Ctrl+- is the browser\'s zoom');
  assert.equal(gameKey({ code: 'Equal', key: '+', ctrlKey: true }), null);
  assert.equal(gameKey({ code: 'Equal', key: '=', metaKey: true }), null);
  assert.equal(gameKey({ code: 'Minus', key: '-', altKey: false, ctrlKey: false, metaKey: false }), 'Minus');
  assert.ok(GAME_KEYS.has('Minus') && GAME_KEYS.has('Equal'));
});

test('index.html: a Feed speed group in the pause dialog, after Sound, with a 1 to 5 slider and the step named in words', () => {
  const html = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/index.html', import.meta.url), 'utf8');
  const dialog = html.slice(html.indexOf('<dialog id="menu"'), html.indexOf('</dialog>'));
  const sound = dialog.indexOf('id="menu-sound"'), speed = dialog.indexOf('id="menu-speed"');
  assert.ok(sound > 0 && speed > sound, 'the Feed speed group comes after Sound');
  assert.match(dialog, /<legend>Feed speed<\/legend>/);
  assert.match(dialog, /<input id="feed-speed" type="range" min="1" max="5" step="1" value="3"[^>]*aria-label="Feed speed"/);
  assert.match(dialog, /id="feed-speed-name"/);
});

test('main.js: the speed keys are handled by the page and never passed to the game as held keys', () => {
  const js = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
  assert.match(js, /createFeedSpeed\(/);
  assert.match(js, /feedSpeed\.bump\(code === page\.SLOWER_FEED_KEY \? -1 : 1\)/);
  assert.match(js, /Host\.SetFeedSpeed\(/);
});

test('gameKey: R and the two square-bracket keys are game keys (the feed log), and a layout with + on a bracket key still means the speed key', () => {
  assert.equal(gameKey({ code: 'KeyR', key: 'r' }), 'KeyR');
  assert.equal(gameKey({ code: 'KeyR', key: '' }), 'KeyR');
  assert.equal(gameKey({ code: 'BracketLeft', key: '[' }), 'BracketLeft');
  assert.equal(gameKey({ code: 'BracketRight', key: ']' }), 'BracketRight');
  assert.equal(gameKey({ code: 'Unidentified', key: ']' }), 'BracketRight');
  assert.equal(gameKey({ code: 'BracketRight', key: '+' }), 'Equal');
  for (const c of ['KeyR', 'BracketLeft', 'BracketRight']) assert.ok(GAME_KEYS.has(c));
});

test('feedSpeed: the lowest, highest and starting step are the same on the page and in the engine, and every step has a name', () => {
  const engine = readFileSync(new URL('../../src/FeedRaider.Engine/Raider/CardField.cs', import.meta.url), 'utf8');
  const steps = /class FeedSpeed\b[^}]*?const int Min = (\d+), Max = (\d+), Default = (\d+);/.exec(engine);
  assert.ok(steps, 'CardField.cs declares FeedSpeed.Min, Max and Default on one line');
  const [min, max, start] = steps.slice(1).map(Number);
  assert.deepEqual([FEED_SPEED_MIN, FEED_SPEED_MAX, FEED_SPEED_DEFAULT], [min, max, start]);
  assert.equal(FEED_SPEED_NAMES.length, max - min + 1, 'one name for each step');
});
