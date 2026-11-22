import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createLogList, menuKey, createMenu, chromeLayout, menuGrants, sessionGrants, FIELD_MAX_COLS, GEAR_PX, SIDE_MAX_COLS, SIDE_MIN_COLS, SIDE_GAP_COLS } from '../../src/FeedRaider.Web/wwwroot/page.js';

const readSiteFile = (name) => readFileSync(new URL(`../../src/FeedRaider.Web/wwwroot/${name}`, import.meta.url), 'utf8');
const readIndexHtml = () => readSiteFile('index.html'), readMainJs = () => readSiteFile('main.js');
/** The start of the pause menu's dialog in index.html, and the line that closes a top-level function in main.js. */
const MENU_DIALOG = '<dialog id="menu"', TOP_LEVEL_END = '\n}\n';

const esc = (more = {}) => ({ key: 'Escape', code: 'Escape', repeat: false, ctrlKey: false, metaKey: false, altKey: false, ...more });

test('menuKey: Esc opens the pause menu during a run and closes it when open; nothing on the start page or with a modifier', () => {
  assert.equal(menuKey(esc(), { playing: true, open: false }), 'open');
  assert.equal(menuKey(esc(), { playing: true, open: true }), 'close');
  assert.equal(menuKey(esc(), { playing: false, open: false }), null, 'the start page keeps Esc for its suggestion list');
  assert.equal(menuKey(esc({ repeat: true }), { playing: true, open: false }), null, 'a held Esc does not flicker the pause menu');
  assert.equal(menuKey(esc({ ctrlKey: true }), { playing: true, open: false }), null);
  assert.equal(menuKey({ key: 'p', code: 'KeyP' }, { playing: true, open: false }), null);
});

test('menuKey: Alt+Esc and Meta+Esc do not open or close the pause menu', () => {
  for (const modifier of ['altKey', 'metaKey']) {
    assert.equal(menuKey(esc({ [modifier]: true }), { playing: true, open: false }), null, `${modifier} with the pause menu shut`);
    assert.equal(menuKey(esc({ [modifier]: true }), { playing: true, open: true }), null, `${modifier} with the pause menu open`);
  }
});

function menuRig() {
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
/** What the host is asked to do as the pause menu opens, and as Back to start page leaves an open one. */
const OPENING = ['release', 'pause', 'show'], BACK_TO_START = ['hide', 'quit', 'start'];

test('menu: nothing opens before a run starts', () => {
  const { calls, menu } = menuRig();
  menu.open();
  assert.deepEqual(calls, []);
  assert.equal(menu.isOpen(), false);
});

test('menu: opening lets go of held keys, pauses the game and shows the dialog; closing resumes it once', () => {
  const { calls, menu } = menuRig();
  menu.started();
  menu.open();
  menu.open();
  assert.deepEqual(calls, OPENING);
  assert.equal(menu.isOpen(), true);
  menu.close();
  // the dialog's own close event (Esc in the browser, or the Resume button) arrives too and changes nothing more
  menu.closed();
  assert.deepEqual(calls, [...OPENING, 'hide', 'resume']);
  assert.equal(menu.isOpen(), false);
});

test('menu: a dialog closed by the browser (its own Esc) still resumes the game', () => {
  const { calls, menu } = menuRig();
  menu.started();
  menu.open();
  menu.closed();
  assert.deepEqual(calls, [...OPENING, 'resume']);
});

test('menu: Back to start page ends the run and shows the start page; the stale run cannot reopen the pause menu', () => {
  const { calls, menu } = menuRig();
  menu.started();
  menu.open();
  calls.length = 0;
  menu.backToStartPage();
  menu.closed();
  assert.deepEqual(calls, BACK_TO_START, 'quit releases the pause itself; no resume of a run that is gone');
  assert.equal(menu.isPlaying(), false);
  menu.open();
  assert.deepEqual(calls, BACK_TO_START);
  menu.started();
  menu.open();
  assert.deepEqual(calls.slice(BACK_TO_START.length), OPENING);
});

test('menu: Back to start page twice quits once', () => {
  const { calls, menu } = menuRig();
  menu.started();
  menu.open();
  menu.backToStartPage();
  menu.backToStartPage();
  assert.equal(calls.filter((call) => call === 'quit').length, 1);
});

// a 1280 px window with 8 px cells: 160 columns, the 64-column field centred at 48..112
const wide = { cols: 160, rows: 47, cellW: 8, cellH: 17, left: 0, top: 0 };
// a phone held upright: about 410 by 910 px, too narrow for any side panel
const phone = { cols: 57, rows: 65, cellW: 7.2, cellH: 14, left: 0, top: 0 };
// a desktop browser's cell, in CSS pixels
const DESKTOP_CELL_W = 8.4, DESKTOP_CELL_H = 17;
const grid = ([width, height]) => ({ cols: Math.floor(width / DESKTOP_CELL_W), rows: Math.floor(height / DESKTOP_CELL_H), cellW: DESKTOP_CELL_W, cellH: DESKTOP_CELL_H, left: 0, top: 0 });
const LAPTOP = [1280, 800], FULL_HD = [1920, 1080], QHD = [2560, 1440];

test('chromeLayout: the gear is always on the field in the HUD, clear of the Stage readout, with a full touch target', () => {
  for (const screen of [wide, grid(LAPTOP), grid(FULL_HD), grid(QHD), phone, grid([1000, 700])]) {
    const layout = chromeLayout(screen);
    const fieldCols = Math.min(screen.cols, FIELD_MAX_COLS) & ~1;
    const fieldCol0 = Math.floor((screen.cols - fieldCols) / 2);
    const stageLeft = (fieldCol0 + fieldCols - 1 - 'STAGE 10'.length) * screen.cellW;
    const { gear } = layout;
    assert.equal(gear.size, GEAR_PX);
    assert.ok(gear.x + gear.w + (gear.size - gear.w) / 2 <= stageLeft, 'the touch target stays off the Stage readout');
    assert.ok(gear.x + gear.w <= layout.fieldRight && gear.x >= fieldCol0 * screen.cellW, 'inside the field');
    assert.equal(gear.y, 0);
  }
});

test('chromeLayout: the side panel mirrors the feed log: same width, same gap from the field, same top and bottom rows', () => {
  for (const size of [LAPTOP, FULL_HD, QHD]) {
    const [width, height] = size;
    const screen = grid(size);
    const layout = chromeLayout(screen);
    const fieldCol0 = Math.floor((screen.cols - FIELD_MAX_COLS) / 2);
    const logCols = Math.min(SIDE_MAX_COLS, fieldCol0 - SIDE_GAP_COLS - 1);
    const logLeft = (fieldCol0 - SIDE_GAP_COLS - logCols) * screen.cellW, logRight = (fieldCol0 - SIDE_GAP_COLS) * screen.cellW;
    const { side } = layout;
    assert.ok(side, `${width}x${height} shows both columns`);
    assert.equal(side.cols, logCols);
    assert.equal(side.w, logCols * screen.cellW, 'same width in pixels');
    assert.ok(Math.abs(side.x - layout.fieldRight - (fieldCol0 * screen.cellW - logRight)) < 1e-6, 'same gap from the field');
    assert.equal(side.y, 2 * screen.cellH, 'the log starts at row 2');
    assert.equal(side.y + side.h, (screen.rows - 1) * screen.cellH, 'the log ends with row rows - 2');
    assert.ok(side.x + side.w <= width, 'inside the window');
    assert.ok(logLeft >= 0);
    assert.ok(logCols >= SIDE_MIN_COLS && logCols <= SIDE_MAX_COLS);
  }
});

test('chromeLayout: the shared width is the widest side panel where the margin allows and shrinks to fit below that', () => {
  assert.equal(chromeLayout(grid(FULL_HD)).side.cols, SIDE_MAX_COLS);
  assert.equal(chromeLayout(grid(QHD)).side.cols, SIDE_MAX_COLS);
  assert.ok(chromeLayout(grid(LAPTOP)).side.cols < SIDE_MAX_COLS);
});

test('chromeLayout: below the threshold there is no side panel, and one column more turns both on', () => {
  const at = (margin) => chromeLayout({ ...wide, cols: FIELD_MAX_COLS + 2 * margin });
  const need = SIDE_MIN_COLS + SIDE_GAP_COLS + 1;
  assert.equal(at(need - 1).side, null);
  assert.equal(at(need).side.cols, SIDE_MIN_COLS);
  assert.equal(chromeLayout(phone).side, null);
});

test('index.html: no header bar in the game; the pause menu is a labelled dialog with the title, sound, sign-in, back to start page and the help', () => {
  const html = readIndexHtml();
  assert.match(html, /body\.game:not\(\.debug\) header \{ display: none; \}/);
  assert.match(html, /<dialog id="menu" aria-labelledby="menu-title"/);
  assert.match(html, /<h2 id="menu-title">Feed Raider<\/h2>/);
  for (const id of ['vol', 'mute', 'menu-resume', 'menu-quit', 'menu-who', 'menu-signout']) {
    const at = html.indexOf(`id="${id}"`);
    assert.ok(at > html.indexOf(MENU_DIALOG) && at < html.indexOf('</dialog>'), `${id} is in the pause menu`);
  }
  assert.match(html, /<button type="button" id="menu-quit" class="ghost">Back to start page<\/button>/, 'the quit button names the start page');
  assert.match(html, /<button id="gear" type="button" aria-label="Pause menu"/);
  assert.match(html, /<template id="info">[\s\S]*How to play[\s\S]*What it is[\s\S]*https:\/\/github\.com\/godspede\/feed-raider[\s\S]*<\/template>/);
  assert.match(html, /<aside id="side" class="info" aria-label="About Feed Raider"/);
});

test('index.html: the game has one hidden h1 before the side panel, a main landmark round the terminal, a status mirror and a labelled debug region', () => {
  const html = readIndexHtml();
  assert.match(html, /^<!DOCTYPE html>/);
  const titleAt = html.indexOf('id="play-title"');
  assert.ok(titleAt > 0 && titleAt < html.indexOf('<aside id="side"'));
  assert.match(html, /<main id="term" aria-label="Game"><h1 id="play-title" class="sr-only">[^<]+<\/h1><\/main>/);
  assert.match(html, /<div id="announce" class="sr-only" role="status"><\/div>/);
  assert.match(html, /<section id="debug" aria-label="[^"]+"><\/section>/);
  assert.doesNotMatch(html, /<img id="start-avatar"/, 'the avatar image is made from script, with its source');
  for (const [tag] of html.matchAll(/<input[^>]*>/g)) assert.match(tag, /\stype=/, 'every input names its type: ' + tag);
});

test('main.js: Tab leaves the terminal, and the terminal textarea is out of the tab order only while playing', () => {
  const main = readMainJs();
  assert.match(main, /term\.attachCustomKeyEventHandler\(\(e\) => e\.key !== 'Tab'\)/);
  assert.match(main, /helperTextarea\.tabIndex = -1/);
  assert.match(main, /helperTextarea\.removeAttribute\('tabindex'\)/);
  assert.match(main, /^  announce,$/m, 'the engine can announce through the bridge');
});

test('music.js: the sound controls stay in the tab order in the spectator view (in the header), not in the game', () => {
  const music = readSiteFile('music.js');
  assert.match(music, /closest\('header'\)\) continue;\s+el\.tabIndex = -1;/);
});

const GRANULAR = 'atproto repo:app.bsky.feed.like repo:app.bsky.feed.repost';
const OLDER = 'atproto repo:app.bsky.feed.like?action=create';
const LIKE_ONLY_NOTE = 'This sign-in can like, but not repost or undo.', SIGN_IN_AGAIN = 'Sign in again';
const session = (scope) => ({ did: 'did:plc:a', handle: 'a.test', scope });

test('menuGrants: the "can like, but not repost or undo" line and the button only for a session without all four grants', () => {
  assert.equal(menuGrants(null), null, 'signed out: nothing to sign in again to');
  assert.equal(menuGrants(session(GRANULAR)), null);
  assert.equal(menuGrants(session('atproto transition:generic')), null);
  assert.deepEqual(menuGrants(session(OLDER)), { note: LIKE_ONLY_NOTE, button: SIGN_IN_AGAIN });
  assert.deepEqual(menuGrants(session(OLDER), { popup: false }), { note: LIKE_ONLY_NOTE, button: `${SIGN_IN_AGAIN} (ends this run)` });
  assert.equal(menuGrants(session('atproto repo:app.bsky.feed.like repo:app.bsky.feed.repost?action=create')).button, SIGN_IN_AGAIN);
  assert.equal(menuGrants(session('atproto')).note, 'This sign-in cannot like or repost.');
  // a session whose scope could not be read is not called short: the game tries the write and the server answers
  assert.equal(menuGrants(session(null)), null);
  assert.deepEqual(sessionGrants(session(null)), { like: true, unlike: true, repost: true, unrepost: true });
  assert.deepEqual(sessionGrants(session(OLDER)), { like: true, unlike: false, repost: false, unrepost: false });
  assert.deepEqual(sessionGrants(null), { like: true, unlike: true, repost: true, unrepost: true }, 'signed out nothing is written, so nothing is locked');
});

test('the pause menu carries the line and the Sign in again button under "signed in as", both hidden until a session needs them', () => {
  const html = readIndexHtml();
  const who = html.indexOf('id="menu-who"'), note = html.indexOf('id="menu-grants"'), again = html.indexOf('id="menu-signin-again"'), out = html.indexOf('id="menu-signout"');
  assert.ok(who > 0 && note > who && again > note && out > again, 'who, then the line, then the button, then sign out');
  assert.match(html, /<p id="menu-grants"[^>]*\bhidden\b/);
  assert.match(html, /<button type="button" id="menu-signin-again"[^>]*\bhidden\b/);
  const main = readMainJs();
  assert.match(main, /page\.menuGrants\(/);
  assert.match(main, /page\.signInAgain\(/);
  assert.match(main, /exports\.Host\.SetGrants\(/);
  // ?feed=random starts a run, and so draws the pause menu, while main.js is still being read: what renderMenuWho and giveGrants
  // call must be hoisted functions, for a const declared beside them is not yet set and the run dies on it
  const used = main.slice(main.indexOf('function giveGrants('), main.indexOf(TOP_LEVEL_END, main.indexOf('function renderMenuWho(')));
  const startRunAt = main.indexOf('function startRun(');
  for (const [, name] of used.matchAll(/(?<![.\w$])([A-Za-z_][\w]*)\(/g)) {
    const asConst = main.search(new RegExp(`\\n(?:const|let) ${name} = `));
    if (asConst >= 0) assert.ok(asConst < startRunAt, `${name} is a const declared after startRun: declare it as a function`);
  }
});

// --- the pause menu's Feed log list ---

test('menu: opening refreshes the Feed log list before the dialog shows', () => {
  const calls = [];
  const menu = createMenu({
    show: () => calls.push('show'), hide: () => {}, setPaused: () => {}, releaseKeys: () => {}, quit: () => {}, toStart: () => {},
    refreshLog: () => calls.push('refreshLog'),
  });
  menu.started();
  menu.open();
  const refreshThenShow = ['refreshLog', 'show'];
  assert.deepEqual(calls, refreshThenShow);
  menu.open();
  assert.deepEqual(calls, refreshThenShow, 'an open menu is not refreshed twice');
});

function logRig(entries) {
  const makeElement = (tag) => {
    const el = { tag, children: [], attrs: {}, listeners: {}, textContent: '', hidden: false, disabled: false, title: '', className: '',
      append(...kids) { el.children.push(...kids); }, replaceChildren(...kids) { el.children = kids; },
      setAttribute(name, value) { el.attrs[name] = String(value); }, getAttribute(name) { return el.attrs[name] ?? null; },
      addEventListener(type, listener) { (el.listeners[type] ||= []).push(listener); },
      click() { for (const listener of el.listeners.click || []) listener({}); } };
    return el;
  };
  const toggles = [];
  const state = { entries };
  const els = { list: makeElement('ol'), empty: makeElement('p'), status: makeElement('p') };
  const logList = createLogList({
    els, doc: { createElement: makeElement },
    read: () => JSON.stringify(state.entries),
    toggle: (uri, action) => toggles.push([uri, action]),
  });
  return { els, logList, toggles, state };
}
const row = (id, more = {}) => ({ uri: `at://x/${id}`, name: `N${id}`, handle: `h${id}.test`, text: `t${id}`, like: 'off', repost: 'off', likeCount: '0', repostCount: '0', likeLocked: false, repostLocked: false, ...more });
const li = (els, index) => els.list.children[index];
const btn = (el, index) => el.children.flatMap(function walk(child) { return child.tag === 'button' ? [child] : (child.children || []).flatMap(walk); })[index];
/** The like and repost buttons of the list's first row, and whether a button reads as pressed. */
const likeButton = (els) => btn(li(els, 0), 0), repostButton = (els) => btn(li(els, 0), 1);
const pressed = (button) => button.getAttribute('aria-pressed');

test('logList: refresh reads the log and builds the list; an empty log shows the empty line', () => {
  const entries = [row(2), row(1)];
  const { els, logList } = logRig(entries);
  logList.refresh();
  assert.equal(els.list.children.length, entries.length);
  assert.equal(els.empty.hidden, true);
  const emptyRig = logRig([]);
  emptyRig.logList.refresh();
  assert.equal(emptyRig.els.empty.textContent, 'No posts have gone by yet.');
});

test('logList: pressing a button calls Toggle with the post and the action', () => {
  const post = row(1);
  const { els, logList, toggles } = logRig([post]);
  logList.refresh();
  likeButton(els).click();
  repostButton(els).click();
  assert.deepEqual(toggles, [[post.uri, 'like'], [post.uri, 'repost']]);
});

test('logList: the status line speaks the answer and the list is rebuilt after each one', () => {
  const { els, logList, state } = logRig([row(1)]);
  logList.refresh();
  assert.equal(els.status.textContent, '');
  const liked = 'Liked.', unliked = 'Like removed.', needsSignIn = 'Undo needs a fresh sign-in. See the pause menu.';
  state.entries = [row(1, { like: 'lit' })];
  logList.said(liked);
  assert.equal(els.status.textContent, liked);
  assert.equal(pressed(likeButton(els)), 'true');
  state.entries = [row(1)];
  logList.said(unliked);
  assert.equal(els.status.textContent, unliked);
  assert.equal(pressed(likeButton(els)), 'false');
  logList.said(needsSignIn);
  assert.equal(els.status.textContent, needsSignIn);
});

test('logList: a game that throws, or answers what is not JSON, is a fault the refresh throws on', () => {
  const { els } = logRig([row(1)]);
  const doc = { createElement: () => ({ children: [], append() {}, setAttribute() {}, addEventListener() {} }) };
  const throwing = createLogList({ els, doc, read: () => { throw new Error('no game'); }, toggle() {} });
  assert.throws(() => throwing.refresh(), /no game/);
  const garbled = createLogList({ els, doc, read: () => 'not json', toggle() {} });
  assert.throws(() => garbled.refresh(), SyntaxError);
});

test('index.html: no rule resizes one checkbox, so each is the theme\'s switch with its knob inside the track', () => {
  const html = readIndexHtml();
  const ids = [...html.matchAll(/<input id="([\w-]+)" type="checkbox"/g)].map((match) => match[1]);
  assert.ok(ids.includes('pictures'), 'the Pictures switch is a checkbox');
  for (const id of ids) {
    for (const [, body] of html.matchAll(new RegExp(`#${id}\\s*\\{([^}]*)\\}`, 'g'))) {
      assert.doesNotMatch(body, /(^|[\s;])(width|height)\s*:/, `#${id} keeps the switch's own size`);
    }
  }
});

test('index.html: a post button in the list looks different off, marked and lit, and not by colour alone', () => {
  const html = readIndexHtml();
  const rule = (selector) => {
    const at = html.indexOf('\n    ' + selector + ' {');
    assert.ok(at >= 0, 'a rule for ' + selector);
    return html.slice(at, html.indexOf('}', at));
  };
  const button = '#menu #menu-log-list button';
  const MARKED = '.marked', LIT = '.lit';
  const off = rule(button);
  assert.match(off, /background: transparent;/);
  assert.match(off, /color: var\(--dim\);/);
  assert.match(off, /box-shadow: none;/);
  const MIN_TOUCH_TARGET_PX = GEAR_PX;  // a list button is a thumb's target, as the gear is
  const [, minWidth, minHeight] = /min-width: (\d+)px; min-height: (\d+)px;/.exec(off);
  assert.ok(Number(minWidth) >= MIN_TOUCH_TARGET_PX && Number(minHeight) >= MIN_TOUCH_TARGET_PX, 'a touch target');
  const weight = (css) => Number(/font-weight: (\d+);/.exec(css)[1]);
  const fill = (css) => Number(/background: rgba\([\d, ]+, ([\d.]+)\);/.exec(css)[1]);
  const ring = (css, colour) => Number(new RegExp('box-shadow: inset 0 0 0 (\\d+)px var\\(--' + colour + '\\);').exec(css)[1]);
  const markedWeight = weight(rule(button + MARKED));
  assert.ok(weight(off) < markedWeight && markedWeight < weight(rule(button + LIT)), 'weight steps up');
  for (const state of [MARKED, LIT]) assert.match(rule(button + state), /color: var\(--text\);/, state + ' counts are full-strength text');
  for (const [kind, colour] of [['.log-like', 'like'], ['.log-repost', 'repost']]) {
    const marked = rule(button + MARKED + kind), lit = rule(button + LIT + kind);
    assert.ok(fill(marked) > 0 && fill(marked) < fill(lit), kind + ' fill steps up');
    assert.ok(ring(marked, colour) >= 1 && ring(marked, colour) < ring(lit, colour), kind + ' ring steps up');
  }
  assert.match(html, /--like: #EC4899; --repost: #0AC266;/);
  assert.match(rule(button + '[aria-busy="true"]'), /opacity: \.\d+; cursor: progress;/);
  assert.match(rule(button + ':disabled'), /opacity: \.\d+; cursor: not-allowed;/);
  // forced colours drop the fills, so the states keep a border each
  const forced = html.slice(html.indexOf('@media (forced-colors: active)'));
  assert.match(forced, /#menu #menu-log-list button\.marked \{ border: /);
  const status = rule('#menu-log-status');
  assert.match(status, /margin: 0;/);
  assert.doesNotMatch(status, /height|padding/);
  assert.match(rule('#menu-log-status:not(:empty)'), /margin-bottom: \d+px;/);
});

test('index.html: panels are told apart by hairlines and spacing; none has an accent bar down its left edge', () => {
  const html = readIndexHtml();
  assert.doesNotMatch(html, /border-left|border-inline-start|border-width|border-style/);
  assert.doesNotMatch(html, /box-shadow:[^;]*inset -?[1-9]\d*px 0(px)? /, 'no bar drawn with a one-sided inset shadow');
  assert.match(html, /#side \{[^}]*background: var\(--surface\); border: 1px solid var\(--line\); border-radius: 12px;/);
  assert.match(html, /:where\(#menu\) fieldset \{[^}]*background: var\(--surface\); border: 1px solid var\(--line\); border-radius: 12px;/);
  assert.match(html, /#menu \{[^}]*background: var\(--bg\); border: 1px solid var\(--line\); border-radius: 16px;/);
  assert.match(html, /#gear \{[^}]*background: transparent;[^}]*border-radius: 50%;/);
  assert.match(html, /#gear:hover:not\(:disabled\), #gear:focus-visible \{ background: rgba\(/);
  assert.match(html, /<button type="button" id="menu-resume" autofocus>/);
  assert.doesNotMatch(html.slice(html.indexOf(MENU_DIALOG), html.indexOf('<template id="info">')), /class="[^"]*primary/);
  assert.match(html, /#mute, #mute:hover:not\(:disabled\) \{ background: var\(--primary-tint\); box-shadow: inset 0 0 0 1px var\(--primary\); \}/);
});

// --- a run started from the address bar reaches the pause menu with every top-level binding in place ---

test('main.js: no top-level const, let or class comes after the block that starts a run from the address bar (?feed=random, ?handle=)', () => {
  const main = readMainJs();
  const lines = main.split('\n');
  const start = lines.findIndex((line) => /^  installMenu\(\);/.test(line));
  assert.notEqual(start, -1, 'main.js builds the pause menu in the top-level block that starts the run');
  const late = lines.map((line, index) => [index + 1, line]).filter(([lineNumber, line]) => lineNumber > start && /^(const|let|class)\s/.test(line));
  // a function called while the top-level block runs may read any of these before their line is reached (a temporal dead zone
  // error); function declarations are hoisted and are fine
  assert.deepEqual(late, [], 'declare these above the run-starting block');
});

test('main.js: every name installMenu and its helpers read at the top level is a function declaration or declared above the run block', () => {
  const main = readMainJs();
  const from = main.indexOf('function installMenu() {');
  assert.notEqual(from, -1);
  const body = main.slice(from, main.indexOf(TOP_LEVEL_END, from));
  const runBlock = main.indexOf('  installMenu();');
  for (const name of ['canPopup', 'giveGrants', 'renderMenuWho', 'placeChrome', 'logList']) {
    if (!body.includes(name)) continue;
    const decl = new RegExp(`^(async )?function ${name}\\b`, 'm').exec(main);
    const bound = new RegExp(`^(const|let) ${name}\\b`, 'm').exec(main);
    assert.ok(decl || (bound && bound.index < runBlock), `${name} is hoisted or declared before the run starts`);
  }
});
