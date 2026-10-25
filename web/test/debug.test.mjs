import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// main.js is browser top-level code (it boots the whole game), so it cannot be imported. The ?debug=feed panel is one
// self-contained block of it: read that block out of the source and run it against stand-ins for the page.
const main = readFileSync(new URL('../../src/FeedRaider.Web/wwwroot/main.js', import.meta.url), 'utf8');
const start = main.indexOf("const panel = document.getElementById('debug');");
const end = main.indexOf("if (debug === 'feed') renderDebug();", start);
assert.ok(start > 0 && end > start, 'the debug block is still where this test looks for it');
const block = main.slice(start, end);

const node = (tag) => ({
  tag, children: [], style: {}, textContent: '',
  append(...kids) { this.children.push(...kids); },
  replaceChildren(...kids) { this.children = kids; },
});

/** The panel after renderDebug has run on a page with the given state. */
async function run({ me = { handle: 'me.test' }, auth = {}, feed = null, posts = [], bridge = {} } = {}) {
  const panel = node('section');
  const document = { getElementById: () => panel, createElement: node };
  const calls = [];
  const exports = { Bridge: {
    FeedPage: async (...a) => { calls.push(a); return JSON.stringify({ posts }); },
    ...bridge,
  } };
  const params = new URLSearchParams(feed ? { feed } : {});
  const make = new Function('document', 'renderSession', 'auth', 'params', 'exports', `${block}\nreturn renderDebug;`);
  await make(document, () => me, auth, params, exports)();
  return { panel, calls };
}

const post = (i) => ({ uri: `at://did:plc:x/app.bsky.feed.post/${i}`, authorHandle: `user${i}.test`, text: `post number ${i}`, likeCount: i, repostCount: 2, replyCount: 3, replyRoot: null });
const posts = (n) => Array.from({ length: n }, (_, i) => post(i + 1));
const rows = (panel) => panel.children.filter((c) => c.className === 'post');
const text = (n) => n.textContent;

test('debug panel: opens with its heading and shows itself', async () => {
  const { panel } = await run({ me: null, auth: {} });
  assert.equal(panel.style.display, 'block');
  assert.equal(panel.children[0].tag, 'h2');
  assert.equal(text(panel.children[0]), 'feed debug');
});

test('debug panel: signed out, it asks for a sign-in; with no sign-in on the page, it says why', async () => {
  const asks = await run({ me: null, auth: {} });
  assert.equal(asks.panel.children.length, 2);
  assert.equal(text(asks.panel.children[1]), 'Sign in with Bluesky (top right) to read the feed.');
  assert.equal(asks.calls.length, 0, 'no feed is read without a session');
  const none = await run({ me: null, auth: null });
  assert.equal(text(none.panel.children[1]), 'Sign-in is unavailable on this page (it needs HTTPS or localhost).');
});

test('debug panel: signed in, it reads ten posts of the discover feed by default and counts them', async () => {
  const { panel, calls } = await run({ posts: posts(3) });
  assert.deepEqual(calls, [['discover', null, 10]]);
  assert.equal(text(panel.children[1]), '3 posts from discover (as @me.test)');
  assert.equal(rows(panel).length, 3);
});

test('debug panel: the feed named in the URL is the one read and reported', async () => {
  const { panel, calls } = await run({ feed: 'following', posts: posts(1) });
  assert.equal(calls[0][0], 'following');
  assert.equal(text(panel.children[1]), '1 posts from following (as @me.test)');
});

test('debug panel: lists at most ten posts, the first ten, while the count says how many came', async () => {
  const { panel } = await run({ posts: posts(25) });
  assert.equal(text(panel.children[1]), '25 posts from discover (as @me.test)');
  const shown = rows(panel);
  assert.equal(shown.length, 10);
  assert.match(text(shown[0].children[1]), /post number 1$/);
  assert.match(text(shown[9].children[1]), /post number 10$/);
  assert.equal((await run({ posts: posts(10) })).panel.children.length, 12, 'ten posts: heading, status and ten rows');
});

test('debug panel: a row shows the author, the counts and the text, and marks a reply', async () => {
  const { panel } = await run({ posts: [{ ...post(7), replyRoot: 'at://root' }, post(8)] });
  const [reply, plain] = rows(panel);
  assert.equal(text(reply.children[0]), '@user7.test (a reply)  likes 7  reposts 2  replies 3');
  assert.equal(text(plain.children[0]), '@user8.test  likes 8  reposts 2  replies 3');
  assert.equal(text(reply.children[1]), 'post number 7');
  assert.equal(reply.children[2].children[0].textContent, 'like');
});

test('debug panel: a feed that fails to load says so in place of the count', async () => {
  const { panel } = await run({ bridge: { FeedPage: async () => { throw new Error('boom'); } } });
  assert.equal(text(panel.children[1]), 'feed failed: boom');
  assert.equal(rows(panel).length, 0);
});

test('debug panel: the like button likes that post only, and a failed like can be tried again', async () => {
  const liked = [];
  let fail = true;
  const { panel } = await run({ posts: posts(2), bridge: { Like: async (uri) => { liked.push(uri); if (fail) throw new Error('no'); return JSON.stringify({ uri: 'at://like/1' }); } } });
  const [like, result] = rows(panel)[1].children[2].children;
  await like.onclick();
  assert.deepEqual(liked, [post(2).uri]);
  assert.equal(text(result), 'like failed: no');
  assert.equal(like.disabled, false);
  fail = false;
  await like.onclick();
  assert.equal(text(result), 'liked: at://like/1');
  assert.equal(like.disabled, true, 'a like that went through cannot be sent twice');
});
