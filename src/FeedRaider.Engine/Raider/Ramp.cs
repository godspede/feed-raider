namespace FeedRaider.Engine.Raider;

/// <summary>
/// The difficulty ramp: what each stage asks of the player. Pure functions of the stage, so the tests can walk every stage.
/// The stage comes from progress (<see cref="RaiderGame.Progress"/>): one point per frame alive, plus <see cref="Tuning.StageKillFrames"/>
/// per kill. Time is the floor, so a quiet feed or a weak shot still moves on; kills speed it up for a player who is coping.
/// Stage 1 is the game as it always was. Every lever moves in even steps to its cap at <see cref="Tuning.StageCap"/>.
/// </summary>
public static class Ramp
{
    /// <summary>The newer kinds in the order they unlock, one per stage from stage 2, so a first-time player meets them one at a time.</summary>
    public static readonly EnemyKind[] UnlockOrder = [EnemyKind.Mine, EnemyKind.Diver, EnemyKind.Splitter, EnemyKind.Turret];

    /// <summary>The stage the last newer kind unlocks at; until then each new stage introduces its kind alone.</summary>
    public static int LastUnlockStage => UnlockOrder.Length + 1;

    public static int StageAt(long progress) => (int)Math.Clamp(1 + Math.Max(0, progress) / Tuning.StageFrames, 1, Tuning.StageCap);

    /// <summary>The progress a stage begins at.</summary>
    public static long StageStart(int stage) => (long)(Math.Clamp(stage, 1, Tuning.StageCap) - 1) * Tuning.StageFrames;

    /// <summary>Progress after a death: the stage reached is kept and <see cref="Tuning.DeathSetback"/> of the way into the next is
    /// given back. The ramp eases for a moment and never starts over, and no stage, not even the first, can trap a player.</summary>
    public static long AfterDeath(long progress)
    {
        long start = StageStart(StageAt(progress)), into = Math.Max(0, progress - start);
        return start + into - (long)(into * Tuning.DeathSetback);
    }

    /// <summary>0 at stage 1, 1 at the cap and beyond.</summary>
    public static double T(int stage) => (Math.Clamp(stage, 1, Tuning.StageCap) - 1) / (double)(Tuning.StageCap - 1);

    private static double Lerp(double a, double b, int stage) => stage <= 1 ? a : a + (b - a) * T(stage);

    public static int FillerGap(int stage) => (int)Math.Round(Lerp(Tuning.FillerGapFrames, Tuning.FillerGapCap, stage));
    public static int FillerExtra(int stage) => (int)Math.Round(Lerp(0, Tuning.FillerExtraCap, stage));
    public static double HpMul(int stage) => Lerp(1, Tuning.HpMulCap, stage);
    public static double BulletSpeedMul(int stage) => Lerp(1, Tuning.BulletSpeedMulCap, stage);
    public static double FireGapMul(int stage) => Lerp(1, Tuning.FireGapMulCap, stage);

    /// <summary>Groups of newer kinds in a filler wave: none at stage 1, one from stage 2, rising to <see cref="Tuning.SpecialsCap"/>.</summary>
    public static int Specials(int stage) => stage <= 1 ? 0
        : 1 + (int)Math.Round((Tuning.SpecialsCap - 1) * (Math.Min(stage, Tuning.StageCap) - 2) / (double)(Tuning.StageCap - 2));

    public static int UnlockStage(EnemyKind kind)
    {
        int i = Array.IndexOf(UnlockOrder, kind);
        return i < 0 ? 1 : i + 2;
    }

    public static bool Unlocked(EnemyKind kind, int stage) => UnlockStage(kind) <= stage;

    /// <summary>How many of <see cref="UnlockOrder"/> a stage has.</summary>
    public static int UnlockedCount(int stage) => Math.Clamp(stage - 1, 0, UnlockOrder.Length);

    /// <summary>The kind this stage introduces, or null for a stage that adds none.</summary>
    public static EnemyKind? Introduces(int stage) => stage >= 2 && stage <= LastUnlockStage ? UnlockOrder[stage - 2] : null;

    /// <summary>A one-line hint for the stage banner when a stage brings a new kind.</summary>
    public static string? Hint(int stage) => Introduces(stage) switch
    {
        EnemyKind.Mine => "mines: fly around them",
        EnemyKind.Diver => "divers charge where they aim",
        EnemyKind.Splitter => "splitters burst in two",
        EnemyKind.Turret => "turrets aim a burst",
        _ => null,
    };
}
