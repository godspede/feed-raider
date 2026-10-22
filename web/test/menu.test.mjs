import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { menuKey, createMenu, chromeLayout, FIELD_MAX_COLS, GEAR_PX } from '../../src/FeedRaider.Web/wwwroot/page.js';

const esc = (more = {}) => ({ key: 'Escape', code: 'Escape', repeat: false, ctrlKey: false, metaKey: false, altKey: false, ...more });

test('menuKey: Esc opens the menu during a run and closes it when open; nothing on the start page or with a modifier', () => {
  assert.equal(menuKey(esc(), { playing: true, open: false }), 'open');
  assert.equal(menuKey(esc(), { playing: true, open: true }), 'close');
  assert.equal(menuKey(esc(), { playing: false, open: false }), null, 'the start page keeps Esc for its suggestion list');
  assert.equal(menuKey(esc({ repeat: true }), { playing: true, open: false }), null, 'a held Esc does not flicker the menu');
  assert.equal(menuKey(esc({ ctrlKey: true }), { playing: true, open: false }), null);
  assert.equal(menuKey({ key: 'p', code: 'KeyP' }, { playing: true, open: false }), null);
  assert.equal(menuKey({ key: 'Esc', code: '' }, { playing: true, open: false }), 'open', 'an old browser\'s key name');
});

function rig() {
  const calls = [];
  const host = {
    show: () => calls.push('show'),
    hide: () => calls.push('hide'),
    setPaused: (on) => calls.push(on ? 'pause' : 'resume'),
    releaseKeys: () => calls.push('release'),
    quit: () => calls.push('quit'),
    toStart: () => calls.push('start'),
  };
  return { calls, menu: createMenu(host) };
}

test('menu: nothing opens before a run starts', () => {
  const { calls, menu } = rig();
  menu.open();
  assert.deepEqual(calls, []);
  assert.equal(menu.isOpen(), false);
});

test('menu: opening lets go of held keys, pauses the game and shows the dialog; closing resumes it once', () => {
  const { calls, menu } = rig();
  menu.started();
  menu.open();
  menu.open();
  assert.deepEqual(calls, ['release', 'pause', 'show']);
  assert.equal(menu.isOpen(), true);
  menu.close();
  // the dialog's own close event (Esc in the browser, or the Resume button) arrives too and changes nothing more
  menu.closed();
  assert.deepEqual(calls, ['release', 'pause', 'show', 'hide', 'resume']);
  assert.equal(menu.isOpen(), false);
});

test('menu: a dialog closed by the browser (its own Esc) still resumes the game', () => {
  const { calls, menu } = rig();
  menu.started();
  menu.open();
  menu.closed();
  assert.deepEqual(calls, ['release', 'pause', 'show', 'resume']);
});

test('menu: Back to main menu ends the run and shows the start page; the stale run cannot reopen the menu', () => {
  const { calls, menu } = rig();
  menu.started();
  menu.open();
  calls.length = 0;
  menu.backToMenu();
  menu.closed();
  assert.deepEqual(calls, ['hide', 'quit', 'start'], 'quit releases the pause itself; no resume of a run that is gone');
  assert.equal(menu.isPlaying(), false);
  menu.open();
  assert.deepEqual(calls, ['hide', 'quit', 'start']);
  // a new run is a fresh start
  menu.started();
  menu.open();
  assert.deepEqual(calls.slice(3), ['release', 'pause', 'show']);
});

test('menu: Back to main menu twice quits once', () => {
  const { calls, menu } = rig();
  menu.started();
  menu.open();
  menu.backToMenu();
  menu.backToMenu();
  assert.equal(calls.filter((c) => c === 'quit').length, 1);
});

// a 1280 px window with 8 px cells: 160 columns, the 64-column field centred at 48..112
const wide = { cols: 160, rows: 47, cellW: 8, cellH: 17, left: 0, top: 0, width: 1280 };

test('chromeLayout: on a wide window the gear sits just right of the field, by the Stage readout, and the side panel fills the right margin', () => {
  const l = chromeLayout(wide);
  assert.equal(l.fieldRight, 112 * 8);
  assert.equal(l.gear.inField, false);
  assert.ok(l.gear.x >= l.fieldRight && l.gear.x - l.fieldRight <= 8, 'beside the field edge');
  assert.equal(l.gear.y, 0);
  assert.ok(l.gear.size >= GEAR_PX);
  assert.ok(l.side, 'the right margin is wide enough for the panel');
  assert.ok(l.side.x >= l.gear.x + l.gear.size || l.side.y >= l.gear.y + l.gear.size, 'the panel does not sit under the gear');
  assert.ok(l.side.x >= l.fieldRight, 'never over the game');
  assert.ok(l.side.x + l.side.w <= wide.width);
});

test('chromeLayout: on a phone the field fills the width, so the gear goes in the HUD left of the Stage readout and there is no side panel', () => {
  const l = chromeLayout({ cols: 57, rows: 65, cellW: 7.2, cellH: 14, left: 0, top: 0, width: 412 });
  assert.equal(l.gear.inField, true);
  // the field is 56 columns from column 0; "STAGE 10" ends one column short of its right edge
  const stageLeft = (56 - 1 - 8) * 7.2;
  assert.ok(l.gear.x + l.gear.w <= stageLeft, 'clear of the longest Stage readout');
  assert.ok(l.gear.x + l.gear.w >= stageLeft - 2 * 7.2, 'still next to it');
  assert.equal(l.gear.y, 0);
  assert.equal(l.side, null);
});

test('chromeLayout: a margin too narrow for the panel keeps the gear outside the field but shows no panel', () => {
  const cols = 64 + 2 * 8; // 8 columns each side
  const l = chromeLayout({ cols, rows: 40, cellW: 8, cellH: 17, left: 0, top: 0, width: cols * 8 });
  assert.equal(l.gear.inField, false);
  assert.equal(l.side, null);
});

test('chromeLayout: the field width matches the engine\'s Tuning.FieldMaxCols', () => {
  const tuning = readFileSync(new URL('../../src/FeedRaider.Engine/Raider/Tuning.cs', import.meta.url), 'utf8');
  assert.equal(Number(/FieldMaxCols = (\d+)/.exec(tuning)[1]), FIELD_MAX_COLS);
});

test('index.html: no header bar in the game; the pause menu is a labelled dialog with the title, sound, sign-in, back to menu and the help', () => {
  const html = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/index.html', import.meta.url), 'utf8');
  assert.match(html, /body\.game:not\(\.debug\) header \{ display: none; \}/);
  assert.match(html, /<dialog id="menu" aria-labelledby="menu-title"/);
  assert.match(html, /<h1 id="menu-title">Feed Raider<\/h1>/);
  for (const id of ['vol', 'mute', 'menu-resume', 'menu-quit', 'menu-who', 'menu-signout']) {
    const at = html.indexOf(`id="${id}"`);
    assert.ok(at > html.indexOf('<dialog id="menu"') && at < html.indexOf('</dialog>'), `${id} is in the menu`);
  }
  assert.match(html, /<button id="gear" type="button" tabindex="-1" aria-label="Pause menu"/);
  assert.match(html, /<template id="info">[\s\S]*How to play[\s\S]*What it is[\s\S]*https:\/\/github\.com\/godspede\/feed-raider[\s\S]*<\/template>/);
  assert.match(html, /<aside id="side" class="info" aria-label="About Feed Raider"/);
});
