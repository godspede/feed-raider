# Feed Raider

A vertical shoot-'em-up that plays like scrolling Bluesky. It runs in your browser, which draws the game in a terminal-style emulator (xterm.js); you don't need a terminal. There is also a real terminal version.

**Play: <https://godspede.github.io/feed-raider/>**

Spam and rude replies attack in waves. Posts from your Bluesky feed fly in as wingmen and talk to you. Fly to one and press like to like the post for real and get a power-up. A post with 1000+ likes drops a screen-clearing bomb.

## Controls

| | |
|---|---|
| Move | WASD or arrow keys; mouse (the plane follows the pointer); touch drag |
| Fire | Space (hold), F toggles auto-fire; the mouse and touch fire automatically |
| Bomb | B; right click; two-finger tap |
| Like a wingman's post | E; or hold the left button or a finger on (or near) the wingman for about half a second until its ring fills. A click or tap never likes; letting go early cancels |
| Music | M mutes, or use the speaker button and slider |

Address options: `?sfx=off` turns the sound effects off, `?music=off` the music. `?mode=spectator` shows a watch-only crowd view of a handle's feed. `?debug=feed` lists the first posts of a feed with a like button each, for checking the sign-in.

## Signing in

Pick one on the start page. "Sign in with Bluesky" plays with your own feed and lets you like posts for real. "Play as guest" needs no account: the wingmen are the posts of the people a handle you type follows, or the public Discover feed if you leave the box empty, and liking is off. Signing in uses Bluesky's OAuth, and the page talks to your account directly from your browser.

- It reads your Following timeline. Add `?feed=discover` or `?feed=foryou` to the address for those feeds instead. If it can't read your timeline, the game says why on screen and shows Discover.
- The only thing it writes is a like, and only when you press like on a wingman, through your own session.
- Nothing is stored on any server. The session lives in your browser and "sign out" revokes it.

## Privacy

The game shows real Bluesky posts. Wingmen are posts from your feed. The enemies include posts by strangers, drawn from Bluesky's public moderation label stream (`mod.bsky.app`) and from replies that thread owners hid, read from Bluesky's Jetstream. These are shown without the author: mentions (`@...`), links, hashtags, DIDs and anything that looks like a handle or domain name is removed, the text is cut to about 60 characters, and the game stores none of it.

The page loads xterm.js from cdn.jsdelivr.net (pinned, with subresource integrity hashes) and looks handles up through plc.directory and the public Bluesky API. The browser keeps only your last handle, your best score and your sound settings in localStorage. Sign-in is OAuth, and the session is held by the ATProto library in your browser, not by this project.

Feed Raider is an independent project, not made or endorsed by Bluesky.

## Build and run locally

You need the .NET 10 SDK.

    dotnet test FeedRaider.slnx                          # tests
    dotnet run --project src/FeedRaider.Console          # terminal version (truecolor terminal, about 80x24 or larger)
    (cd web && npm ci && npm run build)                  # bundles the sign-in code; needs Node 22
    dotnet publish src/FeedRaider.Web -c Release -o out  # browser version
    cd out/wwwroot && python -m http.server 8000

The publish prints "Publishing without optimizations... wasm-tools"; that is expected and harmless.

Then open <http://localhost:8000/>. Signing in works on localhost and on HTTPS pages only.

The sign-in code is bundled from `web/` into `src/FeedRaider.Web/wwwroot/atproto.js`, which is not committed and which `main.js` imports, so the npm step must come before `dotnet publish`. `npm test` in `web/` runs the page tests.

## Credits

The music was made with Google Gemini.

Sound effects are synthesised in the browser, with no audio files, and follow the same mute and volume controls. The music is a menu theme, a boss theme and a shuffled rotation of gameplay tracks.

Contributions are not accepted yet.

See [CONTRIBUTING.md](CONTRIBUTING.md).

Licensed under the Apache License 2.0; see `LICENSE`. To report a security problem, see `SECURITY.md`.

Forged on construct/famelos
