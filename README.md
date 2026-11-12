# Feed Raider

A vertical shoot-'em-up that plays like scrolling Bluesky. It runs in your browser, which draws the game in a terminal-style emulator (xterm.js); you don't need a terminal. There is also a real terminal version.

**Play: <https://feed-raider.famelos.com/>**

It is for Bluesky users who would like their feed to fly at them, and for anyone who wants a small arcade game made of real posts. Playing in the browser needs no install. To run the terminal version you need the .NET 10 SDK and a truecolor terminal of about 80x24 or larger:

    git clone https://github.com/godspede/feed-raider && cd feed-raider && dotnet run --project src/FeedRaider.Console

Spam and rude replies attack in waves. The ones that carry text (a spam carrier, a rude gunship) speak in the same comm box the wingmen use, but above them, in red on black, with the label (SPAM, RUDE, the labeler's own word, or HIDDEN REPLY) where a wingman's name would be. Posts from your Bluesky feed fly in as wingmen and talk to you. Fly to one and press like to like the post for real and get a power-up. If the author replied to it in their own thread, the reply opens in the same bubble under the post, and the next press likes that one, so you can read the thread as long as it goes on. Signed out, the press plays the same effects and opens the next post but writes nothing; the first one says to sign in, later ones stay quiet. A post with 1000+ likes drops a screen-clearing bomb.

The game gets harder in stages, shown at the top right. A stage lasts about 50 seconds of flying, less if you shoot a lot. Each new stage brings more enemies, tougher ones and faster bullets, up to stage 10, and stages 2 to 5 each bring a new kind of enemy, named on screen when it arrives:

- **Mines** (amber spiked orbs) drift down and shrug off bullets; fly around them, or bomb them. They are dark and harmless until they light up.
- **Divers** (lime darts) stop, shake and draw a line to where you are, then charge along it. Move off the line.
- **Splitters** (pink blobs) crack as you hit them and burst into two small shards that fly out to the sides.
- **Turrets** (teal pods) glow and close a target on you, then fire a quick aimed burst.

Losing your last heart keeps the stage you reached and only sets back half your progress toward the next one.

## Controls

| | |
|---|---|
| Move | WASD or arrow keys; mouse (the plane follows the pointer); touch drag |
| Fire | Space (hold), F toggles auto-fire; the mouse and touch fire automatically |
| Bomb | B; right click; two-finger tap |
| Like a wingman's post | E; or hold the left button or a finger on (or near) the wingman for about half a second until its ring fills. A click or tap never likes; letting go early cancels |
| Pause | Esc, or the gear beside the stage: the pause menu holds the sound controls, sign-in and the way back to the main menu |
| Sound | M mutes all sound, in the terminal version too; the volume slider is in the pause menu |

Address options: `?sfx=off` turns the sound effects off, `?music=off` the music. `?mode=spectator` shows a watch-only crowd view of a handle's feed. The spectator view has its own cast: the people the handle follows are guys, and the stranger posts drawn from the label stream are monsters, where the game has wingmen and enemies. `?debug=feed` lists the first posts of a feed with a like button each, for checking the sign-in. `?stage=6` starts at a later stage, for trying out the later enemies.

The terminal version has the same sound as the web: the music tracks and the synthesised effects, and M mutes everything. `--sfx off` and `--music off` turn a part off, like the address options. The sound plays on the machine the game runs on, so over SSH you hear the remote machine, not your own. It plays on Windows only for now (NAudio, no install needed); on other systems, or with no audio device, the game runs silent.

## Signing in

The start page has three choices.

- **Sign in with Bluesky** plays with your own feed and lets you like posts for real.
- **Play as someone** needs no account: type a handle and the wingmen are the posts of the people that handle follows. An empty box asks for a handle. Liking is off.
- **Random** is one click, no account: posts from public Bluesky comedy feeds. It mixes the "LMAO (Comedy/Humor)" and "Daily Bangers" feeds, turns to a comedy starter-pack feed when both run short, and plays Discover if all three fail. It also leaves out any post that carries a label, on the post or on its author's account, whether from Bluesky's moderation or set by the author, and a thread reply that carries one ends the thread. Liking is off. `?feed=random` in the address starts it directly.

Signing in uses Bluesky's OAuth, and the page talks to your account directly from your browser.

- It reads your Following timeline. Add `?feed=discover` or `?feed=foryou` to the address for those feeds instead. If it can't read your timeline, the game says why on screen and shows Discover.
- The only thing it writes is a like, and only when you press like on a wingman, through your own session.
- To open the next post of a thread it reads that thread (`getPostThread`) from Bluesky's public service, or through your own session when you are signed in. It keeps nothing from it.
- Nothing is stored on any server. The session lives in your browser and "sign out" revokes it.

## Privacy

The game shows real Bluesky posts. Wingmen are plain single posts from the feed you picked: text of at least 20 characters with no attachment of any kind (picture, video, link card, quote), not just a link or a link to another post, not a reply and not a repost. The one reply that is shown is the author's own, opened under their post in the thread follow, and it must be plain by the same rule. Signed out (Play as someone, Random), posts by authors who carry Bluesky's `!no-unauthenticated` label, which asks not to be shown to logged-out viewers, are left out. Posts, or posts by authors, labelled `porn`, `sexual`, `nudity` or `graphic-media` are left out in every mode. The enemies include posts by strangers, drawn from Bluesky's public moderation label stream (`mod.bsky.app`) and from replies that thread owners hid, read from Bluesky's Jetstream. These are shown without the author: mentions (`@...`), links, hashtags, DIDs and anything that looks like a handle or domain name is removed, the rest of the text may be shown whole, and the game stores none of it.

xterm.js (MIT) is served with the page from `xterm/`, so no third-party CDN is asked for anything. The page looks handles up through plc.directory and the public Bluesky API, and reads avatars from each author's own PDS. The browser keeps only your last handle, your best score and your sound settings in localStorage.

Feed Raider is an independent project, not made or endorsed by Bluesky.

## Build and run locally

You need the .NET 10 SDK.

    dotnet test FeedRaider.slnx                          # tests
    dotnet run --project src/FeedRaider.Console          # terminal version (truecolor terminal, about 80x24 or larger; add --sfx off / --music off to silence it)
    (cd web && npm ci && npm run build)                  # bundles the sign-in code; needs Node 22
    dotnet publish src/FeedRaider.Web -c Release -o out  # browser version
    cd out/wwwroot && python -m http.server 8000

The publish prints "Publishing without optimizations... wasm-tools"; that is expected and harmless.

Then open <http://localhost:8000/>. Signing in works on localhost and on HTTPS pages only.

The sign-in code is bundled from `web/` into `src/FeedRaider.Web/wwwroot/atproto.js`, which is not committed and which `main.js` imports, so the npm step must come before `dotnet publish`. `npm test` in `web/` runs the page tests, and `npm run bench` there measures frame time in a throttled, phone-sized headless browser (it needs playwright-core: installed, or named by `PLAYWRIGHT_CORE`).

## Credits

The music was made with Google Gemini.

Sound effects are synthesised by the game, with no audio files, and follow the same mute and volume controls. The music is a menu theme, a boss theme and a shuffled rotation of gameplay tracks.

Contributions are not accepted yet.

See [CONTRIBUTING.md](CONTRIBUTING.md).

Licensed under the Apache License 2.0; see `LICENSE`. To report a security problem, see `SECURITY.md`.

Forged on construct/famelos
