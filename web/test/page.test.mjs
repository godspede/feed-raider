import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, readdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { avatarCid, didDocUrl, metadataUrl } from '../../src/FeedRaider.Web/wwwroot/page.js';

const wwwroot = new URL('../../src/FeedRaider.Web/wwwroot/', import.meta.url);
const SITE = 'https://feed-raider.famelos.com';
const PAGES = [`${SITE}/`, `${SITE}/index.html`, `${SITE}/?handle=bsky.app`, `${SITE}/?debug=feed#state=x`];

test('client_id and redirect_uri match what make-client-metadata writes for the site URL', () => {
  const out = join(mkdtempSync(join(tmpdir(), 'fr-meta-')), 'client-metadata.json');
  execFileSync('node', [new URL('../make-client-metadata.mjs', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'), SITE, out]);
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
    execFileSync('node', [new URL('../make-client-metadata.mjs', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'), site, join(dir, name)]);
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
  for (const bad of ['did:web:host.example.com%3A8080', 'did:web:', 'did:key:z6Mk', null, 7]) assert.equal(didDocUrl(bad), null, String(bad));
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
  const png = readFileSync(new URL('og.png', wwwroot));
  assert.equal(png.subarray(1, 4).toString(), 'PNG');
  assert.equal(png.toString('latin1', 12, 16), 'IHDR');
  assert.equal(png.readUInt32BE(16), 1200);
  assert.equal(png.readUInt32BE(20), 630);
});
