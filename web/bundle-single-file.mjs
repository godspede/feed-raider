// Turns a publish output wwwroot into ONE self-contained html file (no sibling files).
//   node web/bundle-single-file.mjs <wwwroot> <out.html> [--music <manifest.json>]
// index.html keeps its CDN xterm tags; main.js, atproto.js, every _framework file the runtime loads (the boot config is
// embedded in dotnet.js on this .NET version) are gzipped + base64'd into the page. At load the page unpacks them with
// DecompressionStream, hands JS to the runtime as blob: URLs and wasm as in-memory Responses via main.js's
// globalThis.__frBundle seam (withResourceLoader). The runtime's SRI hashes cover the uncompressed bytes; a loader-supplied
// Response bypasses the fetch integrity check, so this script checks every hash from the boot config at build time instead.
import { readFileSync, writeFileSync, readdirSync, existsSync, statSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { join } from 'node:path';
import { readMusicManifest } from './music-manifest.mjs';

// The music is never embedded (music/ under the wwwroot is not read at all): --music names a JSON list of {role, url} (role: menu,
// gameplay, boss or gameover; url: the absolute mp3 URL), and that small list is inlined. Without it the bundle has no music.
const args = process.argv.slice(2);
let musicFile = null;
const mi = args.indexOf('--music');
if (mi >= 0) {
  musicFile = args[mi + 1];
  if (!musicFile || musicFile.startsWith('--')) { console.error('--music needs a manifest.json'); process.exit(2); }
  args.splice(mi, 2);
}
const [root, out] = args;
if (!root || !out || args.length > 2) {
  console.error('usage: node web/bundle-single-file.mjs <wwwroot> <out.html> [--music <manifest.json>]');
  process.exit(2);
}
let music = null;
if (musicFile) music = readMusicManifest(JSON.parse(readFileSync(musicFile, 'utf8')), musicFile);
else {
  console.error('no --music manifest: this bundle will have no music (sound effects still play)');
}
const fw = join(root, '_framework');
if (!existsSync(join(fw, 'dotnet.js'))) throw new Error(`no _framework/dotnet.js under ${root}; pass a publish output wwwroot`);

const dotnetSrc = readFileSync(join(fw, 'dotnet.js'), 'utf8');
const hashes = {};
for (const m of dotnetSrc.matchAll(/"name":\s*"([^"]+)",\s*"hash":\s*"sha256-([^"]+)"/g)) hashes[m[1]] = m[2];

// Loaded from a blob: URL, import.meta.url is useless as a base (new URL(name, blob:...) throws in dotnet.js's locateFile), so
// the framework scripts get a real-looking https base from the unpacker instead. The runtime resolves every resource to
// <base>/_framework/<name> and the resource loader answers it from memory by name.
const BASE = 'globalThis.__frBundle.baseUrl';
const rebase = (buf) => Buffer.from(buf.toString('utf8').replaceAll('import.meta.url', BASE));
const b64gz = (buf) => gzipSync(buf, { level: 9 }).toString('base64');
const files = {}; // key -> { type, data }
const add = (key, buf, type) => { files[key] = { type, data: b64gz(buf) }; };

add('main.js', readFileSync(join(root, 'main.js')), 'js');
add('page.js', readFileSync(join(root, 'page.js')), 'js');
if (existsSync(join(root, 'atproto.js'))) add('atproto.js', readFileSync(join(root, 'atproto.js')), 'js');
else console.warn('no atproto.js: login will be unavailable in the bundle');
// music.js also carries the master volume and mute that sfx.js plays through, so both are always embedded
add('music.js', readFileSync(join(root, 'music.js')), 'js');
add('sfx.js', readFileSync(join(root, 'sfx.js')), 'js');
add('dotnet.js', rebase(Buffer.from(dotnetSrc)), 'js');

let checked = 0;
for (const name of readdirSync(fw)) {
  if (name === 'dotnet.js' || /\.(br|gz)$/.test(name) || !statSync(join(fw, name)).isFile()) continue;
  if (!/\.(js|wasm)$/.test(name)) { console.warn(`skipping ${name}`); continue; }
  const buf = readFileSync(join(fw, name));
  if (hashes[name]) {
    if (createHash('sha256').update(buf).digest('base64') !== hashes[name]) throw new Error(`hash mismatch for ${name}`);
    checked++;
  }
  add(name, name.endsWith('.js') ? rebase(buf) : buf, name.endsWith('.js') ? 'js' : 'wasm');
}

// Runs inside the page. Keys are file names; 'main.js', 'atproto.js' and 'dotnet.js' are the entry modules.
const boot = `
const files = JSON.parse(document.getElementById('fr-files').textContent);
const unpack = async (b64) => {
  const raw = (await fetch('data:application/octet-stream;base64,' + b64)).body;
  return new Response(raw.pipeThrough(new DecompressionStream('gzip')));
};
const urls = {};
const jsUrl = async (k) => urls[k] ??= URL.createObjectURL(new Blob([await (await unpack(files[k].data)).arrayBuffer()], { type: 'text/javascript' }));
// dotnetjs resources must be URL strings, so every JS module is turned into a blob: URL up front.
for (const k of Object.keys(files)) if (files[k].type === 'js') await jsUrl(k);
globalThis.__frBundle = {
  baseUrl: new URL('_framework/dotnet.js', document.baseURI).href,
  dotnetUrl: urls['dotnet.js'],
  atprotoUrl: urls['atproto.js'],
  pageUrl: urls['page.js'],
  musicUrl: urls['music.js'],
  sfxUrl: urls['sfx.js'],
  music: ${JSON.stringify(music).replace(/</g, "\\u003c")},
  loadBootResource(type, name) {
    const f = files[name];
    if (!f || name === 'dotnet.js') return undefined; // not embedded: the runtime fetches it as usual
    if (f.type === 'js') return urls[name];
    return unpack(f.data).then((r) => new Response(r.body, { headers: { 'Content-Type': 'application/wasm' } }));
  },
};
await import(urls['main.js']);
`;

let html = readFileSync(join(root, 'index.html'), 'utf8');
const tag = /<script type="module" src="\.\/main\.js"><\/script>/;
if (!tag.test(html)) throw new Error('index.html has no <script type="module" src="./main.js">');
const json = JSON.stringify(files).replace(/</g, '\u003c');
html = html.replace(tag, () => `<script type="application/json" id="fr-files">${json}</script>\n  <script type="module">${boot}</script>`);
writeFileSync(out, html);
process.stdout.write(`wrote ${out}: ${(html.length / 1048576).toFixed(2)} MB, ${Object.keys(files).length} embedded files, ${checked} hashes verified\n`);
