// Every way into a run really shows the game, in a real browser (Firefox): the start page is gone, the field is on top, the gear is
// reachable, and Back to start page brings the start page back with its buttons on top. The unit tests read the stylesheet and the script,
// but only a browser can tell that a `display: flex` beat the `hidden` attribute and left the start page over a running game.
// Run it with node, giving the address of a served build as the argument (for example http://127.0.0.1:8000/): test/e2e/start-shows-game.e2e.mjs
// `npm run e2e:start` (test/e2e/run-start-e2e.mjs) builds, publishes, serves a build and runs this.
// PLAYWRIGHT_CORE names playwright-core when it is not importable here; FIREFOX_EXE names another Playwright build of Firefox to use instead of the installed one.
//
// No network: every request to a host other than the served one is stubbed with page.route, and WebSockets are closed.
//  - the handle lookup (resolveHandle) and the typeahead are answered with canned JSON in the shape main.js reads, because Play as
//    someone only starts a run once the handle resolves.
//  - every other request (the feeds, posts, labels, avatars, plc.directory) is REFUSED. The game starts its run before any feed
//    answers (startRun hides the start page and shows the field, the engine reports a failed feed later), so a refusal is what the
//    player gets with no connection, and it needs no canned post shapes to go stale. What is asserted is the page, not the posts.
// Prints one line per way in and exits non-zero when any check failed.
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const address = process.argv[2];
if (!address) throw new Error('usage: start-shows-game.e2e.mjs <address of a served build>');
const origin = new URL(address).origin;
const HANDLE = 'e2e.test';
const SLOW = 90000; // the runtime is a few dozen MB of WebAssembly, loaded on the first page of each context
const RUN_UI_MS = 30e3;       // the gear is placed, or the game has stopped playing, within this once the run (or the quit) is under way
const MENU_OPEN_MS = 10e3;    // the pause menu opens within this of a press on the gear
const PAINT_SETTLE_MS = 500;  // after the run or the start page shows, the terminal gets this long to paint
const HANDLE_SETTLE_MS = 1500;   // a status line under the choices may appear this long after the handle is typed

async function playwright() {
  try { return await import('playwright-core'); } catch { /* not installed here */ }
  const dir = process.env.PLAYWRIGHT_CORE;
  if (!dir) throw new Error('install playwright-core, or set PLAYWRIGHT_CORE to its directory');
  return import(pathToFileURL(join(dir, 'index.mjs')).href);
}

const json = (body, status = 200) => ({ status, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify(body) });
const stubbed = new Map(); // host -> requests turned away, so the run says what it kept off the network

async function cutOffTheNetwork(context) {
  await context.route('**/*', (route) => {
    const url = new URL(route.request().url());
    if (url.origin === origin || url.protocol === 'data:' || url.protocol === 'blob:') return route.continue();
    stubbed.set(url.host, (stubbed.get(url.host) || 0) + 1);
    if (url.pathname.endsWith('/com.atproto.identity.resolveHandle')) return route.fulfill(json({ did: 'did:plc:e2etestaccount0000000000' }));
    if (url.pathname.endsWith('/app.bsky.actor.searchActorsTypeahead')) return route.fulfill(json({ actors: [] }));
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 204, headers: { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*' } });
    return route.abort('connectionrefused');
  });
  await context.routeWebSocket(/.*/, (ws) => { stubbed.set('ws:' + new URL(ws.url()).host, 1); ws.close(); });
}

// what a player needs, read in the page
function inPage() {
  const $ = (id) => document.getElementById(id);
  const centre = (e) => { const r = e.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; };
  const top = (e) => { const [x, y] = centre(e); return document.elementFromPoint(x, y); };
  const name = (e) => (e ? e.tagName.toLowerCase() + (e.id ? '#' + e.id : '') : 'nothing');
  const start = $('start');
  const gear = $('gear');
  const field = top($('term'));
  const gearTop = top(gear);
  const buttons = [...start.querySelectorAll('button')].filter((b) => b.checkVisibility() && b.getClientRects().length).map((b) => {
    const t = top(b);
    return { id: b.id, top: name(t), on: t === b || b.contains(t) };
  });
  return {
    startDisplay: getComputedStyle(start).display,
    playing: document.body.classList.contains('playing'),
    fieldTop: name(field),
    fieldInStart: !field || !!field.closest('#start'),
    fieldInTerm: !!field && !!field.closest('#term'),
    gearDisplay: getComputedStyle(gear).display,
    gearTop: name(gearTop),
    gearOnTop: !!gearTop && (gearTop === gear || gear.contains(gearTop)),
    buttons,
    search: location.search,
  };
}

async function waitForRun(page) {
  await page.waitForFunction(() => document.body.classList.contains('playing'), null, { timeout: SLOW });
  // the gear is placed once the terminal has painted at its final cell size
  await page.waitForFunction(() => { const g = document.getElementById('gear'); return !g.hidden && g.style.width; }, null, { timeout: RUN_UI_MS });
  await page.waitForTimeout(PAINT_SETTLE_MS);
}

const problems = [];
function expect(label, ok, detail) {
  if (!ok) problems.push(`${label}: ${detail}`);
}
async function showsTheGame(page, label) {
  await waitForRun(page).catch(async (e) => {
    const said = await page.evaluate(() => ['start-error', 'start-note', 'error'].map((id) => document.getElementById(id)?.textContent.trim()).filter(Boolean).join(' / ')).catch(() => '' /* best effort: the page may be gone; the failure is recorded below either way */);
    problems.push(`${label} (run): the game never came up, ${e.message.split('\n')[0]}${said ? ' (the page says: ' + said + ')' : ''}`);
  });
  const s = await page.evaluate(inPage);
  expect(`${label} (run)`, s.startDisplay === 'none', `#start has display ${s.startDisplay}, not none`);
  expect(`${label} (run)`, s.playing, 'body has no "playing" class');
  expect(`${label} (run)`, !s.fieldInStart && s.fieldInTerm, `the centre of the field shows ${s.fieldTop}${s.fieldInStart ? ', inside #start' : ''}`);
  expect(`${label} (run)`, s.gearDisplay !== 'none' && s.gearOnTop, `the gear is ${s.gearDisplay === 'none' ? 'not displayed' : 'under ' + s.gearTop}`);
  return s;
}
async function backToStartPage(page, label) {
  await page.click('#gear');
  await page.waitForSelector('#menu[open]', { timeout: MENU_OPEN_MS });
  await page.click('#menu-quit');
  await page.waitForFunction(() => !document.body.classList.contains('playing'), null, { timeout: RUN_UI_MS });
  await page.waitForTimeout(PAINT_SETTLE_MS);
  const s = await page.evaluate(inPage);
  expect(`${label} (start page)`, s.startDisplay !== 'none', 'the start page is not displayed after Back to start page');
  expect(`${label} (start page)`, !s.playing, 'body still has the "playing" class');
  expect(`${label} (start page)`, s.buttons.length >= 3, `only ${s.buttons.length} start buttons are visible`);
  for (const b of s.buttons) expect(`${label} (start page)`, b.on, `#${b.id} is under ${b.top} at its centre`);
  expect(`${label} (start page)`, !/handle=|feed=random/i.test(s.search), `the address still names a run: ${s.search}`);
  return s;
}
async function playRandom(page) {
  await page.waitForSelector('#start-random:not([disabled])', { timeout: SLOW });
  await page.click('#start-random');
}
async function playAsSomeone(page, press) {
  await page.waitForSelector('#start-guest:not([disabled])', { timeout: SLOW });
  await page.click('#start-guest'); // the first press opens the handle box
  await page.fill('#start-handle', HANDLE);
  // with the handle typed, a status line may show under the choices; the box losing focus must not move the button out from under a mouse press
  await page.waitForTimeout(HANDLE_SETTLE_MS);
  if (press === 'mouse') await page.click('#start-guest'); else await page.press('#start-handle', 'Enter');
}

const ways = [
  ['an address with ?handle=', async (page) => { await page.goto(`${origin}/?handle=${HANDLE}`, { waitUntil: 'domcontentloaded', timeout: SLOW }); }],
  ['an address with ?feed=random', async (page) => { await page.goto(`${origin}/?feed=random`, { waitUntil: 'domcontentloaded', timeout: SLOW }); }],
  ['a click on Play random', async (page) => { await page.goto(`${origin}/`, { waitUntil: 'domcontentloaded', timeout: SLOW }); await playRandom(page); }],
  ['Play as someone with a typed handle and Enter', async (page) => { await page.goto(`${origin}/`, { waitUntil: 'domcontentloaded', timeout: SLOW }); await playAsSomeone(page, 'enter'); }],
  ['Play as someone with a typed handle and a mouse click on the button', async (page) => { await page.goto(`${origin}/`, { waitUntil: 'domcontentloaded', timeout: SLOW }); await playAsSomeone(page, 'mouse'); }],
  ['Back to start page, then a second Play random', async (page, label) => {
    await page.goto(`${origin}/`, { waitUntil: 'domcontentloaded', timeout: SLOW });
    await playRandom(page);
    await showsTheGame(page, label + ', first run');
    await backToStartPage(page, label);
    await playRandom(page);
  }],
];

const { firefox } = await playwright();
const browser = await firefox.launch({
  headless: true,
  timeout: 300000, // a first launch on a busy box can take minutes
  firefoxUserPrefs: { 'media.volume_scale': '0.0', 'media.default_volume': '0.0' },
  ...(process.env.FIREFOX_EXE ? { executablePath: process.env.FIREFOX_EXE } : {}),
});
try {
  for (const [label, enter] of ways) {
    if (process.env.ONLY && !label.includes(process.env.ONLY)) continue;
    const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    const before = problems.length;
    try {
      context.setDefaultTimeout(SLOW); // the box may be busy with other work; a wedged page still fails, just later
      await cutOffTheNetwork(context);
      const page = await context.newPage();
      const log = [];
      page.on('pageerror', (e) => log.push('pageerror: ' + e.message));
      await enter(page, label);
      await showsTheGame(page, label);
      // a covered gear cannot be pressed: the way in is already failed, so say so rather than wait on the click
      if (problems.length === before) await backToStartPage(page, label);
      if (problems.length > before && log.length) problems.push(`${label}: ${log.join('; ')}`);
    } catch (e) {
      problems.push(`${label}: ${e.message.split('\n')[0]}`);
    } finally {
      await context.close();
    }
    process.stdout.write((problems.length === before ? 'ok    ' : 'FAIL  ') + label + '\n');
  }
} finally {
  await browser.close();
}
process.stdout.write('kept off the network: ' + ([...stubbed].map(([h, n]) => `${h} x${n}`).join(', ') || 'nothing was asked for') + '\n');
for (const p of problems) process.stdout.write('  - ' + p + '\n');
if (problems.length) { process.stdout.write(`${problems.length} check(s) failed\n`); process.exit(1); }
process.stdout.write('every way in shows the game\n');
