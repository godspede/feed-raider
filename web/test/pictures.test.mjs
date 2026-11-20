// A post's pictures as the page fetches them for the cards: from the author's own PDS and nowhere else, downsampled to the
// size the game asked for, a few at a time, and any failure is only an empty box.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { AVATAR_SIDE, PICTURE_MAX_SIDE, PICTURE_WAIT_MS, PICTURES_KEY, bindPicturesSwitch, createPictures, pictureReader, pdsFinder, pdsOrigin, pictureCid, picturesStartOn,
  rememberRecent, shrinkPixels, thinConnection } from '../../src/FeedRaider.Web/wwwroot/page.js';

const wwwroot = new URL('../../src/FeedRaider.Web/wwwroot/', import.meta.url);
const DID = 'did:plc:abc123', PDS = 'https://pds.example.com';
const thumb = (cid, did = DID) => `https://cdn.bsky.app/img/feed_thumbnail/plain/${did}/${cid}@jpeg`;
const doc = (endpoint) => ({ service: [{ id: '#atproto_pds', type: 'AtprotoPersonalDataServer', serviceEndpoint: endpoint }] });

const CHUNK_BYTES = 500_000;
const OVER_LIMIT_BYTES = 3_000_000; // more than the reader will take for one picture
// A body of `size` bytes in chunks, which records when a reader gives up on it.
const bodyOf = (size, onCancel) => {
  let sent = 0;
  return new ReadableStream({
    pull(ctl) {
      if (sent >= size) return ctl.close();
      const n = Math.min(CHUNK_BYTES, size - sent);
      sent += n;
      ctl.enqueue(new Uint8Array(n));
    },
    cancel: onCancel,
  });
};

// A stand-in network: every URL asked for is recorded; the DID document and the blobs are answered from the tables given.
function net({ pds = PDS, blobs = {}, hold = null } = {}) {
  const asked = [], cancelled = [];
  const fetch = async (url, init) => {
    asked.push(String(url));
    const u = new URL(url);
    if (u.host === 'plc.directory') return { ok: true, json: async () => doc(pds) };
    if (hold) await hold(u, init);
    const blob = blobs[u.searchParams.get('cid')];
    if (blob === undefined) return { ok: false, status: 404, headers: new Map() };
    if (blob instanceof Error) throw blob;
    return { ok: true, headers: new Map(blob.length ? [['content-length', String(blob.length)]] : []), body: bodyOf(blob.size, () => cancelled.push(u.searchParams.get('cid'))) };
  };
  return { asked, cancelled, fetch };
}
// A `hold` for net() that keeps every blob fetch open until the test lets it go. nextGate() resolves, once a fetch is held, to
// the function that lets the oldest held fetch go; open() is how many are held now and most() the most there were at once.
function heldFetches() {
  const gates = [], waiters = [];
  let open = 0, most = 0;
  const hold = () => new Promise((go) => {
    open++;
    most = Math.max(most, open);
    const gate = () => { open--; go(); };
    if (waiters.length > 0) waiters.shift()(gate); else gates.push(gate);
  });
  const nextGate = () => new Promise((found) => { if (gates.length > 0) found(gates.shift()); else waiters.push(found); });
  return { hold, nextGate, open: () => open, most: () => most };
}
const flat = (colour) => async (_blob, w, h) => Array(w * h).fill(colour);
const avatarUrl = (cid, did = DID) => `https://cdn.bsky.app/img/avatar/plain/${did}/${cid}@jpeg`;
// The reader with the pictures on, as most tests below want it.
const readerWithPicturesOn = (opts) => pictureReader({ wanted: () => true, ...opts });
const pictureGetter = (opts) => readerWithPicturesOn(opts).picture;
// A storage of plain entries behind { get, set }, as the page wraps localStorage.
const memory = (entries = {}) => { const m = new Map(Object.entries(entries)); return { get: (k) => m.get(k) ?? null, set: (k, v) => m.set(k, v) }; };
const blobAsks = (n) => n.asked.filter((u) => u.includes('getBlob'));

test('the picture URL gives the blob cid only when it is a feed picture of that author', () => {
  assert.equal(pictureCid(thumb('bafkreiabc'), DID), 'bafkreiabc');
  assert.equal(pictureCid(`https://cdn.bsky.app/img/feed_fullsize/plain/${DID}/bafkreiabc`, DID), 'bafkreiabc');
  for (const bad of [thumb('bafkreiabc', 'did:plc:other'), `https://cdn.bsky.app/img/avatar/plain/${DID}/bafk@jpeg`, 'https://evil.example/x.png',
    `https://cdn.bsky.app/img/feed_thumbnail/plain/${DID}/../../x`, 'builtin:tea', null, 7]) assert.equal(pictureCid(bad, DID), null, String(bad));
});

test('a picture is asked of the DID directory and the author\'s PDS and of no other host, and never of the CDN', async () => {
  const n = net({ blobs: { bafkone: { size: 5000 } } });
  const get = pictureGetter({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode: flat(0x102030) });
  assert.equal(await get(DID, thumb('bafkone'), 2, 2), '1056816,1056816,1056816,1056816');
  assert.deepEqual(n.asked, [`https://plc.directory/${DID}`, `${PDS}/xrpc/com.atproto.sync.getBlob?did=${encodeURIComponent(DID)}&cid=bafkone`]);
  for (const url of n.asked) assert.ok(['plc.directory', 'pds.example.com'].includes(new URL(url).host), url);
  // the page's own code has no other way to a picture: it never fetches the URL the post carries
  const main = readFileSync(new URL('main.js', wwwroot), 'utf8');
  assert.doesNotMatch(main, /fetch\(url\)(?!,)[^\n]*thumb/);
  // main.js names the CDN only to choose the small size of a start page avatar, which an <img> shows and no code reads
  assert.deepEqual(main.match(/[^'"`\s]*cdn\.bsky\.app[^'"`\s]*/g), ['https://cdn.bsky.app/img/avatar/', 'https://cdn.bsky.app/img/avatar_thumbnail/']);
  assert.match(main, /page\.pictureReader\(/);
  assert.match(main, /fetchAvatar: pictureReader\.avatar,/);
  assert.match(main, /fetchPicture: pictureReader\.picture,/);
  // the avatars ask the same finder, so an author's DID document is fetched once, and no address is built from one in main.js
  assert.equal(main.match(/page\.pdsFinder\(/g).length, 1);
  assert.doesNotMatch(main, /didDocUrl|serviceEndpoint/);
});

test('a URL that is not the author\'s feed picture, or a size out of bounds, asks the network for nothing', async () => {
  const n = net({ blobs: { bafkone: { size: 10 } } });
  const get = pictureGetter({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode: flat(1) });
  assert.equal(await get(DID, 'https://tracker.example/pixel.gif', 4, 4), null);
  assert.equal(await get(DID, thumb('bafkone', 'did:plc:someoneelse'), 4, 4), null);
  for (const [w, h] of [[0, 4], [4, 65], [65, 4], [2.5, 2], [-1, 2]]) assert.equal(await get(DID, thumb('bafkone'), w, h), null, `${w}x${h}`);
  assert.deepEqual(n.asked, []);
});

test('the picture is decoded once at the kept size and shrunk to exactly the size asked for; a decode of another size is dropped', async () => {
  const n = net({ blobs: { bafkone: { size: 10 }, bafktwo: { size: 10 } } });
  const sizes = [];
  const decode = async (_b, w, h) => { sizes.push([w, h]); return Array(w * h).fill(0x204060); };
  const get = pictureGetter({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode });
  const csv = await get(DID, thumb('bafkone'), 48, 16);
  assert.deepEqual(sizes, [[PICTURE_MAX_SIDE, PICTURE_MAX_SIDE]]);
  assert.deepEqual(csv.split(',').map(Number), Array(48 * 16).fill(0x204060));
  const short = pictureGetter({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode: async () => [1, 2, 3] });
  assert.equal(await short(DID, thumb('bafktwo'), 2, 2), null);
});

test('a picture is kept by its CID: another size of it is shrunk from the kept copy with no second request', async () => {
  const n = net({ blobs: { bafkone: { size: 10 } } });
  let decodes = 0;
  const get = pictureGetter({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode: async (_b, w, h) => { decodes++; return Array(w * h).fill(3); } });
  assert.equal(await get(DID, thumb('bafkone'), 4, 4), Array(16).fill(3).join(','));
  assert.equal(await get(DID, thumb('bafkone'), 2, 3), Array(6).fill(3).join(','));
  assert.equal(await get(DID, thumb('bafkone'), PICTURE_MAX_SIDE, 1), Array(PICTURE_MAX_SIDE).fill(3).join(','));
  assert.equal(blobAsks(n).length, 1);
  assert.equal(decodes, 1);
});

test('an answer is kept: the same picture is fetched once, failures too, and only so many are kept', async () => {
  const n = net({ blobs: { bafkone: { size: 10 } } });
  const get = pictureGetter({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode: flat(7), picturesKept: 2 });
  await Promise.all([get(DID, thumb('bafkone'), 2, 2), get(DID, thumb('bafkone'), 2, 2)]);
  assert.equal(await get(DID, thumb('bafkgone'), 2, 2), null);
  assert.equal(await get(DID, thumb('bafkgone'), 2, 2), null);
  assert.equal(blobAsks(n).length, 2);
  assert.equal(n.asked.filter((u) => u.includes('plc.directory')).length, 1, 'the PDS is looked up once per author');
  await get(DID, thumb('bafkthird'), 2, 2); // pushes the oldest answer out
  await get(DID, thumb('bafkone'), 2, 2);
  assert.equal(blobAsks(n).length, 4);
});

test('no more than a few pictures are fetched at once and the rest wait their turn', async () => {
  const held = heldFetches();
  const blobs = Object.fromEntries(Array.from({ length: 8 }, (_, i) => [`bafk${i}`, { size: 10 }]));
  const n = net({ blobs, hold: held.hold });
  const get = pictureGetter({ fetch: n.fetch, pdsOf: async () => PDS, decode: flat(1), atOnce: 3 });
  const all = Array.from({ length: 8 }, (_, i) => get(DID, thumb(`bafk${i}`), 2, 2));
  for (let i = 0; i < 8; i++) {
    const letGo = await held.nextGate();
    assert.ok(held.open() <= 3, `${held.open()} open at once`);
    letGo();
  }
  assert.deepEqual(await Promise.all(all), Array(8).fill('1,1,1,1'));
  assert.equal(held.most(), 3);
});

test('an ask that waited longer than PICTURE_WAIT_MS for its turn is skipped, and a younger one is fetched', async () => {
  let clock = 0;
  const held = heldFetches();
  const n = net({ blobs: { bafkfirst: { size: 10 }, bafkold: { size: 10 }, bafkyoung: { size: 10 }, bafkagain: { size: 10 } }, hold: held.hold });
  const get = pictureGetter({ fetch: n.fetch, pdsOf: async () => PDS, decode: flat(1), atOnce: 1, now: () => clock });
  const first = get(DID, thumb('bafkfirst'), 2, 2);
  const old = get(DID, thumb('bafkold'), 2, 2);
  clock = PICTURE_WAIT_MS;
  const young = get(DID, thumb('bafkyoung'), 2, 2);
  clock = PICTURE_WAIT_MS + 1;
  (await held.nextGate())();
  (await held.nextGate())();
  assert.deepEqual(await Promise.all([first, old, young]), ['1,1,1,1', null, '1,1,1,1']);
  assert.deepEqual(blobAsks(n).map((u) => new URL(u).searchParams.get('cid')), ['bafkfirst', 'bafkyoung']);
  assert.equal(held.most(), 1);
  // the skipped ask is not kept as an empty answer: a later card with the same picture fetches it
  const again = get(DID, thumb('bafkold'), 2, 2);
  (await held.nextGate())();
  assert.equal(await again, '1,1,1,1');
});

test('a failed, oversized, slow or unreadable picture is null and the next one is still fetched', async () => {
  const never = (_u, init) => new Promise((_, no) => init.signal.addEventListener('abort', () => no(new DOMException('aborted', 'AbortError'))));
  const n = net({ blobs: { bafkbig: { size: 10, length: OVER_LIMIT_BYTES }, bafkfat: { size: OVER_LIMIT_BYTES }, bafkthrows: new TypeError('Failed to fetch'), bafkok: { size: 10 } } });
  const pdsOf = pdsFinder({ fetch: n.fetch });
  const get = pictureGetter({ fetch: n.fetch, pdsOf, decode: flat(9), atOnce: 1 });
  assert.equal(await get(DID, thumb('bafkmissing'), 2, 2), null, '404');
  assert.equal(await get(DID, thumb('bafkbig'), 2, 2), null, 'content-length over the limit');
  assert.equal(await get(DID, thumb('bafkfat'), 2, 2), null, 'blob over the limit');
  assert.equal(await get(DID, thumb('bafkthrows'), 2, 2), null, 'the fetch throws');
  const undecodable = pictureGetter({ fetch: n.fetch, pdsOf, decode: async () => { throw new DOMException('could not be decoded', 'InvalidStateError'); }, atOnce: 1 });
  assert.equal(await undecodable(DID, thumb('bafkok'), 2, 2), null, 'not a picture');
  const slowNet = net({ blobs: { bafkok: { size: 10 } }, hold: never });
  const slow = pictureGetter({ fetch: slowNet.fetch, pdsOf: async () => PDS, decode: flat(9), atOnce: 1, timeoutMs: 20 });
  // this fetch answers only when it is aborted, so an answer at all is the time limit at work
  assert.equal(await slow(DID, thumb('bafkslow'), 2, 2), null, 'slower than the time limit');
  // the one slot was given back every time
  assert.equal(await get(DID, thumb('bafkok'), 2, 2), '9,9,9,9');
});

test('an author with no PDS, or one that is not https, gets no picture and no blob request', async () => {
  for (const pds of [null, 'http://pds.example.com', 'javascript:alert(1)', 'not a url']) {
    const n = net({ pds, blobs: { bafkone: { size: 10 } } });
    const get = pictureGetter({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode: flat(1) });
    assert.equal(await get(DID, thumb('bafkone'), 2, 2), null, String(pds));
    assert.deepEqual(n.asked, [`https://plc.directory/${DID}`], String(pds));
  }
  const none = net();
  const get = pictureGetter({ fetch: none.fetch, pdsOf: pdsFinder({ fetch: none.fetch }), decode: flat(1) });
  assert.equal(await get('did:key:z6Mk', thumb('bafkone', 'did:key:z6Mk'), 2, 2), null);
  assert.deepEqual(none.asked, []);
});

test('a body with no content-length is read as it arrives and given up on past the limit', async () => {
  const n = net({ blobs: { bafkfat: { size: OVER_LIMIT_BYTES }, bafkok: { size: 10 } } });
  const get = pictureGetter({ fetch: n.fetch, pdsOf: async () => PDS, decode: flat(5) });
  assert.equal(await get(DID, thumb('bafkfat'), 2, 2), null);
  assert.deepEqual(n.cancelled, ['bafkfat'], 'the download was cancelled rather than read to the end');
  assert.equal(await get(DID, thumb('bafkok'), 2, 2), '5,5,5,5');
});

test('a decode that fails for a reason other than a lost picture is not taken for an empty box', async () => {
  const n = net({ blobs: { bafkone: { size: 10 } } });
  const get = pictureGetter({ fetch: n.fetch, pdsOf: async () => PDS, decode: async () => { throw new RangeError('a bug in the decoder'); } });
  await assert.rejects(get(DID, thumb('bafkone'), 2, 2), RangeError);
});

test('an avatar comes through the same reader: from the author\'s PDS by its CID, decoded at the drawn size, and kept', async () => {
  const n = net({ blobs: { bafkface: { size: 10 } } });
  const sizes = [];
  const reader = readerWithPicturesOn({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode: async (_b, w, h) => { sizes.push([w, h]); return Array(w * h).fill(4); } });
  const face = Array(AVATAR_SIDE * AVATAR_SIDE).fill(4).join(',');
  assert.equal(await reader.avatar(DID, avatarUrl('bafkface')), face);
  assert.equal(await reader.avatar(DID, avatarUrl('bafkface')), face);
  assert.deepEqual(sizes, [[AVATAR_SIDE, AVATAR_SIDE]]);
  assert.deepEqual(n.asked, [`https://plc.directory/${DID}`, `${PDS}/xrpc/com.atproto.sync.getBlob?did=${encodeURIComponent(DID)}&cid=bafkface`]);
  // a picture URL, or none at all, is no avatar and asks nothing
  assert.equal(await reader.avatar(DID, thumb('bafkone')), null);
  assert.equal(await reader.avatar(DID, null), null);
  // a failed avatar is null, which the card shows as no avatar
  assert.equal(await reader.avatar(DID, avatarUrl('bafkgone')), null);
  assert.equal(blobAsks(n).length, 2);
});

test('an avatar shares the pictures\' limit on requests at once and their byte cap', async () => {
  const held = heldFetches();
  const n = net({ blobs: { bafkp: { size: 10 }, bafka: { size: 10 }, bafkhuge: { size: OVER_LIMIT_BYTES } }, hold: held.hold });
  const reader = readerWithPicturesOn({ fetch: n.fetch, pdsOf: async () => PDS, decode: flat(1), atOnce: 1 });
  const asks = [reader.picture(DID, thumb('bafkp'), 2, 2), reader.avatar(DID, avatarUrl('bafka')), reader.avatar(DID, avatarUrl('bafkhuge'))];
  for (let i = 0; i < asks.length; i++) {
    (await held.nextGate())();
  }
  assert.deepEqual(await Promise.all(asks), ['1,1,1,1', Array(AVATAR_SIDE * AVATAR_SIDE).fill(1).join(','), null]);
  assert.equal(held.most(), 1);
  assert.deepEqual(n.cancelled, ['bafkhuge'], 'the avatar over the byte cap was not read to the end');
});

test('with the pictures off no picture or avatar is asked for, and a kept answer is not shown', async () => {
  const n = net({ blobs: { bafkone: { size: 10 }, bafkface: { size: 10 } } });
  let on = true;
  const reader = pictureReader({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode: flat(5), wanted: () => on });
  assert.equal(await reader.picture(DID, thumb('bafkone'), 2, 2), '5,5,5,5');
  on = false;
  const before = n.asked.length;
  assert.equal(await reader.picture(DID, thumb('bafkone'), 2, 2), null);
  assert.equal(await reader.picture(DID, thumb('bafktwo'), 2, 2), null);
  assert.equal(await reader.avatar(DID, avatarUrl('bafkface')), null);
  assert.equal(n.asked.length, before);
  on = true;
  assert.equal(await reader.avatar(DID, avatarUrl('bafkface')), Array(AVATAR_SIDE * AVATAR_SIDE).fill(5).join(','));
});

test('switching the pictures off drops the asks waiting for a turn, and turning them on fetches a card that comes in after', async () => {
  const held = heldFetches();
  const n = net({ blobs: { bafk0: { size: 10 }, bafk1: { size: 10 }, bafkface: { size: 10 } }, hold: held.hold });
  const pictures = createPictures({ storage: memory(), thin: false, switchedOff: () => reader.drop() });
  const reader = pictureReader({ fetch: n.fetch, pdsOf: async () => PDS, decode: flat(2), wanted: () => pictures.on, atOnce: 1 });
  const first = reader.picture(DID, thumb('bafk0'), 2, 2);
  const queued = [reader.picture(DID, thumb('bafk1'), 2, 2), reader.avatar(DID, avatarUrl('bafkface'))];
  const letFirstGo = await held.nextGate();
  pictures.set(false);
  assert.deepEqual(await Promise.all(queued), [null, null]);
  assert.equal(blobAsks(n).length, 1, 'only the request already under way was sent');
  letFirstGo();
  assert.equal(await first, '2,2,2,2', 'the one under way still finishes');
  pictures.set(true);
  const later = reader.picture(DID, thumb('bafk1'), 2, 2);
  (await held.nextGate())();
  assert.equal(await later, '2,2,2,2', 'a dropped ask is not kept as a failure');
  assert.equal(blobAsks(n).length, 2);
});

// A blob whose cid starts with bafkslow never answers and ends only when the reader gives up on it.
const slowBlobs = (u, init) => {
  if (!u.searchParams.get('cid').startsWith('bafkslow')) return undefined;
  return new Promise((_, no) => init.signal.addEventListener('abort', () => no(new DOMException('aborted', 'AbortError'))));
};
// The reader as the page wires it to the Pictures setting, with a 1 ms time limit.
const slowLink = (blobs = {}) => {
  const n = net({ blobs: { bafkok: { size: 10 }, ...blobs }, hold: slowBlobs });
  const storage = memory();
  const seen = { slows: 0 };
  const pictures = createPictures({ storage, thin: false, switchedOff: () => reader.switchedOff() });
  const reader = pictureReader({ fetch: n.fetch, pdsOf: async () => PDS, decode: flat(1), wanted: () => pictures.on, atOnce: 1, timeoutMs: 1,
    tooSlow: () => { seen.slows++; pictures.offForVisit(); } });
  const get = (cid) => reader.picture(DID, thumb(cid), 2, 2);
  return { n, storage, seen, pictures, get };
};
const expectNoPicture = async (get, ...cids) => { for (const cid of cids) assert.equal(await get(cid), null, cid); };

test('three reads in a row that run out of time call tooSlow once, and with the pictures off nothing more is fetched', async () => {
  const { n, storage, seen, pictures, get } = slowLink();
  const asks = [get('bafkslow1'), get('bafkslow2'), get('bafkslow3'), get('bafkok')];
  assert.deepEqual(await Promise.all(asks), [null, null, null, null]);
  assert.equal(seen.slows, 1);
  assert.equal(pictures.on, false);
  assert.equal(blobAsks(n).length, 3, 'the ask waiting for a turn was dropped, not fetched');
  assert.equal(await get('bafkok'), null);
  assert.equal(blobAsks(n).length, 3);
  assert.equal(storage.get(PICTURES_KEY), null, 'the stored choice is left alone');
});

test('two reads that run out of time, a picture, then two more do not call tooSlow; a third in a row does', async () => {
  const { seen, get } = slowLink();
  await expectNoPicture(get, 'bafkslow1', 'bafkslow2');
  assert.equal(await get('bafkok'), '1,1,1,1');
  await expectNoPicture(get, 'bafkslow3', 'bafkslow4');
  assert.equal(seen.slows, 0);
  await expectNoPicture(get, 'bafkslow5');
  assert.equal(seen.slows, 1);
});

test('a refusal, an over-size blob or a lost connection between reads that run out of time starts the count again', async () => {
  const blobs = { bafkbig: { size: 10, length: OVER_LIMIT_BYTES }, bafkfat: { size: OVER_LIMIT_BYTES }, bafkthrows: new TypeError('Failed to fetch') };
  for (const between of ['bafkmissing', 'bafkbig', 'bafkfat', 'bafkthrows']) {
    const { seen, get } = slowLink(blobs);
    await expectNoPicture(get, 'bafkslow1', 'bafkslow2', between, 'bafkslow3', 'bafkslow4');
    assert.equal(seen.slows, 0, between);
  }
});

test('off for the visit is not stored, runs switchedOff and shows Off; turning the pictures on is stored and fetches again from a count of none', async () => {
  const counted = memory();
  let offs = 0;
  const plain = createPictures({ storage: counted, thin: false, switchedOff: () => offs++ });
  plain.offForVisit();
  plain.offForVisit();
  assert.equal(plain.on, false);
  assert.equal(offs, 1);
  assert.equal(counted.get(PICTURES_KEY), null);
  const box = { checked: true, addEventListener: () => {} };
  const name = { textContent: '' };
  bindPicturesSwitch(box, plain, name).sync();
  assert.equal(box.checked, false);
  assert.equal(name.textContent, 'Off');

  const { n, storage, seen, pictures, get } = slowLink();
  await expectNoPicture(get, 'bafkslow1', 'bafkslow2', 'bafkslow3');
  assert.equal(seen.slows, 1);
  pictures.set(true);
  assert.equal(storage.get(PICTURES_KEY), 'on');
  await expectNoPicture(get, 'bafkslow4', 'bafkslow5');
  assert.equal(blobAsks(n).length, 5, 'fetches started again');
  assert.equal(seen.slows, 1, 'two more in a row are not three');
  assert.equal(await get('bafkok'), '1,1,1,1');
});

test('turning the pictures off and on starts the count of reads that ran out of time again', async () => {
  const { seen, pictures, get } = slowLink();
  await expectNoPicture(get, 'bafkslow1', 'bafkslow2');
  pictures.set(false);
  pictures.set(true);
  await expectNoPicture(get, 'bafkslow3', 'bafkslow4');
  assert.equal(seen.slows, 0);
});

test('the page turns the pictures off for the visit, shows that on the switch and says so in a notice when the reader finds the link too slow', () => {
  const main = readFileSync(new URL('main.js', wwwroot), 'utf8');
  assert.match(main, /switchedOff: \(\) => pictureReader\.switchedOff\(\)/);
  assert.match(main, /syncPicturesSwitch = picturesSwitch\.sync;/);
  assert.match(main, /tooSlow: \(\) => \{\s*pictures\.offForVisit\(\);\s*syncPicturesSwitch\(\);\s*showNotice\('Pictures are off because the connection is too slow for them\. [^']*pause menu[^']*'\);/);
});

test('shrinkPixels averages each block of the source, channel by channel', () => {
  const RED = 0xff0000, BLUE = 0x0000ff;
  // a 2x2 source: red on the left, blue on the right
  assert.deepEqual(shrinkPixels([RED, BLUE, RED, BLUE], 2, 2, 1), [RED, BLUE]);
  assert.deepEqual(shrinkPixels([RED, BLUE, RED, BLUE], 2, 1, 1), [0x800080]);
  assert.deepEqual(shrinkPixels([0x102030], 1, 1, 1), [0x102030]);
  assert.deepEqual(shrinkPixels(Array(16).fill(0x0a0b0c), 4, 3, 2), Array(6).fill(0x0a0b0c));
});

test('the pictures start on, or off on a thin connection, and a stored choice always wins', () => {
  for (const [stored, thin, on] of [[null, false, true], [null, true, false], ['on', true, true], ['on', false, true], ['off', false, false],
    ['off', true, false], ['maybe', false, true], ['maybe', true, false]]) {
    assert.equal(picturesStartOn(stored, thin), on, `${stored} ${thin}`);
    const storage = memory(stored == null ? {} : { [PICTURES_KEY]: stored });
    assert.equal(createPictures({ storage, thin, switchedOff: () => {} }).on, on, `${stored} ${thin}`);
  }
});

test('a change of the Pictures setting is stored as on or off, and switching off calls switchedOff once', () => {
  const storage = memory();
  let offs = 0;
  const pictures = createPictures({ storage, thin: false, switchedOff: () => offs++ });
  pictures.set(false);
  assert.equal(storage.get(PICTURES_KEY), 'off');
  assert.equal(offs, 1);
  pictures.set(false);
  assert.equal(offs, 1, 'no change, no call');
  pictures.set(true);
  assert.equal(storage.get(PICTURES_KEY), 'on');
  assert.equal(offs, 1);
});

test('with storage blocked the Pictures setting still starts and switches, for this visit only', () => {
  const blocked = { get() { throw new DOMException('denied', 'SecurityError'); }, set() { throw new DOMException('full', 'QuotaExceededError'); } };
  let offs = 0;
  const pictures = createPictures({ storage: blocked, thin: true, switchedOff: () => offs++ });
  assert.equal(pictures.on, false);
  pictures.set(true);
  assert.equal(pictures.on, true);
  pictures.set(false);
  assert.equal(offs, 1);
  const broken = { get() { throw new RangeError('a bug'); }, set() {} };
  assert.throws(() => createPictures({ storage: broken, thin: false, switchedOff: () => {} }), RangeError);
});

test('the Pictures switch opens on the setting, and a change is kept and named', () => {
  const listeners = {};
  const box = { checked: false, addEventListener: (type, fn) => { listeners[type] = fn; } };
  const name = { textContent: '' };
  const storage = memory();
  const pictures = createPictures({ storage, thin: false, switchedOff: () => {} });
  bindPicturesSwitch(box, pictures, name).sync();
  assert.equal(box.checked, true);
  assert.equal(name.textContent, 'On');
  box.checked = false;
  listeners.change();
  assert.equal(pictures.on, false);
  assert.equal(storage.get(PICTURES_KEY), 'off');
  assert.equal(name.textContent, 'Off');
});

test('the pause menu has the Pictures switch after feed speed, named for a screen reader', () => {
  const html = readFileSync(new URL('index.html', wwwroot), 'utf8');
  const speed = html.indexOf('id="menu-speed"'), pics = html.indexOf('id="menu-pictures"');
  assert.ok(speed > 0 && pics > speed, 'the Pictures fieldset follows feed speed');
  const field = html.slice(pics, html.indexOf('</fieldset>', pics));
  assert.match(field, /<legend>Pictures<\/legend>/);
  assert.match(field, /<input id="pictures" type="checkbox" role="switch" aria-label="Pictures" aria-describedby="pictures-note"/);
  assert.match(field, /id="pictures-note"/);
  const main = readFileSync(new URL('main.js', wwwroot), 'utf8');
  assert.match(main, /page\.bindPicturesSwitch\(\$\('pictures'\), pictures, \$\('pictures-name'\)\)/);
  assert.match(main, /picturesSwitch\.sync\(\)/);
});

test('thinConnection is true for data saver, a cellular connection, and slow-2g, 2g or 3g, and false otherwise', () => {
  const was = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  try {
    const under = (connection) => { Object.defineProperty(globalThis, 'navigator', { value: { connection }, configurable: true }); return thinConnection(); };
    for (const thin of [{ saveData: true }, { type: 'cellular' }, { type: 'cellular', effectiveType: '4g' }, { effectiveType: 'slow-2g' },
      { effectiveType: '2g' }, { effectiveType: '3g' }]) assert.equal(under(thin), true, JSON.stringify(thin));
    for (const fine of [{ saveData: false, effectiveType: '4g' }, { type: 'wifi', effectiveType: '4g' }, {}, undefined]) assert.equal(under(fine), false, JSON.stringify(fine));
    Object.defineProperty(globalThis, 'navigator', { value: undefined, configurable: true });
    assert.equal(thinConnection(), false, 'no navigator at all');
  } finally {
    if (was) Object.defineProperty(globalThis, 'navigator', was); else delete globalThis.navigator;
  }
});

test('rememberRecent keeps the newest entries and drops the oldest first', () => {
  const cache = new Map(), made = [];
  const get = (k) => rememberRecent(cache, k, () => { made.push(k); return k.toUpperCase(); }, 2);
  assert.equal(get('a'), 'A');
  get('b'); get('a');
  assert.deepEqual(made, ['a', 'b'], 'a kept entry is not made again');
  get('c');
  assert.deepEqual([...cache.keys()], ['b', 'c'], 'the oldest went');
});

test('pdsOrigin takes only a plain https host name and gives its origin', () => {
  // Built, not written out: a scanner for leaked secrets reads a literal address with a password in it as one.
  const withPassword = Object.assign(new URL('https://pds.example.com'), { username: 'user', password: 'word' }).href;
  const refused = ['http://pds.example.com', 'https://127.0.0.1', 'https://[::1]', 'https://localhost', 'https://printer.local',
    'https://pds.example.com:8443', 'https://pds.example.com/x', 'https://pds.example.com?x=1', 'https://pds.example.com#top',
    'https://user@pds.example.com', withPassword, 'https://10.0.0.5', 'https://nodots', 'not a url', 'https://',
    'https://a.localhost', 'https://box.internal', 'https://nas.lan', 'https://router.home', 'https://router.home.arpa', 'https://PRINTER.LOCAL',
    'https://box.localdomain', 'https://wiki.intranet', 'https://mail.corp', 'https://nas.private',
    'https://0x7f.0.0.1', 'https://under_score.example.com', 'javascript:alert(1)', undefined, null, 7];
  for (const bad of refused) assert.equal(pdsOrigin(bad), null, String(bad));
  assert.equal(pdsOrigin('https://pds.example.com'), 'https://pds.example.com');
  assert.equal(pdsOrigin('https://pds.example.com/'), 'https://pds.example.com');
  assert.equal(pdsOrigin('https://Morel.us-east.host.bsky.network'), 'https://morel.us-east.host.bsky.network');
});

test('a DID document naming a refused address gives no PDS, and nothing more is asked of anyone', async () => {
  for (const pds of ['https://127.0.0.1', 'https://printer.local', 'https://pds.example.com/xrpc?evil=1', 'https://pds.example.com:8443']) {
    const n = net({ pds, blobs: { bafkone: { size: 10 } } });
    const find = pdsFinder({ fetch: n.fetch });
    assert.equal(await find(DID), null, pds);
    assert.deepEqual(n.asked, [`https://plc.directory/${DID}`], pds);
  }
});

test('a did:web author is looked up on their own host only when it is a public name', async () => {
  const n = net();
  const find = pdsFinder({ fetch: n.fetch });
  const refused = ['did:web:localhost', 'did:web:10.0.0.5', 'did:web:127.0.0.0x1', 'did:web:10.0.0.0X5', 'did:web:192.168.1.0x', 'did:web:pds.example.com%3A8443',
    'did:web:pds.example.com:alice'];
  for (const did of refused) assert.equal(await find(did), null, did);
  assert.deepEqual(n.asked, []);
});

test('the DID document and the blob are asked for with redirects refused', async () => {
  const inits = [];
  const n = net({ blobs: { bafkone: { size: 10 } } });
  const fetch = (url, init) => { inits.push([new URL(url).host, init?.redirect]); return n.fetch(url, init); };
  const get = pictureGetter({ fetch, pdsOf: pdsFinder({ fetch }), decode: flat(1) });
  assert.equal(await pdsFinder({ fetch })('did:web:pds.example.com'), null);
  assert.equal(await get(DID, thumb('bafkone'), 2, 2), '1,1,1,1');
  assert.deepEqual(inits, [['pds.example.com', 'error'], ['plc.directory', 'error'], ['pds.example.com', 'error']]);
});

test('a refused redirect gives no picture, and nothing more is asked of anyone', async () => {
  // fetch rejects with a TypeError when it meets a redirect it was told to refuse
  const refused = () => Promise.reject(new TypeError('Failed to fetch'));
  const toDoc = [];
  const docRefused = (url) => { toDoc.push(String(url)); return refused(); };
  const noDoc = pictureGetter({ fetch: docRefused, pdsOf: pdsFinder({ fetch: docRefused }), decode: flat(1) });
  assert.equal(await noDoc('did:web:pds.example.com', thumb('bafkone', 'did:web:pds.example.com'), 2, 2), null);
  assert.deepEqual(toDoc, ['https://pds.example.com/.well-known/did.json']);
  const n = net({ blobs: { bafkone: new TypeError('Failed to fetch') } });
  const noBlob = pictureGetter({ fetch: n.fetch, pdsOf: pdsFinder({ fetch: n.fetch }), decode: flat(1) });
  assert.equal(await noBlob(DID, thumb('bafkone'), 2, 2), null);
  assert.deepEqual(n.asked, [`https://plc.directory/${DID}`, `${PDS}/xrpc/com.atproto.sync.getBlob?did=${encodeURIComponent(DID)}&cid=bafkone`]);
});

test('the PDS lookups are kept for only so many authors', async () => {
  const n = net();
  const find = pdsFinder({ fetch: n.fetch, kept: 2 });
  for (const id of ['one', 'two', 'three', 'one']) await find(`did:plc:${id}`);
  assert.equal(n.asked.length, 4, 'the first author was asked again after it was pushed out');
});
