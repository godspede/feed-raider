// A stand-in for bsky.social, for the popup sign-in check in Firefox: one origin is the account's PDS, its authorization server and
// its did:web host. Its authorization page sends Cross-Origin-Opener-Policy: same-origin like the real one, so the popup is cut off
// from the page that opened it, and answers in the fragment after the "user" presses Sign in. Nothing is checked: any request
// gets a code, any code gets a token. Never point it at anything real.
import { createServer } from 'node:http';
import { randomBytes } from 'node:crypto';

export function startMockAs({ port = 0 } = {}) {
  const requests = new Map(); // request_uri -> PAR parameters
  const codes = new Map(); // code -> PAR parameters
  const log = [];
  let origin, did;
  const cors = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type, DPoP, Authorization',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Expose-Headers': 'DPoP-Nonce, WWW-Authenticate',
  };
  const json = (res, status, body, headers = {}) => {
    res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store', ...cors, ...headers });
    res.end(JSON.stringify(body));
  };
  const readForm = (req) => new Promise((resolve) => {
    let body = '';
    req.on('data', (c) => { body += c; });
    req.on('end', () => resolve(Object.fromEntries(new URLSearchParams(body))));
  });
  const server = createServer(async (req, res) => {
    const url = new URL(req.url, origin);
    log.push(`${req.method} ${url.pathname}`);
    if (req.method === 'OPTIONS') { res.writeHead(204, cors); return res.end(); }
    switch (url.pathname) {
      case '/.well-known/did.json':
        return json(res, 200, {
          '@context': ['https://www.w3.org/ns/did/v1'],
          id: did,
          alsoKnownAs: ['at://tester.localhost'],
          service: [{ id: '#atproto_pds', type: 'AtprotoPersonalDataServer', serviceEndpoint: origin }],
        });
      case '/.well-known/oauth-protected-resource':
        return json(res, 200, { resource: origin, authorization_servers: [origin], scopes_supported: [], bearer_methods_supported: ['header'] });
      case '/.well-known/oauth-authorization-server':
        return json(res, 200, {
          issuer: origin,
          authorization_endpoint: `${origin}/oauth/authorize`,
          token_endpoint: `${origin}/oauth/token`,
          pushed_authorization_request_endpoint: `${origin}/oauth/par`,
          revocation_endpoint: `${origin}/oauth/revoke`,
          require_pushed_authorization_requests: true,
          response_types_supported: ['code'],
          response_modes_supported: ['query', 'fragment', 'form_post'],
          grant_types_supported: ['authorization_code', 'refresh_token'],
          code_challenge_methods_supported: ['S256'],
          token_endpoint_auth_methods_supported: ['none', 'private_key_jwt'],
          token_endpoint_auth_signing_alg_values_supported: ['ES256'],
          dpop_signing_alg_values_supported: ['ES256'],
          scopes_supported: ['atproto', 'transition:generic'],
          authorization_response_iss_parameter_supported: true,
          client_id_metadata_document_supported: true,
          display_values_supported: ['page', 'popup', 'touch'],
        });
      case '/oauth/par': {
        const params = await readForm(req);
        const requestUri = 'urn:ietf:params:oauth:request_uri:' + randomBytes(8).toString('hex');
        requests.set(requestUri, params);
        return json(res, 201, { request_uri: requestUri, expires_in: 300 });
      }
      case '/oauth/authorize': {
        const params = requests.get(url.searchParams.get('request_uri'));
        if (!params) { res.writeHead(400); return res.end('unknown request_uri'); }
        const code = randomBytes(8).toString('hex');
        codes.set(code, params);
        const answer = new URLSearchParams({ iss: origin, state: params.state, code });
        const back = `${params.redirect_uri}#${answer}`;
        res.writeHead(200, { 'Content-Type': 'text/html', 'Cross-Origin-Opener-Policy': 'same-origin', 'Cache-Control': 'no-store' });
        // like bsky.social's page: once the user is through, it replaces itself with the redirect
        return res.end(`<!doctype html><title>mock sign-in</title><p>display=${params.display}</p>
<button id="go">Sign in</button>
<script>document.getElementById('go').onclick = () => location.replace(${JSON.stringify(back).replace(/</g, '\\u003c')});</script>`);
      }
      case '/oauth/token': {
        const form = await readForm(req);
        if (form.grant_type === 'authorization_code' && !codes.delete(form.code)) return json(res, 400, { error: 'invalid_grant' });
        return json(res, 200, {
          access_token: 'at-' + randomBytes(8).toString('hex'),
          token_type: 'DPoP',
          refresh_token: 'rt-' + randomBytes(8).toString('hex'),
          expires_in: 3600,
          scope: 'atproto',
          sub: did,
        });
      }
      case '/oauth/revoke':
        return json(res, 200, {});
      default:
        res.writeHead(404, cors);
        return res.end();
    }
  });
  return new Promise((resolve) => {
    server.listen(port, 'localhost', () => {
      const p = server.address().port;
      origin = `http://localhost:${p}`;
      did = `did:web:localhost%3A${p}`;
      resolve({ origin, did, log, close: () => new Promise((r) => { server.closeAllConnections?.(); server.close(r); }) });
    });
  });
}
