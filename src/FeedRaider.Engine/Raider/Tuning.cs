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

    // --- background: scroll-the-feed pace (pixels per frame; 0.45 px/frame is about 13 px/s) ---
    public const double ScrollNear = 0.45;
    public const double ScrollFar = 0.2;
    public const double ScrollStars = 0.06;

    // --- player ---
    public const double PlayerSpeed = 1.5;            // keyboard, px/frame
    public const double PlayerMaxChase = 2.6;         // pointer/touch chase, px/frame
    public const double PointerEase = 0.22;           // fraction of the gap closed per frame
    public const double TouchGain = 1.0;              // plane px per finger px
    public const int PlayerHp = 3;
    public const int PlayerBombs = 3;
    public const int InvulnFrames = 75;
    public const int RespawnFrames = 40;
    public const int FireInterval = 4;
    public const double PlayerBulletSpeed = 3.2;
    public const double PlayerBulletDamage = 1.0;
    public const int KeyHoldMs = 150;                 // console: a key press counts as held this long

    // --- power-up from a like ---
    public const int PowerUpFrames = 8 * Fps;
    public const double PowerDamageMul = 1.5;

    // --- wingmen ---
    public const int MaxWingmen = 2;
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
    public const int TypeCharsPerSecond = 48;
    public const int CommCharsPerSecond = 36, CommHoldFrames = 135, CommCollapseFrames = 8;

    // --- enemies and waves ---
    public const int GruntHp = 3, CarrierHp = 5, GunshipHp = 70;
    public const int SpamMin = 5, SpamMax = 12;
    public const int WaveGapFrames = 75;
    public const int FillerGapFrames = 100;
    public const int EmptyScreenFrames = 18;
    public const int MaxPendingWaves = 6;
    public const double EnemyBulletSpeed = 0.95;
    public const int ScoreGrunt = 100, ScoreCarrier = 400, ScoreGunship = 3000;

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
    public const double DropGrunt = 0.08, DropCarrier = 0.25;   // gunships and a cleared live wave always drop
    public const int MaxPickups = 3;
    public const double PickupDrift = 0.25;           // px/frame down the field
    public const double PickupBobAmp = 2.0, PickupBobRate = 0.12;
    public const int PickupLifeFrames = 9 * Fps;
    public const int PickupBlinkFrames = 2 * Fps;     // blinks for the last 2 s
    public const double PickupReach = 2.0;            // pixels around the ship's body that still collect
    public const int ScorePickupOverflow = 500;       // a pickup that would do nothing gives this instead
    // drop mix: relative weights, scaled down for what the player already has
    public const double WeightShield = 3, WeightBomb = 2, WeightPower = 3, WeightFull = 0.25;

    // --- shield ---
    public const int ShieldHits = 3;
    public const int ShieldInvulnFrames = 20;         // after an absorbed hit, so one ram cannot eat all three
    public const double ShakeShield = 2.5;
    public const double KnockShield = 1.6;            // px shoved onto whatever hit the shield

    // --- bombs ---
    public const int BombCap = 5;

    // --- weapon levels: 1 single, 2 twin, 3 twin + two angled, 4 five-way and a faster trigger ---
    public const int MaxWeaponLevel = 4;
    public const int FireIntervalMax = 3;             // level 4
}
