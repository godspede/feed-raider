// Bundles src/atproto.js (OAuth client + XRPC agent) into the web host's wwwroot as one ES module.
import { build } from 'esbuild';

await build({
  entryPoints: ['src/atproto.js'],
  outfile: '../src/FeedRaider.Web/wwwroot/atproto.js',
  bundle: true,
  format: 'esm',
  platform: 'browser',
  target: 'es2022',
  minify: true,
  legalComments: 'none',
  logLevel: 'info',
});
