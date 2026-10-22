// The e2e bundle's OAuth client: the shipped one, allowed to use the http mock authorization server (test only).
import { BrowserOAuthClient as Shipped } from '@atproto/oauth-client-browser';

export * from '@atproto/oauth-client-browser';
export class BrowserOAuthClient extends Shipped {
  constructor(options) { super({ ...options, allowHttp: true }); }
}
