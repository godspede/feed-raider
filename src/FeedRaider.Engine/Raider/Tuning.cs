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

    // --- background: how fast the stars drift (pixels per frame) ---
    public const double ScrollStars = 0.06;

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
    public const int FireInterval = 7;               // frames between volleys: few enough shots that aim and timing matter
    public const double ShipShotSpeed = 2.8;     // slow enough that a moving enemy can slip a shot

    // --- power from a like ---
    public const int PowerFrames = 5 * Fps;

    // --- holding the ship on a post's button ---
    public const int HoldFrames = 3 * Fps;            // ticks on the button to like or repost
    public const int HoldGraceFrames = 6;             // ticks off the button before it starts to drain
    public const int HoldDrain = 2;                   // progress lost per tick after that
    public const double PowerDamageMul = 1.15;

    // --- the feed ---
    public const int PostQueueMax = 12;              // posts kept waiting to become cards
    public const int PostQueueLow = 6;               // the feed loop asks for more posts when fewer than this wait
    public const int TextOnlyMinChars = 20;          // Random mode: a post shorter than this is skipped
    public const int ThreadLookupMs = 6000;           // a lookup of an author's next reply slower than this shows nothing
    public const int SpeechBoxCharsPerSecond = 36, SpeechBoxHoldFrames = 135, SpeechBoxPageHoldFrames = 75;

    // --- enemies and waves ---
    public const int GruntHp = 2, CarrierHp = 4, GunshipHp = 90;   // a grunt falls to one bullet of the starting gun; a gunship that holds the gate must not take too long to kill
    public const int GunshipStayFrames = 1200, GunshipVolleyFrames = 26;   // a gunship leaves after this long; one volley per this many frames
    public const int GunshipTellFrames = 12;          // the muzzle shows white for this long before each volley: two fifths of a second, under half the gap between volleys
    public const int SpamMin = 5, SpamMax = 12;
    public const int WaveGapFrames = 75;
    public const int FillerGapFrames = 70;            // between filler waves at stage 1; the ramp shortens it to FillerGapCap
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
    public const int LesserFirstStage = 4, LesserTwoVisitsLastStage = 6, LesserThreeVisitsLastStage = 8;   // no visit before stage 4; stages 4-6 see two, 7-8 three
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
    // mine: drifts down, shrugs off bullets (only a bomb clears it), detonates on contact. Unlit and harmless until armed.
    public const double MineDrift = 0.42;             // px/frame down the field
    public const int MineArmFrames = 45;              // from arrival (it starts above the field) until it lights and can hurt
    public const int MinesPerSpecial = 2;             // a mine special is a pair, so there is a gap to thread
    // diver: falls to a hold row, winds up while it shows where it will go, then charges that spot in a straight line
    public const int DiverHp = 3;                     // more than the starting gun deals in one shot
    public const double DiverDashArmour = 0.35;       // a charging diver takes this share of a bullet's damage (bombs ignore it): shots ping off and it keeps coming
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
    public const double FireGapMulCap = 0.45;          // grunt fire cooldown multiplier at the cap: more bullets in the air
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
    public const int HitStopHeavyHit = 1;             // a bullet landing on a gunship
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
    public const double KnockHit = 0.9;               // px/frame shoved back by a bullet
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

    // --- shield ---
    public const int ShieldHits = 1;
    public const int ShieldInvulnFrames = 20;         // after an absorbed hit, so one ram cannot also hurt the ship
    public const double ShakeShield = 2.5;
    public const double KnockShield = 1.6;            // px shoved onto whatever hit the shield
    public const double ShieldShoveRadius = 24;       // px around the ship within which an absorbed hit shoves enemies

    // --- bombs ---
    public const int BombCap = 3;
    public const int GoldLikes = 1000;                // a post with this many likes is a gold card: the first hold finished on it gives a bomb

    // --- weapon levels: 1 single, 2 twin, 3 twin + two angled, 4 five-way and a faster trigger ---
    public const int MaxWeaponLevel = 4;
    public const int FireIntervalMax = 6;             // level 4: a slightly quicker trigger

    // --- enemies made of parts (the numbers of each kind live in its own file under Enemies/) ---
    public const double BombPartDamage = 12;          // a bomb's damage to each open part (an armoured one takes nothing)
    public const double ClearPartShare = 0.25;        // the feed-clearing like takes this share of a living part's full hit points
    public const double PartDropChance = 0.35;        // a destroyed part's own drop roll
    public const int MaxParts = 64;                   // the parts budget: the tests hold every kind on every field size, and every boss through its fight, to this many parts at most
    public const int RudeWaiting = 24;                // rude posts kept waiting for a speaker; a 25th drops the oldest
    public const int RudeSeen = 64;                   // seeds of the last posts spoken, so a post offered again is not said twice
    public const int RudeEchoes = 8;                  // the last posts spoken, kept for the mirror
    public const int RudeHeard = 32;                  // posts the run has heard, kept to be said again; a 33rd drops the oldest
    public const int VoiceIdleFrames = 6 * Fps;       // a boss's box shut this long, with a post waiting or heard, raises the Idle cue (a boss may set its own pace)
    public const int MaxSpeechBoxes = 3;              // speech boxes of enemies on show at once
}
