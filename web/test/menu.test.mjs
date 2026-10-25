import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { menuKey, createMenu, chromeLayout, FIELD_MAX_COLS, GEAR_PX, SIDE_MAX_COLS, SIDE_MIN_COLS, SIDE_GAP_COLS } from '../../src/FeedRaider.Web/wwwroot/page.js';

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
const wide = { cols: 160, rows: 47, cellW: 8, cellH: 17, left: 0, top: 0 };
const grid = (width, height, cellW = 8.4, cellH = 17) => ({ cols: Math.floor(width / cellW), rows: Math.floor(height / cellH), cellW, cellH, left: 0, top: 0 });

test('chromeLayout: the gear is always on the field in the HUD, clear of the Stage readout, with a full touch target', () => {
  for (const g of [wide, grid(1280, 800), grid(1920, 1080), grid(2560, 1440), { cols: 57, rows: 65, cellW: 7.2, cellH: 14, left: 0, top: 0 }, grid(1000, 700)]) {
    const l = chromeLayout(g);
    const c0 = Math.floor((g.cols - (Math.min(g.cols, FIELD_MAX_COLS) & ~1)) / 2);
    const stageLeft = (c0 + (Math.min(g.cols, FIELD_MAX_COLS) & ~1) - 1 - 'STAGE 10'.length) * g.cellW;
    assert.equal(l.gear.inField, true);
    assert.equal(l.gear.size, GEAR_PX);
    assert.ok(l.gear.x + l.gear.w + (l.gear.size - l.gear.w) / 2 <= stageLeft, 'the touch target stays off the Stage readout');
    assert.ok(l.gear.x + l.gear.w <= l.fieldRight && l.gear.x >= c0 * g.cellW, 'inside the field');
    assert.equal(l.gear.y, 0);
  }
});

test('chromeLayout: the side panel mirrors the feed log: same width, same gap from the field, same top and bottom rows', () => {
  for (const [w, h] of [[1280, 800], [1920, 1080], [2560, 1440]]) {
    const g = grid(w, h);
    const l = chromeLayout(g);
    const c0 = Math.floor((g.cols - 64) / 2);
    const logCols = Math.min(SIDE_MAX_COLS, c0 - SIDE_GAP_COLS - 1);
    const logLeft = (c0 - SIDE_GAP_COLS - logCols) * g.cellW, logRight = (c0 - SIDE_GAP_COLS) * g.cellW;
    assert.ok(l.side, `${w}x${h} shows both columns`);
    assert.equal(l.side.cols, logCols);
    assert.equal(l.side.w, logCols * g.cellW, 'same width in pixels');
    assert.ok(Math.abs(l.side.x - l.fieldRight - (c0 * g.cellW - logRight)) < 1e-6, 'same gap from the field');
    assert.equal(l.side.y, 2 * g.cellH, 'the log starts at row 2');
    assert.equal(l.side.y + l.side.h, (g.rows - 1) * g.cellH, 'the log ends with row rows - 2');
    assert.ok(l.side.x + l.side.w <= w, 'inside the window');
    assert.ok(logLeft >= 0);
    assert.ok(logCols >= SIDE_MIN_COLS && logCols <= SIDE_MAX_COLS);
  }
});

test('chromeLayout: the shared width is 44 columns where the margin allows and shrinks to fit below that', () => {
  assert.equal(chromeLayout(grid(1920, 1080)).side.cols, 44);
  assert.equal(chromeLayout(grid(2560, 1440)).side.cols, 44);
  assert.ok(chromeLayout(grid(1280, 800)).side.cols < 44);
});

test('chromeLayout: below the threshold there is no side panel, and one column more turns both on', () => {
  const at = (c0) => chromeLayout({ cols: 64 + 2 * c0, rows: 40, cellW: 8, cellH: 17, left: 0, top: 0 });
  const need = SIDE_MIN_COLS + SIDE_GAP_COLS + 1;
  assert.equal(at(need - 1).side, null);
  assert.equal(at(need).side.cols, SIDE_MIN_COLS);
  const phone = chromeLayout({ cols: 57, rows: 65, cellW: 7.2, cellH: 14, left: 0, top: 0 });
  assert.equal(phone.side, null);
  assert.equal(phone.gear.inField, true);
});

test('chromeLayout: the side column constants match the engine\'s Tuning', () => {
  const tuning = readFileSync(new URL('../../src/FeedRaider.Engine/Raider/Tuning.cs', import.meta.url), 'utf8');
  assert.equal(Number(/FieldMaxCols = (\d+)/.exec(tuning)[1]), FIELD_MAX_COLS);
  const m = /SidePanelMaxCols = (\d+), SidePanelMinCols = (\d+), SideGapCols = (\d+)/.exec(tuning);
  assert.deepEqual(m.slice(1).map(Number), [SIDE_MAX_COLS, SIDE_MIN_COLS, SIDE_GAP_COLS]);
});

test('index.html: no header bar in the game; the pause menu is a labelled dialog with the title, sound, sign-in, back to menu and the help', () => {
  const html = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/index.html', import.meta.url), 'utf8');
  assert.match(html, /body\.game:not\(\.debug\) header \{ display: none; \}/);
  assert.match(html, /<dialog id="menu" aria-labelledby="menu-title"/);
  assert.match(html, /<h2 id="menu-title">Feed Raider<\/h2>/);
  for (const id of ['vol', 'mute', 'menu-resume', 'menu-quit', 'menu-who', 'menu-signout']) {
    const at = html.indexOf(`id="${id}"`);
    assert.ok(at > html.indexOf('<dialog id="menu"') && at < html.indexOf('</dialog>'), `${id} is in the menu`);
  }
  assert.match(html, /<button id="gear" type="button" aria-label="Pause menu"/);
  assert.match(html, /<template id="info">[\s\S]*How to play[\s\S]*What it is[\s\S]*https:\/\/github\.com\/godspede\/feed-raider[\s\S]*<\/template>/);
  assert.match(html, /<aside id="side" class="info" aria-label="About Feed Raider"/);
});

test('index.html: the game has one hidden h1 before the side panel, a main landmark round the terminal, a status mirror and a labelled debug region', () => {
  const html = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/index.html', import.meta.url), 'utf8');
  assert.match(html, /^<!DOCTYPE html>/);
  assert.ok(html.indexOf('id="play-title"') > 0 && html.indexOf('id="play-title"') < html.indexOf('<aside id="side"'));
  assert.match(html, /<main id="term" aria-label="Game"><h1 id="play-title" class="sr-only">[^<]+<\/h1><\/main>/);
  assert.match(html, /<div id="announce" class="sr-only" role="status"><\/div>/);
  assert.match(html, /<section id="debug" aria-label="[^"]+"><\/section>/);
  assert.doesNotMatch(html, /<img id="start-avatar"/, 'the avatar image is made from script, with its source');
  for (const [tag] of html.matchAll(/<input[^>]*>/g)) assert.match(tag, /\stype=/, 'every input names its type: ' + tag);
});

test('main.js: Tab leaves the terminal, and the terminal textarea is out of the tab order only while playing', () => {
  const main = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
  assert.match(main, /term\.attachCustomKeyEventHandler\(\(e\) => e\.key !== 'Tab'\)/);
  assert.match(main, /helperTextarea\.tabIndex = -1/);
  assert.match(main, /helperTextarea\.removeAttribute\('tabindex'\)/);
  assert.match(main, /^  announce,$/m, 'the engine can announce through the bridge');
});

test('music.js: the sound controls stay in the tab order in the spectator view (in the header), not in the game', () => {
  const music = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/music.js', import.meta.url), 'utf8');
  assert.match(music, /closest\('header'\)\) continue;\s+el\.tabIndex = -1;/);
});
