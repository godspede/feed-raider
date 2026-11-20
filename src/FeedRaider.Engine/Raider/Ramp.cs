namespace FeedRaider.Engine.Raider;

/// <summary>
/// The difficulty ramp: what each stage asks of the player. Pure functions of the stage, so the tests can walk every stage.
/// The stage comes from progress (<see cref="RaiderGame.Progress"/>): one point per frame alive, plus <see cref="Tuning.StageKillFrames"/>
/// per kill. Time is the floor, so a quiet feed or a weak shot still moves on; kills speed it up for a player who is coping.
/// Stage 1 is the base difficulty, with every lever at its starting value. Every lever moves in even steps to its cap at <see cref="Tuning.StageCap"/>.
/// </summary>
public static class Ramp
{
    /// <summary>A kind that unlocks in a stage, and how far into the stage it starts: a share of <see cref="Tuning.StageFrames"/>,
    /// 0 for the start of the stage.</summary>
    public readonly record struct Unlock(EnemyKind Kind, double Share = 0);

    /// <summary>The kinds that unlock at each stage, stage 1 first. A stage's first group of each filler
    /// wave is its own unlocks, turn and turn about. Every stage past the cap has none.</summary>
    private static readonly Unlock[][] UnlockTable =
    [
        [new(EnemyKind.Mine, 0.5)],
        [new(EnemyKind.Diver), new(EnemyKind.Warden)],
        [new(EnemyKind.Splitter), new(EnemyKind.Sower)],
        [new(EnemyKind.Turret), new(EnemyKind.Flanker)],
        [new(EnemyKind.Lobber)],
        [new(EnemyKind.Nest)],
        [new(EnemyKind.Yoke), new(EnemyKind.Lurk)],
        [new(EnemyKind.Chorus)],
        [new(EnemyKind.Glint)],
        [new(EnemyKind.Fuse)],
    ];

    /// <summary>The boss of each stage, stage 1 first.</summary>
    private static readonly EnemyKind[] BossTable =
    [
        EnemyKind.Gunship, EnemyKind.Bulwark, EnemyKind.Shutter, EnemyKind.Thread, EnemyKind.Spotlight,
        EnemyKind.Brood, EnemyKind.Schism, EnemyKind.PileOn, EnemyKind.Mirror, EnemyKind.Doomscroll,
    ];

    /// <summary>The kinds a stage brings by the time the run is <paramref name="share"/> of the way through it (the whole stage by
    /// default).</summary>
    public static EnemyKind[] Unlocks(int stage, double share = 1) =>
        stage >= 1 && stage - 1 < UnlockTable.Length ? UnlockTable[stage - 1].Where(a => a.Share <= share).Select(a => a.Kind).ToArray() : [];

    /// <summary>How far into its stage a progress is, 0 to 1 (1 once the run is past the cap stage, where nothing more unlocks).</summary>
    public static double StageShare(long progress)
    {
        long p = Math.Max(0, progress);
        return p >= (long)Tuning.StageCap * Tuning.StageFrames ? 1 : p % Tuning.StageFrames / (double)Tuning.StageFrames;
    }

    public static int StageAt(long progress) => (int)Math.Clamp(1 + Math.Max(0, progress) / Tuning.StageFrames, 1, Tuning.StageCap);

    /// <summary>The progress a stage begins at.</summary>
    public static long StageStart(int stage) => (long)(Math.Clamp(stage, 1, Tuning.StageCap) - 1) * Tuning.StageFrames;

    /// <summary>The stage number the run is in, counted past the cap: the laps after boss 10 are stages 11, 12 and so on.</summary>
    public static int VStage(long progress) => (int)Math.Min(int.MaxValue - 1, 1 + Math.Max(0, progress) / Tuning.StageFrames);

    /// <summary>The last frame of stage <paramref name="v"/> (counted past the cap): where progress waits for the boss.</summary>
    public static long GateFrame(int v) => (long)Math.Max(1, v) * Tuning.StageFrames - 1;

    /// <summary>Progress after a death: the stage reached is kept and <see cref="Tuning.DeathSetback"/> of the way into the next is
    /// given back. The ramp eases for a moment and never starts over, and no stage, not even the first, can trap a player.
    /// The stage here is the virtual one, so a death in the laps after boss 10 keeps its lap.</summary>
    public static long AfterDeath(long progress)
    {
        long start = (long)(VStage(progress) - 1) * Tuning.StageFrames, into = Math.Max(0, progress - start);
        return start + into - (long)(into * Tuning.DeathSetback);
    }

    /// <summary>0 at stage 1, 1 at the cap and beyond.</summary>
    private static double Fraction(int stage) => (Math.Clamp(stage, 1, Tuning.StageCap) - 1) / (double)(Tuning.StageCap - 1);

    private static double Lerp(double a, double b, int stage) => stage <= 1 ? a : a + (b - a) * Fraction(stage);

    public static int FillerGap(int stage) => (int)Math.Round(Lerp(Tuning.FillerGapFrames, Tuning.FillerGapCap, stage));
    public static int FillerExtra(int stage) => (int)Math.Round(Lerp(0, Tuning.FillerExtraCap, stage));
    public static double HpMul(int stage) => Lerp(1, Tuning.HpMulCap, stage);
    public static double BoltSpeedMul(int stage) => Lerp(1, Tuning.BoltSpeedMulCap, stage);
    /// <summary>Frames an evasive enemy waits between sidesteps: long at stage 1, short at the cap.</summary>
    public static int EvadeCooldown(int stage) => (int)Math.Round(Lerp(Tuning.EvadeCooldownStart, Tuning.EvadeCooldownCap, stage));
    public static double FireGapMul(int stage) => Lerp(1, Tuning.FireGapMulCap, stage);

    /// <summary>Groups of newer kinds in a filler wave: none in the first half of stage 1, one in its second half, two from stage 2,
    /// three from stage 6. <paramref name="share"/> is how far into the stage the run is (the whole stage by default).</summary>
    public static int Specials(int stage, double share = 1) => stage <= 1 ? (share >= FirstUnlockShare ? 1 : 0)
        : stage < Tuning.ThreeGroupsFromStage ? 2 : Tuning.SpecialsCap;

    /// <summary>The share of stage 1 at which the first newer kind unlocks.</summary>
    private static double FirstUnlockShare => UnlockTable[0].Min(a => a.Share);

    /// <summary>The stage a kind first unlocks at; 1 for a kind that is there from the start.</summary>
    public static int UnlockStage(EnemyKind kind)
    {
        for (int s = 1; s <= UnlockTable.Length; s++)
            if (UnlockTable[s - 1].Any(a => a.Kind == kind)) return s;
        return 1;
    }

    /// <summary>The share of its stage at which a kind unlocks: 0 for the start of the stage, and for a kind that is there from the start.</summary>
    public static double UnlockShare(EnemyKind kind)
    {
        foreach (var row in UnlockTable)
            foreach (var a in row) if (a.Kind == kind) return a.Share;
        return 0;
    }

    /// <summary>Whether a kind has unlocked by the time the run is <paramref name="share"/> of the way through <paramref name="stage"/>
    /// (the whole stage by default, which is whether it comes at all in that stage).</summary>
    public static bool Unlocked(EnemyKind kind, int stage, double share = 1)
    {
        if (!InTable(kind)) return true;
        int s = UnlockStage(kind);
        return s < stage || s == stage && UnlockShare(kind) <= share;
    }

    private static bool InTable(EnemyKind kind) => UnlockTable.Any(row => row.Any(a => a.Kind == kind));

    /// <summary>The kind that unlocks part way through a stage, for a hint shown when the run reaches it: the last of a stage's unlocks
    /// with a share above 0. Null for a stage with none.</summary>
    public static EnemyKind? MidStageKind(int stage)
    {
        EnemyKind? found = null;
        if (stage >= 1 && stage - 1 < UnlockTable.Length)
            foreach (var a in UnlockTable[stage - 1]) if (a.Share > 0) found = a.Kind;
        return found;
    }

    /// <summary>Every kind this stage's banner hints at: its unlocks that start with the stage and have a line, in table order.</summary>
    public static EnemyKind[] Introduced(int stage) => Unlocks(stage, 0).Where(k => HintFor(k) is not null).ToArray();

    /// <summary>The mini-bosses: the kinds that arrive to the sting and are capped on the field (<see cref="MiniBossCapAt"/>). The lesser gunship has its own rule.</summary>
    public static bool IsMiniBoss(EnemyKind kind) => kind is EnemyKind.Warden or EnemyKind.Nest or EnemyKind.Yoke or EnemyKind.Chorus;

    /// <summary>How many of one mini-boss kind may be alive (and not leaving) at once.</summary>
    public static int MiniBossCapAt(int stage) => stage >= Tuning.MiniBossCapLateFromStage ? Tuning.MiniBossCapLate : Tuning.MiniBossCap;

    public static string? HintFor(EnemyKind kind) => kind switch
    {
        EnemyKind.Mine => "mines: fly around them",
        EnemyKind.Diver => "divers charge where they aim",
        EnemyKind.Splitter => "splitters burst in two",
        EnemyKind.Turret => "turrets aim a burst",
        EnemyKind.Warden => "wardens shield others: kill one first",
        EnemyKind.Sower => "sowers leave one gap: find the dots",
        EnemyKind.Flanker => "flankers hold a lane: change height",
        EnemyKind.Lobber => "lobbers throw slow shells",
        EnemyKind.Nest => "nests hatch gnats: shoot the nest",
        EnemyKind.Yoke => "yokes pull in pairs",
        EnemyKind.Lurk => "lurks wait unseen",
        EnemyKind.Chorus => "a chorus sings in turn",
        EnemyKind.Glint => "glints flash before they strike",
        EnemyKind.Fuse => "fuses burn down: get clear",
        _ => null,
    };

    /// <summary>The boss kind of a stage (counted past the cap, where the bosses come round again).</summary>
    public static EnemyKind Boss(int stage) => BossTable[BossNumber(stage) - 1];

    /// <summary>Which of the ten bosses a (virtual) stage holds, 1 to 10.</summary>
    public static int BossNumber(int stage) => stage <= Tuning.StageCap ? Math.Max(1, stage) : (stage - Tuning.StageCap - 1) % Tuning.StageCap + 1;

    /// <summary>The boss for the gate at the end of the stage progress is in: its number, and how much tougher this lap makes it.</summary>
    public readonly record struct LapBossInfo(int Number, double HpMul, EnemyKind Kind);

    /// <summary>After boss 10 the bosses return in order, one for every <see cref="Tuning.StageFrames"/> of further progress, each lap
    /// <see cref="Tuning.BossLapHp"/> tougher than the one before, up to <see cref="Tuning.BossLapHpCap"/> times.</summary>
    public static LapBossInfo LapBoss(long progress)
    {
        int v = VStage(progress);
        int lap = v <= Tuning.StageCap ? 0 : (v - Tuning.StageCap - 1) / Tuning.StageCap + 1;
        return new LapBossInfo(BossNumber(v), Math.Min(Tuning.BossLapHpCap, 1 + Tuning.BossLapHp * lap), Boss(v));
    }

    /// <summary>When lesser gunships visit a stage: a number of visits spread evenly over the stage, or one every so many frames
    /// with a limit on how many are alive at once.</summary>
    public readonly record struct LesserPlan(int Visits, int Every, int MaxAlive)
    {
        /// <summary>The progress into the stage at which the <paramref name="i"/>th visit (from 0) is due.</summary>
        public int At(int i) => (i + 1) * Tuning.StageFrames / (Visits + 1);
    }

    public static LesserPlan Lessers(int stage) => stage switch
    {
        < Tuning.LesserFirstStage => new LesserPlan(0, 0, 0),
        <= Tuning.LesserTwoVisitsLastStage => new LesserPlan(2, 0, 0),
        <= Tuning.LesserThreeVisitsLastStage => new LesserPlan(3, 0, 0),
        Tuning.LesserRepeatFromStage => new LesserPlan(0, Tuning.LesserRepeatFramesStage9, Tuning.LesserMaxAlive),   // the last stages are meant to see the lesser gunship most
        _ => new LesserPlan(0, Tuning.LesserRepeatFramesStage10, Tuning.LesserMaxAlive),
    };
}
