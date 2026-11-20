namespace FeedRaider.Engine.Raider;

/// <summary>Where the stage's boss gate is. <see cref="None"/>: progress runs. Then, at the last frame
/// of a stage whose boss is unbeaten: the field clears, a banner warns, the boss fights.</summary>
public enum GateState { None, Clearing, Warning, Fighting }

/// <summary>
/// The boss gate and the lesser gunships. A stage does not end on a clock: progress stops on its last frame until that stage's
/// boss is down, or has stayed <see cref="Tuning.BossStayFrames"/> frames and left. Lesser gunships come
/// between times, on the schedule <see cref="Ramp.Lessers"/> gives. With <see cref="BossGates"/> false none of this runs
/// and the game runs without bosses.
/// </summary>
public sealed partial class RaiderGame
{
    /// <summary>False turns the gate and the lesser gunships off (the balance tests that measure waves alone). Set once, when the game is made.</summary>
    public bool BossGates { get; init; } = true;

    public GateState Gate { get; private set; }

    /// <summary>Frames spent in Clearing or Warning; 0 on entering either.</summary>
    public int GateAge { get; private set; }

    /// <summary>The highest (virtual) stage whose boss is down or gone.</summary>
    public int BossesBeaten { get; private set; }

    /// <summary>The boss of the fight in progress; null outside Fighting.</summary>
    public Enemy? Boss { get; private set; }

    /// <summary>The name the warning banner shows: the planned boss's.</summary>
    public string BossName { get; private set; } = "";

    private long _fightStart;
    private EnemyKind _bossKind = EnemyKind.Gunship;

    /// <summary>Frames the gate has been Fighting. Counted on the frame clock, so a death or a hit-stop never moves when the boss leaves.</summary>
    public int FightFrames => Gate == GateState.Fighting ? (int)(Frame - _fightStart) : 0;

    /// <summary>The last frame progress may reach before the next unbeaten boss.</summary>
    private long GateTarget => Ramp.GateFrame(BossesBeaten + 1);

    /// <summary>For a test: puts progress where it says, with nothing else moved.</summary>
    internal void SetProgress(long progress) => Progress = Math.Max(0, progress);

    /// <summary>Jumps to the warning of stage <paramref name="stage"/>'s boss (a dev jump, and tests).</summary>
    public void StartAtBoss(int stage)
    {
        stage = Math.Max(1, stage);
        if (!BossGates) { StartAtStage(stage); return; }
        StartAtStage(Math.Min(stage, Tuning.StageCap));
        BossesBeaten = stage - 1;
        Progress = Ramp.GateFrame(stage);
        StageShown = Stage;
        StageBannerLeft = 0;
        EnterWarning();
    }

    private void ResetGate()
    {
        Gate = GateState.None;
        GateAge = 0;
        Boss = null;
        BossName = "";
        _lesserV = 0;
    }

    // ---------------------------------------------------------------- progress and the gate

    /// <summary>The body of <c>UpdateStage</c>: moves progress, or steps the gate that holds it.</summary>
    private void StepProgress()
    {
        if (!BossGates) { if (Alive) Progress++; return; }
        if (Gate != GateState.None) { StepGate(); return; }
        if (!Alive) return;
        long target = GateTarget;
        if (Progress < target) Progress++;
        if (Progress >= target)
        {
            Progress = target;
            Gate = GateState.Clearing;
            GateAge = 0;
        }
    }

    private void StepGate()
    {
        switch (Gate)
        {
            case GateState.Clearing:
                GateAge++;
                if (Enemies.Count == 0 && _pending.Count == 0 || GateAge >= Tuning.BossClearFrames)
                {
                    foreach (var e in Enemies) e.Leaving = true;
                    _pending.Clear();
                    EnterWarning();
                }
                break;
            case GateState.Warning:
                if (++GateAge >= Tuning.BossWarnFrames) EnterFight();
                break;
            case GateState.Fighting:
                if (Frame - _fightStart >= Tuning.BossStayFrames) BossLeaves();
                break;
        }
    }

    private void EnterWarning()
    {
        Gate = GateState.Warning;
        GateAge = 0;
        _bossKind = Ramp.LapBoss(GateTarget).Kind;
        BossName = BehaviourFor(_bossKind).Name;
        Sound(SfxNames.Warn);
    }

    private const double BossSpawnX = 0.5;
    private const double GunshipSway = 0.22;
    private const double LesserXMin = 0.3, LesserXSpan = 0.4;
    private const int LesserXSeed = 700, LesserPhaseSeed = 800;

    private void EnterFight()
    {
        var plan = Ramp.LapBoss(GateTarget);
        _bossKind = plan.Kind;
        var spawn = new EnemySpawn(_bossKind, BossSpawnX, 0, GunshipSway, 0, null);
        // a boss's hit points are written per boss and never scaled by the stage; only the lap after boss 10 makes it tougher
        double hp = BehaviourFor(_bossKind).Hp * plan.HpMul;
        Boss = SpawnEnemy(spawn, false, null, null, e => { e.Hp = hp; e.MaxHp = hp; });
        Gate = GateState.Fighting;
        GateAge = 0;
        _fightStart = Frame;
    }

    /// <summary>The boss stayed too long: it leaves, and the stage advances with no score and no drop.</summary>
    private void BossLeaves()
    {
        if (Boss is { } b)
        {
            Cue(b, VoiceCue.Leaving);   // while it still has the floor
            if (b.Voice is not null) b.Voice.LeftCued = true;
            b.Leaving = true;
        }
        Advance();
    }

    /// <summary>The boss is down: its score and drop were handled by <c>Kill</c>; the stage advances.</summary>
    private void BossDown(Enemy boss)
    {
        Sound(SfxNames.BossDown, boss.X);
        Bullets.RemoveAll(b => !b.Friendly);
        Advance();
    }

    /// <summary>The fight is over either way: the next stage begins and its banner shows (even from stage 10 to stage 10).</summary>
    private void Advance()
    {
        int v = BossesBeaten + 1;
        BossesBeaten = v;
        Gate = GateState.None;
        GateAge = 0;
        Boss = null;
        BossName = "";
        Progress = (long)v * Tuning.StageFrames;
        StageShown = Stage;
        StageBannerLeft = Tuning.StageBannerFrames;
    }

    /// <summary>What a death does to the gate: nothing in a fight (the boss keeps its damage and no progress is lost); in Clearing or
    /// Warning the usual setback, and the gate is cancelled to open again at the end of the stage.</summary>
    private void GateDeath()
    {
        if (BossGates && Gate == GateState.Fighting) return;
        Progress = Ramp.AfterDeath(Progress);
        if (Gate is GateState.Clearing or GateState.Warning)
        {
            Gate = GateState.None;
            GateAge = 0;
            BossName = "";
        }
    }

    /// <summary>Called at the end of <c>Kill</c>: the boss going down ends the fight; any other kill's progress stops at the gate.</summary>
    private void GateAfterKill(Enemy e)
    {
        if (!BossGates) return;
        if (Gate == GateState.Fighting && ReferenceEquals(e, Boss)) { BossDown(e); return; }
        long target = GateTarget;
        if (Progress > target) Progress = target;
    }

    /// <summary>True while a boss is announced or fighting: the boss track plays, and the HUD says BOSS.</summary>
    private bool BossUp => BossGates && Gate is GateState.Warning or GateState.Fighting;

    /// <summary>The music key of the boss that is announced or fighting: its kind's name in lower case. Null when no boss is up.</summary>
    private string? BossMusicKey => BossUp ? Key(_bossKind) : null;

    /// <summary>The key of the boss the gate of the stage in progress will bring, for the music's fetch-ahead hint. Null with the gates off.</summary>
    private string? AheadMusicKey => BossGates ? Key(Ramp.LapBoss(GateTarget).Kind) : null;

    private static string Key(EnemyKind kind) => kind.ToString().ToLowerInvariant();

    // ---------------------------------------------------------------- lesser gunships

    private int _lesserV, _visitsDone, _lesserClock, _lesserMade;

    /// <summary>Sends the lesser gunships the schedule calls for. A visit that falls due while a gate is up is dropped, not queued.</summary>
    private void UpdateLessers()
    {
        if (!BossGates) return;
        int v = Ramp.VStage(Progress);
        if (v != _lesserV) { _lesserV = v; _visitsDone = 0; _lesserClock = 0; }
        var plan = Ramp.Lessers(v);
        long into = Progress - (long)(v - 1) * Tuning.StageFrames;
        while (_visitsDone < plan.Visits && into >= plan.At(_visitsDone))
        {
            if (Gate == GateState.None) SpawnLesser();
            _visitsDone++;
        }
        if (plan.Every <= 0) return;
        if (Gate != GateState.None) { _lesserClock = 0; return; }
        if (!Alive) return;
        if (++_lesserClock >= plan.Every && Enemies.Count(e => e.Lesser) < plan.MaxAlive)
        {
            SpawnLesser();
            _lesserClock = 0;
        }
    }

    /// <summary>One lesser gunship: the gunship's sprite and volleys with a flat hit-point pool and a short stay.</summary>
    internal Enemy SpawnLesser()
    {
        int n = ++_lesserMade;
        var spawn = new EnemySpawn(EnemyKind.Gunship, LesserXMin + Hashing.Unit(_seed, LesserXSeed + n) * LesserXSpan, 0, GunshipSway, Hashing.Unit(_seed, LesserPhaseSeed + n) * Math.Tau, null);
        return SpawnEnemy(spawn, false, null, null, e => { e.Lesser = true; e.Hp = Tuning.LesserHp; e.MaxHp = Tuning.LesserHp; });
    }
}
