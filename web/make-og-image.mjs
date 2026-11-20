// Usage (from web/): npm i --no-save playwright@1.64.1 && npx playwright install firefox && node make-og-image.mjs <site url>
// Plays the page as a guest on the Random feed at stage 4, with every network request answered locally from an invented feed, takes
// frames of the game while the ship sweeps, and composes og.png from the frame showing the most mines, divers, splitters and turrets.
// Set OG_FRAMES=<dir> to keep every frame, then OG_SHOT=<one of those pngs> to compose the card from that frame without playing.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { firefox } from 'playwright';

const site = process.argv[2]?.replace(/\/+$/, '');
if (!site && !process.env.OG_SHOT) throw new Error('usage: node make-og-image.mjs <site url>');
const out = new URL('../src/FeedRaider.Web/wwwroot/og.png', import.meta.url);

// The invented feed: eight authors with one post each; the even-numbered authors have an avatar and two posts carry one picture.
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
const handles = ['ada', 'bo', 'cy', 'di', 'eli', 'fay', 'gus', 'hal'];
const likeCounts = [240, 1800, 35, 90, 12, 410, 1200, 60];
const REPOST_COUNT = 3;
const withAvatar = (i) => i % 2 === 0;
const pictureAuthors = [1, 4];
// Hosts that exist nowhere: the posts point the page at them, and the script answers in their place.
const CDN = 'https://cdn.example.test', PDS = 'https://pds.example.test';
const didOf = (i) => `did:plc:example${i}`;
const avatarCid = (i) => `avatar${i}`, pictureCid = (i) => `picture${i}`;
const PICTURE_ASPECT = { width: 4, height: 3 };
const BLOCK_PX = 64;   // side of a generated avatar or picture; the page downsamples each to a few cells
const BLOCK_HUES = 360, BLOCK_HUE_STEP = 47;   // each block turns the hue by a prime step so neighbours differ
const blockColour = (n) => `hsl(${(n * BLOCK_HUE_STEP) % BLOCK_HUES} 60% 50%)`;
const feed = texts.map((text, i) => ({
  post: {
    uri: `at://${didOf(i)}/app.bsky.feed.post/${i}`, cid: `cid${i}`,
    author: {
      did: didOf(i), handle: `${handles[i]}.example.com`,
      ...(withAvatar(i) && { avatar: `${CDN}/img/avatar/plain/${didOf(i)}/${avatarCid(i)}@jpeg` }),
    },
    record: { text }, likeCount: likeCounts[i], repostCount: REPOST_COUNT, replyCount: 0,
    ...(pictureAuthors.includes(i) && {
      embed: {
        $type: 'app.bsky.embed.images#view',
        images: [{ thumb: `${CDN}/img/feed_thumbnail/plain/${didOf(i)}/${pictureCid(i)}@jpeg`, alt: 'an invented picture', aspectRatio: PICTURE_ASPECT }],
      },
    }),
  },
}));

const CARD_W = 1200, CARD_H = 630, SHOT_W = 560;   // the card, and the game frame on its right
const TEXT_W = CARD_W - SHOT_W;                    // the left column, divider included
const COLOUR_TOLERANCE = 14;   // summed per-channel distance under which a pixel has a kind's colour
const MIN_KIND_PIXELS = 12;    // pixels of a kind's colour that make a frame show that kind
const LOAD_TIMEOUT_MS = 120_000;   // the game runtime downloads before the first frame
const WARM_UP_MS = 8000;           // flying time before the first frame, so stage-4 enemies are on screen
const FRAME_COUNT = 30, FRAME_GAP_MS = 400;
const SWEEP_STEP_MS = 250;         // pause between pointer moves during the warm-up
const SWEEP_PERIOD_MS = 7000;      // one slow left-right pass of the ship
const SWEEP_X = 0.38 * SHOT_W, SWEEP_Y = 0.12 * CARD_H;   // half-width and half-height of the sweep
const SWEEP_CENTRE_Y = 0.78 * CARD_H;                      // the ship stays in the lower part of the field
const SWEEP_Y_SPEED = 1.7;         // vertical drift relative to the horizontal pass, so the path does not retrace itself
const POINTER_STEPS = 4;           // intermediate moves per sweep step, so the ship glides

// These mirror the page's colours and face (CSS custom properties in wwwroot/index.html, :root).
const PAGE_BG = '#151D28', PAGE_SURFACE_2 = '#222E3F', PAGE_LINE = '#2C3A4E', PAGE_LINE_STRONG = '#394960';
const PAGE_TEXT = '#FFFFFF', PAGE_DIM = '#ABB8C9';
const PAGE_SANS = 'Inter, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';
const PILL_RADIUS = '999px';

const browser = await firefox.launch();
const card = await browser.newPage({ viewport: { width: CARD_W, height: CARD_H } });
// The sprite colours of the mine, diver, splitter and turret, from Sprites.cs (MineLit, DiverLime, SplitterPink, TurretTeal).
// A frame counts a kind when enough pixels are close to its colour.
const kindColours = [[0xff, 0xb3, 0x47], [0x9d, 0xff, 0x5a], [0xff, 0x6b, 0xe0], [0x3f, 0xe0, 0xd0]];
const kindsIn = (png) => card.evaluate(async ([b64, colours, tolerance, minPixels]) => {
  const img = new Image();
  img.src = `data:image/png;base64,${b64}`;
  await img.decode();
  const c = new OffscreenCanvas(img.width, img.height).getContext('2d');
  c.drawImage(img, 0, 0);
  const px = c.getImageData(0, 0, img.width, img.height).data;
  const seen = colours.map(() => 0);
  for (let i = 0; i < px.length; i += 4) colours.forEach(([r, g, b], k) => { if (Math.abs(px[i] - r) + Math.abs(px[i + 1] - g) + Math.abs(px[i + 2] - b) < tolerance) seen[k]++; });
  return seen.filter((n) => n >= minPixels).length;
}, [png.toString('base64'), kindColours, COLOUR_TOLERANCE, MIN_KIND_PIXELS]);
// A flat colour block as PNG bytes, drawn in the card page, which already has a canvas.
const blockPng = async (colour) => Buffer.from(await card.evaluate(async ([side, fill]) => {
  const c = new OffscreenCanvas(side, side);
  const g = c.getContext('2d');
  g.fillStyle = fill;
  g.fillRect(0, 0, side, side);
  const bytes = new Uint8Array(await (await c.convertToBlob({ type: 'image/png' })).arrayBuffer());
  return btoa(String.fromCharCode(...bytes));
}, [BLOCK_PX, colour]), 'base64');

let shot;
if (process.env.OG_SHOT) shot = readFileSync(process.env.OG_SHOT).toString('base64');
else {
  const blocks = new Map();   // blob CID -> PNG bytes
  for (let i = 0; i < texts.length; i++) {
    if (withAvatar(i)) blocks.set(avatarCid(i), await blockPng(blockColour(i)));
    if (pictureAuthors.includes(i)) blocks.set(pictureCid(i), await blockPng(blockColour(i + texts.length)));
  }
  const origin = new URL(site).origin;
  const page = await browser.newPage({ viewport: { width: SHOT_W, height: CARD_H } });
  // Routes registered later win: the catch-all comes first, so only what is answered below gets a reply.
  await page.route((url) => url.origin !== origin, (r) => r.abort());
  await page.route('**/xrpc/app.bsky.feed.getFeed*', (r) => r.fulfill({ json: { feed, cursor: 'next' } }));
  // The page looks an author up in the DID document, then asks the PDS named there for the blob.
  await page.route('https://plc.directory/**', (r) => r.fulfill({ json: { service: [{ id: '#atproto_pds', serviceEndpoint: PDS }] } }));
  await page.route(`${PDS}/xrpc/com.atproto.sync.getBlob*`, (r) => {
    const body = blocks.get(new URL(r.request().url()).searchParams.get('cid'));
    return body ? r.fulfill({ body, contentType: 'image/png' }) : r.abort();
  });
  // The page never reads the CDN addresses; answering them keeps a stray image request off the network.
  await page.route(`${CDN}/**`, (r) => {
    const body = blocks.get(/\/([a-z0-9]+)@jpeg$/.exec(r.request().url())?.[1]);
    return body ? r.fulfill({ body, contentType: 'image/png' }) : r.abort();
  });

  // ?feed=random starts a guest run on the Random feed at once, with no start page.
  await page.goto(`${site}/?darkness=off&sfx=off&music=off&stage=4&feed=random`);
  await page.locator('#term').waitFor({ state: 'visible', timeout: LOAD_TIMEOUT_MS });
  const began = Date.now();
  const sweep = () => {
    const phase = (2 * Math.PI * (Date.now() - began)) / SWEEP_PERIOD_MS;
    return page.mouse.move(SHOT_W / 2 + SWEEP_X * Math.sin(phase), SWEEP_CENTRE_Y + SWEEP_Y * Math.sin(phase * SWEEP_Y_SPEED), { steps: POINTER_STEPS });
  };
  // Holding fire needs no pointer press: a press on a post is a like, and a guest's like opens the sign-in prompt.
  await page.keyboard.down('Space');
  while (Date.now() - began < WARM_UP_MS) {
    await sweep();
    await page.waitForTimeout(SWEEP_STEP_MS);
  }

  const frames = [];
  for (let f = 0; f < FRAME_COUNT; f++) {
    await sweep();
    frames.push(await page.locator('#term').screenshot());
    await page.waitForTimeout(FRAME_GAP_MS);
  }
  await page.keyboard.up('Space');
  if (process.env.OG_FRAMES) {
    mkdirSync(process.env.OG_FRAMES, { recursive: true });
    frames.forEach((png, f) => writeFileSync(`${process.env.OG_FRAMES}/f${f}.png`, png));
  }
  const kinds = await Promise.all(frames.map(kindsIn));
  const best = kinds.lastIndexOf(Math.max(...kinds));
  console.info(`frame ${best} shows ${kinds[best]} new kinds`);
  shot = frames[best].toString('base64');
}

// The left column has 48 px of side padding, which leaves 544 px for text. The title is 11 characters at 64 px (about 420 px); the
// 24 px pill lines are at most 35 characters (about 470 px with their padding); the 32 px line may take two balanced lines.
await card.setContent(`<style>
  * { box-sizing: border-box; }
  body { margin: 0; width: ${CARD_W}px; height: ${CARD_H}px; display: flex; background: ${PAGE_BG}; color: ${PAGE_TEXT}; font: 24px/1.5 ${PAGE_SANS}; }
  .text { flex: none; width: ${TEXT_W}px; padding: 48px; display: flex; flex-direction: column; justify-content: center; align-items: flex-start; border-right: 1px solid ${PAGE_LINE}; }
  h1 { margin: 0 0 20px; font-size: 64px; line-height: 1.1; font-weight: 800; letter-spacing: -.01em; white-space: nowrap; }
  .lede { margin: 0 0 40px; font-size: 32px; line-height: 1.3; color: ${PAGE_DIM}; text-wrap: balance; }
  ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; align-items: flex-start; gap: 14px; }
  li { padding: 6px 20px; white-space: nowrap; border-radius: ${PILL_RADIUS}; background: ${PAGE_SURFACE_2}; box-shadow: inset 0 0 0 1px ${PAGE_LINE_STRONG}; font-weight: 600; }
  img { width: ${SHOT_W}px; height: ${CARD_H}px; display: block; flex: none; }
</style>
<div class="text"><h1>Feed Raider</h1>
<p class="lede">A shoot-'em-up built from your Bluesky feed</p>
<ul><li>Real posts scroll behind the fight</li><li>Rest on a post's heart to like it</li><li>Ten bosses, in your browser</li></ul></div>
<img src="data:image/png;base64,${shot}">`);
writeFileSync(out, await card.screenshot({ type: 'png' }));
await browser.close();
