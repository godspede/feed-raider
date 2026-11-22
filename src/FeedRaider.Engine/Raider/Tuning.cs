namespace FeedRaider.Engine.Raider;

/// <summary>
/// Every number that shapes how Feed Raider plays and feels. Frames are sim frames at <see cref="Fps"/>;
/// distances are field pixels (one terminal cell is 2x2 pixels). Nothing else in the game hard-codes these.
/// </summary>
public static class Tuning
{
    // --- timing and field ---
    public const int Fps = 30;
    public const int FieldMaxCols = 64;
    public const int FieldMinCols = 30, FieldMinRows = 16;
    /// <summary>The two side panels (feed log left, how-to-play right) are mirror images: each is at most
    /// <c>SidePanelMaxCols</c> wide, starts <c>SideGapCols</c> from the field, keeps one column from the window edge, and
    /// shows only when it can be <c>SidePanelMinCols</c> wide.</summary>
    public const int SidePanelMaxCols = 44, SidePanelMinCols = 30, SideGapCols = 2;

    // --- background: how fast the stars drift (pixels per frame), in three layers. The cards move a pixel every five frames at the
    // default feed speed, slow enough to read; the near and far layers run at and above the cards' pace, so the field reads as flying
    // up the page while the posts stay readable ---
    public const double ScrollStars = 0.06;           // the faint dust, furthest back
    public const double ScrollStarsFar = 0.2;         // the cards' own pace at the default feed speed, so the far stars sit with the posts
    public const double ScrollStarsNear = 0.6;        // three times the cards' 0.2 at the default feed speed, so the eye reads speed from it
    public const int StarsFarPixels = 400;            // one far star to this many pixels of sky: thick enough to read as a layer between the cards
    public const int StarsNearPixels = 900;           // one near star to this many: sparser than the far ones, as near things are fewer and brighter

    // --- ship ---
    public const double ShipSpeed = 1.5;            // keyboard, px/frame
    public const double ShipMaxChase = 2.6;         // pointer/touch chase, px/frame
    public const double PointerEase = 0.22;           // fraction of the gap closed per frame
    public const double TouchGain = 1.0;              // ship px per finger px
    public const int ShipHp = 1;
    public const int ShipStartAbove = 14;           // pixels between the ship's start row and the bottom edge
    public const int ShipBombs = 2;
    public const int InvulnFrames = 45;
    public const int RespawnFrames = 40;
    public const int FireInterval = 7;               // frames between volleys of the light laser, the first weapon: few enough shots that aim and timing matter
    public const double ShipShotSpeed = 2.8;     // slow enough that a moving enemy can slip a shot

    // --- liking or reposting: a click on a post's button, or a key for the post under the ship ---
    public const int ShieldRewardCooldownFrames = 15 * Fps;   // after a like or repost refills the shield, the next waits this long: with a click a like is free, so the shield cannot be had for the pressing; 0 turns the wait off
    public const int TouchPadCells = 1;                 // a finger's press counts this far outside a button's box on each side, so one landing on the edge still likes; a mouse gets none, as it points to the pixel
    public const int PressRepeatFrames = 9;           // 0.3 s: a second press on the same button, by key or click, this soon is a bounce or a double tap and is ignored, so a like is never followed by its own undo
    public const int ShipKeyReachCells = 2;           // the like and repost keys reach a card this far from the ship's centre when it is over none: the gap between two cards (CardField.Gap), so a ship between them always has one, and no further, so a key never likes a post the player is not near

    // --- the feed ---
    public const int PostQueueMax = 12;              // posts kept waiting to become cards
    public const int PostQueueLow = 6;               // the feed loop asks for more posts when fewer than this wait
    public const int TextOnlyMinChars = 20;          // Random mode: a post shorter than this is skipped
    public const int ThreadLookupMs = 6000;           // a lookup of an author's next reply slower than this shows nothing
    public const int SpeechBoxCharsPerSecond = 36, SpeechBoxHoldFrames = 135, SpeechBoxPageHoldFrames = 75;

    // --- enemies and waves ---
    public const int GruntHp = 2, CarrierHp = 4, GunshipHp = 90;   // a grunt falls to one shot of the starting gun; a gunship that holds the gate must not take too long to kill
    public const int GunshipStayFrames = 1200, GunshipVolleyFrames = 26;   // a gunship leaves after this long; one volley per this many frames
    public const int GunshipTellFrames = 12;          // the muzzle shows white for this long before each volley: two fifths of a second, under half the gap between volleys
    public const int SpamMin = 5, SpamMax = 12;
    public const int WaveGapFrames = 75;
    public const int FillerGapFrames = 70;            // between filler waves at stage 1; the ramp shortens it to FillerGapCap
    public const int FillerMaxAliveFirstStage = 4;    // a filler wave starts only while fewer enemies than this are up: grunts die to one shot, so the opening needs one more up to stay busy
    public const int FillerMaxAlive = 3;              // the same after stage 1: one fewer, as stage 2's divers and wardens among waves of 5 to 9 grunts would crowd the field past dodging
    public const int FillerBase = 5, FillerGruntsSpread = 5;   // grunts in a filler wave at stage 1: the base plus a draw below the spread (0 to 4 more), before the ramp's extra
    public const int EmptyScreenFrames = 18;
    public const int MinWaveGapFrames = 24;           // a wave never follows another sooner than this
    public const int MaxParticles = 420;
    public const int SeenPostsMax = 4000;             // the post queue forgets the posts it has seen past this many
    public const int InterleaverCapacity = 300;       // posts the feed interleaver holds before it takes no more
    public const int MaxPendingWaves = 6;
    public const double EnemyBoltSpeed = 1.05;
    public const double EnemyFallMin = 0.75, EnemyFallJitter = 0.2;   // grunt and carrier fall speed, px/frame: the minimum plus up to the jitter
    public const int EnemyFireGapLive = 55, EnemyFireGapIdle = 85, EnemyFireGapJitter = 50;   // frames between bolts: base by kind, plus up to the jitter
    // a grunt crosses its firing zone in about 80 frames, so the first bolt decides whether it fires at all: counted down only inside the zone
    public const int EnemyFirstBoltMin = 25, EnemyFirstBoltJitter = 45;
    public const int ScoreGrunt = 100, ScoreCarrier = 400, ScoreGunship = 3000;

    // --- bosses: each stage ends in a boss fight, and from LesserFirstStage on a lesser gunship visits between bosses, as Ramp.Lessers plans ---
    public const int BossClearFrames = 75;            // the field gets this long to empty before what is left is told to leave
    public const int BossWarnFrames = 45;             // the WARNING banner, then the boss enters
    public const int BossStayFrames = 9000;           // five minutes of Fighting; then the boss leaves and the stage advances with no score
    public const double BossLapHp = 0.15, BossLapHpCap = 2;   // after boss 10 each lap adds this to the bosses' hit points, up to the cap
    public const int LesserHp = 40;                   // a lesser gunship: flat at every stage, and dead well inside its stay even to the starting gun
    public const int LesserStayFrames = 20 * Fps;
    public const int ScoreLesser = 1500;
    public const double LesserDropChance = 0.5;
    public const int LesserFirstStage = 1, LesserOneVisitLastStage = 3, LesserTwoVisitsLastStage = 6, LesserThreeVisitsLastStage = 8;   // stages 1-3 see one, 4-6 two, 7-8 three: the opening needs a speaker, or a rude post waits silent until the first boss
    public const double LesserOneVisitShare = 0.55;   // a stage's lone visit comes this far in: past the stage 1 mine's arrival at half way, so the two do not land together, and inside the first half-minute
    public const int LesserRepeatFromStage = 9;       // from here lesser gunships come on a timer instead of a fixed count
    public const int LesserRepeatFramesStage9 = 20 * Fps, LesserRepeatFramesStage10 = 14 * Fps;   // one every 20 s at stage 9, one every 14 s at stage 10 and beyond
    public const int LesserMaxAlive = 2;

    // --- evasion: grunts, carriers, splitters and falling divers sidestep a ship shot that is lined up on them. Never instant:
    // the enemy shivers for EvadeWindupFrames first (the tell), then slides. A shot that is already close still lands. ---
    public const double EvadeSenseY = 44;             // px: it only reacts to a shot this near below it
    public const int EvadeWindupFrames = 8;           // the shiver before the slide (a shot flies about 22 px meanwhile)
    public const int EvadeSlideFrames = 12;
    public const double EvadeSpeed = 1.7;             // px/frame while sliding
    public const int EvadeCooldownStart = 75, EvadeCooldownCap = 36;   // frames before it can sidestep again: stage 1 -> the cap stage
    public const double EvadeLane = 3.0;              // px beyond the sprite's half width that counts as lined up
    public const double DiverWeaveAmp = 3.0, DiverWeaveRate = 0.13;   // a diver jinks side to side on its way down

    // --- the newer kinds; each unlocks at a stage (see Ramp.UnlockStage) and every danger shows a tell first ---
    // mine: drifts down, shrugs off shots (only a bomb clears it), detonates on contact. Unlit and harmless until armed.
    public const double MineDrift = 0.42;             // px/frame down the field
    public const int MineArmFrames = 45;              // from arrival (it starts above the field) until it lights and can hurt
    public const int MinesPerSpecial = 2;             // a mine special is a pair, so there is a gap to thread
    // a dead quote (deleted, blocked, removed by its author): its notice becomes mines that ride the card down (Quote.cs)
    public const int DeadQuoteMines = MinesPerSpecial; // a pair on the notice, at its thirds, as a mine special is a pair: a gap to thread between them
    public const int MaxMinesAlive = 6;               // no dead quote adds a mine while this many are up: three pairs, so a run of dead quotes cannot wall the field
    // diver: falls to a hold row, winds up while it shows where it will go, then charges that spot in a straight line
    public const int DiverHp = 3;                     // more than the starting gun deals in one shot
    public const double DiverDashArmour = 0.35;       // a charging diver takes this share of a shot's damage (bombs ignore it): shots ping off and it keeps coming
    public const double DiverFall = 1.05;             // px/frame on the way to its hold row
    public const double DiverHoldRow = 0.2;           // fraction of the field height where it stops to wind up
    public const int DiverWindupFrames = 30;          // 1 s of shaking and an aim line before the charge
    public const double DiverDashSpeed = 3.0;         // px/frame, straight at the spot locked when the wind-up began
    // splitter: a slow weaving blob that cracks as it is hurt and bursts into two shards when killed
    public const int SplitterHp = 4;
    public const double SplitterFall = 0.55;
    public const int ShardHp = 1;
    public const double ShardSpeedX = 1.2, ShardSpeedY = 1.05;
    public const int ShardGraceFrames = 12;           // a new shard cannot touch the ship this long
    // turret: settles high, then charges (a growing glow and a reticle on the locked spot) and fires an aimed burst
    public const int TurretHp = 5;
    public const double TurretHoldRow = 0.16;
    public const int TurretWindupFrames = 27;         // the tell before every burst
    public const int TurretRestFrames = 55;           // between bursts
    public const int TurretBurst = 3, TurretBurstGap = 4;
    public const int TurretVolleys = 3;               // bursts before it pulls out
    public const double TurretBoltSpeed = 1.5;      // before the ramp's bolt-speed multiplier
    public const int ScoreMine = 150, ScoreDiver = 400, ScoreSplitter = 300, ScoreShard = 50, ScoreTurret = 600;

    // --- the difficulty ramp: the stage follows time alive plus a little per kill (see Ramp). Stage 1 is the base difficulty.
    // every lever below moves in even steps from its stage-1 value to its cap at StageCap, and no further ---
    public const int StageFrames = 50 * Fps;          // progress per stage: 50 s of flying alive...
    public const int StageKillFrames = 15;            // ...less 0.5 s for every kill, so a sharp player climbs faster
    public const int StageCap = 10;                   // the last stage; a long run stays here, hard but playable
    public const int MiniBossCap = 1;                 // how many of one mini-boss kind (warden, nest, yoke, chorus) may be alive at once in stages 1 to 5
    public const int MiniBossCapLate = 2;             // ...and from MiniBossCapLateFromStage on
    public const int MiniBossCapLateFromStage = 6;
    public const int StageBannerFrames = 75;          // "STAGE 3" fades in the middle of the field this long
    public const int FillerGapCap = 45;               // frames between filler waves at the cap (FillerGapFrames at stage 1)
    public const int FillerExtraCap = 4;              // grunts added to every filler wave at the cap (a filler wave is 5 to 9 grunts at stage 1, 9 to 13 at the cap)
    public const double HpMulCap = 1.5;               // enemy hit point multiplier at the cap (mines and shards excepted)
    public const double BoltSpeedMulCap = 1.5;      // enemy bolt speed multiplier at the cap
    public const double FireGapMulCap = 0.45;          // grunt fire cooldown multiplier at the cap: more bolts in the air
    public const int SpecialsCap = 3;                 // newer-kind groups per filler wave: 0 before half of stage 1, 1 after it, 2 from stage 2, 3 from ThreeGroupsFromStage
    public const int ThreeGroupsFromStage = 6;
    public const int LiveSpecialsFromStage = 3;       // from here a live wave may swap a few grunts (never the carrier) for newer kinds
    public const double DeathSetback = 0.5;           // dying keeps the stage and gives back this share of the way into the next one

    // --- impact feel ---
    public const int HitStopSmallKill = 0;
    public const int HitStopCarrierKill = 2;
    public const int HitStopBigKill = 7;
    public const int HitStopShipHit = 6;
    public const int HitStopBomb = 5;
    public const int HitStopHeavyHit = 1;             // a shot landing on a gunship
    public const double ShakeHit = 0.8;
    public const double ShakeSmallKill = 1.4;
    public const double ShakeBigKill = 5.0;
    public const double ShakeShipHit = 5.5;
    public const double ShakeBomb = 6.5;
    public const double ShakeMax = 7.0;
    public const double ShakeDecay = 0.82;            // multiplied in every frame
    public const double ShakeFloor = 0.25;            // below this the shake is off
    public const int FlashFrames = 3;                 // white sprite on hit
    public const int ScreenFlashFrames = 4;           // bright wash on bombs
    public const double KnockHit = 0.9;               // px/frame shoved back by a shot
    public const double KnockHeavy = 0.35;
    public const double KnockDecay = 0.72;
    public const int SparksPerHit = 4;
    public const int DebrisSmallKill = 9;
    public const int DebrisBigKill = 34;
    public const int RingFrames = 16;
    public const double RingRadiusSmall = 7, RingRadiusBig = 24;
    public const int MuzzleFrames = 2;
    public const int PopupFrames = 28;
    public const double DebrisGravity = 0.06;

    // --- pickups: fly over them to collect ---
    public const double DropGrunt = 0.03, DropCarrier = 0.12;   // a beaten boss and a cleared live wave always drop; a lesser gunship at LesserDropChance
    public const int MaxPickups = 3;
    public const double PickupDrift = 0.25;           // px/frame down the field
    public const double PickupBobAmp = 2.0, PickupBobRate = 0.12;
    public const int PickupLifeFrames = 9 * Fps;
    public const int PickupBlinkFrames = 2 * Fps;     // blinks for the last 2 s
    public const double PickupReach = 2.0;            // pixels around the ship's body that still collect
    public const int ScorePickupOverflow = 500;       // a pickup that would do nothing gives this instead
    // drop mix: relative weights, scaled down for what the player already has
    public const double WeightShield = 1, WeightBomb = 1.5, WeightWeapon = 2, WeightFull = 0.25;
    public const double WeightBurst = 0.35;           // the speed burst: about one drop in fourteen, so a zoom is a treat and not the way the page is read

    // --- the speed burst (Burst.cs): flown over, it fires at once and the ship scrolls up the page, past bolts and small enemies ---
    public const int BurstInFrames = 9;               // 0.3 s of ease-in: the page gathers speed rather than jumping
    public const int BurstFrames = 90;                // 3 s at full speed: some four cards fly past, enough to feel the page move
    public const int BurstOutFrames = 30;             // 1 s of ease-out, slow enough that the player sees the burst ending and can line up again
    public const int BurstFramesPerRow = 2;           // the page's pace at full burst, a pixel a frame: past this the cards blur and cannot be read
    public const int BurstEaseSteps = 3;              // the ease runs through this many paces, each BurstFramesPerRow slower than the next, so it reads as gathering speed
    public const double BurstStarsMul = 8;            // the stars fall this many times their usual speed at full burst: the field reads as rushing
    public const int BurstProgressMul = 2;            // the stage clock runs this many times its pace while a burst runs: the scroll ahead brings the boss nearer
    public const int BurstExitGraceFrames = 20;       // a burst ending with the ship inside an enemy gives this much respawn grace, so the end never kills it on the spot
    public const int OpeningBurstFrame = 2 * Fps;     // the run's first burst pickup appears this soon, so the player zooms up into the feed at once
    public const double OpeningBurstRow = 0.3;        // ...this far down the field, centred and ahead of the ship
    public const int OpeningBurstReachFrames = 45;    // ...and drifts down to reach a ship that has not moved this long after (by about 3.5 s)
    public const int BurstRepeatFrames = 20 * Fps;    // no burst pickup drops this soon after the last was taken, so bursts stay a treat

    // --- shield ---
    public const int ShieldHits = 1;
    public const int ShieldInvulnFrames = 20;         // after an absorbed hit, so one ram cannot also hurt the ship
    public const double ShakeShield = 2.5;
    public const double KnockShield = 1.6;            // px shoved onto whatever hit the shield
    public const double ShieldShoveRadius = 24;       // px around the ship within which an absorbed hit shoves enemies

    // --- bombs ---
    public const int BombCap = 3;
    public const int GoldLikes = 1000;                // a post with this many likes is a gold card: the first like or repost on it gives a bomb

    // --- weapon kinds (Weapons.cs): ten, each a step of about x1.1 to x1.15 in damage per second on one target over the last, so a
    // pickup is felt and the top kind is about three times the first, not ten. The grunt kill time is the second rule: grunts have
    // 2 to 3 hit points, so a kind that kills one in a shot sets its kill time by its trigger, and each kind's trigger is picked so
    // one step kills grunts at most x1.4 faster than the last (WeaponRampTests prints the table) ---
    public const int MaxWeaponLevel = 10;
    public const int HeavyInterval = 10;              // the heavy laser's slow trigger: a single big shot, so a grunt at stage 2 dies in 10 frames, not the light laser's 14 or 7
    public const double HeavyDamage = 3.25;           // one heavy shot kills a grunt at every stage (3 hit points at the cap)
    public const double HeavySpeed = 2.5;             // a heavy shot is a little slower than the light one, so it reads as weightier
    public const int TwinInterval = 8;
    public const double TwinDamage = 1.5;             // each of two lines; the pair kills a grunt at the cap in one volley
    public const double TwinGap = 3;                  // px each line sits off the middle: both land on a grunt
    public const int MissileInterval = 10;
    public const double MissileDamage = 2.125;        // each of two; a missile homes, so it is paid less than its speed would earn
    public const double MissileGap = 5;               // px each missile leaves the wing from
    public const double MissileLaunchSpeed = 1.6, MissileTopSpeed = 3.0, MissileThrust = 0.12;   // a missile leaves slow and gathers speed, so it is seen to fly
    public const double MissileLockRange = 50;       // px ahead a missile locks on from: it hunts what presses the ship, not what has just come on at the top
    public const double MissileTurn = 0.14;          // px per frame a missile may change its sideways speed by: it bends toward a target, it cannot turn on one
    public const double BlastRadius = 10, BlastShare = 0.5;   // a missile's blast takes this share of its damage to the other small enemies this near (never a boss's parts)
    public const int TwinHeavyInterval = 9;
    public const double TwinHeavyDamage = 2.1;        // each of two heavy lines
    public const int SpreadInterval = 7;
    public const double SpreadMidDamage = 1.68, SpreadInnerDamage = 1.0, SpreadOuterDamage = 0.15;   // the fan's middle, its inner pair (on target) and its outer pair, which only clears the sides: on a boss the fan is not ahead of the next kind
    public const double SpreadInnerSlant = 0.35, SpreadOuterSlant = 1.2;   // px per frame sideways: the inner pair lands on a grunt from 50 px, the outer pair drifts some 21 px out for each 50 px it climbs, so it clears the sides
    public const double SpreadInnerGap = 2, SpreadOuterGap = 4;   // px each pair leaves the muzzle off the middle: the fan opens from the ship's nose, not from one point
    public const double SpreadInnerBack = 1, SpreadOuterBack = 2;   // px each pair starts behind the middle shot, so the volley reads as a chevron from its first frame
    public const int MachineGunInterval = 3;          // the machine gun's quick trigger, heard every second shot
    public const double MachineGunDamage = 1.75;      // two slugs kill a grunt at every stage, so its kill time is 6 frames
    public const double MachineGunBarrel = 1;         // px the slugs leave left and right of the middle, in turn
    public const double SlugSpeed = 3.0;              // the machine gun's and the gatling's slug: quicker than a laser shot but under the speed an enemy can no longer slip
    public const int GatlingInterval = 3;
    public const double GatlingDamage = 0.965;        // each of two slugs: kept a hair under a grunt's 2 hit points a volley, so the gatling kills in two volleys like the machine gun
    public const double GatlingGap = 2;
    public const int SwarmInterval = 8;
    public const double SwarmDamage = 1.42;           // each of the volley's small missiles
    public const double SwarmGap = 4;                 // px between missiles across the wing
    public const int SwarmMissiles = 4;               // missiles a volley, laid evenly across the wing about the middle
    public const int StormInterval = 6;
    public const double StormDamage = 1.59;           // each of three heavy lines a volley; the top three steps are the ramp's smallest and the top holds no missiles, which pick off what the pilot is not aiming at, so it clears a wave at most about a third quicker than the first
    public const double StormGap = 4;                 // px the outer lines sit off the middle: all three land on a grunt

    // --- enemies made of parts (the numbers of each kind live in its own file under Enemies/) ---
    public const double BombPartDamage = 12;          // a bomb's damage to each open part (an armoured one takes nothing)
    public const double ClearPartShare = 0.25;        // the feed-clearing like takes this share of a living part's full hit points
    public const double PartDropChance = 0.35;        // a destroyed part's own drop roll
    public const int MaxParts = 64;                   // the parts budget: the tests hold every kind on every field size, and every boss through its fight, to this many parts at most
    public const int RudeWaiting = 48;                // rude posts kept waiting for a speaker; one more drops the oldest. An hour's replay keeps some 30 in English before the first boss speaks, and none is lost unheard
    public const int VoicesBeforeBoss = 8;            // posts a run should know before its first boss: while it knows fewer, a hidden reply in English is kept although the player reads other languages, so the fight is not one post said over and over when the player's own languages bring few
    public const int HiddenBacklog = 96;              // hidden replies waiting to be fetched: the replay lands some 80 in its first seconds, and a shorter line would drop the oldest before they are fetched
    public const int HiddenReplayMinutes = 60;        // how far back the hidden-reply stream starts: an hour holds enough speakable posts in English for the first bosses to keep changing what they say
    public const int RudeSeen = 64;                   // seeds of the last posts spoken, so a post offered again is not said twice
    public const int RudeEchoes = 8;                  // the last posts spoken, kept for the mirror
    public const int RudeHeard = 32;                  // posts the run has heard, kept to be said again; a 33rd drops the oldest
    public const int VoiceIdleFrames = 6 * Fps;       // a boss's box shut this long, with a post waiting or heard, raises the Idle cue (a boss may set its own pace)
    public const int MaxSpeechBoxes = 3;              // speech boxes of enemies on show at once
}
