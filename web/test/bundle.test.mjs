// node --test web/test: bundle-single-file.mjs on a tiny fake publish output.
import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { gunzipSync } from 'node:zlib';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('../bundle-single-file.mjs', import.meta.url));
const PAGE = `<!doctype html><html><head>
<link rel="stylesheet" href="./xterm/xterm.min.css">
<script src="./xterm/xterm.min.js"></script>
<script src="./xterm/addon-fit.min.js"></script>
</head><body><script type="module" src="./main.js"></script></body></html>`;

// The _framework files a publish writes: the runtime and, beside it, the runtime's and one package's licence texts.
const FRAMEWORK = {
  'dotnet.js': '// runtime',
  'LICENSE.txt': 'runtime licence text',
  'THIRD-PARTY-NOTICES.txt': 'runtime notices text',
  'Some.Package.LICENSE.txt': 'package licence text',
};

/**
 * Bundles a fake wwwroot whose main.js holds a closing script tag. `extra` are more _framework files, or replace those in
 * FRAMEWORK; a null text leaves that file out. Returns the page.
 */
function bundle(extra = {}) {
  const root = mkdtempSync(join(tmpdir(), 'fr-bundle-'));
  mkdirSync(join(root, 'xterm'));
  mkdirSync(join(root, '_framework'));
  writeFileSync(join(root, 'index.html'), PAGE);
  for (const f of ['xterm.min.css', 'xterm.min.js', 'addon-fit.min.js', 'addon-webgl.js', 'LICENSE']) writeFileSync(join(root, 'xterm', f), '/* x */');
  writeFileSync(join(root, 'main.js'), 'const s = "</script><script>alert(1)</script>";');
  for (const f of ['page.js', 'music.js', 'sfx.js']) writeFileSync(join(root, f), '// x');
  for (const [name, text] of Object.entries({ ...FRAMEWORK, ...extra })) {
    if (text !== null) writeFileSync(join(root, '_framework', name), text);
  }
  const out = join(root, 'out.html');
  execFileSync('node', [script, root, out], { stdio: 'pipe' });
  return readFileSync(out, 'utf8');
}

/** What an HTML parser sees: the JSON data block ends at the first closing script tag. */
function filesBlock(html) {
  const open = '<script type="application/json" id="fr-files">';
  const start = html.indexOf(open) + open.length;
  return html.slice(start, html.indexOf('</script>', start));
}

test('a closing script tag in an embedded file cannot end the data block early', () => {
  const block = filesBlock(bundle());
  const files = JSON.parse(block); // a truncated block would not parse
  assert.equal(gunzipSync(Buffer.from(files['main.js'].data, 'base64')).toString(), 'const s = "</script><script>alert(1)</script>";');
  assert.ok(!block.includes('<'), 'no raw < inside the data block');
});

test('a < in an embedded file name is escaped, so the page still parses', (t) => {
  const probe = mkdtempSync(join(tmpdir(), 'fr-name-'));
  try { writeFileSync(join(probe, 'a<b.js'), ''); } catch { return t.skip('this file system cannot name a file with <'); }
  const html = bundle({ 'a<b.js': '// x' });
  const block = filesBlock(html);
  assert.ok(block.includes('a\\u003cb.js'), 'the < in the name is written as \\u003c');
  assert.ok(!block.includes('<'), 'no raw < inside the data block');
  assert.ok('a<b.js' in JSON.parse(block));
});

test('the page holds the .NET runtime and package licence texts in its licence comment', () => {
  const html = bundle();
  const comment = html.slice(html.indexOf('<!--'), html.indexOf('-->'));
  for (const [name, text] of [
    ['_framework/LICENSE.txt', 'runtime licence text'],
    ['_framework/THIRD-PARTY-NOTICES.txt', 'runtime notices text'],
    ['_framework/Some.Package.LICENSE.txt', 'package licence text'],
  ]) {
    assert.ok(comment.includes(`${name}:\n\n${text}`), `the licence comment holds ${name}`);
  }
  const files = JSON.parse(filesBlock(html));
  assert.ok(!('LICENSE.txt' in files), 'a licence text is not embedded as a runtime file');
});

test('a publish output without the runtime licence stops the bundle, naming the file', () => {
  assert.throws(() => bundle({ 'LICENSE.txt': null }), /_framework\/LICENSE\.txt is missing/);
});
