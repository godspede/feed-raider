import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const root = new URL('../../src/FeedRaider.Web/wwwroot/', import.meta.url);
const html = readFileSync(new URL('index.html', root), 'utf8');
const main = readFileSync(new URL('main.js', root), 'utf8');

test('the play field takes no text selection in any engine, while page chrome stays selectable', () => {
  const rule = html.match(/body\.game #term, body\.game #term \* \{([^}]*)\}/);
  assert.ok(rule, 'a rule for the field and its children');
  for (const d of ['user-select: none', '-moz-user-select: none', '-webkit-user-select: none']) assert.ok(rule[1].includes(d), d);
  // the no-select rules are scoped to the game; the start page and how-to-play panel have none
  assert.ok(!/^\s*(body|html|\*)\s*\{[^}]*user-select: none/m.test(html), 'no unscoped user-select: none');
});

test('the field cancels selectstart and dragstart, and a press on it never reaches the browser (Firefox chatbot shortcut)', () => {
  const g = main.slice(main.indexOf('function installGameInput'));
  assert.match(g, /screen\.addEventListener\('selectstart', \(e\) => e\.preventDefault\(\)\)/);
  assert.match(g, /screen\.addEventListener\('dragstart', \(e\) => e\.preventDefault\(\)\)/);
  // the browser must never see a press on the field, or Firefox offers its selection shortcut; the game's gestures still get it
  assert.match(g, /addEventListener\('mousedown', \(e\) => \{\s*e\.preventDefault\(\);\s*e\.stopPropagation\(\);\s*fieldPress = true;\s*gestures\.mouseDown/);
  // the release of that press is stopped at the window in capture, wherever the pointer is, and still ends the hold.
  // A release whose press began elsewhere (a button, the menu) passes through untouched.
  assert.match(g, /window\.addEventListener\('mouseup', \(e\) => \{\s*if \(fieldPress\) \{ fieldPress = false; e\.stopPropagation\(\); \}\s*gestures\.mouseUp\(e\.button\);\s*\}, true\)/);
});
