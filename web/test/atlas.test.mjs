import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { watchAtlas, ATLAS_PAGE_LIMIT } from '../../src/FeedRaider.Web/wwwroot/page.js';

// a stand-in for the WebGL addon's three atlas events
function fakeAddon() {
  const on = { change: [], add: [], remove: [] };
  return {
    onChangeTextureAtlas: (f) => on.change.push(f),
    onAddTextureAtlasCanvas: (f) => on.add.push(f),
    onRemoveTextureAtlasCanvas: (f) => on.remove.push(f),
    change: () => on.change.forEach((f) => f({})),
    add: () => on.add.forEach((f) => f({})),
    remove: () => on.remove.forEach((f) => f({})),
  };
}

test('watchAtlas renews the renderer once the atlas nears the page count where xterm starts merging pages', () => {
  const gl = fakeAddon();
  let renewals = 0;
  watchAtlas(gl, () => renewals++);
  // the atlas starts with one page; WebGL2 has at least 16 texture units, the fewest pages at which the addon merges
  assert.ok(ATLAS_PAGE_LIMIT < 16);
  for (let pages = 1; pages < ATLAS_PAGE_LIMIT - 1; pages++) gl.add();
  assert.equal(renewals, 0, `${ATLAS_PAGE_LIMIT - 1} pages are fine`);
  gl.add();
  assert.equal(renewals, 1, `the ${ATLAS_PAGE_LIMIT}th page asks for a fresh renderer`);
  gl.add(); gl.add();
  assert.equal(renewals, 1, 'once per addon');
});

test('watchAtlas renews at the first page merge, whatever the count, and a new atlas restarts the count', () => {
  const gl = fakeAddon();
  let renewals = 0;
  watchAtlas(gl, () => renewals++);
  gl.add(); gl.add();
  gl.remove();
  assert.equal(renewals, 1, 'a merge is where the stale textures come from');

  const gl2 = fakeAddon();
  let renewals2 = 0;
  watchAtlas(gl2, () => renewals2++);
  for (let i = 0; i < ATLAS_PAGE_LIMIT - 2; i++) gl2.add();
  gl2.change(); // a font or pixel-ratio change hands the renderer a new one-page atlas
  for (let i = 0; i < ATLAS_PAGE_LIMIT - 2; i++) gl2.add();
  assert.equal(renewals2, 0);
});

test('main.js loads the WebGL addon through watchAtlas and renews it by disposing the old addon first', () => {
  const main = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
  assert.match(main, /page\.watchAtlas\(gl,/);
  // the atlas is cached per terminal: a new addon loaded before the old one is disposed would get the same full atlas
  assert.match(main, /gl\.dispose\(\);[^\n]*loadWebgl\(\)/);
});
