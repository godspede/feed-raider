// node --test web/test: parseManifest on its own, with only the page globals music.js touches when it loads.
import test from 'node:test';
import assert from 'node:assert/strict';

globalThis.document = { addEventListener() {}, hidden: false };
const { parseManifest } = await import('../../src/FeedRaider.Web/wwwroot/music.js');

const BASE = 'https://x.test/m/tracks.json';

test('an entry with a role the player does not know is dropped', () => {
  const tracks = parseManifest([
    { file: 'a.mp3', role: 'gameplay' }, { file: 'b.mp3', role: 'jingle' }, { file: 'c.mp3' }, { file: 'd.mp3', role: 'boss' },
  ], BASE);
  assert.deepEqual(tracks.map((t) => t.id), ['a.mp3', 'd.mp3']);
});

test('an entry that is not an object is dropped', () => {
  const tracks = parseManifest([null, 'a.mp3', 3, { file: 'b.mp3', role: 'start' }], BASE);
  assert.deepEqual(tracks.map((t) => t.id), ['b.mp3']);
});

test('an entry with no file and no url is dropped, and so is one whose location is not text', () => {
  const tracks = parseManifest([
    { role: 'start' }, { role: 'start', file: '' }, { role: 'start', file: 7 }, { role: 'gameplay', file: 'ok.mp3' },
  ], BASE);
  assert.deepEqual(tracks.map((t) => t.id), ['ok.mp3']);
});

test('a url on an entry is kept as it is and wins over the file; a file resolves against the base', () => {
  const [a, b] = parseManifest([
    { role: 'start', url: 'https://cdn.test/a.mp3' }, { role: 'gameplay', file: 'b.mp3' },
  ], BASE);
  assert.equal(a.url, 'https://cdn.test/a.mp3');
  assert.equal(b.url, 'https://x.test/m/b.mp3');
});
