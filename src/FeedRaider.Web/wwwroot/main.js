// Browser glue: xterm.js on one side, the .NET engine (via JS interop) on the other.
// All URLs are relative so the site works from a subpath such as https://<user>.github.io/feed-raider/.
// Single-file build seam: web/bundle-single-file.mjs sets globalThis.__frBundle to an object holding dotnetUrl, atprotoUrl and loadBootResource
// before loading this file from memory. Without it (the normal site) everything loads from sibling files as usual.
const frBundle = globalThis.__frBundle;
const page = await import(frBundle ? frBundle.pageUrl : './page.js');

const params = new URLSearchParams(location.search);
const handle = (params.get('handle') || '').trim().replace(/^@/, '');
const debug = params.get('debug');
// The game is the default view; ?mode=spectator keeps the crowd view.
const spectatorMode = (params.get('mode') || '').toLowerCase() === 'spectator';
if (!spectatorMode) document.body.classList.add('game');
else document.querySelector('header form').insertAdjacentHTML('beforeend', '<input type="hidden" name="mode" value="spectator">');
const errorBox = document.getElementById('error');
const showError = (message) => {
  errorBox.textContent = message;
  errorBox.style.display = 'block';
};

// The start page is plain HTML and CSS, painted before any script runs. This wires it up while the runtime still downloads:
// the game starts once the runtime is ready and a handle is picked (typed as a guest, or from a live sign-in session).
let queued = null; // "play" or "guest" once a start was pressed before the runtime was ready
const savedKey = 'feed-raider.handle';
const $ = (id) => document.getElementById(id);
const startScreen = $('start');
const startHandle = $('start-handle');
const needsStart = !spectatorMode && !handle;
const finePointer = matchMedia('(pointer: fine)').matches;
// ?handle= in the URL skips the panel: only the title and the load bar show until the game starts
if (!needsStart) startScreen.classList.add('booting');
else startHandle.value = localStorage.getItem(savedKey) || '';
// desktop: the handle box is the first thing to type in; touch: focus nothing, so no keyboard opens on load
if (needsStart && finePointer) startHandle.focus();


const term = new Terminal({
  convertEol: false,
  cursorBlink: false,
  disableStdin: true,
  fontFamily: 'ui-monospace, "Cascadia Mono", Menlo, Consolas, monospace',
  fontSize: !spectatorMode && window.innerWidth < 600 ? 12 : 14,
  customGlyphs: true,
  scrollback: spectatorMode ? 1000 : 0, // the game redraws in place; history is dead memory
  theme: { background: '#101316', foreground: '#d8cba3', selectionBackground: 'transparent' },
});
const fit = new FitAddon.FitAddon();
term.loadAddon(fit);
term.open(document.getElementById('term'));
term.write('\x1b[?25l');
fit.fit();

// The default DOM renderer rebuilds spans for every changed cell, which is the slow part on a weak phone or old laptop. The
// WebGL addon paints the same cells on the GPU. It comes from the same CDN as xterm (pinned, with SRI) and is loaded in the
// background: with no WebGL, a blocked CDN, an offline page or a lost context the terminal keeps the DOM renderer.
// ?renderer=dom forces the DOM renderer.
const WEBGL_SRC = 'https://cdn.jsdelivr.net/npm/@xterm/addon-webgl@0.18.0/lib/addon-webgl.js';
const WEBGL_SRI = 'sha384-AkzdQDeATxpW8V7I2EPcwg17qAoLQMjEFHb//1QaAAIyIlbDAHGKno5+huH7RKxq';
if (params.get('renderer') !== 'dom') {
  const tag = document.createElement('script');
  tag.src = WEBGL_SRC;
  tag.integrity = WEBGL_SRI;
  tag.crossOrigin = 'anonymous';
  tag.onload = () => {
    try {
      const gl = new WebglAddon.WebglAddon();
      gl.onContextLoss(() => gl.dispose());
      term.loadAddon(gl);
    } catch (e) {
      console.warn('webgl renderer unavailable, using the DOM renderer: ' + (e.message || e));
    }
  };
  tag.onerror = () => console.warn('webgl addon failed to load, using the DOM renderer');
  document.head.appendChild(tag);
}

// Sign-in (ATProto OAuth) needs crypto.subtle, which browsers expose only on HTTPS or localhost. The bundle loads only there, so a
// plain-HTTP page still runs the game. A failed load or init leaves sign-in off and quietly signed out, not an error bar.
// It starts at once and runs beside the runtime download, so sign-in works while the game loads.
let auth = null;
let authPending = window.isSecureContext;
// the page we land on after the OAuth redirect (or in the popup): the runtime waits for the callback to be handled first
const oauthCallback = params.has('state') && (params.has('code') || params.has('error'));
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
    if (mod.init({ ...opts, sfx: sfxWanted })) music = mod;
    if (sfxWanted) {
      const s = await import(frBundle ? frBundle.sfxUrl : './sfx.js');
      if (s.init({ master: mod.master, duck: mod.duck })) sfx = s;
    }
  } catch (e) {
    console.warn('audio unavailable: ' + (e.message || e));
  }
})();
const musicCue = (name) => { try { music?.cue(name); } catch { /* the game never waits on music */ } };
const sfxPlay = (name, pan, step, size) => { try { sfx?.play(name, pan, step, size); } catch { /* nor on sound effects */ } };
const bestKey = 'feed-raider.best';
const bestScore = () => { try { return Number.parseInt(localStorage.getItem(bestKey), 10) || 0; } catch { return 0; } };
const saveBestScore = (n) => { try { localStorage.setItem(bestKey, String(n)); } catch { /* not persisted */ } };
if (needsStart) audioReady.then(() => musicCue('menu'));

// Frames the terminal has not drawn yet; the game skips drawing while the terminal is behind.
let pendingWrites = 0;
const avatarCache = new Map();

// Fetches an author's avatar and downsamples it to 8x8 as 64 comma-joined 0xRRGGBB values. Tries the avatar URL first, then
// the author's own PDS (getRecord for the profile blob, then getBlob). If CORS blocks both the answer is null and the game
// keeps its tinted placeholder.
async function fetchAvatar(did, url) {
  if (avatarCache.has(did)) return avatarCache.get(did);
  const tryBlob = async (get) => {
    try {
      const r = await get();
      if (!r.ok) return null;
      const bmp = await createImageBitmap(await r.blob());
      const c = new OffscreenCanvas(8, 8);
      const x = c.getContext('2d', { willReadFrequently: true });
      x.drawImage(bmp, 0, 0, 8, 8);
      const px = x.getImageData(0, 0, 8, 8).data;
      const out = [];
      for (let i = 0; i < 64; i++) out.push((px[i * 4] << 16) | (px[i * 4 + 1] << 8) | px[i * 4 + 2]);
      return out.join(',');
    } catch { return null; }
  };
  let res = url ? await tryBlob(() => fetch(url)) : null;
  if (!res && did.startsWith('did:plc:')) {
    try {
      const doc = await (await fetch('https://plc.directory/' + did)).json();
      const pds = (doc.service || []).find((s) => s.id === '#atproto_pds')?.serviceEndpoint;
      if (pds) {
        const rec = await (await fetch(`${pds}/xrpc/com.atproto.repo.getRecord?repo=${did}&collection=app.bsky.actor.profile&rkey=self`)).json();
        const cid = rec?.value?.avatar?.ref?.$link;
        if (cid) res = await tryBlob(() => fetch(`${pds}/xrpc/com.atproto.sync.getBlob?did=${did}&cid=${cid}`));
      }
    } catch { /* blocked or gone: no avatar */ }
  }
  avatarCache.set(did, res);
  return res;
}

// --- start page: sign in or play as a guest from one handle box; a start pressed before the runtime is ready is queued ---
let pickHandle;
const handlePicked = new Promise((resolve) => { pickHandle = resolve; });
let busy = false;
const startError = $('start-error');
const startFail = (message) => {
  startError.textContent = message;
  startError.style.display = 'block';
};
const startEls = {
  load: $('start-load'), loadText: $('start-load-text'), retry: $('start-retry'), bar: $('start-bar'), barFill: $('start-bar').firstElementChild,
  form: $('start-handle-form'), signIn: $('start-login'), guest: $('start-guest'), hint: $('start-hint'),
  signed: $('start-signed'), play: $('start-play'), signOut: $('start-signout'),
};
const currentSession = () => (auth ? JSON.parse(auth.session() || 'null') : null);
let avatarFor = null;
// The signed-in player's face; best effort, the name shows without it.
async function showAvatar(s) {
  const img = $('start-avatar');
  if (!s || avatarFor === s.did) return;
  avatarFor = s.did;
  img.hidden = true;
  try {
    const r = await fetch('https://public.api.bsky.app/xrpc/app.bsky.actor.getProfile?actor=' + encodeURIComponent(s.did));
    const url = r.ok ? (await r.json()).avatar : null;
    if (url && avatarFor === s.did) { img.src = url; img.hidden = false; }
  } catch { /* no avatar */ }
}
renderStartPage = () => {
  if (spectatorMode) return;
  const s = currentSession();
  page.renderStart(startEls, page.startView({ loginAvailable: authPending || !!auth, handle: s?.handle ?? null, load: loadState, queued, busy, authPending }));
  $('start-who-name').textContent = s ? '@' + s.handle : '';
  showAvatar(s);
};
renderStartPage();
// Saves the handle and writes it into the URL, so a reload or shared link skips this screen. An empty handle is the public
// Discover feed and is not remembered. The page stays up until the game starts; before the runtime is ready the button counts.
function pick(h, kind) {
  const p = new URLSearchParams(location.search);
  if (h) { localStorage.setItem(savedKey, h); p.set('handle', h); } else p.delete('handle');
  history.replaceState(null, '', location.pathname + '?' + p + location.hash);
  queued = kind;
  renderStartPage();
  pickHandle(h);
}
if (needsStart) {
  startEls.retry.addEventListener('click', () => location.reload());
  // sign in (the primary action, and what Enter does): popup, or redirect where a popup cannot work
  $('start-handle-form').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    if (!auth || busy || queued) return;
    startError.style.display = 'none';
    busy = true;
    renderStartPage();
    try {
      if (await auth.signIn(startHandle.value)) {
        busy = false;
        renderStartPage();
        if (finePointer) startEls.play.focus();
      }
    } catch (e) {
      if (e?.name !== 'SignInCancelled') startFail('sign in: ' + (e.message || e));
    } finally {
      busy = false;
      renderStartPage();
    }
  });
  // guest: wingmen from the follows of the typed handle; an empty box is the public Discover feed
  startEls.guest.addEventListener('click', async () => {
    if (busy || queued) return;
    const h = page.normalizeHandle(startHandle.value);
    startError.style.display = 'none';
    if (!h) return pick('', 'guest');
    if (/^(did:|https?:)/i.test(h)) return startFail('Type a handle, e.g. you.bsky.social.');
    busy = true;
    renderStartPage();
    // the public API answers 400 (or 404) for a handle no account uses
    const r = await fetch('https://public.api.bsky.app/xrpc/com.atproto.identity.resolveHandle?handle=' + encodeURIComponent(h)).catch(() => null);
    busy = false;
    renderStartPage();
    if (!r) return startFail('could not reach Bluesky, check your connection');
    if (r.status === 400 || r.status === 404) return startFail(`no Bluesky account is called @${h}`);
    if (!r.ok) return startFail(`Bluesky did not answer (${r.status}), try again`);
    pick(h, 'guest');
  });
  startEls.play.addEventListener('click', () => {
    const s = currentSession();
    if (s && !queued) pick(s.handle, 'play');
  });
  startEls.signOut.addEventListener('click', async () => {
    await auth.logout();
    renderSession();
    renderStartPage();
    if (finePointer) startHandle.focus();
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
      if (a.avatar) img.src = a.avatar;
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
  const typeahead = page.createTypeahead({ onResults: showSuggestions });
  const choose = (i) => {
    startHandle.value = actors[i].handle;
    typeahead.cancel();
    showSuggestions([]);
    startHandle.focus();
  };
  startHandle.addEventListener('input', () => typeahead.input(startHandle.value));
  startHandle.addEventListener('blur', () => { typeahead.cancel(); showSuggestions([]); });
  startHandle.addEventListener('keydown', (ev) => {
    if (ev.key === 'Escape' && actors.length) { ev.preventDefault(); showSuggestions([]); }
    else if ((ev.key === 'ArrowDown' || ev.key === 'ArrowUp') && actors.length) { ev.preventDefault(); setActive(page.stepActive(active, actors.length, ev.key)); }
    else if (ev.key === 'Enter' && active >= 0) { ev.preventDefault(); choose(active); }
  });
}
// a restored session (or the one back from the redirect) turns the panel into "Play as @handle"
authReady.then(() => {
  authPending = false;
  // back from the redirect: restore the page we left (the callback URL has no query of its own)
  if (auth && auth.returnTo() !== null && auth.returnTo() !== location.search) location.replace(location.pathname + auth.returnTo());
  renderStartPage();
  if (needsStart && finePointer) (currentSession() ? startEls.play : startHandle).focus();
});

const { setModuleImports, getAssemblyExports, getConfig } = await runtimeReady;
setModuleImports('main.js', {
  writeFrame: (frame) => { pendingWrites++; term.write(frame, () => { pendingWrites--; }); },
  termBusy: () => pendingWrites > 1,
  fetchAvatar,
  showError,
  musicCue,
  sfxPlay,
  bestScore,
  saveBestScore,
  authSession: () => auth?.session() ?? null,
  authGetFeed: async (feed, cursor, limit) => liveAuth().getFeed(feed, cursor, limit),
  authGetTimeline: async (cursor, limit) => liveAuth().getTimeline(cursor, limit),
  authLike: async (uri, cid) => liveAuth().like(uri, cid),
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
const loginForm = document.getElementById('login');
const whoBox = document.getElementById('who');
const renderSession = () => {
  const s = currentSession();
  loginForm.style.display = s || !auth ? 'none' : '';
  whoBox.style.display = s ? '' : 'none';
  whoBox.querySelector('span').textContent = s ? '@' + s.handle : '';
  return s;
};
renderSession();
if (auth) {
  loginForm.addEventListener('submit', async (ev) => {
    ev.preventDefault();
    try {
      await auth.login(new FormData(loginForm).get('login-handle'));
    } catch (e) {
      showError('sign in: ' + (e.message || e));
    }
  });
  document.getElementById('logout').addEventListener('click', async () => {
    await auth.logout();
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
async function renderDebug() {
  panel.style.display = 'block';
  panel.replaceChildren(el('h2', { textContent: 'feed debug' }));
  const me = renderSession();
  if (!me) {
    panel.append(el('p', { textContent: auth ? 'Sign in with Bluesky (top right) to read the feed.' : 'Sign-in is unavailable on this page (it needs HTTPS or localhost).' }));
    return;
  }
  const feed = params.get('feed') || 'discover';
  const status = el('p', { textContent: 'loading ' + feed + '...' });
  panel.append(status);
  let page;
  try {
    page = JSON.parse(await exports.Bridge.FeedPage(feed, null, 10));
  } catch (e) {
    status.textContent = 'feed failed: ' + (e.message || e);
    return;
  }
  status.textContent = `${page.posts.length} posts from ${feed} (as @${me.handle})`;
  for (const p of page.posts.slice(0, 10)) panel.append(postRow(p));
}
function postRow(p) {
  const result = el('span', { className: 'result' });
  const like = el('button', { textContent: 'like' });
  like.onclick = async () => {
    like.disabled = true;
    try { result.textContent = 'liked: ' + JSON.parse(await exports.Bridge.Like(p.uri)).uri; }
    catch (e) { result.textContent = 'like failed: ' + (e.message || e); like.disabled = false; }
  };
  return el('div', { className: 'post' },
    el('div', { className: 'meta', textContent: `@${p.authorHandle}${p.replyRoot ? ' (a reply)' : ''}  likes ${p.likeCount}  reposts ${p.repostCount}  replies ${p.replyCount}` }),
    el('div', { className: 'text', textContent: p.text }),
    el('div', {}, like, result));
}
if (debug === 'feed') renderDebug();

// --- the game ---
// A hidden tab pauses the engine's loop, feed paging and both live sockets, and the music; coming back resumes all of it.
function pauseForTab() {
  const hidden = document.hidden;
  try { exports.Host.SetHidden(hidden); } catch { /* the engine is not up yet */ }
  try { music?.setPaused(hidden); } catch { /* the game never waits on music */ }
}
document.addEventListener('visibilitychange', pauseForTab);
// the runtime is up: a start queued on the start page goes now, and the page lets go of its "loading" label
setLoad('ready');
if (spectatorMode) {
  if (!handle) {
    term.write('\x1b[38;2;216;203;163menter a Bluesky handle above (or add ?handle=bsky.app to the URL)\x1b[0m');
  }
  // Main is empty; the exports are usable straight after create(), so dotnet.run() is not needed.
  if (handle) await exports.Host.Start(handle, (params.get('darkness') || 'on').toLowerCase());
} else {
  // waits here for the start screen's click (or a session) unless the URL already named a handle
  const who = handle || await handlePicked;
  await audioReady;
  pauseForTab();
  startScreen.hidden = true;
  installGameInput();
  // hides the header's sign-in and watch forms; StartGame runs the game, so this must come first
  document.body.classList.add('playing');
  await exports.Host.StartGame(who, params.get('feed') || '', (params.get('darkness') || 'on').toLowerCase(), params.get('fps') === '1', params.get('quality') === 'adaptive');
}

// Keyboard, mouse and touch become the engine's input calls. Nothing here sends a like: E latches in the game, and a held
// press charges on a wingman there; the game likes when its own checks pass.
function installGameInput() {
  const screen = document.getElementById('term');
  const cell = () => {
    const r = term.element.querySelector('.xterm-screen').getBoundingClientRect();
    return { left: r.left, top: r.top, w: r.width / term.cols, h: r.height / term.rows };
  };
  const toCell = (cx, cy) => { const c = cell(); return [(cx - c.left) / c.w, (cy - c.top) / c.h]; };
  const held = new Set();
  // Capture phase on window: nothing inside the page (xterm's helper textarea, a focused button) can swallow a game key first.
  const typing = () => page.isTextEntry(document.activeElement);
  window.addEventListener('keydown', (e) => {
    const code = page.gameKey(e);
    if (!code || typing() || e.ctrlKey || e.metaKey || e.altKey) return;
    e.preventDefault();
    if (e.repeat) return;
    held.add(code);
    exports.Host.Key(code, true);
  }, true);
  window.addEventListener('keyup', (e) => {
    const code = page.gameKey(e);
    if (!code) return;
    held.delete(code);
    exports.Host.Key(code, false);
  }, true);
  window.addEventListener('blur', () => { for (const c of held) exports.Host.Key(c, false); held.clear(); exports.Host.HoldEnd(); });
  // the start screen's Play button keeps focus after the click; the game listens to the window, not to it
  if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();

  const gestures = page.createGestures({
    holdStart: (c) => exports.Host.HoldStart(...c),
    holdEnd: () => exports.Host.HoldEnd(),
    tap: (c) => exports.Host.Tap(...c),
    bomb: () => exports.Host.Bomb(),
  });

  // mouse: the plane eases toward the pointer; right click bombs; holding the left button on a wingman likes it (a click does not)
  screen.addEventListener('mousemove', (e) => exports.Host.Pointer(...toCell(e.clientX, e.clientY)));
  screen.addEventListener('mouseleave', () => { exports.Host.PointerGone(); gestures.mouseLeave(); });
  // Capture phase, stopped here: xterm's own drag-to-select listens on an inner element and must never see the press.
  screen.addEventListener('mousedown', (e) => {
    e.preventDefault();
    e.stopPropagation();
    gestures.mouseDown(e.button, toCell(e.clientX, e.clientY));
  }, true);
  window.addEventListener('mouseup', (e) => gestures.mouseUp(e.button), true);
  screen.addEventListener('dblclick', (e) => { e.preventDefault(); e.stopPropagation(); }, true);
  term.onSelectionChange(() => { if (term.hasSelection()) term.clearSelection(); });
  screen.addEventListener('contextmenu', (e) => e.preventDefault());

  // touch: a relative drag moves, holding a finger on a wingman likes it, a tap does nothing, a two-finger tap bombs
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
    gestures.touchMove(1, t.clientX - last.x, t.clientY - last.y);
    exports.Host.Drag((t.clientX - last.x) / c.w, (t.clientY - last.y) / c.h);
    last = { x: t.clientX, y: t.clientY };
  }, opt);
  screen.addEventListener('touchend', (e) => {
    e.preventDefault();
    gestures.touchEnd(e.touches.length);
    if (e.touches.length === 0) last = null;
  }, opt);
  screen.addEventListener('touchcancel', () => { gestures.touchCancel(); last = null; });
}
