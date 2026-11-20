import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createErrorBar, NOTICE_MS } from '../../src/FeedRaider.Web/wwwroot/page.js';

const HALF = NOTICE_MS / 2;

function rig() {
  const box = { textContent: '', style: { display: 'none' } };
  const spoken = [];
  const timers = new Map();
  let nextId = 1;
  let now = 0;
  const setTimer = (fn, ms) => { const id = nextId++; timers.set(id, { fn, at: now + ms }); return id; };
  const clearTimer = (id) => { timers.delete(id); };
  const advance = (ms) => {
    now += ms;
    for (const [id, t] of [...timers]) if (t.at <= now) { timers.delete(id); t.fn(); }
  };
  const bar = createErrorBar({ el: box, announce: (m) => spoken.push(m), setTimer, clearTimer });
  return { box, spoken, bar, advance };
}

test('a notice shows, is spoken once, and leaves after NOTICE_MS', () => {
  const { box, spoken, bar, advance } = rig();
  bar.notice("Could not find @x on Bluesky. Showing Discover instead.");
  assert.equal(box.style.display, 'block');
  assert.deepEqual(spoken, ["Could not find @x on Bluesky. Showing Discover instead."]);
  advance(NOTICE_MS - 1);
  assert.equal(box.style.display, 'block');
  advance(1);
  assert.equal(box.style.display, 'none');
  assert.equal(spoken.length, 1);
});

test('a second notice replaces the first and restarts the wait', () => {
  const { box, spoken, bar, advance } = rig();
  bar.notice('first');
  advance(HALF);
  bar.notice('second');
  assert.equal(box.textContent, 'second');
  advance(HALF + 1);
  assert.equal(box.style.display, 'block', 'the first notice timer must not hide the second');
  advance(HALF);
  assert.equal(box.style.display, 'none');
  assert.deepEqual(spoken, ['first', 'second']);
});

test('a sticky message never leaves by itself', () => {
  const { box, spoken, bar, advance } = rig();
  bar.sticky('unknown feed');
  advance(NOTICE_MS * 10);
  assert.equal(box.style.display, 'block');
  assert.deepEqual(spoken, ['unknown feed']);
});

test('a sticky message replacing a notice cancels the notice timer', () => {
  const { box, bar, advance } = rig();
  bar.notice('trying again');
  advance(HALF);
  bar.sticky('sign in: failed');
  advance(NOTICE_MS);
  assert.equal(box.style.display, 'block');
  assert.equal(box.textContent, 'sign in: failed');
});

test('hide clears the bar and the pending timer', () => {
  const { box, bar, advance } = rig();
  bar.notice('x');
  bar.hide();
  assert.equal(box.style.display, 'none');
  bar.sticky('y');
  advance(NOTICE_MS);
  assert.equal(box.style.display, 'block');
});

test('the runtime is handed both a sticky and a notice import, and the game uses the notice for trouble in a run', () => {
  const main = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
  assert.match(main, /setModuleImports\('main\.js', \{[\s\S]*showError,\s*showNotice,/);
  const program = readFileSync(new URL('../../src/FeedRaider.Web/Program.cs', import.meta.url), 'utf8');
  assert.match(program, /onFallback: ShowNotice, [^;]*onTrouble: ShowNotice/);
  assert.match(program, /onTrouble: ShowNotice\), m =>/);
  assert.match(program, /plan is null\) \{ ShowError\(/);
});
