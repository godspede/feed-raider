// Usage: node make-client-metadata.mjs <site origin and path, e.g. https://owner.github.io/feed-raider> <out file>
// Writes the ATProto OAuth client metadata document that is served beside index.html on GitHub Pages.
import { readFileSync, writeFileSync } from 'node:fs';

const [site, out] = process.argv.slice(2);
if (!site || !out) throw new Error('usage: make-client-metadata.mjs <site-url> <out-file>');
const base = `${site.replace(/\/+$/, '')}/`;
const { granular } = JSON.parse(readFileSync(new URL('./scope.json', import.meta.url), 'utf8'));

writeFileSync(out, JSON.stringify({
  client_id: base + 'client-metadata.json',
  client_name: 'feed-raider',
  client_uri: base,
  redirect_uris: [base],
  scope: granular,
  grant_types: ['authorization_code', 'refresh_token'],
  response_types: ['code'],
  token_endpoint_auth_method: 'none',
  application_type: 'web',
  dpop_bound_access_tokens: true,
}, null, 2) + '\n');
