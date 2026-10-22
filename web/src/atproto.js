// ATProto OAuth login and the few XRPC calls the game needs. Everything crosses to .NET as JSON strings.
import { BrowserOAuthClient } from '@atproto/oauth-client-browser';
import { buildAtprotoLoopbackClientId } from '@atproto/oauth-types';
import { Agent } from '@atproto/api';
import scopes from '../scope.json';
import { metadataUrl, signIn as signInWith } from '../../src/FeedRaider.Web/wwwroot/page.js';

const PUBLIC_API = 'https://public.api.bsky.app';
const APPVIEW = 'did:web:api.bsky.app#bsky_appview';
const isLoopback = ['127.0.0.1', '[::1]', 'localhost'].includes(location.hostname);

let client, agent, reader, did = null, handle = null, returnSearch = null;

// ?scope=transition requests atproto transition:generic instead of the granular scopes (loopback only; Pages is fixed by client-metadata.json).
export function requestedScope() {
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
      handleResolver: 'https://bsky.social',
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
  return new BrowserOAuthClient({ handleResolver: 'https://bsky.social', clientMetadata: await res.json() });
}

async function adopt(session) {
  did = session.did;
  // Writes go straight to the user's PDS. Only reads carry the appview proxy header: with it on a write, the PDS would forward
  // createRecord to the appview instead of saving the like.
  agent = new Agent(session);
  reader = agent.withProxy('bsky_appview', APPVIEW.split('#')[0]);
  try {
    const r = await fetch(`${PUBLIC_API}/xrpc/app.bsky.actor.getProfile?actor=${encodeURIComponent(did)}`);
    handle = r.ok ? (await r.json()).handle : did;
  } catch { handle = did; }
}

// Restores a saved session or finishes the redirect callback. Returns the session JSON, or null when logged out.
export async function init() {
  client = await makeClient();
  const result = await client.init();
  if (result?.session) {
    await adopt(result.session);
    // a fresh login carries the query string we left with (?debug=feed, ?handle=...), so the page can go back to it
    if (typeof result.state === 'string' && result.state.startsWith('?')) returnSearch = result.state;
  }
  return session();
}

// The query string the user had before the login redirect, or null when this load was not a login callback.
export function returnTo() {
  return returnSearch;
}

export function session() {
  return did ? JSON.stringify({ did, handle }) : null;
}

// An empty input goes to bsky.social itself: its own page offers the account picker or the remembered session.
export async function login(input) {
  const h = input.trim().replace(/^@/, '') || 'https://bsky.social';
  await client.signInRedirect(h, { scope: client.clientMetadata.scope, state: location.search });
}

// The start page's sign-in: the popup (the page and the runtime download stay put), the redirect where a popup cannot work.
// Resolves the session JSON once the popup finishes; on the redirect path the page leaves and this never returns.
export async function signIn(input) {
  const out = await signInWith(client, input, { scope: client.clientMetadata.scope, state: location.search, standalone: navigator.standalone === true });
  if (out.via === 'popup') await adopt(out.session);
  return session();
}

export async function logout() {
  if (client && did) await client.revoke(did);
  did = handle = agent = reader = null;
}

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

export async function like(uri, cid) {
  const res = await need().app.bsky.feed.like.create({ repo: did }, { subject: { uri, cid }, createdAt: new Date().toISOString() });
  return JSON.stringify({ uri: res.uri, cid: res.cid });
}
