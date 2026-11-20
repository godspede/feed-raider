// node --test web/test: the licence notices written beside the atproto.js bundle (third-party-notices.mjs and build.mjs).
import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';
import { bundledPackageDirs, noticesFor, packageDirOf, readPackage } from '../third-party-notices.mjs';

const OUT = 'out/app.js';
const wwwroot = new URL('../../src/FeedRaider.Web/wwwroot/', import.meta.url);
const NOTICES_NAME = 'atproto.js.LICENSES.txt';

/** A made-up metafile: the output holds the project's own file and files from three packages, one of them nested. */
function fakeMetafile() {
  const inputs = {
    'src/app.js': {},
    'node_modules/zeta/index.js': {},
    'node_modules/zeta/lib/more.js': {},
    'node_modules/@scope/alpha/dist/a.js': {},
    'node_modules/@scope/alpha/node_modules/zeta/index.js': {},
  };
  return { inputs, outputs: { [OUT]: { inputs } } };
}

/** A package reader over made-up packages; `bare` names the directory whose package ships no licence file. */
function fakeReader(bare = null) {
  const versions = { 'node_modules/zeta': '2.0.0', 'node_modules/@scope/alpha': '1.0.0', 'node_modules/@scope/alpha/node_modules/zeta': '1.0.0' };
  return (dir) => ({
    name: dir.split('node_modules/').pop(),
    version: versions[dir],
    license: 'MIT',
    texts: dir === bare ? [] : [{ file: 'LICENSE', text: `licence of ${dir}` }],
  });
}

// a section's heading: a rule, `<name> <version> (<licence>)`, a rule
const SECTION_HEAD = /^=+\n(?:\S+) \S+ \(.*\)\n=+$/m;

/** The package names, in order, that a notices text has a section for. */
function sectionNames(text) {
  return [...text.matchAll(/^=+\n(\S+) \S+ \(.*\)\n=+$/gm)].map((m) => m[1]);
}

test('packageDirOf: the innermost package folder of a dependency file, null for the project own files', () => {
  assert.equal(packageDirOf('node_modules/@scope/alpha/dist/a.js'), 'node_modules/@scope/alpha');
  assert.equal(packageDirOf('node_modules/@scope/alpha/node_modules/zeta/index.js'), 'node_modules/@scope/alpha/node_modules/zeta');
  assert.equal(packageDirOf('src/app.js'), null);
});

test('noticesFor: one section per bundled package, sorted by name then version, each with its licence text', () => {
  const text = noticesFor(fakeMetafile(), OUT, fakeReader());
  assert.deepEqual(sectionNames(text), ['@scope/alpha', 'zeta', 'zeta']);
  assert.match(text, /^zeta 1\.0\.0 \(MIT\)\n[^]*^zeta 2\.0\.0 \(MIT\)$/m);
  assert.match(text, /^licence of node_modules\/@scope\/alpha\/node_modules\/zeta$/m);
  assert.equal(noticesFor(fakeMetafile(), OUT, fakeReader()), text, 'two runs give the same bytes');
});

test('noticesFor: a bundled package with no licence file fails by name', () => {
  assert.throws(() => noticesFor(fakeMetafile(), OUT, fakeReader('node_modules/@scope/alpha')), /no licence file for bundled package\(s\): @scope\/alpha 1\.0\.0/);
});

test('noticesFor: an output the metafile does not hold fails by name', () => {
  assert.throws(() => noticesFor(fakeMetafile(), 'out/other.js', fakeReader()), /no output out\/other\.js/);
});

/**
 * Runs bundle-single-file.mjs on a fake publish output whose xterm/LICENSE, runtime licence texts and `extra` files hold
 * their own names.
 */
function singleFilePage(extra) {
  const root = mkdtempSync(join(tmpdir(), 'fr-licences-'));
  mkdirSync(join(root, 'xterm'));
  mkdirSync(join(root, '_framework'));
  writeFileSync(join(root, 'index.html'), '<!doctype html><html><head><link rel="stylesheet" href="./xterm/xterm.min.css">'
    + '<script src="./xterm/xterm.min.js"></script><script src="./xterm/addon-fit.min.js"></script></head>'
    + '<body><script type="module" src="./main.js"></script></body></html>');
  for (const f of ['xterm.min.css', 'xterm.min.js', 'addon-fit.min.js', 'addon-webgl.js']) writeFileSync(join(root, 'xterm', f), '/* x */');
  writeFileSync(join(root, 'xterm', 'LICENSE'), 'xterm licence text');
  for (const f of ['main.js', 'page.js', 'music.js', 'sfx.js', join('_framework', 'dotnet.js')]) writeFileSync(join(root, f), '// x');
  for (const f of ['LICENSE.txt', 'THIRD-PARTY-NOTICES.txt']) writeFileSync(join(root, '_framework', f), `runtime ${f}`);
  for (const [name, text] of Object.entries(extra)) writeFileSync(join(root, name), text);
  const out = join(root, 'out.html');
  execFileSync('node', [fileURLToPath(new URL('../bundle-single-file.mjs', import.meta.url)), root, out], { stdio: 'pipe' });
  return readFileSync(out, 'utf8');
}

test('the single-file page carries xterm licence and, with atproto.js embedded, its notices in a comment after the doctype', () => {
  const comment = /^<!doctype html>\n<!--\n([^]*?)\n-->/;
  const bare = singleFilePage({}).match(comment)[1];
  assert.match(bare, /xterm\/LICENSE:\n\nxterm licence text/);
  assert.doesNotMatch(bare, new RegExp(NOTICES_NAME));
  const signedIn = singleFilePage({ 'atproto.js': '// bundle', [NOTICES_NAME]: 'atproto notices text' }).match(comment)[1];
  assert.match(signedIn, new RegExp(`${NOTICES_NAME}:\n\natproto notices text`));
  assert.throws(() => singleFilePage({ 'atproto.js': '// bundle' }), new RegExp(`${NOTICES_NAME} is missing`));
});

test('the built notices file has a section with licence text for every package in the atproto.js bundle', async () => {
  const bundle = new URL('atproto.js', wwwroot);
  const notices = new URL(NOTICES_NAME, wwwroot);
  assert.ok(existsSync(bundle) && existsSync(notices), 'run `npm run build` in web/ first');
  const webDir = fileURLToPath(new URL('..', import.meta.url));
  const { metafile } = await build({
    absWorkingDir: webDir, entryPoints: ['src/atproto.js'], outfile: OUT, bundle: true, format: 'esm', platform: 'browser',
    target: 'es2022', minify: true, write: false, metafile: true, logLevel: 'silent',
  });
  const names = bundledPackageDirs(metafile, OUT).map((dir) => readPackage(`${webDir}/${dir}`).name).sort();
  assert.ok(names.length > 0, 'the bundle holds npm packages');
  const built = readFileSync(notices, 'utf8');
  assert.deepEqual([...new Set(sectionNames(built))].sort(), [...new Set(names)]);
  const bodies = built.split(SECTION_HEAD).slice(1);
  assert.equal(bodies.length, sectionNames(built).length);
  for (const body of bodies) assert.match(body, /^--- \S+ ---\n\S/m, 'every section holds a licence text');
  assert.match(readFileSync(bundle, 'utf8').split('\n')[0], new RegExp(`^/\\*.*${NOTICES_NAME.replaceAll('.', '\\.')}.*\\*/$`));
});
