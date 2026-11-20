// Browser glue: xterm.js on one side, the .NET engine (via JS interop) on the other.
// All URLs are relative so the page keeps working at a root or a subpath.
// Single-file build seam: web/bundle-single-file.mjs sets globalThis.__frBundle (its keys are set there, in the loader it writes into
// the page) before loading this file from memory. It stands in for the sibling files: each `frBundle.` read below takes a module's
// blob: URL, the music's track list or the runtime's resource loader from it. Without it (the normal site) everything loads from
// sibling files as usual.
const frBundle = globalThis.__frBundle;
const page = await import(frBundle ? frBundle.pageUrl : './page.js');

const params = new URLSearchParams(location.search);
const handle = (params.get('handle') || '').trim().replace(/^@/, '');
const debug = params.get('debug');
const spectatorMode = (params.get('mode') || '').toLowerCase() === 'spectator';
// body.game and body.debug are set before first paint, by the inline script at the top of index.html's body
if (spectatorMode) {
  document.querySelector('header form').insertAdjacentHTML('beforeend', '<input type="hidden" name="mode" value="spectator">');
  // the game keeps the sound controls in its pause menu; the spectator view has no menu, so they go in its header
  document.getElementById('right').prepend(document.getElementById('vol'), document.getElementById('mute'));
}
// ?debug=feed keeps the header bar for its own sign-in. It is a test page, not the game: the start page never shows over it
// (CSS hides it on body.debug), nothing behind it is made inert, and no run or start page tour starts.
const debugFeed = debug === 'feed';
const darknessParam = (params.get('darkness') || 'on').toLowerCase();
const errorBarEl = document.getElementById('error');
// The game's messages (the error bar, the terminal's toasts) are drawn where a screen reader cannot read them, so each one is also
// written to a visually hidden status element. The spectator view has no toasts and keeps its error bar as the alert.
const announceBox = document.getElementById('announce');
let logList = null;          // the pause menu's Feed log (page.createLogList), once the pause menu is built; declared up here because announce reads it
const ANNOUNCE_GAP_MS = 50;   // long enough for the screen reader to see the box empty before it is filled again
const announce = (message) => {
  // while the pause menu is open its Feed log speaks the answer too, and rebuilds its list
  if (logList && document.getElementById('menu')?.open) logList.said(message);
  announceBox.textContent = '';
  // an empty node first, so the same sentence twice in a row is announced twice
  setTimeout(() => { announceBox.textContent = message; }, ANNOUNCE_GAP_MS);
};
if (!spectatorMode) errorBarEl.removeAttribute('role');
const bar = page.createErrorBar({ el: errorBarEl, announce: (m) => { if (!spectatorMode) announce(m); } });
const showError = bar.sticky;
const showNotice = bar.notice;

// The start page is plain HTML and CSS, painted before any script runs. This wires it up while the runtime still downloads:
// the game starts once the runtime is ready and a way in is picked (a typed handle, Random, or a live sign-in session).
let queued = null; // "play", "guest" or "random" once a start was pressed before the runtime was ready
const savedKey = 'feed-raider.handle';
const $ = (id) => document.getElementById(id);
const startPage = $('start');
const startHandle = $('start-handle');
const isRandomFeed = (p) => (p.get('feed') || '').trim().toLowerCase() === 'random';
const randomFromUrl = isRandomFeed(params);
const needsStart = !spectatorMode && !handle && !randomFromUrl && !debugFeed;
const finePointer = matchMedia('(pointer: fine)').matches;
// ?handle= or ?feed=random in the URL skips the start page's buttons: only the title and the load bar show until the game starts
if (!needsStart) startPage.classList.add('booting');
else startHandle.value = savedHandle();
// The start page covers the whole window. What lies behind it (the header, the terminal and xterm's helper textarea, which
// would take Tab and keep it) is inert until the game starts, so Tab walks the cards, the handle box and the buttons.
const behindStart = spectatorMode || debugFeed ? [] : [...document.body.children].filter((n) => n !== startPage && n.tagName !== 'SCRIPT');
for (const n of behindStart) n.inert = true;

const NARROW_SCREEN_PX = 600, FONT_PX_NARROW = 12, FONT_PX_WIDE = 14;   // a phone-width page gets the smaller type so the field fits
const SPECTATOR_SCROLLBACK_LINES = 1000;   // the spectator view keeps history to scroll back through
const TERM_BACKGROUND = '#050711', TERM_FOREGROUND = '#f1f3f5';
// xterm control sequences: hide the cursor, clear the screen with the cursor home, a khaki hint colour, and back to plain text
const HIDE_CURSOR = '\x1b[?25l', CLEAR_SCREEN = '\x1b[H\x1b[2J', HINT_COLOUR = '\x1b[38;2;216;203;163m', PLAIN_TEXT = '\x1b[0m';
const term = new Terminal({
  convertEol: false,
  cursorBlink: false,
  disableStdin: true,
  fontFamily: 'ui-monospace, "Cascadia Mono", Menlo, Consolas, monospace',
  fontSize: !spectatorMode && window.innerWidth < NARROW_SCREEN_PX ? FONT_PX_NARROW : FONT_PX_WIDE,
  customGlyphs: true,
  scrollback: spectatorMode ? SPECTATOR_SCROLLBACK_LINES : 0, // the game redraws in place; history is dead memory
  theme: { background: TERM_BACKGROUND, foreground: TERM_FOREGROUND, selectionBackground: 'transparent' },
});
const fit = new FitAddon.FitAddon();
term.loadAddon(fit);
term.open(document.getElementById('term'));
// Tab belongs to the page, not the terminal: no game control uses it, so xterm lets it through and focus can leave the terminal.
// While playing, the helper textarea is also taken out of the tab order (the game reads its keys at window level), so Tab goes
// from the page to the gear and the side panel's link without stopping there. The spectator view keeps it a normal stop.
term.attachCustomKeyEventHandler((e) => e.key !== 'Tab');
const helperTextarea = term.textarea;
term.write(HIDE_CURSOR);
fit.fit();

// The default DOM renderer rebuilds spans for every changed cell, which is the slow part on a weak phone or old laptop. The
// WebGL addon paints the same cells on the GPU. It is served beside xterm (xterm/, or from memory in the single-file build) and
// is loaded in the background: with no WebGL, a file that fails to load or a lost context the terminal keeps the DOM renderer.
// ?renderer=dom forces the DOM renderer.
// Before the glyph atlas fills to where the addon merges pages and starts drawing stale glyphs (page.watchAtlas), the atlas is
// started afresh in place: flipping an option the game never shows (it draws only true colour, never bold) makes xterm drop its
// atlas and make a one-page one in the same addon. The GL context stays, so there is no new-context hitch.
const loadWebgl = () => {
  try {
    const gl = new WebglAddon.WebglAddon();
    gl.onContextLoss(() => gl.dispose());
    // The addon raises its atlas events from inside a draw, while it is placing a glyph on a page, and xterm drops the atlas the
    // moment the option changes: the timer lets that draw finish first.
    page.watchAtlas(gl, () => setTimeout(() => {
      term.options.drawBoldTextInBrightColors = !term.options.drawBoldTextInBrightColors;
    }));
    term.loadAddon(gl);
  } catch (e) {
    // a browser without WebGL2 throws here; the DOM renderer already draws every cell, so the game carries on unchanged
    console.warn('webgl renderer unavailable, using the DOM renderer: ' + (e.message || e));
  }
};
if (params.get('renderer') !== 'dom') {
  const tag = document.createElement('script');
  tag.src = frBundle ? frBundle.webglUrl : './xterm/addon-webgl.js';
  tag.onload = loadWebgl;
  tag.onerror = () => console.warn('webgl addon failed to load, using the DOM renderer');
  document.head.appendChild(tag);
}

// Sign-in (ATProto OAuth) needs crypto.subtle, which browsers expose only on HTTPS or localhost. The bundle loads only there, so a
// plain-HTTP page still runs the game. A bundle or client that fails to load leaves sign-in off, not an error bar; a callback or
// restore that fails leaves sign-in on and says why on the start page.
// It starts at once and runs beside the runtime download, so sign-in works while the game loads.
let auth = null;
let authPending = window.isSecureContext;
// the page we land on after the OAuth redirect (or in the popup; the answer comes in the fragment): the runtime waits for the
// callback to be handled first
const oauthCallback = page.isOAuthCallback(location.href);
const authReady = (async () => {
  if (!window.isSecureContext) return;
  try {
    const mod = await import(frBundle ? frBundle.atprotoUrl : './atproto.js');
    await mod.init();
    auth = mod;
  } catch (e) {
    console.warn('sign-in unavailable: ' + (e.message || e));
  }
})();
const liveAuth = () => {
  if (!auth) throw new Error('sign-in unavailable on this page');
  return auth;
};

// The runtime downloads in the background; .NET reports resources loaded of total, which drives the start page's bar.
// ?handle= does not wait for the click, and the popup/redirect callback page waits for auth, which may close it.
let loadState = { status: 'loading', percent: null };
let renderStartPage = () => {};
const setLoad = (status, percent = null) => {
  loadState = { status, percent };
  renderStartPage();
};
const runtimeReady = (async () => {
  if (oauthCallback) await authReady;
  const { dotnet } = await import(frBundle ? frBundle.dotnetUrl : './_framework/dotnet.js');
  let builder = dotnet;
  try {
    builder = builder.withModuleConfig({ onDownloadResourceProgress: (loaded, total) => setLoad('loading', page.loadPercent(loaded, total)) }) || builder;
  } catch (e) {
    console.warn('no download progress, the bar stays indeterminate: ' + (e.message || e));
  }
  if (frBundle) builder = builder.withResourceLoader(frBundle.loadBootResource) || builder;
  return builder.create();
})();
runtimeReady.catch((e) => {
  console.error('runtime failed to load', e);
  queued = null;
  setLoad('failed');
});


// Music: ?music=off disables it. The single-file bundle carries a track list ([{role, url}], frBundle.music, null without
// --music); the normal site lists its tracks in music/tracks.json next to the page. A missing or broken player leaves the game silent.
// Sound effects (sfx.js, synthesised, no files): ?sfx=off disables them. They share music.js's master gain, mute and volume,
// so music.js loads (and installs the controls) whenever either is on.
let music = null, sfx = null;
const musicWanted = (params.get('music') || '').toLowerCase() !== 'off', sfxWanted = (params.get('sfx') || '').toLowerCase() !== 'off';
// It loads beside the runtime download; the game awaits it before it starts.
const audioReady = (async () => {
  if (!musicWanted && !sfxWanted) return;
  try {
    const opts = !musicWanted ? {} : frBundle ? { tracks: frBundle.music } : { manifestUrl: 'music/tracks.json' };
    const mod = await import(frBundle ? frBundle.musicUrl : './music.js');
    if (mod.init({ ...opts, sfx: sfxWanted, thin: page.thinConnection })) music = mod;
    if (sfxWanted) {
      const s = await import(frBundle ? frBundle.sfxUrl : './sfx.js');
      if (s.init({ master: mod.master, duck: mod.duck })) sfx = s;
    }
  } catch (e) {
    console.warn('audio unavailable: ' + (e.message || e));
  }
})();
const musicCue = (name) => { try { music?.cue(name); } catch { /* audio is best effort: a failed cue must not stop the game */ } };
const sfxPlay = (name, pan, step, size) => { try { sfx?.play(name, pan, step, size); } catch { /* audio is best effort: a failed effect must not stop the game */ } };
const bestKey = 'feed-raider.best';
// localStorage throws a DOMException where the browser blocks it (or, on a save, where it is full)
const bestScore = () => {
  try { return Number.parseInt(localStorage.getItem(bestKey), 10) || 0; }
  catch (e) { if (!(e instanceof DOMException)) throw e; return 0; /* storage unavailable: no best score kept */ }
};
const saveBestScore = (n) => {
  try { localStorage.setItem(bestKey, String(n)); }
  catch (e) { if (!(e instanceof DOMException)) throw e; /* storage unavailable: the score is just not kept */ }
};
// Function declarations, not consts: the start page reads the saved handle at load, above this line
function savedHandle() {
  try { return localStorage.getItem(savedKey) || ''; }
  catch (e) { if (!(e instanceof DOMException)) throw e; return ''; /* storage unavailable: no handle kept, the box starts empty */ }
}
function saveHandle(h) {
  try { localStorage.setItem(savedKey, h); }
  catch (e) { if (!(e instanceof DOMException)) throw e; /* storage unavailable: the handle is just not kept */ }
}
if (needsStart) audioReady.then(() => musicCue('start'));

// Frames the terminal has not drawn yet; the game skips drawing while the terminal is behind.
let pendingWrites = 0;
// one frame may be drawing while the game builds the next; any more and the terminal is falling behind
const MAX_PENDING_WRITES = 1;
// The settings the page keeps in localStorage, which throws a DOMException where the browser blocks it.
const pageStorage = { get: (k) => localStorage.getItem(k), set: (k, v) => localStorage.setItem(k, v) };

// Avatars and a post's pictures, from the author's own PDS (Bluesky's CDN sends no CORS headers, so a page cannot read its
// pixels), a few at a time, with a size and a time limit, and only while the Pictures setting is on; see page.pictureReader. A
// picture that is not fetched leaves the game's tinted placeholder or its empty box.
const RGBA_BYTES = 4, RED_SHIFT = 16, GREEN_SHIFT = 8;
const packPixels = (px, n) => { const out = []; for (let i = 0; i < n; i++) out.push((px[i * RGBA_BYTES] << RED_SHIFT) | (px[i * RGBA_BYTES + 1] << GREEN_SHIFT) | px[i * RGBA_BYTES + 2]); return out; };
let syncPicturesSwitch = () => {};   // shows the setting on the pause menu's Pictures switch; installMenu fills it in
const pictures = page.createPictures({ storage: pageStorage, thin: page.thinConnection(), switchedOff: () => pictureReader.switchedOff() });
const pictureReader = page.pictureReader({
  fetch,
  pdsOf: page.pdsFinder({ fetch }),
  decode: async (blob, w, h) => {
    // decoded straight to the size asked for, so a large photo is never held at full size
    const bmp = await createImageBitmap(blob, { resizeWidth: w, resizeHeight: h, resizeQuality: 'medium' });
    const x = new OffscreenCanvas(w, h).getContext('2d', { willReadFrequently: true });
    x.drawImage(bmp, 0, 0);
    bmp.close();
    return packPixels(x.getImageData(0, 0, w, h).data, w * h);
  },
  wanted: () => pictures.on,
  tooSlow: () => {
    pictures.offForVisit();
    syncPicturesSwitch();
    showNotice('Pictures are off because the connection is too slow for them. The Pictures switch in the pause menu turns them back on.');
  },
});

// --- start page: sign in, play as someone (a typed handle) or play Random; a start pressed before the runtime is ready is queued ---
// resolves { handle, feed }; armed again for each run, so the start page can pick anew after Back to start page
let pickHandle, handlePicked;
const armPick = () => { handlePicked = new Promise((resolve) => { pickHandle = resolve; }); };
armPick();
let busy = false;
const startError = $('start-error');
// the handle box's suggestion list; it opens under the box and would cover the error bar
let closeSuggestions = () => {};
const startFail = (message) => {
  closeSuggestions();
  startError.textContent = message;
  startError.style.display = 'block';
};
// the callback page's own line: wait while it finishes, then whether it saved the session
// The note shows, in this order: what is in flight (checking, signing in), the typeahead's line, the callback page's line.
const startNote = $('start-note');
let pageNote = '', suggestNote = '', busyNote = '';
const drawNote = () => {
  const message = busyNote || suggestNote || pageNote;
  startNote.textContent = message;
  startNote.hidden = !message;
};
const showNote = (message) => { pageNote = message || ''; drawNote(); };
const showSuggestNote = (message) => { suggestNote = message; drawNote(); };
if (oauthCallback && needsStart) showNote(page.CALLBACK_WAIT_NOTE);
const startEls = {
  form: $('start-handle-form'), load: $('start-load'), loadText: $('start-load-text'), retry: $('start-retry'), bar: $('start-bar'), barFill: $('start-bar').firstElementChild,
  cardSignIn: $('start-card-signin'), signIn: $('start-signin'), guest: $('start-guest'), random: $('start-random'), hint: $('start-hint'),
  signed: $('start-signed'), play: $('start-play'), signOut: $('start-signout'),
};
// "Play as someone" is a big button like the others: the first press opens the handle box, the next one (or Enter in the box) plays.
// Closed again whenever the start page comes back.
const guestCombo = $('start-combo');
const setGuestOpen = (open) => {
  guestCombo.hidden = !open;
  startEls.guest.setAttribute('aria-expanded', String(open));
};
startEls.guest.setAttribute('aria-controls', 'start-combo');
setGuestOpen(false);
if (!spectatorMode) {
  $('howto-info').append($('info').content.cloneNode(true));
  $('start-howto').addEventListener('click', () => $('howto').showModal());
  $('howto-close').addEventListener('click', () => $('howto').close());
}
const currentSession = () => (auth ? JSON.parse(auth.session() || 'null') : null);
// Bluesky's CDN serves an avatar full size under /img/avatar/ and small under /img/avatar_thumbnail/; the start page's avatar boxes
// are 32px at most, so they take the small one. Any other address is used as it is.
const CDN_AVATAR = 'https://cdn.bsky.app/img/avatar/', CDN_AVATAR_THUMBNAIL = 'https://cdn.bsky.app/img/avatar_thumbnail/';
const avatarThumbnail = (url) => (url.startsWith(CDN_AVATAR) ? CDN_AVATAR_THUMBNAIL + url.slice(CDN_AVATAR.length) : url);
// a CDN that stops serving the small avatar must not leave an empty box, so a failed thumbnail loads the full-size one
function loadAvatar(img, url) {
  const small = avatarThumbnail(url);
  if (small !== url) img.addEventListener('error', () => { img.src = url; }, { once: true });
  img.decoding = 'async';
  img.src = small;
}
let avatarFor = null;
// The signed-in player's face; best effort, the name shows without it.
async function showAvatar(s) {
  if (!s || avatarFor === s.did) return;
  avatarFor = s.did;
  $('start-avatar')?.remove();
  try {
    const r = await fetch(`${page.PUBLIC_API}/xrpc/app.bsky.actor.getProfile?actor=` + encodeURIComponent(s.did));
    const url = r.ok ? (await r.json()).avatar : null;
    if (url && avatarFor === s.did) {
      // made here, with its source, so the page never carries an image without one
      const img = document.createElement('img');
      img.id = 'start-avatar';
      img.alt = '';
      loadAvatar(img, url);
      $('start-who').prepend(img);
    }
  } catch (e) {
    // fetch fails with a TypeError when offline, and a broken answer fails to parse: either way the name shows without the face
    if (!(e instanceof TypeError || e instanceof SyntaxError)) throw e;
  }
}
renderStartPage = () => {
  if (spectatorMode) return;
  const s = currentSession();
  const view = page.startView({ signInAvailable: authPending || !!auth, handle: s?.handle ?? null, load: loadState, queued, busy, authPending });
  page.renderStart(startEls, view);
  busyNote = view.busyNote;
  drawNote();
  $('start-who-name').textContent = s ? '@' + s.handle : '';
  showAvatar(s);
};
renderStartPage();
// Saves the handle and writes the choice into the URL, so a reload or shared link skips this screen. Random is ?feed=random with
// no handle and is not remembered. The pick is marked queued on the start page, which stays up until the game starts.
function commitPick(h, kind, feed = '') {
  const p = new URLSearchParams(location.search);
  if (h) { saveHandle(h); p.set('handle', h); } else p.delete('handle');
  if (feed) p.set('feed', feed); else if (isRandomFeed(p)) p.delete('feed');
  history.replaceState(null, '', location.pathname + '?' + p + location.hash);
  queued = kind;
  renderStartPage();
  pickHandle({ handle: h, feed });
}
// wired with ?handle= in the URL too: Back to start page brings the start page up later
if (!spectatorMode) {
  startEls.retry.addEventListener('click', () => location.reload());
  // sign in: popup, or redirect where a popup cannot work; Bluesky's own page asks which account. Once the popup shows Bluesky
  // the page is free again: a closed popup cannot be told from one that is still open, so pressing again starts over.
  let pendingSignIn = null;
  startEls.signIn.addEventListener('click', async () => {
    if (!auth || busy || queued) return;
    startError.style.display = 'none';
    pendingSignIn?.abort();
    const mine = pendingSignIn = new AbortController();
    busy = 'signin';
    renderStartPage();
    try {
      const onWaiting = () => { busy = false; renderStartPage(); };
      if (await auth.signIn('', { onWaiting, signal: mine.signal })) {
        busy = false;
        renderStartPage();
        if (finePointer) startEls.play.focus();
      }
    } catch (e) {
      if (e?.name !== 'SignInCancelled') {
        console.warn('sign-in failed: ' + (e?.message || e));
        startFail(page.signInFailed(e));
      }
    } finally {
      // a press that started over owns busy now
      if (pendingSignIn === mine) {
        pendingSignIn = null;
        busy = false;
        renderStartPage();
      }
    }
  });
  $('start-handle-form').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    if (busy || queued) return;
    if (guestCombo.hidden) { setGuestOpen(true); startHandle.focus(); return; }
    // a search still in flight must not open the list over the answer
    closeSuggestions();
    const t = page.guestTarget(startHandle.value);
    startError.style.display = 'none';
    if (t.error) { startFail(t.error); startHandle.focus(); return; }
    busy = 'check';
    renderStartPage();
    // the public API answers 400 (or 404) for a handle no account uses
    const r = await fetch(`${page.PUBLIC_API}/xrpc/com.atproto.identity.resolveHandle?handle=` + encodeURIComponent(t.handle)).catch(() => null);
    busy = false;
    renderStartPage();
    if (!r) return startFail('Could not reach Bluesky. Check your connection.');
    if (r.status === 400 || r.status === 404) return startFail(`Could not find @${t.handle} on Bluesky.`);
    if (!r.ok) return startFail(`Bluesky answered with an error (${r.status}). Try again.`);
    commitPick(t.handle, 'guest');
  });
  startEls.random.addEventListener('click', () => {
    if (busy || queued) return;
    startError.style.display = 'none';
    commitPick('', 'random', 'random');
  });
  startEls.play.addEventListener('click', () => {
    const s = currentSession();
    if (s && !queued) commitPick(s.handle, 'play');
  });
  startEls.signOut.addEventListener('click', async () => {
    await auth.signOut();
    renderSession();
    renderStartPage();
    if (finePointer) startEls.guest.focus();
  });

  // typeahead: accounts matching what is typed, with avatars; Up/Down move, Enter takes the highlighted one, Esc closes
  const list = $('start-suggest');
  let actors = [], active = -1;
  const setActive = (i) => {
    active = i;
    [...list.children].forEach((li, n) => li.setAttribute('aria-selected', String(n === i)));
    if (i >= 0) startHandle.setAttribute('aria-activedescendant', 'start-suggest-' + i); else startHandle.removeAttribute('aria-activedescendant');
  };
  const showSuggestions = (found) => {
    actors = found;
    list.replaceChildren(...found.map((a, i) => {
      const li = document.createElement('li');
      li.id = 'start-suggest-' + i;
      li.setAttribute('role', 'option');
      const img = document.createElement('img');
      img.alt = '';
      img.referrerPolicy = 'no-referrer';
      if (a.avatar) loadAvatar(img, a.avatar);
      const text = document.createElement('span');
      const name = document.createElement('span');
      name.className = 'name';
      name.textContent = a.displayName || a.handle;
      const at = document.createElement('span');
      at.className = 'at';
      at.textContent = '@' + a.handle;
      text.append(name, at);
      li.append(img, text);
      li.addEventListener('mousedown', (ev) => ev.preventDefault()); // the box keeps focus
      li.addEventListener('click', () => choose(i));
      return li;
    }));
    list.hidden = !found.length;
    startHandle.setAttribute('aria-expanded', String(found.length > 0));
    setActive(-1);
  };
  // an answer with no accounts, or a search that failed, says so in the start page's status line; any list clears it
  const typeahead = page.createTypeahead({
    onSearching: () => showSuggestNote('Searching…'),
    onResults: (found, info) => {
      showSuggestions(found);
      showSuggestNote(!found.length && info?.answered ? 'No matching accounts' : '');
    },
    onError: () => showSuggestNote('Suggestions unavailable; type the full handle'),
  });
  const choose = (i) => {
    startHandle.value = actors[i].handle;
    closeSuggestions();
    startHandle.focus();
  };
  closeSuggestions = () => { typeahead.cancel(); showSuggestions([]); showSuggestNote(''); };
  startHandle.addEventListener('input', () => { showSuggestNote(''); typeahead.input(startHandle.value); });
  startHandle.addEventListener('blur', closeSuggestions);
  // a mouse press on the button leaves the focus in the box: a blur would clear the status line, the button would move, and the release would miss it
  startEls.guest.addEventListener('mousedown', (ev) => { if (!guestCombo.hidden) ev.preventDefault(); });
  startHandle.addEventListener('keydown', (ev) => {
    if (ev.key === 'Escape' && actors.length) { ev.preventDefault(); showSuggestions([]); }
    else if (ev.key === 'Escape') { ev.preventDefault(); setGuestOpen(false); startEls.guest.focus(); }
    else if ((ev.key === 'ArrowDown' || ev.key === 'ArrowUp') && actors.length) { ev.preventDefault(); setActive(page.stepActive(active, actors.length, ev.key)); }
    else if (ev.key === 'Enter' && active >= 0) { ev.preventDefault(); choose(active); }
  });
}
// a restored session (or the one back from the redirect) turns the first button into Play, with the handle in the small print
authReady.then(() => {
  authPending = false;
  const why = auth?.initError();
  if (why && needsStart) startFail(why);
  if (needsStart) showNote(auth?.initNote());
  // back from the redirect: restore the page we left (the callback URL has no query of its own)
  if (auth && auth.returnTo() !== null && auth.returnTo() !== location.search) location.replace(location.pathname + auth.returnTo());
  renderStartPage();
  if (needsStart && finePointer && currentSession()) startEls.play.focus();
  // a popup this page stopped waiting for (pressed again, timed out) may still save a session: take it when it lands
  if (needsStart && auth) {
    page.watchSaved({
      restore: () => auth.adoptSaved(),
      signedIn: () => !!currentSession() || startPage.hidden || !!queued,
      onSession: () => {
        startError.style.display = 'none';
        renderStartPage();
        if (finePointer) startEls.play.focus();
      },
    });
  }
});

const { setModuleImports, getAssemblyExports, getConfig } = await runtimeReady;
// the game's download is done, so the start page's track may be fetched ahead of the first click
audioReady.then(() => music?.allowFetchAhead());
setModuleImports('main.js', {
  writeFrame: (frame) => { pendingWrites++; term.write(frame, () => { pendingWrites--; }); },
  termBusy: () => pendingWrites > MAX_PENDING_WRITES,
  fetchAvatar: pictureReader.avatar,
  fetchPicture: pictureReader.picture,
  showError,
  showNotice,
  announce,
  musicCue,
  sfxPlay,
  bestScore,
  saveBestScore,
  authSession: () => auth?.session() ?? null,
  authGetFeed: async (feed, cursor, limit) => liveAuth().getFeed(feed, cursor, limit),
  authGetTimeline: async (cursor, limit) => liveAuth().getTimeline(cursor, limit),
  authGetPostThread: async (uri, depth, parentHeight) => liveAuth().getPostThread(uri, depth, parentHeight),
  authLike: async (uri, cid) => liveAuth().like(uri, cid),
  authRepost: async (uri, cid) => liveAuth().repost(uri, cid),
  authDelete: async (recordUri) => liveAuth().remove(recordUri),
});

const exports = await getAssemblyExports(getConfig().mainAssemblyName);
const resize = () => {
  fit.fit();
  exports.Host.SetSize(term.cols, term.rows);
};
resize();
window.addEventListener('resize', resize);

// --- sign-in (ATProto OAuth): the header's Sign in / Sign out, for the spectator view and ?debug=feed; the start page has its own ---
await authReady;
const signInForm = document.getElementById('signin');
const whoBox = document.getElementById('who');
const renderSession = () => {
  const s = currentSession();
  signInForm.style.display = s || !auth ? 'none' : '';
  whoBox.style.display = s ? '' : 'none';
  whoBox.querySelector('span').textContent = s ? '@' + s.handle : '';
  return s;
};
renderSession();
if (auth) {
  signInForm.addEventListener('submit', async (ev) => {
    ev.preventDefault();
    try {
      await auth.signInRedirect(new FormData(signInForm).get('signin-handle'));
    } catch (e) {
      showError(page.signInFailed(e));
    }
  });
  document.getElementById('signout').addEventListener('click', async () => {
    await auth.signOut();
    renderSession();
    if (debug === 'feed') renderDebug();
  });
}

// --- ?debug=feed: first posts of a feed with like buttons; every write is an explicit click ---
const panel = document.getElementById('debug');
function el(tag, props = {}, ...kids) {
  const n = Object.assign(document.createElement(tag), props);
  n.append(...kids);
  return n;
}
const DEBUG_POSTS = 10;
async function renderDebug() {
  panel.style.display = 'block';
  panel.replaceChildren(el('h2', { textContent: 'feed debug' }));
  const me = renderSession();
  if (!me) {
    panel.append(el('p', { textContent: auth ? 'Sign in with Bluesky (top right) to read the feed.' : 'Sign-in is not available on this page (it needs HTTPS or localhost).' }));
    return;
  }
  const feed = params.get('feed') || 'discover';
  const status = el('p', { textContent: 'loading ' + feed + '...' });
  panel.append(status);
  let feedPage;
  try {
    feedPage = JSON.parse(await exports.Bridge.FeedPage(feed, null, DEBUG_POSTS));
  } catch (e) {
    status.textContent = 'feed failed: ' + (e.message || e);
    return;
  }
  status.textContent = `${feedPage.posts.length} posts from ${feed} (as @${me.handle})`;
  for (const p of feedPage.posts.slice(0, DEBUG_POSTS)) panel.append(postRow(p));
}
function postRow(p) {
  const result = el('span', { className: 'result' });
  const made = []; // the records this row has written, newest last: undo deletes the newest
  const wireWriteButton = (button, name, done, send) => {
    button.onclick = async () => {
      button.disabled = true;
      try {
        const uri = await send();
        if (uri) made.push(uri);
        result.textContent = `${done}: ${uri}`;
      } catch (e) { result.textContent = `${name} failed: ` + (e.message || e); button.disabled = false; }
    };
    return button;
  };
  const like = wireWriteButton(el('button', { textContent: 'like' }), 'like', 'liked', async () => JSON.parse(await exports.Bridge.Like(p.uri)).uri);
  const repost = wireWriteButton(el('button', { textContent: 'repost' }), 'repost', 'reposted', async () => JSON.parse(await exports.Bridge.Repost(p.uri)).uri);
  const undo = el('button', { textContent: 'undo' });
  undo.onclick = async () => {
    const uri = made.at(-1);
    if (!uri) { result.textContent = 'nothing to undo'; return; }
    undo.disabled = true;
    try {
      await exports.Bridge.Delete(uri);
      made.pop();
      result.textContent = 'undone: ' + uri;
      // the written button may be pressed again once its record is gone
      (uri.includes('/app.bsky.feed.repost/') ? repost : like).disabled = false;
    } catch (e) { result.textContent = 'undo failed: ' + (e.message || e); }
    undo.disabled = false;
  };
  return el('div', { className: 'post' },
    el('div', { className: 'meta', textContent: `@${p.authorHandle}${p.replyRoot ? ' (a reply)' : ''}  likes ${p.likeCount}  reposts ${p.repostCount}  replies ${p.replyCount}` }),
    el('div', { className: 'text', textContent: p.text }),
    el('div', {}, like, repost, undo, result));
}
if (debug === 'feed') renderDebug();

// --- the game ---
const gear = $('gear'), side = $('side');
let menu = null;              // the pause menu (page.createMenu), once the game is up
let runsStarted = 0;          // counts startRun's runs, so a run that ends late knows whether a newer one is under way
let releaseKeys = () => {};   // lets go of the held game keys and presses; installGameInput fills it in
// The feed speed (1 to 5): ?feed-speed= for this visit, else the step kept in localStorage. The game hears of a change from a key at once,
// from the pause menu's slider when the pause menu closes; Host.SetFeedSpeed is a no-op while no run is on.
const feedSpeed = page.createFeedSpeed({
  search: location.search,
  storage: pageStorage,
  // the step is already kept on the page; a game that did not take it must not stop the run starting or the pause menu closing
  tell: (step, say) => { try { exports.Host.SetFeedSpeed(step, say); } catch (e) { console.warn('feed speed not passed on: ' + (e?.message || e)); } },
});
// A hidden tab pauses the engine's loop, feed paging and both live sockets, and the music; coming back resumes all of it.
function followTabVisibility() {
  const hidden = document.hidden;
  exports.Host.SetHidden(hidden);
  try { music?.setPaused(hidden); } catch { /* audio is best effort: a failed pause must not stop the game */ }
}
document.addEventListener('visibilitychange', followTabVisibility);
// a tab opened in the background is hidden from the start, before the tour or a run sees a frame
followTabVisibility();
// The start page's backdrop: the game plays itself under the start page (Host.StartTour), on built-in posts, with no sound effects and
// nothing saved. It runs only while the start page shows. Reduced motion gets it at a quarter speed.
const tour = page.createTour({
  // the tour is only a backdrop: one that stops leaves the start page over a still field, and the start page still works
  start: (seed, slow) => { exports.Host.StartTour(seed, slow).catch((e) => console.warn('start page tour stopped: ' + (e?.message || e))); },
  stop: () => exports.Host.StopTour(),
  sleep: () => { exports.Host.SetTourAsleep(true); startPage.classList.add('asleep'); },
  wake: () => { exports.Host.SetTourAsleep(false); startPage.classList.remove('asleep'); },
  slow: matchMedia('(prefers-reduced-motion: reduce)').matches,
});
// Anything the visitor does anywhere on the page keeps the tour awake. Capture, so a handler that stops an event still counts it.
for (const type of ['keydown', 'pointermove', 'pointerdown', 'touchstart', 'wheel']) {
  window.addEventListener(type, () => tour.visitorActed(), { capture: true, passive: true });
}
tour.ready();
if (needsStart && !queued) tour.show();
setLoad('ready');
if (spectatorMode) {
  if (!handle) {
    term.write(HINT_COLOUR + 'Enter a Bluesky handle above, or add ?handle=bsky.app to the address.' + PLAIN_TEXT);
  }
  // Main is empty; the exports are usable straight after create(), so dotnet.run() is not needed.
  if (handle) {
    try { await exports.Host.StartSpectator(handle, darknessParam); }
    catch (e) {
      console.warn('spectator failed: ' + (e?.message || e));
      showError(`Could not reach Bluesky for @${handle}. Check your connection and try again.`);
    }
  }
} else {
  // waits here for the start page's click (or a session) unless the URL already named a handle
  let choice = !debugFeed && (handle || randomFromUrl) ? { handle, feed: params.get('feed') || '' } : await handlePicked;
  await audioReady;
  followTabVisibility();
  installGameInput();
  installMenu();
  // one run per pick; Back to start page ends it and brings the start page back for the next pick
  for (;;) {
    armPick();
    startRun(choice);
    choice = await handlePicked;
  }
}

function startRun(choice) {
  tour.hide();
  const run = ++runsStarted;
  for (const n of behindStart) n.inert = false;
  startPage.hidden = true;
  // hides the header's sign-in and watch forms; StartGame runs the game, so this must come first
  document.body.classList.add('playing');
  helperTextarea.tabIndex = -1;
  menu.started();
  renderMenuWho();
  gear.hidden = false;
  placeChrome();
  requestAnimationFrame(placeChrome);   // once the terminal has painted at its final cell size
  giveGrants();
  // the run ends when Back to start page cancels it; it is not waited on, so the next pick never hangs on a slow socket
  exports.Host.StartGame(choice.handle, choice.feed || params.get('feed') || '', darknessParam, params.get('fps') === '1', params.get('quality') === 'adaptive',
    // development aid: ?stage=6 starts the run at stage 6, to see the later enemies without playing up to them
    Number.parseInt(params.get('stage') || '1', 10) || 1,
    // development aid: ?boss=4 opens the run on the warning of stage 4's boss
    Number.parseInt(params.get('boss') || '0', 10) || 0,
    // development aid: ?gates=off runs without boss gates, as the balance tests do
    (params.get('gates') || 'on').toLowerCase() !== 'off').catch((e) => showError(e?.message || String(e)))
    // pictures still waiting for a turn are for cards no longer on screen; a newer run's own asks are left alone
    .finally(() => { if (run === runsStarted) pictureReader.drop(); });
  // StartGame has made the game by now (nothing in it waits first), so the step goes in before the first card
  feedSpeed.begin();
}

// --- the pause menu: the gear or Esc opens it, the game holds still behind it; Back to start page ends the run ---
function installMenu() {
  for (const box of [side, $('menu-info')]) box.append($('info').content.cloneNode(true));
  $('menu-sound').hidden = !music && !sfx;
  const dialog = $('menu');
  const speedSlider = page.bindFeedSlider($('feed-speed'), feedSpeed, $('feed-speed-name'));
  const picturesSwitch = page.bindPicturesSwitch($('pictures'), pictures, $('pictures-name'));
  syncPicturesSwitch = picturesSwitch.sync;
  logList = page.createLogList({
    els: { list: $('menu-log-list'), empty: $('menu-log-empty'), status: $('menu-log-status') },
    read: () => exports.Host.FeedLog(),
    toggle: (uri, action) => exports.Host.Toggle(uri, action),
  });
  menu = page.createMenu({
    refreshLog: () => { logList.clear(); logList.refresh(); },
    show: () => { speedSlider.sync(); picturesSwitch.sync(); dialog.showModal(); },
    hide: () => { if (dialog.open) dialog.close(); },
    setPaused: (on) => exports.Host.SetMenu(on),
    releaseKeys,
    resuming: () => feedSpeed.flush(),
    quit: () => exports.Host.Quit(),
    toStart: showStartPage,
  });
  // however the dialog closed (the browser's own Esc, Resume), the game resumes and focus goes back to the game, not a button
  dialog.addEventListener('close', () => {
    menu.closed();
    if (dialog.contains(document.activeElement)) document.activeElement.blur();
  });
  dialog.addEventListener('cancel', (e) => { e.preventDefault(); menu.close(); });
  $('menu-resume').addEventListener('click', () => menu.close());
  $('menu-quit').addEventListener('click', () => menu.backToStartPage());
  $('menu-signout').addEventListener('click', async () => {
    // a failed sign-out is logged, not shown: the pause menu still has to close, and renderSession then shows whatever session is left
    try { await auth?.signOut(); } catch (e) { console.warn('sign-out failed: ' + (e?.message || e)); }
    renderSession();
    menu.backToStartPage();
  });
  // Sign in again: the normal sign-in for the same handle, which asks for what the game can do now. In a popup the run stays
  // paused behind it and carries on with the repost buttons unlocked; by redirect the page leaves and the run ends.
  let renewing = null;
  $('menu-signin-again').addEventListener('click', async () => {
    if (!auth) return;
    renewing?.abort();
    const mine = renewing = new AbortController();
    const note = $('menu-grants');
    try {
      await page.signInAgain(auth, { setGrants: giveGrants, signal: mine.signal });
      renderSession();
      renderMenuWho();
    } catch (e) {
      if (e?.name === 'AccountChanged') {
        // the run was the other account's: it ends here, and the start page shows who is signed in now
        renderSession();
        menu.backToStartPage();
      } else if (e?.name !== 'SignInCancelled') {
        console.warn('sign-in failed: ' + (e?.message || e));
        renderMenuWho();
        note.hidden = false;
        note.textContent = page.signInFailed(e);
      }
    } finally {
      if (renewing === mine) renewing = null;
    }
  });
  // a press on the gear must not take focus from the game
  gear.addEventListener('mousedown', (e) => e.preventDefault());
  gear.addEventListener('click', () => menu.open());
  // capture phase, before the game's keys: Esc opens and closes the pause menu during a run
  window.addEventListener('keydown', (e) => {
    const act = page.menuKey(e, { playing: menu.isPlaying(), open: menu.isOpen() });
    if (!act) return;
    e.preventDefault();
    if (act === 'open') menu.open(); else menu.close();
  }, true);
  window.addEventListener('resize', placeChrome);
}

// A popup cannot open in an installed iOS web app or where there is no window.open: there the sign-in leaves the page.
// A function declaration, not a const: an address that starts a run at once (?feed=random) draws the pause menu before this line runs.
function canPopup() { return navigator.standalone !== true && typeof window.open === 'function'; }
// what the session may write goes to the game: before a run, and again after a fresh sign-in from the pause menu
function giveGrants(g = page.sessionGrants(currentSession())) {
  exports.Host.SetGrants(g.like, g.unlike, g.repost, g.unrepost);
}

function renderMenuWho() {
  const s = currentSession();
  $('menu-who').textContent = s ? `Signed in as @${s.handle}. Holding on a heart or a repost button writes to this account.`
    : 'Not signed in: holding on a heart or a repost button marks it here and sends nothing.';
  $('menu-signout').hidden = !s;
  // a sign-in from before the game could repost: say so, and offer the new one
  const short = page.menuGrants(s, { popup: canPopup() });
  $('menu-grants').hidden = !short;
  $('menu-grants').textContent = short?.note ?? '';
  $('menu-signin-again').hidden = !short;
  if (short) $('menu-signin-again').textContent = short.button;
}

// The gear in the HUD beside the Stage readout and, on a wide window, the help in the margin right of the field.
function placeChrome() {
  if (!menu?.isPlaying()) { gear.hidden = true; side.hidden = true; return; }
  const r = term.element.querySelector('.xterm-screen').getBoundingClientRect();
  const l = page.chromeLayout({ cols: term.cols, rows: term.rows, cellW: r.width / term.cols, cellH: r.height / term.rows, left: r.left, top: r.top });
  const g = l.gear, left = g.x - (g.size - g.w) / 2, top = Math.max(0, g.y - (g.size - g.h) / 2);
  Object.assign(gear.style, { left: left + 'px', top: top + 'px', width: g.size + 'px', height: g.size + 'px' });
  Object.assign(gear.firstElementChild.style, { left: g.x - left + 'px', top: g.y - top + 'px', width: g.w + 'px', height: g.h + 'px' });
  side.hidden = !l.side;
  if (l.side) Object.assign(side.style, { left: l.side.x + 'px', top: l.side.y + 'px', width: l.side.w + 'px', height: l.side.h + 'px' });
}

// Back to start page: the run is over (Host.Quit), so the page goes back to how it looked before the pick: the three choices,
// the start page's theme, an address with no handle in it (a reload lands here too) and nothing of the old run on screen.
function showStartPage() {
  document.body.classList.remove('playing');
  helperTextarea.removeAttribute('tabindex');
  placeChrome();
  const p = new URLSearchParams(location.search);
  p.delete('handle');
  if (isRandomFeed(p)) p.delete('feed');
  const q = String(p);
  history.replaceState(null, '', location.pathname + (q ? '?' + q : '') + location.hash);
  term.write(CLEAR_SCREEN);
  // the old run's cards are gone, so its pictures still waiting for a turn are never fetched; the next run asks for its own
  pictureReader.drop();
  bar.hide();
  startError.style.display = 'none';
  queued = null;
  busy = false;
  if (!startHandle.value) startHandle.value = savedHandle();
  startPage.classList.remove('booting');
  startPage.hidden = false;
  setGuestOpen(false);
  for (const n of behindStart) n.inert = true;
  renderStartPage();
  musicCue('start');
  tour.show();
  // focus moves to the start page: its first live button (a text box would pop a phone's keyboard)
  const first = [startEls.play, startEls.signIn, startEls.guest, startEls.random].find((b) => b.offsetParent !== null && !b.disabled);
  first?.focus({ preventScroll: true });
}

// Keyboard, mouse and touch become the engine's input calls. Nothing here sends a like: E likes the marked log post.
function installGameInput() {
  const screen = document.getElementById('term');
  const cell = () => {
    const r = term.element.querySelector('.xterm-screen').getBoundingClientRect();
    return { left: r.left, top: r.top, w: r.width / term.cols, h: r.height / term.rows };
  };
  const toCell = (cx, cy) => { const c = cell(); return [(cx - c.left) / c.w, (cy - c.top) / c.h]; };
  // the feed log on the left: a press on it is a click on its buttons (the game names the button), never a hold or a steering drag
  const CELL_UNIT = 1;   // c is already in cells, so a cell is one unit wide and tall
  const inLog = (c) => {
    const panel = page.leftPanel(term.cols), at = page.cellAt(c[0], c[1], { left: 0, top: 0, cellW: CELL_UNIT, cellH: CELL_UNIT, cols: term.cols, rows: term.rows });
    return !!panel && !!at && at.col >= panel.col && at.col < panel.col + panel.cols;
  };
  const held = new Set();
  releaseKeys = () => {
    for (const c of held) exports.Host.Key(c, false);
    held.clear();
    exports.Host.PointerGone();
  };
  // Capture phase on window: nothing inside the page (xterm's helper textarea, a focused button) can swallow a game key first.
  const typing = () => page.isTextEntry(document.activeElement);
  const isFeedSpeedKey = (code) => code === page.SLOWER_FEED_KEY || code === page.FASTER_FEED_KEY;
  window.addEventListener('keydown', (e) => {
    const code = page.gameKey(e);
    if (!code || typing() || !menu?.isPlaying() || menu?.isOpen() || e.ctrlKey || e.metaKey || e.altKey) return;
    e.preventDefault();
    if (e.repeat) return;
    // the feed speed keys are the page's: a step, not a key held in the game
    if (isFeedSpeedKey(code)) { feedSpeed.bump(code === page.SLOWER_FEED_KEY ? -1 : 1); return; }
    held.add(code);
    exports.Host.Key(code, true);
  }, true);
  window.addEventListener('keyup', (e) => {
    const code = page.gameKey(e);
    if (!code || isFeedSpeedKey(code)) return;
    held.delete(code);
    exports.Host.Key(code, false);
  }, true);
  window.addEventListener('blur', () => { for (const c of held) exports.Host.Key(c, false); held.clear(); });
  // the start page's Play button keeps focus after the click; the game listens to the window, not to it
  if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();

  const gestures = page.createGestures({
    tap: (c) => exports.Host.Tap(...c),
    bomb: () => exports.Host.Bomb(),
    inPanel: inLog,
    click: (c) => exports.Host.Click(Math.floor(c[0]), Math.floor(c[1])),
  });

  screen.addEventListener('mousemove', (e) => exports.Host.Pointer(...toCell(e.clientX, e.clientY)));
  screen.addEventListener('mouseleave', () => { exports.Host.PointerGone(); });
  // Capture phase, stopped here: xterm's own drag-to-select listens on an inner element and must never see the press.
  let fieldPress = false; // a press that began on the field and has not been released yet
  screen.addEventListener('mousedown', (e) => {
    e.preventDefault();
    e.stopPropagation();
    fieldPress = true;
    gestures.mouseDown(e.button, toCell(e.clientX, e.clientY));
  }, true);
  // Firefox's chatbot shortcut (browser.ml.chat.*) times a press from the last mousedown it saw to the mouseup, and shows
  // itself after a long one even with nothing selected. It never sees the field's mousedown (stopped above), so a release
  // it did see would be timed from some earlier click on the page and always count as long. No page opt-out exists, so the
  // release of a field press stops here too, wherever the pointer has got to by then.
  window.addEventListener('mouseup', (e) => {
    if (fieldPress) { fieldPress = false; e.stopPropagation(); }
  }, true);
  screen.addEventListener('dblclick', (e) => { e.preventDefault(); e.stopPropagation(); }, true);
  // and no selection or drag may start in the field
  screen.addEventListener('selectstart', (e) => e.preventDefault());
  screen.addEventListener('dragstart', (e) => e.preventDefault());
  term.onSelectionChange(() => { if (term.hasSelection()) term.clearSelection(); });
  screen.addEventListener('contextmenu', (e) => e.preventDefault());

  let last = null;
  const opt = { passive: false };
  screen.addEventListener('touchstart', (e) => {
    e.preventDefault();
    const t0 = e.touches[0];
    gestures.touchStart(e.touches.length, t0.clientX, t0.clientY, toCell(t0.clientX, t0.clientY));
    last = { x: t0.clientX, y: t0.clientY };
  }, opt);
  screen.addEventListener('touchmove', (e) => {
    e.preventDefault();
    if (e.touches.length !== 1 || !last) { gestures.touchMove(e.touches.length, 0, 0); return; }
    const t = e.touches[0], c = cell();
    if (gestures.touchMove(1, t.clientX - last.x, t.clientY - last.y)) exports.Host.Drag((t.clientX - last.x) / c.w, (t.clientY - last.y) / c.h);
    last = { x: t.clientX, y: t.clientY };
  }, opt);
  screen.addEventListener('touchend', (e) => {
    e.preventDefault();
    gestures.touchEnd(e.touches.length);
    if (e.touches.length === 0) last = null;
  }, opt);
  screen.addEventListener('touchcancel', () => { gestures.touchCancel(); last = null; });
}
