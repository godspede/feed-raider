// Bundles src/atproto.js (OAuth client + XRPC agent) into the web host's wwwroot as one ES module, and writes the licence
// notices of the npm packages it holds beside it.
import { build } from 'esbuild';
import { basename } from 'node:path';
import { writeNotices } from './third-party-notices.mjs';

const outfile = '../src/FeedRaider.Web/wwwroot/atproto.js';
// named after the bundle it covers; the xterm files beside the page carry their own LICENSE in xterm/
const noticesFile = `${outfile}.LICENSES.txt`;

const result = await build({
  entryPoints: ['src/atproto.js'],
  outfile,
  bundle: true,
  format: 'esm',
  platform: 'browser',
  target: 'es2022',
  minify: true,
  // the notices file carries every bundled package's licence in full, so the comments are not kept a second time
  legalComments: 'none',
  banner: { js: `/* Third-party licences for the code in this file: ${basename(noticesFile)}, beside it. */` },
  metafile: true,
  logLevel: 'info',
});
writeNotices(result.metafile, outfile, noticesFile);
