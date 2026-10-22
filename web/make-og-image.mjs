// Usage (from web/): npm i --no-save playwright@1.64.1 && npx playwright install firefox && node make-og-image.mjs <site url>
// Plays the page as a guest (the Random start) on an invented feed (the Bluesky feed and thread requests are answered locally), likes a wingman
// until its self-thread is open in the bubble, then composes og.png. The run starts at stage 4, where mines, divers and splitters
// are unlocked and turrets are one stage away. Spawns are random, so it takes a frame every 400 ms for twelve seconds once the thread is open and keeps the one showing the most of the new kinds (counted by their sprite colours); set OG_FRAMES=<dir> to keep them all, then set
// OG_SHOT=<one of those pngs> to compose the card from that frame without playing.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { firefox } from 'playwright';

const site = process.argv[2]?.replace(/\/+$/, '');
if (!site && !process.env.OG_SHOT) throw new Error('usage: node make-og-image.mjs <site url>');
const out = new URL('../src/FeedRaider.Web/wwwroot/og.png', import.meta.url);

const texts = [
  'just shipped the thing we have been working on for months',
  'the best part of any feed is the quiet posts nobody reshared',
  'good morning everyone, coffee first and then the rest of the world',
  'finally got the garden bed built, tomatoes next week',
  'reading group is meeting again on thursday, bring a chapter',
  'tiny victory: the tests pass on the first try',
  'whoever left the lighthouse lamp on, thank you',
  'sketching ships all afternoon',
];
// self-threads the invented authors continue: the liked post is followed by these two, each a direct reply by the same author
const threads = {
  0: ['it is a small tool for sorting the photo archive, nothing fancy', 'the first release is on the shelf now, tell me what breaks'],
  1: ['two years of notes turned into one short book', 'the proofs came back this morning and they look right'],
  2: ['the bakery on the corner has started selling rye again', 'got the last loaf, still warm when I left'],
  3: ['the lamp needed a new wick and a lot of patience', 'it burns steady now, the whole bay is lit again'],
  4: ['planted the hedge along the north fence today', 'two rows in, the wind has already stopped complaining'],
  5: ['the pattern has twelve rows and I have lost count twice', 'done at last, the scarf is long enough for two'],
  6: ['rebuilt the old radio from a box of spare parts', 'it picks up the harbour station and nothing else, perfect'],
  7: ['the choir is short of tenors this season', 'three new voices showed up tonight, we are saved'],
};
const handles = ['ada', 'bo', 'cy', 'di', 'eli', 'fay', 'gus', 'hal'];
const postOf = (i, n, text) => ({
  uri: `at://did:plc:example${i}/app.bsky.feed.post/${i}${n ? `r${n}` : ''}`, cid: `cid${i}r${n}`,
  author: { did: `did:plc:example${i}`, handle: `${handles[i]}.example.com` },
  record: { text, ...(n && { reply: { root: { uri: `at://did:plc:example${i}/app.bsky.feed.post/${i}`, cid: `cid${i}r0` }, parent: { uri: `at://did:plc:example${i}/app.bsky.feed.post/${i}${n > 1 ? `r${n - 1}` : ''}`, cid: `cid${i}r${n - 1}` } } }), createdAt: `2026-01-01T10:0${n}:00Z` },
  likeCount: 5, repostCount: 0, replyCount: n < 2 && threads[i] ? 1 : 0,
});
const feed = texts.map((text, i) => ({
  post: {
    uri: `at://did:plc:example${i}/app.bsky.feed.post/${i}`, cid: `cid${i}`,
    author: { did: `did:plc:example${i}`, handle: `${handles[i]}.example.com` },
    record: { text }, likeCount: [240, 1800, 35, 90, 12, 410, 1200, 60][i], repostCount: 3, replyCount: threads[i] ? 1 : 0,
  },
}));

const browser = await firefox.launch();
const card = await browser.newPage({ viewport: { width: 1200, height: 630 } });
// the sprite colours of the mine, diver, splitter and turret; a frame counts a kind when enough pixels are close to its colour
const kindColours = [[0xff, 0xb3, 0x47], [0x9d, 0xff, 0x5a], [0xff, 0x6b, 0xe0], [0x3f, 0xe0, 0xd0]];
const kindsIn = (png) => card.evaluate(async ([b64, colours]) => {
  const img = new Image();
  img.src = `data:image/png;base64,${b64}`;
  await img.decode();
  const c = new OffscreenCanvas(img.width, img.height).getContext('2d');
  c.drawImage(img, 0, 0);
  const px = c.getImageData(0, 0, img.width, img.height).data;
  const seen = colours.map(() => 0);
  for (let i = 0; i < px.length; i += 4) colours.forEach(([r, g, b], k) => { if (Math.abs(px[i] - r) + Math.abs(px[i + 1] - g) + Math.abs(px[i + 2] - b) < 14) seen[k]++; });
  return seen.filter((n) => n >= 12).length;
}, [png.toString('base64'), kindColours]);
let shot;
if (process.env.OG_SHOT) shot = readFileSync(process.env.OG_SHOT).toString('base64');
else {
  const page = await browser.newPage({ viewport: { width: 560, height: 630 } });
  await page.route('**/xrpc/app.bsky.feed.getFeed*', (r) => r.fulfill({ json: { feed, cursor: 'next' } }));
  const opened = new Set();
  await page.route('**/xrpc/app.bsky.feed.getPostThread*', (r) => {
    const uri = new URL(r.request().url()).searchParams.get('uri');
    const m = /example(\d+)\/app\.bsky\.feed\.post\/\d+(?:r(\d))?$/.exec(uri);
    const i = Number(m[1]), n = Number(m[2] ?? 0);
    const next = threads[i]?.[n];
    if (next) opened.add(`${i}:${n}`);
    r.fulfill({ json: { thread: { post: { uri }, replies: next ? [{ post: postOf(i, n + 1, next) }] : [] } } });
  });
  await page.goto(`${site}/?darkness=off&sfx=off&music=off&stage=4`);
  await page.locator('#start-random').waitFor({ state: 'visible' });
  await page.locator('#start-random').click();
  await page.mouse.move(280, 520);
  await page.keyboard.down('Space');
  await page.waitForTimeout(8000);
  // sweep the plane over the field pressing like until a thread opens, then hold still and like the opened post too, so the bubble
  // stacks three posts; the guest's first like asks to sign in, and that popup has faded by the time the frame is taken
  const likes = () => opened.size;
  for (let t = 0; t < 400 && ![...opened].some((k) => k.endsWith(':1')); t++) {
    if (likes() === 0) await page.mouse.move(60 + (t * 37) % 440, 120 + (t * 71) % 440, { steps: 4 });
    await page.keyboard.press('KeyE');
    await page.waitForTimeout(250);
  }

  const frames = [];
  for (let f = 0; f < 30; f++) {
    frames.push(await page.locator('#term').screenshot());
    await page.waitForTimeout(400);
  }
  if (process.env.OG_FRAMES) {
    mkdirSync(process.env.OG_FRAMES, { recursive: true });
    frames.forEach((png, f) => writeFileSync(`${process.env.OG_FRAMES}/f${f}.png`, png));
  }
  const kinds = await Promise.all(frames.map(kindsIn));
  const best = kinds.lastIndexOf(Math.max(...kinds));
  console.info(`frame ${best} shows ${kinds[best]} new kinds`);
  shot = frames[best].toString('base64');
}

await card.setContent(`<style>
  * { box-sizing: border-box; }
  body { margin: 0; width: 1200px; height: 630px; display: flex; background: #101316; color: #d8cba3; font: 28px/1.35 system-ui, sans-serif; }
  .text { flex: 1; padding: 64px 56px; display: flex; flex-direction: column; justify-content: center; }
  h1 { margin: 0 0 28px; font: 700 60px/1 ui-monospace, "Cascadia Mono", Menlo, Consolas, monospace; letter-spacing: .18em; text-transform: uppercase; color: #eef2d6; white-space: nowrap; }
  h1::before { content: "▌"; color: #a3b35f; margin-right: .35em; letter-spacing: 0; }
  .lede { margin: 0 0 40px; color: #d8cba3; }
  ul { list-style: none; margin: 0; padding: 0; font: 26px/1.3 ui-monospace, "Cascadia Mono", Menlo, Consolas, monospace; }
  li { margin: 0 0 14px; color: #eef2d6; } li b { color: #a3b35f; font-weight: 600; }
  img { width: 560px; height: 630px; display: block; border-left: 4px solid #a3b35f; }
</style>
<div class="text"><h1>Feed Raider</h1>
<p class="lede">A shoot-'em-up built from your Bluesky feed</p>
<ul><li><b>Bluesky</b> posts fly in as wingmen</li><li><b>AT Protocol</b> OAuth sign-in</li><li><b>GitHub Pages</b>, static, no server</li></ul></div>
<img src="data:image/png;base64,${shot}">`);
writeFileSync(out, await card.screenshot({ type: 'png' }));
await browser.close();
