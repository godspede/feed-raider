import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createTour, TOUR_SLEEP_MS } from '../../src/FeedRaider.Web/wwwroot/page.js';

const read = (f) => readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/' + f, import.meta.url), 'utf8');
const html = read('index.html'), main = read('main.js'), program = readFileSync(new URL('../../src/FeedRaider.Web/Program.cs', import.meta.url), 'utf8');

const FIELD_COLUMNS = 64;
const MIN_BUTTONS_WIDTH_CH = 52;
const MIN_BUTTON_HEIGHT_FLOOR_PX = 54;
const MIN_BUTTON_HEIGHT_CEIL_PX = 76;
const MIN_BUTTON_TYPE_CEIL_PX = 24;
const MIN_LARGE_PX = 19;
const BOLD = 700;
const AA_TEXT = 4.5;
const AA_LARGE = 3;
const TOUR_SEED = 7;   // any seed: the tests check the tour passes it on unchanged

// a setTimeout and clearTimeout on a clock that moves only when advance() is called
const fakeClock = () => {
  const armed = new Map();
  let now = 0, nextId = 1;
  return {
    armed,
    setTimer: (fn, ms) => { armed.set(nextId, { fn, at: now + ms }); return nextId++; },
    clearTimer: (id) => { armed.delete(id); },
    advance(ms) {
      now += ms;
      for (const [id, { fn, at }] of [...armed]) if (at <= now) { armed.delete(id); fn(); }
    },
  };
};

const tourRig = (extra = {}) => {
  const calls = [], clock = fakeClock();
  const tour = createTour({
    start: (seed, slow) => calls.push(['start', seed, slow]), stop: () => calls.push(['stop']),
    sleep: () => calls.push(['sleep']), wake: () => calls.push(['wake']),
    seed: () => TOUR_SEED, setTimer: clock.setTimer, clearTimer: clock.clearTimer, ...extra,
  });
  return { tour, calls, clock, names: () => calls.map((c) => c[0]) };
};

test('the tour waits for the runtime: show() before ready() starts nothing, ready() then starts it once', () => {
  const { tour, calls } = tourRig();
  tour.show();
  assert.deepEqual(calls, []);
  tour.ready();
  assert.deepEqual(calls, [['start', TOUR_SEED, false]]);
});

test('showing again does not restart a running tour; hiding stops it once; showing after that starts a new one', () => {
  const { tour, calls } = tourRig();
  tour.ready(); tour.show(); tour.show();
  assert.equal(calls.length, 1);
  tour.hide(); tour.hide();
  assert.deepEqual(calls.map((c) => c[0]), ['start', 'stop']);
  tour.show();
  assert.deepEqual(calls.map((c) => c[0]), ['start', 'stop', 'start']);
});

test('a hide before the runtime is up stops nothing and starts nothing', () => {
  const { tour, calls } = tourRig();
  tour.show(); tour.hide(); tour.ready();
  assert.deepEqual(calls, []);
});

test('reduced motion starts the tour slowed', () => {
  const { tour, calls } = tourRig({ slow: true });
  tour.ready(); tour.show();
  assert.deepEqual(calls[0], ['start', TOUR_SEED, true]);
});

test('the tour sleeps once TOUR_SLEEP_MS pass with nothing done, and not a moment before', () => {
  const { tour, clock, names } = tourRig();
  tour.ready(); tour.show();
  clock.advance(TOUR_SLEEP_MS - 1);
  assert.deepEqual(names(), ['start']);
  clock.advance(1);
  assert.deepEqual(names(), ['start', 'sleep']);
});

test('something done before the tour sleeps puts that off by a full TOUR_SLEEP_MS', () => {
  const { tour, clock, names } = tourRig();
  tour.ready(); tour.show();
  clock.advance(TOUR_SLEEP_MS - 1);
  tour.visitorActed();
  clock.advance(TOUR_SLEEP_MS - 1);
  assert.deepEqual(names(), ['start']);
  clock.advance(1);
  assert.deepEqual(names(), ['start', 'sleep']);
});

test('something done while it is asleep wakes the tour once, and it sleeps again after another TOUR_SLEEP_MS', () => {
  const { tour, clock, names } = tourRig();
  tour.ready(); tour.show();
  clock.advance(TOUR_SLEEP_MS);
  tour.visitorActed(); tour.visitorActed();
  assert.deepEqual(names(), ['start', 'sleep', 'wake']);
  clock.advance(TOUR_SLEEP_MS);
  assert.deepEqual(names(), ['start', 'sleep', 'wake', 'sleep']);
});

test('hiding the start page while the tour is asleep ends the sleep; showing it again runs a tour', () => {
  const { tour, clock, names } = tourRig();
  tour.ready(); tour.show();
  clock.advance(TOUR_SLEEP_MS);
  tour.hide();
  assert.deepEqual(names(), ['start', 'sleep', 'stop', 'wake']);
  tour.show();
  assert.deepEqual(names(), ['start', 'sleep', 'stop', 'wake', 'start']);
  tour.visitorActed();
  assert.deepEqual(names(), ['start', 'sleep', 'stop', 'wake', 'start'], 'a running tour is not woken');
});

test('no sleep while the start page is hidden or before the runtime is ready, and nothing done then arms one', () => {
  const hidden = tourRig();
  hidden.tour.ready(); hidden.tour.visitorActed();
  hidden.clock.advance(TOUR_SLEEP_MS);
  assert.deepEqual(hidden.calls, []);
  assert.equal(hidden.clock.armed.size, 0);
  const early = tourRig();
  early.tour.show(); early.tour.visitorActed();
  early.clock.advance(TOUR_SLEEP_MS);
  assert.deepEqual(early.calls, []);
  assert.equal(early.clock.armed.size, 0);
});

test('no timer is left armed after hide(), running or asleep', () => {
  const running = tourRig();
  running.tour.ready(); running.tour.show(); running.tour.visitorActed();
  running.tour.hide();
  assert.equal(running.clock.armed.size, 0);
  const asleep = tourRig();
  asleep.tour.ready(); asleep.tour.show();
  asleep.clock.advance(TOUR_SLEEP_MS);
  asleep.tour.hide();
  assert.equal(asleep.clock.armed.size, 0);
});

test('main.js: one set of passive listeners for keys, pointers, touches and the wheel tells the tour the visitor did something', () => {
  const at = main.indexOf("for (const type of ['keydown', 'pointermove', 'pointerdown', 'touchstart', 'wheel']) {");
  assert.notEqual(at, -1);
  assert.match(main.slice(at), /^[^\n]*\n\s+window\.addEventListener\(type, \(\) => tour\.visitorActed\(\), \{ capture: true, passive: true \}\);\n\}/);
  assert.equal(main.match(/tour\.visitorActed\(\)/g).length, 1, 'registered in one place');
  assert.equal(main[at - 1], '\n', 'at the top level, so it runs once at load');
  assert.match(main, /sleep: \(\) => \{ exports\.Host\.SetTourAsleep\(true\); startPage\.classList\.add\('asleep'\); \},\s+wake: \(\) => \{ exports\.Host\.SetTourAsleep\(false\); startPage\.classList\.remove\('asleep'\); \},/);
});

test('main.js and index.html: the class a sleep puts on the start page pauses both layers of the title glow', () => {
  const [, added] = /sleep: \(\) => \{[^}]*startPage\.classList\.add\('([a-z-]+)'\);[^}]*\},/.exec(main);
  const [, removed] = /wake: \(\) => \{[^}]*startPage\.classList\.remove\('([a-z-]+)'\);[^}]*\},/.exec(main);
  assert.equal(removed, added, 'a wake takes off the class a sleep put on');
  assert.match(main, /const startPage = \$\('start'\);/);
  const rule = new RegExp(`#start\\.${added} h1::before, #start\\.${added} h1::after \\{ animation-play-state: paused; \\}`);
  assert.match(html, rule, 'the stylesheet has a rule for the class, and it pauses both layers');
});

test('Program.cs: a sleep is the tour\'s own hold, apart from the hidden tab\'s, and touches no real run', () => {
  assert.match(program, /public static void SetTourAsleep\(bool asleep\) => _tourPause\.Hold\(SleepHold, asleep\);/);
  const [, sleep] = /private const string SleepHold = "([a-z]+)";/.exec(program);
  const [, tab] = /private const string TabHold = "([a-z]+)";/.exec(program);
  assert.notEqual(sleep, tab);
  assert.doesNotMatch(program, /_pause\.Hold\(SleepHold/, 'the game\'s own pause never holds for a sleep');
});

test('index.html: the start page is four big buttons: sign in (or play), play as someone, random, how to play', () => {
  const start = html.slice(html.indexOf('<div id="start">'));
  assert.match(start, /<main id="start-form" aria-label="Start page">/, 'the buttons are labelled as the start page');
  for (const id of ['start-signin', 'start-play', 'start-guest', 'start-random', 'start-howto']) assert.match(start, new RegExp(`<button[^>]*id="${id}"`), id);
  const choices = [...start.slice(0, start.indexOf('<dialog id="howto"')).matchAll(/<button[^>]*id="(start-[a-z-]+)"/g)].map((m) => m[1]);
  assert.deepEqual(choices, ['start-retry', 'start-signin', 'start-play', 'start-guest', 'start-random', 'start-howto', 'start-signout']);
  // four at a time, never five: Play stands in for Sign in, and every one of them is a big button
  const big = [...start.matchAll(/<button[^>]*id="(start-[a-z-]+)"[^>]*class="[^"]*\bbig\b[^"]*"/g)].map((m) => m[1]);
  assert.deepEqual(big, ['start-signin', 'start-play', 'start-guest', 'start-random', 'start-howto']);
  assert.match(start, /id="start-play" class="primary big" hidden>/);
  assert.match(read('page.js'), /els\.play\.hidden = !view\.signedIn;/);
  assert.match(read('page.js'), /els\.signIn\.hidden = !view\.signIn;/);
});

test('index.html: the handle box, status lines and error are still there; the box starts closed behind its button', () => {
  assert.match(html, /<div class="combo" id="start-combo" hidden>\s*<input type="text" id="start-handle"/);
  assert.match(html, /id="start-suggest" role="listbox"/);
  assert.match(html, /<div id="start-error" role="alert">/);
  assert.match(html, /<p id="start-note" role="status" hidden>/);
  assert.match(main, /startEls\.guest\.setAttribute\('aria-controls', 'start-combo'\)/);
  assert.match(main, /guestCombo\.hidden = !open;\s+startEls\.guest\.setAttribute\('aria-expanded', String\(open\)\)/);
  // the first press opens the box and does not check anything
  assert.match(main, /if \(guestCombo\.hidden\) \{ setGuestOpen\(true\); startHandle\.focus\(\); return; \}/);
});

test('index.html: How to play is a labelled dialog inside the start page (the rest of the page is inert), with the pause menu\'s help', () => {
  const at = html.indexOf('<dialog id="howto" aria-labelledby="howto-title">');
  assert.ok(at > html.indexOf('<div id="start">') && at < html.indexOf('<script src="./xterm/xterm.min.js">'));
  assert.match(html, /id="howto-title"/);
  assert.match(main, /\$\('howto-info'\)\.append\(\$\('info'\)\.content\.cloneNode\(true\)\)/);
});

test('index.html: the field shows through the start page; there is no enclosing panel', () => {
  const css = html.slice(html.indexOf('#start { position: fixed'), html.indexOf('html.spectator #start'));
  assert.doesNotMatch(css.split('#start.booting')[0], /var\(--bg\);\s*$/, 'the start page is not opaque');
  const form = /#start-form \{[^}]*\}/.exec(html)[0];
  assert.doesNotMatch(form, /background|border:|box-shadow/, 'no panel behind the buttons');
  assert.doesNotMatch(html, /class="blurb"/);
});

test('index.html: the buttons are big: most of the field wide, tall, in large type, with room between them', () => {
  const form = /#start-form \{[^}]*\}/.exec(html)[0];
  // the field is 64 cells of the page's own face, so a width in ch follows it; a phone gets the whole window
  const ch = Number(/width: min\((\d+)ch, 100%\)/.exec(form)[1]);
  assert.ok(ch >= MIN_BUTTONS_WIDTH_CH && ch <= FIELD_COLUMNS,'most of the 64-column field');
  const bigRule = /#start \.big \{[^}]*\}/.exec(html)[0];
  const [, lo, hi] = /min-height: clamp\((\d+)px, [\d.]+vh, (\d+)px\)/.exec(bigRule);
  assert.ok(Number(lo) >= MIN_BUTTON_HEIGHT_FLOOR_PX && Number(hi) >= MIN_BUTTON_HEIGHT_CEIL_PX, 'tall');
  const [, tlo, thi] = /font-size: clamp\((\d+)px, [\d.]+vw, (\d+)px\)/.exec(bigRule);
  assert.ok(Number(tlo) >= MIN_LARGE_PX && Number(thi) >= MIN_BUTTON_TYPE_CEIL_PX, 'large type');
  assert.match(bigRule, /width: 100%/);
  assert.match(html, /#start-choices \{ display: flex; flex-direction: column; gap: clamp\(10px, [\d.]+vh, 18px\); \}/);
  // the stack sits clear of the foot, so the ship has a band of the field to fly in under the buttons
  assert.match(html, /#start-foot \{ margin-top: clamp\(24px, [\d.]+vh, \d+px\);/);
});

test('index.html: the small print is one quiet line under the buttons: the hint, who is signed in with sign out, the spectator link, and that Bluesky did not make the game', () => {
  const start = html.slice(html.indexOf('<div id="start">'));
  const foot = start.slice(start.indexOf('<div id="start-foot">'), start.indexOf('</main>'));
  assert.ok(start.indexOf('<div id="start-foot">') > start.indexOf('id="start-howto"'), 'after the last big button');
  for (const id of ['start-hint', 'start-signed', 'start-who', 'start-signout']) assert.match(foot, new RegExp(`id="${id}"`), id);
  assert.match(foot, /<a href="\?mode=spectator">spectator view<\/a>/);
  assert.match(foot, /<p class="hint">Not made or endorsed by Bluesky\.<\/p>/);
  assert.doesNotMatch(foot, /class="[^"]*\b(big|primary|ghost)\b/, 'nothing in the small print is a big button');
  const out = /#start #start-signout,[^{]*\{[^}]*\}/.exec(html)[0];
  assert.match(out, /background: transparent;[^}]*font-size: 13px;[^}]*box-shadow: none/);
  assert.match(html, /#start \.hint \{[^}]*font-size: 12px;/);
});

// no scrim: each big button is see-through only as far as its label still reads over any frame of the field
test('contrast: button labels hold their floor on their own backing', () => {
  // relative luminance and contrast ratio as WCAG 2 defines them
  const lum = (rgb) => { const c = rgb.map((v) => v / 255).map((v) => (v <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4)); return .2126 * c[0] + .7152 * c[1] + .0722 * c[2]; };
  const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + .05) / (y + .05); };
  const hex = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16));
  const token = (name) => hex(new RegExp(`--${name}: (#[0-9A-Fa-f]{6});`).exec(html)[1]);
  const white = token('text');
  assert.deepEqual(white, [255, 255, 255]);
  // body-size labels: a solid button rests on the deeper blue and keeps 4.5:1 at rest, under the pointer and pressed
  assert.match(html, /button\.primary, #menu-resume, #howto-close, #start-retry, header form button \{ background: var\(--primary-deep\); color: var\(--text\)/);
  assert.match(html, /button\.primary:hover:not\(:disabled\)[^{]*\{ background: var\(--primary-hover\); \}/);
  assert.match(html, /button\.primary:active:not\(:disabled\)[^{]*\{ background: var\(--primary-press\); \}/);
  for (const name of ['primary-deep', 'primary-hover', 'primary-press']) assert.ok(ratio(white, token(name)) >= AA_TEXT, 'white on --' + name);
  // and the plain button, on the raised surface and its two pointer steps
  assert.match(html, /\n\s*button \{ background: var\(--surface-2\); color: var\(--text\)/);
  for (const name of ['surface-2', 'line', 'line-strong']) assert.ok(ratio(white, token(name)) >= AA_TEXT, 'white on --' + name);
  // the big buttons are a little see-through, so the worst case is the brightest or the darkest frame behind them
  const [, pa] = /#start \.primary \{ background: rgba\(15, 115, 255, ([\d.]+)\); color: var\(--text\)/.exec(html);
  const [, ga] = /#start \.ghost \{ background: rgba\(34, 46, 63, ([\d.]+)\); color: var\(--text\)/.exec(html);
  assert.deepEqual(token('primary'), [15, 115, 255]);
  assert.deepEqual(token('surface-2'), [34, 46, 63]);
  // their labels are large text (19px and up, bold), whose floor is 3:1; the blue one sits near it, the grey ones clear 4.5:1
  const [, bigPx, bigWeight] = /#start \.big \{[^}]*font-size: clamp\((\d+)px,[^}]*font-weight: (\d+);/.exec(html);
  assert.ok(Number(bigPx) >= MIN_LARGE_PX && Number(bigWeight) >= BOLD, 'a big label counts as large text');
  const over = (rgb, a, back) => rgb.map((v) => Math.round(v * a + back * (1 - a)));
  for (const back of [0, 255]) {
    assert.ok(ratio(white, over(token('primary'), Number(pa), back)) >= AA_LARGE, 'primary over ' + back);
    assert.ok(ratio(white, over(token('surface-2'), Number(ga), back)) >= AA_TEXT, 'ghost over ' + back);
  }
  // under the pointer the blue one turns solid: still 3:1
  assert.match(html, /#start \.primary:hover:not\(:disabled\) \{ background: var\(--primary\);/);
  assert.ok(ratio(white, token('primary')) >= AA_LARGE,'primary under the pointer');
  // the small lines sit on a dark chip, near-opaque
  const [, chipAlpha] = /--chip: rgba\(21, 29, 40, ([\d.]+)\);/.exec(html);
  assert.deepEqual(token('bg'), [21, 29, 40]);
  const chip = over(token('bg'), Number(chipAlpha), 255);
  assert.ok(ratio(token('primary-light'), chip) >= AA_TEXT, 'link on chip over white');
  assert.ok(ratio(token('dim'), chip) >= AA_TEXT, 'small print on chip over white');
});

test('reduced motion: the field is slowed and the page animations are off', () => {
  assert.match(main, /slow: matchMedia\('\(prefers-reduced-motion: reduce\)'\)\.matches/);
  assert.match(html, /@media \(prefers-reduced-motion: reduce\) \{\s+\*, \*::before, \*::after \{ animation: none !important/);
  assert.match(program, /slow \? TourSlowScale : 1/);
  assert.match(program, /TourSlowScale = 0\.25;/);
});

test('main.js: the tour runs only while the start page shows, and a pick ends it before the game starts', () => {
  assert.match(main, /function startRun\(choice\) \{\s+tour\.hide\(\);/);
  assert.match(main, /musicCue\('start'\);\s+tour\.show\(\);/);
  assert.match(main, /if \(needsStart && !queued\) tour\.show\(\)/);
  // the game's keys do nothing while the start page or the pause menu shows
  assert.match(main, /!menu\.isPlaying\(\) \|\| menu\.isOpen\(\)/);
});

test('index.html: the title glow pulses by opacity alone, over a title whose own shadow holds still, and shows nothing when still', () => {
  assert.doesNotMatch(/#start h1 \{[^}]*\}/.exec(html)[0], /animation/);
  for (const name of ['glow-settle', 'glow-swell']) {
    const frames = new RegExp(`@keyframes ${name} \\{(.*)\\}`).exec(html)[1];
    assert.deepEqual([...frames.matchAll(/([a-z-]+):/g)].map((m) => m[1]), ['opacity', 'opacity'], name);
  }
  assert.match(/#start h1::before, #start h1::after \{[^}]*\}/.exec(html)[0], /opacity: 0;/);
});

test('main.js: a tab opened in the background tells the game it is hidden before the tour first shows', () => {
  const told = main.indexOf('\nfollowTabVisibility();\n');
  assert.notEqual(told, -1, 'followTabVisibility runs at load, outside any handler');
  assert.ok(told < main.indexOf('if (needsStart && !queued) tour.show()'));
});

test('Program.cs: the tour has its own pause and cancel, plays no sound and saves nothing; a real game ends it', () => {
  const tourBody = program.slice(program.indexOf('public static async Task StartTour'), program.indexOf('public static void StopTour'));
  assert.doesNotMatch(tourBody, /SfxPlay|MusicCue|SaveBestScore|Bridge\./);
  assert.match(program, /public static async Task StartGame\([^)]*\)\s+\{\s+StopTour\(\);/);
  assert.match(program, /_tourPause\.Hold\(TabHold, hidden\)/);
});
