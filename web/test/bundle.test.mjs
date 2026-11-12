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

/** Bundles a fake wwwroot whose main.js holds a closing script tag; `extra` are more _framework files. Returns the page. */
function bundle(extra = {}) {
  const root = mkdtempSync(join(tmpdir(), 'fr-bundle-'));
  mkdirSync(join(root, 'xterm'));
  mkdirSync(join(root, '_framework'));
  writeFileSync(join(root, 'index.html'), PAGE);
  for (const f of ['xterm.min.css', 'xterm.min.js', 'addon-fit.min.js', 'addon-webgl.js']) writeFileSync(join(root, 'xterm', f), '/* x */');
  writeFileSync(join(root, 'main.js'), 'const s = "</script><script>alert(1)</script>";');
  for (const f of ['page.js', 'music.js', 'sfx.js']) writeFileSync(join(root, f), '// x');
  writeFileSync(join(root, '_framework', 'dotnet.js'), '// runtime');
  for (const [name, text] of Object.entries(extra)) writeFileSync(join(root, '_framework', name), text);
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
  let html;
  try { html = bundle({ 'a<b.js': '// x' }); } catch { return t.skip('this file system cannot name a file with <'); }
  const block = filesBlock(html);
  assert.ok(block.includes('a\\u003cb.js'), 'the < in the name is written as \\u003c');
  assert.ok(!block.includes('<'), 'no raw < inside the data block');
  assert.ok('a<b.js' in JSON.parse(block));
});
