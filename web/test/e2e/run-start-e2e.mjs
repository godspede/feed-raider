// One command for the start-page browser check, from a fresh checkout (after `npm ci` in web/):
//   cd web && npm run e2e:start
// It bundles the sign-in code, publishes the web host to a temp folder, serves that folder on a free port bound to 127.0.0.1 (from this
// very process, so nothing else is started and nothing else has to be stopped), runs start-shows-game.e2e.mjs against the address and
// exits with its status. The temp folder is removed at the end.
//   --publish-dir <dir>   serve an earlier publish (its wwwroot is <dir>/wwwroot) instead of building one: for repeated runs
//   --www <dir>           serve this folder as it is, as the site root (a copy of the tree with the page changed, say)
// PLAYWRIGHT_CORE (playwright-core's folder) and FIREFOX_EXE pass through to the test. Needs the .NET 10 SDK, Node 22 and Playwright's build of Firefox.
import { createServer } from 'node:http';
import { spawn, spawnSync } from 'node:child_process';
import { readFile, mkdtemp, rm, stat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const web = fileURLToPath(new URL('../../', import.meta.url));
const root = resolve(web, '..');
const arg = (name) => { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : null; };

function run(cmd, args, cwd) {
  process.stdout.write(`> ${cmd} ${args.join(' ')}\n`);
  const r = spawnSync(cmd, args, { cwd, stdio: 'inherit' });
  if (r.status !== 0) throw new Error(`${cmd} ${args[0]} failed (${r.status ?? r.error?.message})`);
}

const types = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm',
  '.png': 'image/png', '.svg': 'image/svg+xml', '.mp3': 'audio/mpeg', '.ogg': 'audio/ogg', '.wav': 'audio/wav', '.map': 'application/json',
};
function serve(dir) {
  const base = resolve(dir) + sep;
  const server = createServer(async (req, res) => {
    try {
      const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
      let file = resolve(base, '.' + (path.endsWith('/') ? path + 'index.html' : path));
      if (!file.startsWith(base)) { res.writeHead(403); return res.end(); }
      if ((await stat(file)).isDirectory()) file = join(file, 'index.html');
      const body = await readFile(file);
      res.writeHead(200, { 'Content-Type': types[extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
      res.end(req.method === 'HEAD' ? undefined : body);
    } catch (e) {
      if (!['ENOENT', 'ENOTDIR'].includes(e.code) && !(e instanceof URIError)) throw e;   // a missing file or a bad escape is a 404; any other failure is a real fault
      res.writeHead(404); res.end();
    }
  });
  return new Promise((ok) => server.listen(0, '127.0.0.1', () => ok({
    address: `http://127.0.0.1:${server.address().port}/`,
    close: () => new Promise((r) => { server.closeAllConnections?.(); server.close(r); }),
  })));
}

let tmp = null;
let site = null;
let child = null;
let status = 1;
try {
  let www = arg('--www');
  if (!www) {
    let out = arg('--publish-dir');
    if (!out) {
      run(process.execPath, ['build.mjs'], web);
      tmp = await mkdtemp(join(tmpdir(), 'fr-start-e2e-'));
      out = tmp;
      run('dotnet', ['publish', join(root, 'src', 'FeedRaider.Web'), '-c', 'Release', '-o', out], root);
    }
    www = join(out, 'wwwroot');
  }
  site = await serve(www);
  process.stdout.write(`serving ${www} at ${site.address}\n`);
  child = spawn(process.execPath, [fileURLToPath(new URL('./start-shows-game.e2e.mjs', import.meta.url)), site.address], { stdio: 'inherit' });
  const stopChild = () => { try { child.kill(); } catch { /* already gone */ } };
  process.on('SIGINT', stopChild);
  process.on('SIGTERM', stopChild);
  status = await new Promise((ok) => child.on('exit', (code) => ok(code ?? 1)));
} catch (e) {
  console.error(e.message);
} finally {
  await site?.close();
  if (tmp) await rm(tmp, { recursive: true, force: true }).catch((e) => console.warn(`could not remove ${tmp}: ${e.message}`));
}
process.exit(status);
