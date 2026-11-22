import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, readdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { avatarCid, cellAt, createGestures, didDocUrl, FIELD_MAX_COLS, grants, leftPanel, metadataUrl, renderLog, SIDE_GAP_COLS, SIDE_MIN_COLS } from '../../src/FeedRaider.Web/wwwroot/page.js';

const wwwroot = new URL('../../src/FeedRaider.Web/wwwroot/', import.meta.url);
const metadataScript = fileURLToPath(new URL('../make-client-metadata.mjs', import.meta.url));
const SITE = 'https://feed-raider.famelos.com';
const PAGES = [`${SITE}/`, `${SITE}/index.html`, `${SITE}/?handle=bsky.app`, `${SITE}/?debug=feed#state=x`];

test('client_id and redirect_uri match what make-client-metadata writes for the site URL', () => {
  const out = join(mkdtempSync(join(tmpdir(), 'fr-meta-')), 'client-metadata.json');
  execFileSync('node', [metadataScript, SITE, out]);
  const meta = JSON.parse(readFileSync(out, 'utf8'));
  assert.equal(meta.client_id, `${SITE}/client-metadata.json`);
  assert.deepEqual(meta.redirect_uris, [`${SITE}/`]);
  assert.equal(meta.client_uri, `${SITE}/`);
  for (const href of PAGES) {
    // the page fetches the document at exactly the client_id, and the redirect lands back on this page's directory
    assert.equal(metadataUrl(href).href, meta.client_id, href);
    assert.equal(new URL('./', href).href, meta.redirect_uris[0], href);
  }
  const scope = JSON.parse(readFileSync(new URL('../scope.json', import.meta.url), 'utf8'));
  assert.equal(meta.scope, scope.granular);
});

test('a trailing slash on the site argument makes no difference', () => {
  const dir = mkdtempSync(join(tmpdir(), 'fr-meta-'));
  const gen = (site, name) => {
    execFileSync('node', [metadataScript, site, join(dir, name)]);
    return readFileSync(join(dir, name), 'utf8');
  };
  assert.equal(gen(SITE, 'a.json'), gen(`${SITE}/`, 'b.json'));
});

test('multi-file build lists its mp3s in music/tracks.json beside the page under the subpath', () => {
  const list = JSON.parse(readFileSync(new URL('music/tracks.json', wwwroot), 'utf8'));
  assert.ok(list.length >= 4);
  for (const { file } of list) {
    assert.ok(readdirSync(new URL('music/', wwwroot)).includes(file), file);
    for (const base of PAGES) assert.equal(new URL(`music/${file}`, base).href, `${SITE}/music/${file}`);
  }
});

test('nothing in the site code fetches an absolute localhost URL, and index.html loads its own files relatively', () => {
  for (const f of ['main.js', 'page.js', 'music.js', 'index.html']) {
    const src = readFileSync(new URL(f, wwwroot), 'utf8');
    assert.doesNotMatch(src, /https?:\/\/(localhost|127\.|\[::1\])/, f);
  }
  const html = readFileSync(new URL('index.html', wwwroot), 'utf8');
  const own = [...html.matchAll(/<(?:script|link)[^>]*(?:src|href)="([^"]+)"/g)].map((m) => m[1]);
  for (const u of own.filter((u) => !u.startsWith('data:image/svg+xml,'))) assert.doesNotMatch(u, /^(\/|[a-z]+:)/, u);
});

test('xterm is served with the page: the vendored files are the bytes the page used to pin from the CDN, with their licence', () => {
  const pinned = {
    'xterm.min.js': 'J4qzUjBl1FxyLsl/kQPQIOeINsmp17OHYXDOMpMxlKX53ZfYsL+aWHpgArvOuof9',
    'xterm.min.css': 'tStR1zLfWgsiXCF3IgfB3lBa8KmBe/lG287CL9WCeKgQYcp1bjb4/+mwN6oti4Co',
    'addon-fit.min.js': 'XGqKrV8Jrukp1NITJbOEHwg01tNkuXr6uB6YEj69ebpYU3v7FvoGgEg23C1Gcehk',
    'addon-webgl.js': 'AkzdQDeATxpW8V7I2EPcwg17qAoLQMjEFHb//1QaAAIyIlbDAHGKno5+huH7RKxq',
  };
  for (const [f, sha] of Object.entries(pinned)) assert.equal(createHash('sha384').update(readFileSync(new URL('xterm/' + f, wwwroot))).digest('base64'), sha, f);
  const licence = readFileSync(new URL('xterm/LICENSE', wwwroot), 'utf8');
  for (const pkg of ['@xterm/xterm 5.5.0', '@xterm/addon-fit 0.10.0', '@xterm/addon-webgl 0.18.0']) assert.match(licence, new RegExp(pkg.replaceAll('.', '\\.')));
  assert.match(licence, /Permission is hereby granted, free of charge/);
  for (const f of ['main.js', 'index.html']) assert.doesNotMatch(readFileSync(new URL(f, wwwroot), 'utf8'), /cdn\.jsdelivr\.net/, f);
  const html = readFileSync(new URL('index.html', wwwroot), 'utf8');
  for (const f of ['xterm.min.css', 'xterm.min.js', 'addon-fit.min.js']) assert.ok(html.includes(`"./xterm/${f}"`), f);
  assert.match(readFileSync(new URL('main.js', wwwroot), 'utf8'), /'\.\/xterm\/addon-webgl\.js'/);
});

test('a page opened on localhost moves to the loopback IP before any script or stylesheet loads', () => {
  const html = readFileSync(new URL('index.html', wwwroot), 'utf8');
  const redirect = html.indexOf("location.hostname === 'localhost'");
  assert.ok(redirect > 0);
  const firstLoad = html.search(/<(link rel="stylesheet"|script[^>]* src=)/);
  assert.ok(redirect < firstLoad, 'the redirect runs before the first load');
});

test('an avatar is read from the author\'s PDS: the DID document is found, and the CDN URL gives the blob cid', () => {
  assert.equal(didDocUrl('did:plc:abc123'), 'https://plc.directory/did:plc:abc123');
  assert.equal(didDocUrl('did:web:pds.example.com'), 'https://pds.example.com/.well-known/did.json');
  assert.equal(didDocUrl('did:web:PDS.Example.com'), 'https://pds.example.com/.well-known/did.json');
  for (const bad of ['did:web:host.example.com%3A8080', 'did:web:host.example.com:user:alice', 'did:web:', 'did:key:z6Mk', null, 7,
    'did:web:localhost', 'did:web:printer.local', 'did:web:127.0.0.1', 'did:web:nodots', 'did:web:router.home.arpa', 'did:web:box.internal',
    'did:web:a.example.com%2Fpath', 'did:web:[::1]']) assert.equal(didDocUrl(bad), null, String(bad));
  assert.equal(avatarCid('https://cdn.bsky.app/img/avatar/plain/did:plc:abc123/bafkreiabcdef@jpeg'), 'bafkreiabcdef');
  assert.equal(avatarCid('https://cdn.bsky.app/img/avatar_thumbnail/plain/did:web:pds.example.com/bafkreiabcdef'), 'bafkreiabcdef');
  for (const bad of ['https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:abc/bafk@jpeg', 'https://pds.example.com/', undefined]) assert.equal(avatarCid(bad), null, String(bad));
  // the CDN sends no CORS headers, so the page never reads an avatar from it
  assert.doesNotMatch(readFileSync(new URL('main.js', wwwroot), 'utf8'), /fetch\(url/);
});

test('the preview image is declared with absolute URLs on the site and og.png is 1200x630', () => {
  const html = readFileSync(new URL('index.html', wwwroot), 'utf8');
  const tag = (attr, key) => html.match(new RegExp(`<meta ${attr}="${key}" content="([^"]*)"`))?.[1];
  const image = `${SITE}/og.png`;
  assert.equal(tag('property', 'og:image'), image);
  assert.equal(tag('name', 'twitter:image'), image);
  assert.equal(tag('name', 'twitter:card'), 'summary_large_image');
  assert.equal(tag('property', 'og:image:width'), '1200');
  assert.equal(tag('property', 'og:image:height'), '630');
  assert.ok(tag('property', 'og:image:alt')?.length > 20);
  assert.equal(tag('name', 'twitter:image:alt'), tag('property', 'og:image:alt'));
  // the alt says what the picture says: it quotes every pill web/make-og-image.mjs draws
  const pills = [...readFileSync(new URL('../make-og-image.mjs', import.meta.url), 'utf8').matchAll(/<li>([^<]+)<\/li>/g)].map((m) => m[1]);
  assert.ok(pills.length > 0);
  for (const pill of pills) assert.ok(tag('property', 'og:image:alt').includes(pill), pill);
  const png = readFileSync(new URL('og.png', wwwroot));
  assert.equal(png.subarray(1, 4).toString(), 'PNG');
  assert.equal(png.toString('latin1', 12, 16), 'IHDR');
  assert.equal(png.readUInt32BE(16), 1200);
  assert.equal(png.readUInt32BE(20), 630);
});

const NEW_SCOPE = JSON.parse(readFileSync(new URL('../scope.json', import.meta.url), 'utf8')).granular;
const ALL = { like: true, unlike: true, repost: true, unrepost: true };
const NONE = { like: false, unlike: false, repost: false, unrepost: false };

test('grants: the old scope string can like and nothing more', () => {
  const old = 'atproto repo:app.bsky.feed.like?action=create rpc:app.bsky.feed.getFeed?aud=did:web:api.bsky.app%23bsky_appview';
  assert.deepEqual(grants(old), { ...NONE, like: true });
});

test('grants: the new scope string, and transition:generic, can do all four', () => {
  assert.deepEqual(grants(NEW_SCOPE), ALL);
  assert.deepEqual(grants('atproto transition:generic'), ALL);
});

test('grants: nothing granted, or nothing known, is none', () => {
  for (const s of ['', '   ', null, undefined, 'atproto']) assert.deepEqual(grants(s), NONE);
});

test('grants: a collection scope without actions allows every action, and other collections count for nothing', () => {
  assert.deepEqual(grants('atproto repo:app.bsky.feed.repost'), { ...NONE, repost: true, unrepost: true });
  assert.deepEqual(grants('atproto repo:app.bsky.feed.post?action=create&action=delete'), NONE);
  assert.deepEqual(grants('atproto repo:app.bsky.feed.like?action=delete'), { ...NONE, unlike: true });
});

// --- the feed log on the field's left: cells, log clicks and the pause menu's list ---

const metrics = { left: 10, top: 20, cellW: 8, cellH: 16, cols: 100, rows: 40 };

test('cellAt: a pointer position is a column and row of the terminal, and nothing outside it', () => {
  assert.deepEqual(cellAt(10, 20, metrics), { col: 0, row: 0 });
  assert.deepEqual(cellAt(10 + 8 * 5 + 7, 20 + 16 * 3 + 15, metrics), { col: 5, row: 3 });
  assert.deepEqual(cellAt(10 + 8 * 99 + 7, 20 + 16 * 39 + 15, metrics), { col: 99, row: 39 });
  assert.equal(cellAt(9, 30, metrics), null, 'left of the terminal');
  assert.equal(cellAt(30, 19, metrics), null, 'above it');
  assert.equal(cellAt(10 + 8 * 100, 30, metrics), null, 'right of it');
  assert.equal(cellAt(30, 20 + 16 * 40, metrics), null, 'below it');
  assert.equal(cellAt(NaN, 30, metrics), null);
  assert.equal(cellAt(30, 30, { ...metrics, cellW: 0 }), null, 'a terminal not laid out yet');
});

test('leftPanel: where the feed log stands, from the same sums as the game, and nothing when the window is too narrow', () => {
  // 150 columns: the field is 64 wide, 43 columns either side; the panel is 40 wide (one spare column), 2 off the field
  assert.deepEqual(leftPanel(150), { col: 43 - 2 - 40, cols: 40 });
  assert.deepEqual(leftPanel(200), { col: 68 - 2 - 44, cols: 44 });
  assert.equal(leftPanel(100), null);
  assert.equal(leftPanel(48), null);
});

test('leftPanel: a window just wide enough for the narrowest feed log shows it, and one column less shows nothing', () => {
  const narrowest = FIELD_MAX_COLS + 2 * (SIDE_MIN_COLS + SIDE_GAP_COLS + 1);
  assert.deepEqual(leftPanel(narrowest), { col: 1, cols: SIDE_MIN_COLS });
  assert.equal(leftPanel(narrowest - 1), null);
});

function gestureRig(inPanel) {
  const calls = [];
  let t = 0;
  const host = {
    click: (c, touch) => calls.push(['click', c, touch]), bomb: () => calls.push(['bomb']),
    logClick: (c) => calls.push(['logClick', c]), inPanel,
  };
  return { calls, g: createGestures(host, () => t), at: (ms) => { t = ms; } };
}

test('gestures: a click on the left panel is a log click and no field click; a click elsewhere is a field click that is not a touch', () => {
  const { calls, g } = gestureRig((c) => c[0] < 30);
  g.mouseDown(0, [12, 7]);
  assert.deepEqual(calls, [['logClick', [12, 7]]]);
  calls.length = 0;
  g.mouseDown(0, [60, 7]);
  assert.deepEqual(calls, [['click', [60, 7], false]]);
  calls.length = 0;
  g.mouseDown(2, [12, 7]);
  assert.deepEqual(calls, [['bomb']], 'the right button still bombs');
});

test('gestures: a quick touch on the left panel is a log click, and neither a field click nor a steering drag', () => {
  const { calls, g, at } = gestureRig((c) => c[0] < 30);
  at(1000);
  g.touchStart(1, 50, 50, [12, 7]);
  assert.equal(g.touchMove(1, 3, 1), false, 'the finger moved a little: no steering');
  at(1100);
  g.touchEnd(0);
  assert.deepEqual(calls, [['logClick', [12, 7]]]);
});

test('gestures: a long press or a drag that began on the left panel is not a log click and still does not steer', () => {
  const { calls, g, at } = gestureRig((c) => c[0] < 30);
  at(0);
  g.touchStart(1, 50, 50, [12, 7]);
  assert.equal(g.touchMove(1, 30, 0), false);
  at(100);
  g.touchEnd(0);
  assert.equal(calls.some((c) => c[0] === 'logClick'), false);
  calls.length = 0;
  at(0);
  g.touchStart(1, 50, 50, [12, 7]);
  at(900);
  g.touchEnd(0);
  assert.equal(calls.some((c) => c[0] === 'logClick'), false);
});

test('gestures: a touch on the field still steers', () => {
  const { calls, g } = gestureRig((c) => c[0] < 30);
  g.touchStart(1, 50, 50, [60, 7]);
  assert.equal(g.touchMove(1, 3, 1), true);
  assert.deepEqual(calls, []);
});

test('gestures: a 200 ms touch on the field that moved 5 px is a click, sent as a touch', () => {
  const { calls, g, at } = gestureRig(() => false);
  at(1000);
  g.touchStart(1, 50, 50, [60, 7]);
  g.touchMove(1, 3, 2);
  at(1200);
  g.touchEnd(0);
  assert.deepEqual(calls, [['click', [60, 7], true]]);
});

test('gestures: a 200 ms touch on the field that moved 20 px steers and never clicks', () => {
  const { calls, g, at } = gestureRig(() => false);
  at(1000);
  g.touchStart(1, 50, 50, [60, 7]);
  assert.equal(g.touchMove(1, 12, 8), true);
  at(1200);
  g.touchEnd(0);
  assert.deepEqual(calls, []);
});

test('gestures: a still touch held 400 ms does nothing, so a resting thumb never likes', () => {
  const { calls, g, at } = gestureRig(() => false);
  at(1000);
  g.touchStart(1, 50, 50, [60, 7]);
  at(1400);
  g.touchEnd(0);
  assert.deepEqual(calls, []);
});

test('gestures: a quick two-finger touch on the field bombs and does not click', () => {
  const { calls, g, at } = gestureRig(() => false);
  at(1000);
  g.touchStart(1, 50, 50, [60, 7]);
  g.touchStart(2, 80, 50, [70, 7]);
  at(1150);
  g.touchEnd(1);
  g.touchEnd(0);
  assert.deepEqual(calls, [['bomb']]);
});

// A stand-in for the document: just what renderLog uses.
function fakeDoc() {
  const make = (tag) => {
    const el = {
      tag, children: [], attrs: {}, listeners: {}, className: '', textContent: '', hidden: false, disabled: false, title: '',
      append(...kids) { el.children.push(...kids); },
      replaceChildren(...kids) { el.children = kids; },
      setAttribute(k, v) { el.attrs[k] = String(v); },
      getAttribute(k) { return el.attrs[k] ?? null; },
      addEventListener(type, fn) { (el.listeners[type] ||= []).push(fn); },
      click() { for (const fn of el.listeners.click || []) fn({ preventDefault() {} }); },
    };
    return el;
  };
  return { createElement: make };
}
const find = (el, pred, out = []) => { if (pred(el)) out.push(el); for (const c of el.children || []) find(c, pred, out); return out; };
const buttons = (li) => find(li, (e) => e.tag === 'button');
const entry = (n, more = {}) => ({
  uri: `at://x/post/${n}`, name: `Name ${n}`, handle: `h${n}.test`, text: `post number ${n}`,
  like: 'off', repost: 'off', likeCount: String(n), repostCount: '0', likeLocked: false, repostLocked: false, ...more,
});
const logEls = (doc) => ({ list: doc.createElement('ol'), empty: doc.createElement('p') });

test('renderLog: one list item per entry, in the order given, with name, handle, text and two buttons', () => {
  const doc = fakeDoc();
  const els = logEls(doc);
  renderLog(els, [entry(2), entry(1)], { doc, press() {} });
  assert.equal(els.list.children.length, 2);
  const [first, second] = els.list.children;
  assert.equal(first.tag, 'li');
  const text = (li) => find(li, () => true).map((e) => e.textContent).join('|');
  assert.match(text(first), /Name 2.*@h2\.test.*post number 2/);
  assert.match(text(second), /Name 1.*@h1\.test.*post number 1/);
  assert.equal(buttons(first).length, 2);
  assert.equal(els.empty.hidden, true);
  assert.equal(els.list.hidden, false);
});

test('renderLog: an empty log reads "No posts have gone by yet."', () => {
  const doc = fakeDoc();
  const els = logEls(doc);
  renderLog(els, [entry(1)], { doc, press() {} });
  renderLog(els, [], { doc, press() {} });
  assert.equal(els.list.children.length, 0);
  assert.equal(els.empty.hidden, false);
  assert.equal(els.empty.textContent, 'No posts have gone by yet.');
});

test('renderLog: each button is pressed when its state is lit; pending is busy; a marked one is not pressed', () => {
  const doc = fakeDoc();
  const els = logEls(doc);
  renderLog(els, [entry(1, { like: 'lit', repost: 'off' }), entry(2, { like: 'pending', repost: 'marked' })], { doc, press() {} });
  const [a, b] = els.list.children.map(buttons);
  assert.equal(a[0].getAttribute('aria-pressed'), 'true');
  assert.equal(a[1].getAttribute('aria-pressed'), 'false');
  assert.equal(a[0].getAttribute('aria-busy'), null);
  assert.equal(b[0].getAttribute('aria-busy'), 'true');
  assert.equal(b[1].getAttribute('aria-pressed'), 'false');
  assert.match(a[0].textContent, /1/, 'the count shows');
});

test('renderLog: a repost the sign-in may not make is disabled and says why', () => {
  const doc = fakeDoc();
  const els = logEls(doc);
  renderLog(els, [entry(1, { repostLocked: true })], { doc, press() {} });
  const [like, repost] = buttons(els.list.children[0]);
  assert.equal(like.disabled, false);
  assert.equal(repost.disabled, true);
  assert.match(repost.title, /sign in again/i);
  assert.match(repost.getAttribute('aria-label'), /sign in again/i);
});

test('renderLog: pressing a button names the post and the action', () => {
  const doc = fakeDoc();
  const els = logEls(doc);
  const pressed = [];
  renderLog(els, [entry(1)], { doc, press: (uri, action) => pressed.push([uri, action]) });
  const [like, repost] = buttons(els.list.children[0]);
  like.click();
  repost.click();
  assert.deepEqual(pressed, [['at://x/post/1', 'like'], ['at://x/post/1', 'repost']]);
});
