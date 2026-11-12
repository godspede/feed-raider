// The popup sign-in end to end, in Firefox: the real bundle (the OAuth library, IndexedDB, BroadcastChannel), the real page and a
// mock authorization server that cuts the popup off with COOP like bsky.social. Not part of `node --test` (it needs a browser):
//   node test/e2e/popup-signin.e2e.mjs            the real hand-off
//   node test/e2e/popup-signin.e2e.mjs --no-bus   the callback page's BroadcastChannel posts go nowhere
//   node test/e2e/popup-signin.e2e.mjs --abandon  the opening page stops waiting (Sign in pressed again) before the popup is done
//   ... --reload                                  then the popup is closed and the opening page reloaded: it must restore the session
//   ... --reload --lose-sub                       and before the reload the library's pointer to the current account is gone, as a
//                                                 callback page closed between saving and restoring, or a racing failed restore, leaves it
// Nothing brings the opening page to the front: it has to find the session by itself.
// Prints one line per case and exits non-zero when the opening page did not adopt the session.
// PLAYWRIGHT_CORE names playwright-core when it is not importable from here (playwright-cli carries one).
import { createServer } from 'node:http';
import { readFile, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, extname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from 'esbuild';
import { startMockAs } from './mock-as.mjs';

const here = fileURLToPath(new URL('.', import.meta.url));
const wwwroot = fileURLToPath(new URL('../../../src/FeedRaider.Web/wwwroot/', import.meta.url));
const PAGE_READY_MS = 30e3;   // the opening page's Sign in button turns on (or, after a reload, the session is found) within this
const POPUP_MS = 15e3;        // the popup opens, and its authorization page shows, within this
const noBus = process.argv.includes('--no-bus');
const abandon = process.argv.includes('--abandon');
const reload = process.argv.includes('--reload');
const loseSub = process.argv.includes('--lose-sub');

async function playwright() {
  try { return await import('playwright-core'); } catch { /* not installed here */ }
  const dir = process.env.PLAYWRIGHT_CORE;
  if (!dir) throw new Error('install playwright-core, or set PLAYWRIGHT_CORE to its directory');
  return import(pathToFileURL(join(dir, 'index.mjs')).href);
}

// The bundle as shipped, but its OAuth client may talk to an http authorization server (the mock), as the library allows in tests.
async function harnessBundle(outDir) {
  const out = join(outDir, 'atproto.js');
  await build({
    entryPoints: [fileURLToPath(new URL('../../src/atproto.js', import.meta.url))],
    outfile: out, bundle: true, format: 'esm', platform: 'browser', target: 'es2022', logLevel: 'error',
    plugins: [{
      name: 'allow-http',
      setup(b) {
        b.onResolve({ filter: /^@atproto\/oauth-client-browser$/ }, (args) => args.importer.endsWith('allow-http.mjs') ? undefined : { path: join(here, 'allow-http.mjs') });
      },
    }],
  });
  return readFile(out);
}

const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.html': 'text/html', '.css': 'text/css', '.json': 'application/json', '.png': 'image/png' };
function startSite(bundle) {
  const server = createServer(async (req, res) => {
    const path = new URL(req.url, 'http://x').pathname;
    if (path === '/atproto.js') { res.writeHead(200, { 'Content-Type': 'text/javascript' }); return res.end(bundle); }
    try {
      const body = await readFile(join(wwwroot, path === '/' ? 'index.html' : decodeURIComponent(path).replace(/^\/+/, '')));
      res.writeHead(200, { 'Content-Type': types[extname(path)] || (path === '/' ? 'text/html' : 'application/octet-stream') });
      res.end(body);
    } catch { res.writeHead(404); res.end(); }
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve({
    origin: `http://127.0.0.1:${server.address().port}`,
    close: () => new Promise((r) => { server.closeAllConnections?.(); server.close(r); }),
  })));
}

const tmp = await mkdtemp(join(tmpdir(), 'fr-popup-e2e-'));
const as = await startMockAs();
const site = await startSite(await harnessBundle(tmp));
const { firefox } = await playwright();
const browser = await firefox.launch({ headless: true });
let failed = false;
try {
  const context = await browser.newContext();
  const logs = [];
  const watchConsole = (p, who) => p.on('console', (m) => logs.push(`${who}: ${m.text()}`));
  if (noBus) {
    // the callback page's posts are lost: a browser where the hand-off never arrives
    await context.addInitScript(() => {
      if (/[#&]state=/.test(location.hash)) BroadcastChannel.prototype.postMessage = function () {};
    });
  }
  const opener = await context.newPage();
  watchConsole(opener, 'opener');
  await opener.goto(site.origin);
  await opener.waitForFunction(() => !document.getElementById('start-login').disabled, null, { timeout: PAGE_READY_MS });
  // the start page signs in at bsky.social; here the same signIn goes to the mock, from a click so the popup is allowed
  await opener.evaluate(({ target, abandon }) => {
    const b = document.createElement('button');
    b.id = 'e2e-signin';
    b.textContent = 'e2e sign in';
    b.style.cssText = 'position:fixed;top:0;left:0;z-index:99999';
    b.onclick = async () => {
      window.__e2e = 'waiting';
      const stop = new AbortController();
      const onWaiting = () => { if (abandon) stop.abort(); };
      try { const m = await import('./atproto.js'); window.__e2e = (await m.signIn(target, { onWaiting, signal: stop.signal })) || 'null'; } catch (e) { window.__e2e = 'error: ' + (e?.message || e); }
    };
    document.body.append(b);
  }, { target: as.origin, abandon });
  const popupSeen = context.waitForEvent('page', { timeout: POPUP_MS });
  await opener.click('#e2e-signin');
  const popup = await popupSeen;
  watchConsole(popup, 'popup');
  await popup.waitForSelector('#go', { timeout: POPUP_MS });
  const severed = await popup.evaluate(() => window.opener === null); // COOP took effect: the popup lost its opener
  await popup.click('#go');
  let popupClosed = false;
  popup.on('close', () => { popupClosed = true; });
  // no focus, no bringToFront: the opening page must notice by itself (hand-off, storage event or its own asking)
  // a page that never shows the account is caught by the check of `shown` below, with its own message
  await opener.waitForFunction(() => document.getElementById('start-signed')?.hidden === false, null, { timeout: 5000 }).catch(() => {});
  const after = await opener.evaluate(() => window.__e2e);
  const shown = await opener.evaluate(() => (document.getElementById('start-signed')?.hidden === false ? document.getElementById('start-who-name')?.textContent || '?' : ''));
  if (process.env.E2E_VERBOSE) console.info('pages:', context.pages().map((p) => p.url()).join(' | '));
  const popupState = popupClosed ? 'closed' : await popup.evaluate(() => `open at ${location.href} [${document.getElementById('start-error')?.textContent || ''}] who=${document.getElementById('start-who-name')?.textContent || ''} note=${document.getElementById('start-note')?.hidden === false ? document.getElementById('start-note').textContent : ''}`).catch((e) => 'gone: ' + e.message);
  // an abandoned wait ends in SignInCancelled; the start page itself must then show the account
  let adopted = abandon ? !!shown : typeof after === 'string' && after.startsWith('{');
  let reloaded = '';
  if (reload) {
    // the operator's report: he closed the popup himself and reloaded the opening page, which must find the saved session
    if (!popupClosed) await popup.close();
    if (loseSub) await opener.evaluate(() => localStorage.removeItem('@@atproto/oauth-client-browser(sub)'));
    await opener.reload();
    await opener.waitForFunction(() => document.getElementById('start-signed')?.hidden === false || !document.getElementById('start-login').disabled, null, { timeout: PAGE_READY_MS });
    const again = await opener.evaluate(() => ({
      who: document.getElementById('start-signed')?.hidden === false ? document.getElementById('start-who-name')?.textContent || '?' : '',
      error: document.getElementById('start-error')?.style.display === 'block' ? document.getElementById('start-error').textContent : '',
    }));
    adopted = !!again.who;
    reloaded = `; after reload shows=${again.who || '(signed out)'} [${again.error}]`;
  }
  failed = !adopted;
  console.info(`${abandon ? 'abandon' : noBus ? 'no-bus' : 'hand-off'}${reload ? '+reload' : ''}${loseSub ? '+lose-sub' : ''}: opener signIn=${after} start page shows=${shown || '(signed out)'}; popup ${popupState}; severed-check=${severed}${reloaded}`);
  if (!adopted || process.env.E2E_VERBOSE) console.info(logs.join('\n'), '\nmock:', as.log.join(', '));
} finally {
  await browser.close();
  await site.close();
  await as.close();
  await rm(tmp, { recursive: true, force: true });
}
process.exit(failed ? 1 : 0);
