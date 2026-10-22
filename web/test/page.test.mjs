import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { metadataUrl } from '../../src/FeedRaider.Web/wwwroot/page.js';

const wwwroot = new URL('../../src/FeedRaider.Web/wwwroot/', import.meta.url);
const SITE = 'https://feed-raider.famelos.com';
const PAGES = [`${SITE}/`, `${SITE}/index.html`, `${SITE}/?handle=bsky.app`, `${SITE}/?debug=feed#state=x`];

test('client_id and redirect_uri match what make-client-metadata writes for the Pages URL', () => {
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
  for (const u of own.filter((u) => !u.startsWith('https://cdn.jsdelivr.net/') && !u.startsWith('data:image/svg+xml,'))) assert.doesNotMatch(u, /^(\/|[a-z]+:)/, u);
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
