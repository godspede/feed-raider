# Feed Raider

A vertical shoot-'em-up that plays like scrolling Bluesky. It runs in your browser, which draws the game in a terminal-style emulator (xterm.js); you don't need a terminal. There is also a real terminal version.

**Play: <https://feed-raider.famelos.com/>**

It is for Bluesky users who would like their feed to fly at them, and for anyone who wants a small arcade game made of real posts. Playing in the browser needs no install. To run the terminal version you need the .NET 10 SDK and a truecolour terminal of about 80x24 or larger:

    git clone https://github.com/godspede/feed-raider && cd feed-raider && dotnet run --project src/FeedRaider.Console

Posts from the feed you picked scroll down the field behind the fight, as cards you can read: the author's name and handle, the text, the post's pictures, a note for a video, a link card, a quoted post or a picture's description, and the like and repost counts. A post's pictures (up to four) are drawn under its words, small and blocky, and dimmed like the author's avatar so the fight stands out over them; a picture that has not arrived yet, or that could not be read, is an empty box of the same size. The words are drawn over the card, so your ship, a shot, an enemy, a pickup or a blast hides the letters it passes over, and all of them blink out for the moment of a screen flash. A wide character (Japanese, say) takes two cells; an emoji or any other character the terminal has no cell for is left out of the text and never shown as a `?`.

You like a post by resting the ship on its heart for three seconds, and repost it by resting on its repost button: the icon grows and brightens beat by beat, and flying off early drains it with nothing done. A finished hold puts your shield back if it is down, powers up your guns for five seconds and takes a quarter off every living part of the enemies in the fight (it never destroys a part), once for each post. A post with 1000 likes or more is a gold card: it has a gold top edge and, at the end of its name row, a star and its like count (1.8K, say). Finishing a like or a repost on a gold card also gives one bomb, up to three, and BANGER +BOMB pops up (at three bombs it says only BANGER); each gold card pays once. When a like finishes, signed in or not, the game looks up the author's own next reply to that post, one level deep, through your session or the public API. If there is one, it is the next card to scroll in, ahead of every waiting post, with an arrow and the word thread under its words; liking that card brings the author's reply to it after it, and a repost brings none. If there is no reply, if it is by someone else, if it is adult or graphic, if it carries a video, a link card or a quote, if its words are only a link, or if the lookup fails or takes more than six seconds, nothing appears and no message shows.

Each post that scrolls off the bottom goes into the feed log, the last 32 of them, with its like and repost icons, lit if you did that. From there you can undo an accidental like or repost, or like or repost a post you missed. On a wide screen the log is the side panel on the left, empty until the posts you fly past fill it. A phone-sized screen shows just the marked post on one line under the field, and the pause menu's Feed log list has the rest.

Spam attacks in waves. One enemy in each spam wave, the Carrier, shows the post's text in a speech box above it, in red on black. A rude post makes no enemy and no wave: it waits until a boss, a mini-boss or a lesser gunship says it, in the same speech box. A spam enemy's post is one that carries a spam or engagement-farming label, or the newest post of an account labelled spam. The name over the box is the label, the labeler's own word in capitals (SPAM, ENGAGEMENT-FARMING or RUDE). A reply that a thread's owner hid has no label and is named HIDDEN REPLY; any other post with no label is named for what it is, SPAM or RUDE.

Once a rude post has arrived, the bosses speak rude posts: a new one first, and when none is new they go round the ones already said in the run (the last 32 are kept), never the same one twice in a row while another is known. Six of the ten bosses choose which post to say next in a way of their own, and five of those also wait a time of their own between posts; the Gunship, the Pile-On and the Doomscroll take the oldest first. The Mirror says back posts already said, marked RE:, and takes a new one only when it has none to say back. The Thread reads a post in short numbered pages, the Spotlight holds up a new post each time its beam locks on, and the Schism's two halves cut each other off. At the start of a run the game reads back the last half hour of hidden replies, so there are usually rude posts waiting before the first boss. A boss says a line of its own only before any rude post has arrived, and as its last word when it is killed.

The game gets harder in stages, shown at the top right. A stage lasts 50 seconds of flying, and every enemy you kill takes half a second off it. Each new stage brings more enemies, bigger waves and faster bolts, up to stage 10. From the Mine on, each new kind of enemy is named on screen when the first one arrives; the table below lists them by stage. Four of them:

Mines are amber spiked orbs that drift down and shrug off shots; a bomb clears them. They are dark and harmless until they light up. Divers are lime darts that stop, shake and draw a line to where you are, then charge along it. Splitters are pink blobs that crack as you hit them and burst into two small shards that fly out to the sides. Turrets are green pods that glow while an aiming mark closes on your ship, then fire a quick aimed burst at that spot.

There are ten stages, each ended by a boss, and after stage 10 the bosses come round again in order. Between bosses a lesser gunship flies in now and then, as the last column of the table below shows. A destroyed part drops a pickup 35% of the time, and a boss you kill always drops one. Three colours mean the same thing on every enemy and boss. Blue is a shield and the thing that makes it: a blue part takes no damage. White is about to hurt: an attack leaves that spot within about a second. Gold on crimson is where to shoot.

| Stage | New to meet | What it asks of you | Boss | Lesser gunships |
| --- | --- | --- | --- | --- |
| 1 | Grunt, Carrier; halfway through, Mine | shoot, dodge slow bolts; then steer round a mine | Gunship | none |
| 2 | Diver, Warden (mini-boss) | leave the marked spot; kill the shield-giver first | Bulwark | none |
| 3 | Splitter, Sower | kill it where its shards will not reach you; find the gap in a curtain | Shutter | none |
| 4 | Turret, Flanker | kill it between bursts; change height | Thread | two |
| 5 | Lobber | keep off marked ground | Spotlight | two |
| 6 | Nest (mini-boss) with Gnats | kill the source before the swarm | Brood | two |
| 7 | Yoke (mini-boss), Lurk | break a bar or go round it; strike in a short window | Schism | three |
| 8 | Chorus (mini-boss) | make your own gap in a row of fire, or go round its end | Pile-On | three |
| 9 | Glint | do not stand under it while it is lit | Mirror | one every 20 s, two at once |
| 10 | Fuse | kill it before the count ends, or leave | Doomscroll | one every 14 s, two at once |

**When your ship dies.** You fly with one shield: a hit takes it down, and if you are hit while it is down the ship dies. A shield pickup puts it back, and a new ship arrives with its shield up. Dying keeps the stage you reached and only sets back half your progress toward the next one.

## Controls

| | |
|---|---|
| Move | WASD or arrow keys; mouse (the ship follows the pointer); touch drag |
| Fire | Space (hold), F toggles auto-fire; the mouse and touch fire automatically |
| Bomb | B; right click; two-finger tap |
| Like or repost a post | rest the ship on a heart or repost button for three seconds (described above) |
| Feed log | E likes and R reposts the marked post; [ and ] move the mark |
| Feed speed | how fast the posts scroll, from 1 to 5 (3 is the usual): - slows it and + speeds it up, or the slider in the pause menu |
| Pause | Esc, or the gear beside the stage |
| Sound | M mutes all sound, in the terminal version too; the pause menu has a Sound button and the volume slider |

In the terminal version Esc, Q or Ctrl+C quits; there is no pause.

Address options: `?sfx=off` turns the sound effects off, `?music=off` the music. `?mode=spectator` shows the spectator view, a watch-only screen for a handle's feed. In it, enemies made from posts walk in from the edges toward small walking figures, which are the accounts the handle follows. `?darkness=on`, `?darkness=off` or `?darkness=demo` (`--darkness on|off|demo` in the terminal version) sets where those posts come from: demo uses built-in sample posts and off shows no enemies. `?feed-speed=N` sets the feed speed for that visit. `?debug=feed` lists the first posts of a feed with a like button, a repost button and an undo for each, for checking the sign-in. `?handle=name.example` plays as that handle without asking for one on the start page. `?stage=6` starts at a later stage, for trying out the later enemies. `?boss=4` opens the run on the warning of stage 4's boss. `?gates=off` plays the stages with no bosses and no lesser gunships.

The flags for the sound and the feed speed are listed under Build and run locally. The sound plays on the machine the game runs on, so over SSH you hear the remote machine. It plays on Windows only (NAudio, no install needed); on other systems, or with no audio device, the game runs silent.

## The start page

The start page is a few big buttons floating over a scripted tour of the game, about a minute and a half long, that then starts over: built-in posts, some with built-in pictures, scroll by, a first wave comes in, the ship rests on a heart until the like lands, a mini-boss arrives, and three of the ten bosses follow with a later stage's enemies after each. The tour's ship takes hits but cannot die, unlike the ship in a game you play. The HUD, key hints and side panels are not drawn under the start page. The tour needs no network or account, saves no score and has no sound effects, only the start page's music; its like is only a mark on a built-in post, with nothing sent. With reduced motion set, the tour runs at a quarter speed. After two minutes with nothing done on the page the tour sleeps, still on its last frame, to save battery; the next key press, pointer move or press, touch or scroll wakes it, and it carries on from there. The buttons are three ways to play and a fourth, **How to play**, which opens the controls in a dialog. Choosing a way to play ends the tour and starts a fresh game; Back to start page, in the pause menu, starts the tour again.

The three ways to play:

Sign in with Bluesky plays with your own feed; Signing in, below, says what it reads and writes. Play as someone needs no account: the button opens the handle box, and when you type a handle the posts are those of the people that handle follows. An empty box asks for a handle. Random is one click with no account; its posts come from public Bluesky comedy feeds. It mixes the "LMAO (Comedy/Humor)" and "Daily Bangers" feeds, turns to a comedy starter-pack feed when both run short, and plays Discover if all three fail. It also leaves out any post that carries a label, on the post or on its author's account, whether from Bluesky's moderation or set by the author. Of the posts with words it takes only those with nothing attached and at least 20 characters; a post with pictures is taken as it is. `?feed=random` in the address starts it directly.

## Signing in

Signing in uses Bluesky's OAuth, and the page talks to your account directly from your browser.

- It reads your Following timeline. Add `?feed=discover` or `?feed=foryou` to the address for those feeds instead. If it can't read your timeline, the game says why on screen and shows Discover.
- It writes to your account through your own session: one like or one repost of a post for each finished hold or feed log press, the same record the Bluesky app makes, and its removal when you undo it in the feed log. Nothing else is written. Not signed in, the button lights up in the game and nothing is sent.
- The sign-in asks Bluesky for permission to read your feeds and a post's thread, and to make and remove likes and reposts, and no other kind of record. The game uses the thread permission only for the reply lookup after a like; it writes nothing for it. A sign-in from before the game could repost may only like: its repost buttons show a small lock, and the pause menu offers **Sign in again**.
- Nothing is stored on any server. The session lives in your browser, and Sign out in the pause menu revokes it.

## Privacy

The game shows real Bluesky posts. Posts come from the feed you picked: top-level posts with some text beyond a link or a link to another post, or with pictures; replies and reposts are left out. A video, a link card and a quoted post, with any pictures the quote carries, are not fetched or shown beyond the card's note. Signed out (Play as someone, Random, the spectator view), authors who carry Bluesky's `!no-unauthenticated` label, which asks not to be shown to signed-out viewers, are left out: their posts from the feed and the speech boxes, and they themselves from the accounts the spectator view shows. Posts labelled `porn`, `sexual`, `nudity` or `graphic-media`, and posts by authors so labelled, are left out in every mode: from the feed, so their pictures are not fetched, and from the speech boxes and the spectator view's enemies. A followed account's new post reaches the spectator view as it is written, when only a label its author put on the post itself can be known; it is left out if that is one of the four. The posts in the speech boxes, and those the spectator view's enemies are made from, are by strangers: they are drawn from Bluesky's public moderation label stream (`mod.bsky.app`) and from replies that thread owners hid, read from Bluesky's Jetstream; at the start of a run the last half hour of them is read back as well. The text of each is then read from the public Bluesky API only, so a post the API does not return, because it was deleted or its account was deactivated, suspended or taken down, is not shown. These are shown without the author: mentions (`@...`), links, hashtags, DIDs and anything that looks like a handle or domain name are removed, and the rest of the text is shown whole up to 3000 characters (a longer one is cut short at a word). The game stores none of it: a rude post is kept in memory only while the game runs.

xterm.js (MIT) is served with the page from `xterm/`, not from a CDN. The page looks handles up through plc.directory and the public Bluesky API (`public.api.bsky.app`) and, when you sign in, through `bsky.social`, and asks plc.directory where a `did:plc` author's PDS (the server that holds the author's account) is. In the game, it reads avatars and the pictures of posts only from each author's own PDS: a few at a time, each no larger than 2 MB and given 8 seconds, and never started once it has waited 70 seconds for its turn, shrunk in the page to at most 64 pixels a side and kept only while the page is open. After three reads in a row run out of time, the page turns Pictures off for the visit and says so. The PDS, and for a `did:web` author the host that holds their DID document, must be a plain public https host name (no IP address, port, path or local name such as `.local`), and the page follows no redirect from either or from plc.directory, so a picture or avatar behind one is not read. The page judges the name only: which address a public name resolves to is left to the browser. A PDS sends each picture at full size, not a small copy, so the pictures can use a fair amount of data: up to 10 MB for one card's four pictures and its avatar, and a picture too large to show still costs up to that 2 MB. The start page is different. Once you have typed two characters in its handle box and paused for a fifth of a second, it sends what you typed to the public Bluesky API to suggest accounts, and shows each suggestion's avatar from Bluesky's CDN (`cdn.bsky.app`): the CDN's small thumbnail of the picture the API names, or the full-size one if the thumbnail does not load. Signed in, the start page asks the public Bluesky API for your profile and shows your own avatar from Bluesky's CDN the same way. The Pictures setting in the pause menu turns the avatars and post pictures in the game off; it starts off on a data-saver, slow or cellular connection, where the browser says so. It does not cover the start page's avatars. The browser keeps only your last handle, your best score, your sound settings, your feed speed and your Pictures setting in localStorage, and, after a sign-in, a note of how far it got (the account's DID, the step it reached and when, and the error if it failed), which the page reads for up to thirty minutes and which signing out clears.

Feed Raider is an independent project, not made or endorsed by Bluesky.

## Build and run locally

Building needs the same SDK as the terminal version above.

    dotnet test FeedRaider.slnx                          # tests
    dotnet run --project src/FeedRaider.Console          # terminal version; --sfx off and --music off silence it, --feed-speed N sets the feed speed
    (cd web && npm ci && npm run build)                  # bundles the sign-in code; needs Node 22
    dotnet publish src/FeedRaider.Web -c Release -o out  # browser version
    (cd web && npm run bundle:single -- ../out/wwwroot ../feed-raider.html)  # optional: the browser version as one HTML file to open or send with no server; it has no music unless --music names a JSON list of {role, url}
    cd out/wwwroot && python -m http.server 8000

The publish prints "Publishing without optimizations... wasm-tools"; that is expected and harmless.

Then open <http://localhost:8000/>. Signing in works on localhost and on HTTPS pages only.

The npm step writes the sign-in code from `web/` into `src/FeedRaider.Web/wwwroot/atproto.js`, which is not committed and which `main.js` imports, so it must come before `dotnet publish`. `npm test` in `web/` runs the page tests, and `npm run bench` there measures frame time in a throttled, phone-sized headless browser (it needs playwright-core: installed, or named by `PLAYWRIGHT_CORE`).

`npm run e2e:start` there is the browser check that every way into a run really shows the game (`?handle=`, `?feed=random`, Play random, Play as someone, and Back to start page followed by Play random): it bundles the sign-in code, publishes the web host to a temp folder, serves it from a free port on 127.0.0.1 and opens it in a headless, muted Firefox with every request to another host stubbed, so it needs no network and does not talk to Bluesky. It exits non-zero when the start page is still on screen over a run, or the gear or the start buttons are not on top. It needs `npm ci` first and playwright-core, found as for the bench, with Playwright's own build of Firefox (`npx playwright-core install firefox` fetches it; `FIREFOX_EXE` names another such build to use). The CI workflow runs no browser tests, so this one is run by hand.

## Credits

Some of the music was made with Google Gemini, and some with [ACE-Step 1.5](https://github.com/ace-step/ACE-Step-1.5), an open-source music model run locally. The prompts behind every track are in [docs/music/prompts.md](docs/music/prompts.md).

The music is a start page theme, a shuffled rotation of fifteen gameplay tracks, a game-over theme and a boss theme. The Bulwark, the Shutter and the Schism have themes of their own (the Shutter has three and the Schism two, and one of them plays each time); every other boss plays the shared boss theme. The file names are in `src/FeedRaider.Web/wwwroot/music/tracks.json`. A track is about 1 MB to download (a minute of music is about 0.7 MB), and a run downloads three as it starts: the one that plays, the next in the rotation and the stage's boss theme, about 3 MB together, after the start page's 0.8 MB theme. Muted with M, the page downloads no further track, though one it has begun to download still finishes.

Contributions are not accepted yet.
See [CONTRIBUTING.md](CONTRIBUTING.md).

Licensed under the Apache License 2.0; see `LICENSE`, and `NOTICE` for the third-party code the browser version bundles. To report a security problem, see `SECURITY.md`.

Forged on construct/famelos
