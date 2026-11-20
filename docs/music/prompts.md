# Music prompts

Every track in the game came from a text prompt. This page records which prompt made which track, so the next batch can start from what worked. The files are listed in `src/FeedRaider.Web/wwwroot/music/tracks.json`.

Two models made the music. Google Gemini made the start page theme, the shared boss theme, the game-over theme and the first six gameplay tracks from one paragraph each. ACE-Step 1.5, an open-source music model (its turbo model, 8 steps, no language model, run locally), made the other fifteen: nine gameplay tracks and six boss themes. ACE-Step takes a caption of comma-separated tags, a BPM, a key, a time signature, and structure tags such as `[Drop]` and `[Build-up]`. For ACE-Step we made several takes of each prompt and kept the ones that sounded right. A take is one generation of the prompt, with its own seed, and takes are numbered from one in the order they were made. The seed in the table is the one that made the shipped file, and the take is its number. The two psytrance files carry no take number because their take is not recorded.

How each track was matched to its prompt: four ACE-Step gameplay files, `fr-play-afro-house.mp3`, `fr-play-trance.mp3`, `fr-play-psytrance-1.mp3` and `fr-play-psytrance-2.mp3`, kept the name of the take they came from. The other ACE-Step files were trimmed or re-encoded after they were made, so each was matched by the prompt, seed and take written down when the take was kept, and by the order the files were added to the game. The Gemini files are matched by name: each prompt was written for that slot. Every one of the 24 tracks has a recorded prompt.

## The tracks

| File | Role | Model | Prompt |
|---|---|---|---|
| `fr-start.mp3` | start | Google Gemini | Start page |
| `fr-play-filter-house.mp3` | gameplay | Google Gemini | Filter house |
| `fr-play-tech-house.mp3` | gameplay | Google Gemini | Tech house drive |
| `fr-play-uk-garage.mp3` | gameplay | Google Gemini | UK garage two-step |
| `fr-play-electro-house.mp3` | gameplay | Google Gemini | Electro house surge |
| `fr-play-italo-night.mp3` | gameplay | Google Gemini | Italo night drive |
| `fr-play-liquid-dnb.mp3` | gameplay | Google Gemini | Liquid drum and bass |
| `fr-play-afro-house.mp3` | gameplay | ACE-Step 1.5 | 01-afro-house (seed 1385953024, take 2) |
| `fr-play-trance.mp3` | gameplay | ACE-Step 1.5 | 03-trance (seed 1282080570, take 2) |
| `fr-play-psytrance-1.mp3` | gameplay | ACE-Step 1.5 | 08-psytrance (seed 1291859339) |
| `fr-play-psytrance-2.mp3` | gameplay | ACE-Step 1.5 | 08-psytrance (seed 1291859340) |
| `fr-play-goa-trance.mp3` | gameplay | ACE-Step 1.5 | 02-goa-trance (seed 48895971, take 2) |
| `fr-play-acid-trance.mp3` | gameplay | ACE-Step 1.5 | 05-acid-trance (seed 1654244090, take 1) |
| `fr-play-hard-trance-1.mp3` | gameplay | ACE-Step 1.5 | 12-hard-trance (seed 1405622208, take 1) |
| `fr-play-hard-trance-2.mp3` | gameplay | ACE-Step 1.5 | 12-hard-trance (seed 1405622209, take 2) |
| `fr-play-uplifting-psytrance.mp3` | gameplay | ACE-Step 1.5 | 13-uplifting-psytrance (seed 1222738707, take 1) |
| `fr-boss.mp3` | boss | Google Gemini | Boss (shared theme) |
| `fr-boss-bulwark.mp3` | boss | ACE-Step 1.5 | boss-02-bulwark (seed 387420507, take 1) |
| `fr-boss-shutter-1.mp3` | boss | ACE-Step 1.5 | boss-03-shutter (seed 2060260599, take 1) |
| `fr-boss-shutter-2.mp3` | boss | ACE-Step 1.5 | boss-03-shutter (seed 2060260600, take 2) |
| `fr-boss-shutter-3.mp3` | boss | ACE-Step 1.5 | boss-03-shutter (seed 2060260601, take 3) |
| `fr-boss-schism-1.mp3` | boss | ACE-Step 1.5 | boss-07-schism (seed 1238268470, take 2) |
| `fr-boss-schism-2.mp3` | boss | ACE-Step 1.5 | boss-07-schism (seed 1238268471, take 3) |
| `fr-gameover.mp3` | gameover | Google Gemini | Game over |

## The prompts

### Gemini prompts

Made with Google Gemini. Each prompt is one paragraph pasted in as written; Gemini took no seed, key or structure tags.

#### Start page

```
Instrumental deep house track for the start screen of a neon arcade shooter. 122 BPM, a soft, muffled four-on-the-floor kick that sounds like it is coming from the next room, a warm rolling bassline, a shuffling hi-hat, and lush filtered chords with a slow low-pass sweep that keeps opening a little without ever fully dropping. Mood: the club before midnight, waiting to go on. A tiny hint of a retro synth arpeggio, kept far back. No vocals. Must loop seamlessly with no intro, fade-out or big ending. About 60 seconds.
```

#### Filter house

```
Instrumental French filter house track for the gameplay of a fast arcade shooter. 124 BPM, four-on-the-floor kick, shuffling hi-hats, a funky disco bassline and a chopped disco-string sample that a low-pass filter sweeps open and closed. Big, warm, sidechain-pumping groove you can dance to. A short bright synth riff appears now and then as colour, not as the lead. No vocals. Full energy from the first bar, no long intro or fade-out. Leave space in the mids for gunfire and explosions. About 90 seconds.
```

#### Tech house drive

```
Instrumental tech house track for an arcade shooter's main gameplay. 126 BPM, punchy rolling kick, crisp claps, tight percussion loops with congas and rimshots, and a deep, bouncing, rubbery bassline that carries the whole track. Dark and hypnotic with small builds every 16 bars and a hooky stab chord. Club-ready groove, minimal melody. No vocals. Full energy from the first bar, no long intro or fade-out. Keep the midrange clear for sound effects. About 90 seconds.
```

#### UK garage two-step

```
Instrumental UK garage two-step track for a vertical-scrolling shooter. 132 BPM, a skippy, swung two-step drum pattern with shuffled hi-hats and snappy snares, a warm sub-bass that bounces, and lush jazzy organ and pad chords. Late-night city energy, slick and danceable. A hint of a retro synth lead in the background only. No vocals. Full energy from the first bar, no long intro or fade-out. Leave room in the mids for gunfire. About 90 seconds.
```

#### Electro house surge

```
Instrumental electro house track for intense arcade shooter gameplay. 128 BPM, a hard four-on-the-floor kick, a growling, distorted, sidechained saw bass, big clap, and a festival-style build that drops into a bouncy bass groove. Aggressive but fun to move to. A tiny bit of square-wave arpeggio for retro flavour, kept low in the mix. No vocals. Full energy from the first bar, no long intro or fade-out. Keep the midrange open for sound effects. About 90 seconds.
```

#### Italo night drive

```
Instrumental Italo disco and dark disco track for a retro top-down shooter. 118 BPM, a driving octave bassline, a steady disco kick and open hi-hat, handclaps, gated synth toms, and moody analog synth chords under a simple catchy lead. Glamorous, nocturnal, made for a dance floor lit by neon. No vocals. Full energy from the first bar, no long intro or fade-out. Leave the mids clear for explosions and gunfire. About 90 seconds.
```

#### Liquid drum and bass

```
Instrumental liquid drum and bass track for fast arcade shooter gameplay. 172 BPM, a smooth rolling breakbeat with crisp ghost snares, a deep rolling sub-bass, warm Rhodes and pad chords, and a soaring melodic hook. Fast but flowing, the kind you dance to with your head down. No chiptune. No vocals. Full energy from the first bar, no long intro or fade-out. Keep the midrange open for sound effects. About 90 seconds.
```

#### Boss (shared theme)

```
Instrumental peak-time techno and electro house track for a boss fight in a neon arcade shooter, where the boss is a creeping corrupted darkness. 130 BPM, a heavy, distorted four-on-the-floor kick, a menacing, growling, sidechained bassline, metallic percussion, rising noise risers, and a dark, stabbing synth hook that repeats like an alarm. Relentless and danceable, the hardest moment of the night, not orchestral or cinematic. A little glitchy digital stutter as texture. No vocals and no choir. Full energy from the first bar, no long intro or fade-out. Leave space in the mids for explosions and gunfire. About 90 seconds.
```

#### Game over

```
Short instrumental closing track for the game-over screen of a neon arcade shooter. 100 BPM, the after-party: a slow, dubby house groove with a soft kick, a warm, round bassline, a long echoing chord stab, and a wistful filtered pad, like the lights coming up as the club empties. Bittersweet but warm, with a feeling of "one more go". No vocals. Must loop seamlessly with no big ending. About 30 seconds.
```

### ACE-Step 1.5 prompts

The caption, BPM and key were given as separate fields. Every prompt starts on a `[Drop]` so the track has full energy from the first bar, and ends with no outro, because the game crossfades between tracks itself.

#### Afro house

Prompt name `01-afro-house`.

- Caption: afro house, instrumental, warm and tribal, steady four-on-the-floor kick, congas, shakers and djembe percussion loop, deep rolling sub bass, plucked marimba riff, sparse airy pads, hypnotic groove, clean punchy club mix, wide stereo
- BPM: 122
- Key: F# minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 1385953024 (`fr-play-afro-house.mp3`)
- Structure tags (markers with no words under them):

```
[Drop - kick, bass and percussion already full]
[Inst - marimba riff]
[Build-up - percussion roll]
[Drop - full groove]
[Breakdown - bass and shakers only]
[Build-up]
[Drop - full groove]
```

#### Classic trance

Prompt name `03-trance`.

- Caption: uplifting trance, instrumental, driving four-on-the-floor kick, rolling offbeat bassline, supersaw arpeggio, plucked synth sequence, euphoric pads, snare-roll builds, bright energetic club mix, sidechain pumping
- BPM: 138
- Key: A minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 1282080570 (`fr-play-trance.mp3`)
- Structure tags (markers with no words under them):

```
[Drop - kick, rolling bass and arpeggio full]
[Inst - supersaw lead]
[Build-up - snare roll rising]
[Drop]
[Breakdown - pads and arpeggio, kick out]
[Build-up - snare roll rising]
[Drop]
```

#### Full-on psytrance

Prompt name `08-psytrance`.

- Caption: full-on psytrance, instrumental, rolling galloping bassline, punchy kick, offbeat bass pulse, squelchy acid synth lines, trippy arpeggios, hypnotic and relentless, powerful festival dance mix
- BPM: 145
- Key: F minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 1291859339 (`fr-play-psytrance-1.mp3`) and 1291859340 (`fr-play-psytrance-2.mp3`)
- Structure tags (markers with no words under them):

```
[Drop - kick and galloping bass full]
[Inst - acid lead]
[Build-up - rising arpeggio]
[Drop]
[Breakdown - arpeggio and pads, no kick]
[Build-up]
[Drop]
```

#### Goa trance

Prompt name `02-goa-trance`.

- Caption: goa trance, instrumental, driving four-on-the-floor kick, rolling bassline, layered melodic acid leads, eastern-scale arpeggios, bright euphoric, hypnotic and energetic, clean powerful festival mix
- BPM: 142
- Key: E minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 48895971 (`fr-play-goa-trance.mp3`)
- Structure tags (markers with no words under them):

```
[Drop - kick, bass and acid lead full]
[Inst - eastern arpeggio]
[Build-up - rising arpeggio]
[Drop]
[Breakdown - arpeggio and pads, kick out]
[Build-up - rising arpeggio]
[Drop]
```

#### Acid trance

Prompt name `05-acid-trance`.

- Caption: acid trance, instrumental, driving four-on-the-floor kick, squelching 303 acid line, uplifting pads, rolling offbeat bass, bright euphoric, energetic and hypnotic, clean club mix
- BPM: 138
- Key: A minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 1654244090 (`fr-play-acid-trance.mp3`)
- Structure tags (markers with no words under them):

```
[Drop - kick, bass and 303 line full]
[Inst - 303 line]
[Build-up - snare roll rising]
[Drop]
[Breakdown - 303 line and pads, kick out]
[Build-up - snare roll rising]
[Drop]
```

#### Hard trance

Prompt name `12-hard-trance`.

- Caption: hard trance, instrumental, pounding clean four-on-the-floor kick, offbeat bass, soaring supersaw lead, euphoric and driving, energetic powerful club mix, sidechain pumping
- BPM: 145
- Key: D minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 1405622208 (`fr-play-hard-trance-1.mp3`) and 1405622209 (`fr-play-hard-trance-2.mp3`)
- Structure tags (markers with no words under them):

```
[Drop - kick, offbeat bass and soaring lead full]
[Inst - supersaw lead]
[Build-up - snare roll rising]
[Drop]
[Breakdown - lead and pads, kick out]
[Build-up - snare roll rising]
[Drop]
```

#### Uplifting psytrance

Prompt name `13-uplifting-psytrance`.

- Caption: uplifting psytrance, instrumental, punchy four-on-the-floor kick, rolling galloping psytrance bassline, soaring trance melodies, bright arpeggios, euphoric and relentless, clean festival mix
- BPM: 142
- Key: A minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 1222738707 (`fr-play-uplifting-psytrance.mp3`)
- Structure tags (markers with no words under them):

```
[Drop - kick, galloping bass and melody full]
[Inst - trance melody]
[Build-up - rising arpeggio]
[Drop]
[Breakdown - melody and pads, kick out]
[Build-up - rising arpeggio]
[Drop]
```

#### Bulwark theme: peak-time techno

Prompt name `boss-02-bulwark` (a revised prompt; the first version of this prompt did not make the shipped files).

- Caption: peak-time techno, instrumental, marching four-on-the-floor kick, heavy clean synth stabs, rolling bassline, driving and disciplined, minor key riff, tight punchy club mix
- BPM: 132
- Key: E minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 387420507 (`fr-boss-bulwark.mp3`)
- Structure tags (markers with no words under them):

```
[Drop - marching kick and stabs full]
[Inst - stab riff]
[Build-up - snare march]
[Drop]
[Breakdown - bassline and stabs]
[Build-up - snare march]
[Drop]
```

#### Shutter themes: acid trance

Prompt name `boss-03-shutter` (a revised prompt; the first version of this prompt did not make the shipped files).

- Caption: acid trance, instrumental, squelching 303 acid line, punchy four-on-the-floor kick, filter opening and snapping shut, rolling bass, sharp claps, hypnotic and tense, energetic rave mix
- BPM: 138
- Key: A minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 2060260599, 2060260600 and 2060260601 (`fr-boss-shutter-1.mp3`, `-2`, `-3`, in that order)
- Structure tags (markers with no words under them):

```
[Drop - acid line and kick full]
[Inst - hi-hat pattern]
[Build-up - filter opening]
[Drop]
[Breakdown - acid line alone]
[Build-up - filter opening]
[Drop]
```

#### Schism themes: tech trance

Prompt name `boss-07-schism` (a revised prompt; the first version of this prompt did not make the shipped files).

- Caption: tech trance, instrumental, punchy four-on-the-floor kick, two synth riffs trading call and response, one cold and one warm, rolling bass, driving and tense, clean powerful club mix
- BPM: 142
- Key: C# minor
- Time signature: 4/4
- Length: 90 seconds
- Seeds of the shipped files: 1238268470 (`fr-boss-schism-1.mp3`) and 1238268471 (`fr-boss-schism-2.mp3`)
- Structure tags (markers with no words under them):

```
[Drop - kick, bass and cold riff full]
[Inst - call and response]
[Build-up - snare roll rising]
[Drop]
[Breakdown - warm riff and bass, kick out]
[Build-up - snare roll rising]
[Drop]
```

## Writing a prompt for a danceable beat

What our notes say worked, and what we dropped:

- Ask for a steady four-on-the-floor kick by name. Nine of the ten ACE-Step prompts we kept say "four-on-the-floor kick" in the caption (the full-on psytrance caption says "punchy kick" instead), and later prompts put "steady", "punchy" or "pounding" in front of it.
- Put the beat first. The Gemini prompts open with the genre and the arcade-game setting, then give the BPM, the kick and the bassline, and keep any retro synth colour to a hint far in the background; the ACE-Step captions start with the genre, then the kick and bass, then the lead.
- Name the genre and the drum machine or rhythm: the acid trance captions above name the 303. A named instrument or rhythm gives the model more to go on than a mood word does.
- Set the tempo in the BPM field.
- Tempos we shipped run from 100 (the game-over theme) to 172 (liquid drum and bass); the slowest gameplay track is 118 (Italo night drive), and most gameplay sits between 122 and 145 BPM, all in 4/4. Check the tempo of what comes back. The 165 BPM jungle and 174 BPM neurofunk takes measured about two thirds of the requested tempo, so we did not use jungle or neurofunk; psytrance asked at 145 measured 139.7.
- The turbo model held the beat better than the larger sft model, which came out looser and sometimes at the wrong tempo. Use eight steps; more steps changed nothing.
- Start every track with a `[Drop]` and leave out the outro. Put the breakdown inside the track, but keep one voice playing (an arpeggio, the pads) so the track never goes silent, and "kick out" in the tag where the kick should drop for a bar or two.
- Say "instrumental" once in the caption and leave words out of the structure tags. Spell out "vocal-free" for genres that normally have vocals.
- Leave out "orchestral" and "cinematic": the model takes them literally and adds strings.
- In an ACE-Step caption, leave the game out: words like "arcade shooter" add noise. Mood comes from the genre and the production words ("clean", "punchy", "sidechain pumping").
- Add "never half-time" when asking for fast drum and bass.
- Kept: house, tech house, UK garage, electro house, Italo disco, liquid drum and bass, afro house, trance, tech trance, psytrance, goa, acid and hard trance, and peak-time techno for a boss.
- Set aside: the retro-chiptune colour (the first Gemini takes asked for it and we dropped it from the ACE-Step prompts; we found even the later takes mostly too much like chiptune); the first Gemini batch of ten (chiptune-led, 70 to 174 BPM, with calm, victory and terminal-mode tracks), replaced by the dance-first prompts above, and four more Gemini takes that did not ship (neon floor, dogfight disco, breakbeat strafing run, outrun the feed); and eleven ACE-Step prompts (Chicago house, baile funk, amapiano, jungle, moombahton, Eurodance, Jersey club, melodic techno, boogie funk, and the hard techno and neurofunk boss prompts), which are not in the game. Our notes do not record why each was set aside, so this page does not guess.
- Play each take under the game's sound effects before keeping it. A take can match the BPM and still not be danceable, and the model cannot be asked to leave room for gunfire.
