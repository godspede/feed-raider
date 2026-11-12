namespace FeedRaider.Engine.Raider;

/// <summary>
/// Every number that shapes how Feed Raider plays and feels. Frames are sim frames at <see cref="Fps"/>;
/// distances are playfield pixels (one terminal cell is 2x2 pixels). Nothing else in the game hard-codes these.
/// </summary>
public static class Tuning
{
    // --- timing and field ---
    public const int Fps = 30;
    public const int FieldMaxCols = 64;
    public const int FieldMinCols = 30, FieldMinRows = 16;
    /// <summary>The two side columns (feed log left, how-to-play right) are mirror images: each is at most
    /// <c>SidePanelMaxCols</c> wide, starts <c>SideGapCols</c> from the field, keeps one column from the window edge, and
    /// shows only when it can be <c>SidePanelMinCols</c> wide.</summary>
    public const int SidePanelMaxCols = 44, SidePanelMinCols = 30, SideGapCols = 2;

    // --- background: scroll-the-feed pace (pixels per frame; 0.45 px/frame is about 13 px/s) ---
    public const double ScrollNear = 0.45;
    public const double ScrollFar = 0.2;
    public const double ScrollStars = 0.06;

    // --- player ---
    public const double PlayerSpeed = 1.5;            // keyboard, px/frame
    public const double PlayerMaxChase = 2.6;         // pointer/touch chase, px/frame
    public const double PointerEase = 0.22;           // fraction of the gap closed per frame
    public const double TouchGain = 1.0;              // plane px per finger px
    public const int PlayerHp = 2;
    public const int PlayerStartAbove = 14;           // pixels between the player's start row and the bottom edge
    public const int PlayerBombs = 2;
    public const int InvulnFrames = 45;
    public const int RespawnFrames = 40;
    public const int FireInterval = 7;               // frames between volleys: few enough shots that aim and timing matter
    public const double PlayerBulletSpeed = 2.8;     // slow enough that a moving enemy can slip a shot
    public const int KeyHoldMs = 150;                 // console: a key press counts as held this long

    // --- power-up from a like ---
    public const int PowerUpFrames = 5 * Fps;
    public const double PowerDamageMul = 1.15;

    // --- wingmen ---
    public const int MaxWingmen = 1;                 // one ally at a time: its post has the field's whole comm to itself
    public const int WingEnterFrames = 40;
    public const int WingFightFrames = 10 * Fps;
    public const int WingLeaveFrames = 40;
    public const int WingStaggerFrames = 50;
    public const int WingQueueMax = 16;
    public const int WingMinTextChars = 20;
    public const int BombLikeThreshold = 1000;
    public const int BannerFrames = 3 * Fps;
    public const double LikeRadius = 17;
    public const int LikeHoldMs = 450;                // hold the button or a finger on a wingman this long to like it
    public const int LikeHoldFrames = (LikeHoldMs * Fps + 999) / 1000;
    public const double LikeHoldRadius = 14;          // how near a wingman the hold may sit
    // a wingman whose thread is being followed stays at least ThreadReadFrames after each reply opens (time to read it and like it),
    // and at most ThreadMaxExtraFrames beyond its usual stay in all, so a long thread cannot hold a slot for good
    public const int ThreadReadFrames = 9 * Fps;
    public const int ThreadMaxExtraFrames = 40 * Fps;
    public const int ThreadLookupMs = 6000;           // a thread lookup slower than this ends the thread
    public const int CommCharsPerSecond = 36, CommHoldFrames = 135, CommPageHoldFrames = 75, CommCollapseFrames = 8;

    // --- enemies and waves ---
    public const int GruntHp = 4, CarrierHp = 7, GunshipHp = 210;
    public const int GunshipStayFrames = 1200, GunshipVolleyFrames = 26;   // a gunship leaves after this long; one volley per this many frames
    public const int SpamMin = 5, SpamMax = 12;
    public const int WaveGapFrames = 75;
    public const int FillerGapFrames = 100;           // between filler waves at stage 1; the ramp shortens it to FillerGapCap
    public const int EmptyScreenFrames = 18;
    public const int MinWaveGapFrames = 24;           // a wave never follows another sooner than this
    public const int MaxParticles = 420;
    public const int SeenPostsMax = 4000;             // a wingman feed forgets the posts it has seen past this many
    public const int InterleaverCapacity = 300;       // posts the feed interleaver holds before it takes no more
    public const int MaxPendingWaves = 6;
    public const double EnemyBulletSpeed = 1.05;
    public const double EnemyFallMin = 0.75, EnemyFallJitter = 0.2;   // grunt and carrier fall speed, px/frame: the minimum plus up to the jitter
    public const int EnemyFireGapLive = 55, EnemyFireGapIdle = 85, EnemyFireGapJitter = 50;   // frames between shots: base by kind, plus up to the jitter
    // a grunt crosses its firing zone in about 80 frames, so the first shot decides whether it fires at all: counted down only inside the zone
    public const int EnemyFirstShotMin = 25, EnemyFirstShotJitter = 45;
    public const int ScoreGrunt = 100, ScoreCarrier = 400, ScoreGunship = 3000;

    // --- evasion: grunts, carriers, splitters and falling divers sidestep a player shot that is lined up on them. Never instant:
    // the enemy shivers for EvadeWindupFrames first (the tell), then slides. A shot that is already close still lands. ---
    public const double EvadeSenseY = 44;             // px: it only reacts to a shot this near below it
    public const int EvadeWindupFrames = 8;           // the shiver before the slide (a shot flies about 22 px meanwhile)
    public const int EvadeSlideFrames = 12;
    public const double EvadeSpeed = 1.7;             // px/frame while sliding
    public const int EvadeCooldownStart = 75, EvadeCooldownCap = 36;   // frames before it can sidestep again: stage 1 -> the cap stage
    public const double EvadeLane = 3.0;              // px beyond the sprite's half width that counts as lined up
    public const double DiverWeaveAmp = 3.0, DiverWeaveRate = 0.13;   // a diver jinks side to side on its way down

    // --- the newer kinds; each unlocks at a stage (see Ramp.UnlockStage) and every danger is telegraphed first ---
    // mine: drifts down, shrugs off bullets (only a bomb clears it), detonates on contact. Unlit and harmless until armed.
    public const double MineDrift = 0.42;             // px/frame down the field
    public const int MineArmFrames = 45;              // from spawn (it starts above the field) until it lights and can hurt
    public const int MinesPerSpecial = 2;             // a mine special is a pair, so there is a gap to thread
    // diver: falls to a hold row, winds up while it shows where it will go, then charges that spot in a straight line
    public const int DiverHp = 4;                     // more than the starting gun deals in one shot
    public const double DiverDashArmour = 0.35;       // a charging diver takes this share of a bullet's damage (bombs ignore it): shots ping off and it keeps coming
    public const double DiverFall = 1.05;             // px/frame on the way to its hold row
    public const double DiverHoldRow = 0.2;           // fraction of the field height where it stops to wind up
    public const int DiverWindupFrames = 30;          // 1 s of shaking and an aim line before the charge
    public const double DiverDashSpeed = 3.0;         // px/frame, straight at the spot locked when the wind-up began
    // splitter: a slow weaving blob that cracks as it is hurt and bursts into two shards when shot down
    public const int SplitterHp = 6;
    public const double SplitterFall = 0.55;
    public const int ShardHp = 1;
    public const double ShardSpeedX = 1.2, ShardSpeedY = 1.05;
    public const int ShardGraceFrames = 12;           // a new shard cannot touch the ship this long
    // turret: settles high, then charges (a growing glow and a reticle on the locked spot) and fires an aimed burst
    public const int TurretHp = 8;
    public const double TurretHoldRow = 0.16;
    public const int TurretWindupFrames = 27;         // the telegraph before every burst
    public const int TurretRestFrames = 55;           // between bursts
    public const int TurretBurst = 3, TurretBurstGap = 4;
    public const int TurretVolleys = 3;               // bursts before it pulls out
    public const double TurretBulletSpeed = 1.5;      // before the ramp's bullet-speed multiplier
    public const int ScoreMine = 150, ScoreDiver = 400, ScoreSplitter = 300, ScoreShard = 50, ScoreTurret = 600;

    // --- the difficulty ramp: the stage follows time alive plus a little per kill (see Ramp). Stage 1 is the base difficulty.
    // every lever below moves in even steps from its stage-1 value to its cap at StageCap, and no further ---
    public const int StageFrames = 50 * Fps;          // progress per stage: 50 s of flying alive...
    public const int StageKillFrames = 15;            // ...less 0.5 s for every kill, so a sharp player climbs faster
    public const int StageCap = 10;                   // the last stage; a long run stays here, hard but playable
    public const int StageBannerFrames = 75;          // "STAGE 3" fades in the middle of the field this long
    public const int FillerGapCap = 60;               // frames between filler waves at the cap (FillerGapFrames at stage 1)
    public const int FillerGruntsMin = 4, FillerGruntsSpread = 5;   // grunts in a filler wave at stage 1: the minimum plus a draw below the spread (4-8)
    public const int FillerExtraCap = 4;              // grunts added to every filler wave at the cap (4-8 at stage 1, 8-12 at the cap)
    public const double HpMulCap = 1.8;               // enemy health multiplier at the cap (mines and shards excepted)
    public const double BulletSpeedMulCap = 1.5;      // enemy bullet speed multiplier at the cap
    public const double FireGapMulCap = 0.45;          // grunt fire cooldown multiplier at the cap: more bullets in the air
    public const int SpecialsCap = 3;                 // newer-kind groups per filler wave: 0 at stage 1, 1 from stage 2, 3 at the cap
    public const int LiveSpecialsFromStage = 3;       // from here a live spam wave may swap a few grunts (never the carrier) for newer kinds
    public const double DeathSetback = 0.5;           // dying keeps the stage and gives back this share of the way into the next one

    // --- impact feel ---
    public const int HitStopSmallKill = 0;
    public const int HitStopCarrierKill = 2;
    public const int HitStopBigKill = 7;
    public const int HitStopPlayerHit = 6;
    public const int HitStopBomb = 5;
    public const int HitStopHeavyHit = 1;             // a bullet landing on a gunship
    public const double ShakeHit = 0.8;
    public const double ShakeSmallKill = 1.4;
    public const double ShakeBigKill = 5.0;
    public const double ShakePlayerHit = 5.5;
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
    public const int HeartBurst = 14;
    public const double DebrisGravity = 0.06;

    // --- pickups: fly over them to collect ---
    public const double DropGrunt = 0.03, DropCarrier = 0.12;   // gunships and a cleared live wave always drop
    public const int MaxPickups = 3;
    public const double PickupDrift = 0.25;           // px/frame down the field
    public const double PickupBobAmp = 2.0, PickupBobRate = 0.12;
    public const int PickupLifeFrames = 9 * Fps;
    public const int PickupBlinkFrames = 2 * Fps;     // blinks for the last 2 s
    public const double PickupReach = 2.0;            // pixels around the ship's body that still collect
    public const int ScorePickupOverflow = 500;       // a pickup that would do nothing gives this instead
    // drop mix: relative weights, scaled down for what the player already has
    public const double WeightShield = 2, WeightBomb = 1.5, WeightPower = 2, WeightFull = 0.25;

    // --- shield ---
    public const int ShieldHits = 3;
    public const int ShieldInvulnFrames = 20;         // after an absorbed hit, so one ram cannot eat all three
    public const double ShakeShield = 2.5;
    public const double KnockShield = 1.6;            // px shoved onto whatever hit the shield
    public const double ShieldShoveRadius = 24;       // px around the ship within which an absorbed hit shoves enemies

    // --- bombs ---
    public const int BombCap = 3;

    // --- weapon levels: 1 single, 2 twin, 3 twin + two angled, 4 five-way and a faster trigger ---
    public const int MaxWeaponLevel = 4;
    public const int FireIntervalMax = 6;             // level 4: a slightly quicker trigger, no longer a big one
}
