// Screenshots of the page chrome (start page, card hover, signed-in sign-out hover, pause menu) at 1280x800 and 390x844,
// plus the sign-out link's box before and after hover and the text/surface contrast ratios of the Olive dusk tokens.
//   node test/e2e/page-chrome-shots.mjs <out dir>      (PLAYWRIGHT_CORE names playwright-core when it is not importable)
// It loads index.html straight from disk and sets the states by hand, so it needs no server and no game runtime. One Firefox.
import { fileURLToPath, pathToFileURL } from 'node:url';
import { join } from 'node:path';
import { mkdirSync } from 'node:fs';

const HOVER_SETTLE_MS = 400;   // the hover transitions on the cards and the sign-out link finish within this

const out = process.argv[2];
if (!out) { process.stderr.write('usage: node test/e2e/page-chrome-shots.mjs <out dir>\n'); process.exit(2); }
mkdirSync(out, { recursive: true });
const indexHtml = fileURLToPath(new URL('../../../src/FeedRaider.Web/wwwroot/index.html', import.meta.url));
async function playwright() {
  try { return await import('playwright-core'); } catch { /* not installed here */ }
  const dir = process.env.PLAYWRIGHT_CORE;
  if (!dir) throw new Error('install playwright-core, or set PLAYWRIGHT_CORE to its directory');
  return import(pathToFileURL(join(dir, 'index.mjs')).href);
}
const { firefox } = await playwright();
const browser = await firefox.launch();

const lum = (hex) => { const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= .03928 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4)); return .2126 * c[0] + .7152 * c[1] + .0722 * c[2]; };
const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return ((x + .05) / (y + .05)).toFixed(2); };
const pairs = [
  ['text', '#e9ead3', 'ground', '#181a11'], ['text', '#e9ead3', 'paper', '#2b2d24'], ['text', '#e9ead3', 'panel2', '#34362d'],
  ['dim', '#c0c1ab', 'ground', '#181a11'], ['dim', '#c0c1ab', 'paper', '#2b2d24'], ['accent', '#98a8e1', 'ground', '#181a11'],
  ['accent', '#98a8e1', 'paper', '#2b2d24'], ['ground (button text)', '#181a11', 'olive (button)', '#a3b35f'],
  ['ground (button text)', '#181a11', 'olive-light (hover)', '#b9c76e'], ['text', '#e9ead3', 'wine (error)', '#a8324d'],
  ['crit', '#e2919f', 'paper', '#2b2d24'], ['olive', '#a3b35f', 'paper', '#2b2d24'],
];
for (const [n1, a, n2, b] of pairs) process.stdout.write(`contrast ${n1} ${a} on ${n2} ${b}: ${ratio(a, b)}\n`);

for (const [name, vp] of [['desktop', { width: 1280, height: 800 }], ['phone', { width: 390, height: 844 }]]) {
  const ctx = await browser.newContext({ viewport: vp });
  const page = await ctx.newPage();
  await page.goto(pathToFileURL(indexHtml).href);
  await page.addStyleTag({ content: '#start-load{display:none!important}' });
  await page.evaluate(() => { document.getElementById('start-login').disabled = false; document.getElementById('start').classList.remove('booting'); });
  await page.screenshot({ path: join(out, `${name}-start-rest.png`) });
  await page.hover('#start-card-guest');
  await page.waitForTimeout(HOVER_SETTLE_MS);
  await page.screenshot({ path: join(out, `${name}-start-card-hover.png`) });
  await page.mouse.move(0, 0);
  await page.evaluate(() => {
    document.getElementById('start-login').hidden = true;
    document.getElementById('start-card-signin').hidden = false; document.getElementById('start-card-signin').style.display = ''; document.getElementById('start-signed').hidden = false; document.getElementById('start-signout').hidden = false;
    document.getElementById('start-who-name').textContent = 'Signed in as kim.test';
  });
  const box = async () => page.$eval('#start-signout', (e) => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return `${r.x},${r.y},${r.width}x${r.height} font ${s.fontSize} pad ${s.padding} shadow ${s.boxShadow} transform ${s.transform}`; });
  const before = await box();
  await page.hover('#start-signout');
  await page.waitForTimeout(HOVER_SETTLE_MS);
  const after = await box();
  process.stdout.write(`${name} signout rest : ${before}\n${name} signout hover: ${after}\n${name} signout steady: ${before.replace(/\S+ \S+ \S+ \S+$/, '') === after.replace(/\S+ \S+ \S+ \S+$/, '')}\n`);
  await page.screenshot({ path: join(out, `${name}-signed-in-signout-hover.png`) });
  await page.mouse.move(0, 0);
  await page.evaluate(() => { document.getElementById('start').style.display = 'none'; document.getElementById('menu-info').appendChild(document.getElementById('info').content.cloneNode(true)); document.getElementById('menu').showModal(); });
  await page.screenshot({ path: join(out, `${name}-pause-menu.png`) });
  await ctx.close();
}
await browser.close();
