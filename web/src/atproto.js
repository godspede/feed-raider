// ATProto OAuth sign-in and the few XRPC calls the game needs. Everything crosses to .NET as JSON strings.
import { BrowserOAuthClient } from '@atproto/oauth-client-browser';
import { buildAtprotoLoopbackClientId } from '@atproto/oauth-types';
import { Agent } from '@atproto/api';
import scopes from '../scope.json';
import { metadataUrl, signIn as signInWith, signInFailed, popupKey, completeCallback, noteSignIn, clearSignIn, restoreSaved, signInLost, callbackNote, PUBLIC_API, ENTRYWAY } from '../../src/FeedRaider.Web/wwwroot/page.js';

const APPVIEW_DID = 'did:web:api.bsky.app', APPVIEW_SERVICE = 'bsky_appview';
const isLoopback = ['127.0.0.1', '[::1]', 'localhost'].includes(location.hostname);

let client, agent, reader, did = null, handle = null, granted = null, returnSearch = null, failure = null, note = null;
// Reading localStorage throws when the browser blocks storage (private mode, cookies off). The page then runs without it: callers take null as "no storage" and skip saving the sign-in report.
const storage = () => { try { return globalThis.localStorage ?? null; } catch { return null; } };

// ?scope=transition requests atproto transition:generic instead of the granular scopes (loopback only; Pages is fixed by client-metadata.json).
function requestedScope() {
  const pick = new URLSearchParams(location.search).get('scope');
  return pick === 'transition' ? scopes.transition : scopes.granular;
}

async function makeClient() {
  if (isLoopback) {
    // Loopback development client: client_id is http://localhost?redirect_uri=...&scope=..., the redirect is our own page.
    const redirect = new URL(location.href);
    redirect.hostname = redirect.hostname === 'localhost' ? '127.0.0.1' : redirect.hostname;
    redirect.search = '';
    redirect.hash = '';
    const clientId = buildAtprotoLoopbackClientId({ scope: requestedScope(), redirect_uris: [redirect.href] });
    return new BrowserOAuthClient({
      handleResolver: ENTRYWAY,
      clientMetadata: {
        client_id: clientId,
        redirect_uris: [redirect.href],
        scope: requestedScope(),
        response_types: ['code'],
        grant_types: ['authorization_code', 'refresh_token'],
        token_endpoint_auth_method: 'none',
        application_type: 'native',
        dpop_bound_access_tokens: true,
      },
    });
  }
  const res = await fetch(metadataUrl(location.href));
  if (!res.ok) throw new Error('client-metadata.json not found beside index.html');
  return new BrowserOAuthClient({ handleResolver: ENTRYWAY, clientMetadata: await res.json() });
}

async function adopt(session) {
  did = session.did;
  // kept fresh by every page that shows the account, so a later load finds it even when the library forgot it
  noteSignIn(storage(), 'restored', { sub: did });
  // Writes go straight to the user's PDS. Only reads carry the appview proxy header: with it on a write, the PDS would forward
  // createRecord to the appview instead of saving the like.
  agent = new Agent(session);
  // what this sign-in was granted: the page turns it into what the player may do. Null when the token set carries no scope.
  granted = (await session.getTokenInfo(false)).scope ?? null;
  reader = agent.withProxy(APPVIEW_SERVICE, APPVIEW_DID);
  try {
    const r = await fetch(`${PUBLIC_API}/xrpc/app.bsky.actor.getProfile?actor=${encodeURIComponent(did)}`);
    // the handle is only shown; the DID stands in for it when the lookup is refused, and the refusal is logged
    if (!r.ok) console.warn(`profile lookup answered ${r.status}, showing the DID as the handle`);
    handle = r.ok ? (await r.json()).handle : did;
  } catch (e) {
    if (!(e instanceof TypeError)) throw e;
    console.warn('profile lookup failed, showing the DID as the handle: ' + e.message);
    handle = did;
  }
}

// The saved session, with the sign-in report and why its account's restore failed (see restoreSaved).
async function restoreOutcome() {
  const out = await restoreSaved({ initRestore: () => client.initRestore(false), restore: (sub) => client.restore(sub, false), storage: storage() });
  if (out.session && out.report) console.info(`restored ${out.report.sub} from the sign-in report: the library had no current account`);
  return out;
}

// Restores a saved session, or finishes the callback of a redirect or a popup. Returns the session JSON, or null when signed out.
// Only a client that cannot be made throws: a failed callback or restore leaves the page signed out but able to sign in again,
// and initError() says why, as it does for a recent sign-in that left no session.
export async function init() {
  client = await makeClient();
  try {
    const params = client.readCallbackParams();
    const redirectUri = params && client.findRedirectUrl();
    if (redirectUri) await finishCallback(params, redirectUri);
    else {
      const out = await restoreOutcome();
      if (out.session) await adopt(out.session);
      else if (out.report) {
        // said once: the next load starts clean
        failure = signInLost(out.report, out.error);
        clearSignIn(storage());
        console.warn(`no session after the sign-in that reached "${out.report.step}" at ${new Date(out.report.at).toISOString()}: ${failure}`);
      }
    }
  } catch (e) {
    failure = signInFailed(e);
    console.warn('sign-in failed: ' + (e?.message || e));
  }
  return session();
}

// The callback, in the page the authorization server sent back to: a full-page redirect, or the popup, which may have lost its
// opener on the way. The session is saved as this browser's either way, so the opener and any reload restore it. The popup
// hands the result, or why any step failed, to its opener and closes once the opener has it; with no opener listening it stays
// as a signed-in page (or shows why it is not).
async function finishCallback(params, redirectUri) {
  history.replaceState(null, '', location.pathname + location.search); // a reload must not replay the code
  let sub;
  const result = await completeCallback({
    callback: async () => {
      const r = await client.callback(params, { redirect_uri: redirectUri });
      sub = r.session.sub;
      return r;
    },
    adopt: async () => { await adopt(await client.restore(sub, false)); return did; },
    report: (step, extra) => noteSignIn(storage(), step, extra),
  });
  note = callbackNote({ popup: !!popupKey(result.state), handle });
  if (!popupKey(result.state) && typeof result.state === 'string' && result.state.startsWith('?')) {
    // a fresh sign-in carries the query string we left with (?debug=feed, ?handle=...), so the page can go back to it
    returnSearch = result.state;
  }
}

// Why the last callback or restore failed, as the start page's line, or null.
export function initError() {
  return failure;
}

// What the callback page says once it is done (a popup that saved the session), or null.
export function initNote() {
  return note;
}

// The query string the user had before the sign-in redirect, or null when this load was not a sign-in callback.
export function returnTo() {
  return returnSearch;
}

export function session() {
  return did ? JSON.stringify({ did, handle, scope: granted }) : null;
}

// An empty input goes to bsky.social itself: its own page offers the account picker or the remembered session.
export async function signInRedirect(input) {
  const h = input.trim().replace(/^@/, '') || ENTRYWAY;
  await client.signInRedirect(h, { scope: client.clientMetadata.scope, state: location.search });
}

// The start page's sign-in: the popup (the page and the runtime download stay put), the redirect where a popup cannot work.
// Resolves the session JSON once the popup's callback has saved a session; on the redirect path the page leaves and this never
// returns. `onWaiting` runs once the popup shows the authorization page; `signal` gives up on it.
export async function signIn(input, { onWaiting, signal } = {}) {
  noteSignIn(storage(), 'started');
  const restore = async () => (await restoreOutcome()).session;
  const out = await signInWith(client, input, { scope: client.clientMetadata.scope, state: location.search, standalone: navigator.standalone === true, restore, onWaiting, signal });
  if (out.via === 'popup') await adopt(out.session);
  failure = null;
  return session();
}

// A session another page of the site saved since this one loaded (a popup whose sign-in this page stopped waiting for), adopted:
// the session JSON, or null when there is none.
export async function adoptSaved() {
  const { session: saved } = await restoreOutcome();
  if (!saved) return null;
  await adopt(saved);
  failure = null;
  return session();
}

export async function signOut() {
  clearSignIn(storage());
  if (client && did) await client.revoke(did);
  did = handle = agent = reader = granted = null;
}

// The same words @atproto/api throws with no session, which HostBridge's Plain reads as "signed out".
const need = () => { if (!agent) throw new Error('not logged in'); return agent; };
const needReader = () => { if (!reader) throw new Error('not logged in'); return reader; };

export async function getFeed(feed, cursor, limit) {
  const res = await needReader().app.bsky.feed.getFeed({ feed, limit, cursor: cursor || undefined });
  return JSON.stringify(res.data);
}

// The signed-in player's Following timeline.
export async function getTimeline(cursor, limit) {
  const res = await needReader().app.bsky.feed.getTimeline({ limit, cursor: cursor || undefined });
  return JSON.stringify(res.data);
}

// One post's thread, as deep as asked. The game asks when a hold finishes a like, with depth 1 and no parents, and reads
// the author's own next reply out of the answer.
export async function getPostThread(uri, depth, parentHeight) {
  const res = await needReader().app.bsky.feed.getPostThread({ uri, depth, parentHeight });
  return JSON.stringify(res.data);
}

export async function like(uri, cid) {
  const res = await need().app.bsky.feed.like.create({ repo: did }, { subject: { uri, cid }, createdAt: new Date().toISOString() });
  return JSON.stringify({ uri: res.uri, cid: res.cid });
}

// A refusal for missing scope is the player's cue to sign in again, in those words; anything else passes through as it came.
async function write(call) {
  try {
    return await call();
  } catch (e) {
    const m = `${e?.error ?? ''} ${e?.message ?? ''}`;
    if (e?.status === 403 || /scope|insufficient/i.test(m)) throw new Error('sign in again: this sign-in does not allow that', { cause: e });
    throw e;
  }
}

export async function repost(uri, cid) {
  return write(async () => {
    const res = await need().app.bsky.feed.repost.create({ repo: did }, { subject: { uri, cid }, createdAt: new Date().toISOString() });
    return JSON.stringify({ uri: res.uri, cid: res.cid });
  });
}

// Deletes one of the player's own like or repost records, named by its address (at://did/collection/rkey) and nothing else.
export async function remove(recordUri) {
  const m = /^at:\/\/([^/]+)\/(app\.bsky\.feed\.(?:like|repost))\/([^/]+)$/.exec(recordUri);
  if (!m || m[1] !== did) throw new Error('not one of your likes or reposts');
  await write(() => need().com.atproto.repo.deleteRecord({ repo: did, collection: m[2], rkey: m[3] }));
}
