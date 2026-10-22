// Usage (from web/): npm i --no-save playwright && npx playwright install firefox && node make-og-image.mjs <site url>
// Plays the page as a guest on an invented feed (the Bluesky feed request is answered locally), then composes og.png.
import { writeFileSync } from 'node:fs';
import { firefox } from 'playwright';

const site = process.argv[2];
if (!site) throw new Error('usage: node make-og-image.mjs <site url>');
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
const feed = texts.map((text, i) => ({
  post: {
    uri: `at://did:plc:example${i}/app.bsky.feed.post/${i}`, cid: `cid${i}`,
    author: { did: `did:plc:example${i}`, handle: `${['ada', 'bo', 'cy', 'di', 'eli', 'fay', 'gus', 'hal'][i]}.example.com` },
    record: { text }, likeCount: [240, 1800, 35, 90, 12, 410, 1200, 60][i], repostCount: 3, replyCount: 1,
  },
}));

const browser = await firefox.launch();
const page = await browser.newPage({ viewport: { width: 560, height: 630 } });
await page.route('**/xrpc/app.bsky.feed.getFeed*', (r) => r.fulfill({ json: { feed, cursor: 'next' } }));
await page.goto(`${site}/?darkness=off&sfx=off&music=off`);
await page.locator('#start-guest').waitFor({ state: 'visible' });
await page.locator('#start-guest').click();
await page.mouse.move(280, 520);
await page.keyboard.down('Space');
await page.waitForTimeout(20000);
const shot = (await page.locator('#term').screenshot()).toString('base64');

const card = await browser.newPage({ viewport: { width: 1200, height: 630 } });
await card.setContent(`<style>
  * { box-sizing: border-box; }
  body { margin: 0; width: 1200px; height: 630px; display: flex; background: #101316; color: #d8cba3; font: 28px/1.35 system-ui, sans-serif; }
  .text { flex: 1; padding: 64px 56px; display: flex; flex-direction: column; justify-content: center; }
  h1 { margin: 0 0 28px; font-size: 96px; line-height: 1; color: #eef2d6; letter-spacing: -1px; }
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
