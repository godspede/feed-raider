import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const main = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');

// main.js is a module with top-level await and cannot be imported, so the pieces that touch the saved handle are cut out of the
// source and run against stubs: the handle helpers, the start page's read at load, commitPick and showStartPage's read.
function cut(start, end) {
  const from = main.indexOf(start);
  assert.notEqual(from, -1, `main.js has ${start}`);
  const to = main.indexOf(end, from);
  assert.notEqual(to, -1, `main.js has ${end} after ${start}`);
  return main.slice(from, to + end.length);
}

// the key main.js keeps the handle under
const HANDLE_KEY = 'feed-raider.handle';

function loadStartPage(localStorage) {
  const source = [
    cut('function savedHandle() {', '\n}\n'),
    cut('function saveHandle(h) {', '\n}\n'),
    cut("if (!needsStart) startPage.classList.add('booting');", 'savedHandle();\n'),
    cut("function commitPick(h, kind, feed = '') {", '\n}\n'),
  ].join('');
  const reshow = cut('  if (!startHandle.value) startHandle.value = ', '\n');
  const world = {
    localStorage,
    savedHandleKey: HANDLE_KEY,
    needsStart: true,
    startPage: { classList: { add: () => {} } },
    startHandle: { value: '' },
    location: { search: '', pathname: '/', hash: '' },
    history: { replaceState: (_state, _title, url) => { world.location.search = url.slice(url.indexOf('?')); } },
    isRandomFeed: (p) => p.get('feed') === 'random',
    queued: null,
    renderStartPage: () => {},
    pickHandle: (pick) => { world.picked = pick; },
  };
  const boot = new Function(...Object.keys(world), `${source}return { commitPick, reshow: () => {\n${reshow}} };`);
  return { world, page: boot(...Object.values(world)) };
}

const blocked = () => { throw new DOMException('The operation is insecure.', 'SecurityError'); };
const blockedStorage = { getItem: blocked, setItem: blocked, removeItem: blocked, clear: blocked, key: blocked };

function workingStorage() {
  const kept = new Map();
  return { getItem: (k) => (kept.has(k) ? kept.get(k) : null), setItem: (k, v) => kept.set(k, String(v)), kept };
}

test('with storage blocked, the start page wires up, a pick goes through and nothing is remembered', () => {
  const { world, page } = loadStartPage(blockedStorage);
  assert.equal(world.startHandle.value, '');
  page.commitPick('alice.bsky.social', 'guest');
  assert.deepEqual(world.picked, { handle: 'alice.bsky.social', feed: '' });
  assert.equal(new URLSearchParams(world.location.search).get('handle'), 'alice.bsky.social');
  page.reshow();
  assert.equal(world.startHandle.value, '');
});

test('with working storage, a picked handle is saved and read back when the start page comes up', () => {
  const storage = workingStorage();
  const first = loadStartPage(storage);
  first.page.commitPick('alice.bsky.social', 'guest');
  assert.equal(storage.kept.get(HANDLE_KEY), 'alice.bsky.social');
  assert.equal(first.world.startHandle.value, '', 'the box is not filled by the pick itself');
  first.page.reshow();
  assert.equal(first.world.startHandle.value, 'alice.bsky.social');
  const next = loadStartPage(storage);
  assert.equal(next.world.startHandle.value, 'alice.bsky.social');
});

test('a storage failure that is not a DOMException still surfaces', () => {
  const broken = { getItem: () => { throw new TypeError('not storage'); } };
  assert.throws(() => loadStartPage(broken), TypeError);
});

test('main.js reaches localStorage only through the guarded helpers and pageStorage', () => {
  const uses = main.split('\n').filter((line) => /\b(localStorage|sessionStorage)\.\w+\(/.test(line)).map((line) => line.trim());
  assert.deepEqual(uses, [
    'try { return Number.parseInt(localStorage.getItem(bestKey), 10) || 0; }',
    'try { localStorage.setItem(bestKey, String(n)); }',
    "try { return localStorage.getItem(savedHandleKey) || ''; }",
    'try { localStorage.setItem(savedHandleKey, h); }',
    'const pageStorage = { get: (k) => localStorage.getItem(k), set: (k, v) => localStorage.setItem(k, v) };',
  ]);
});
