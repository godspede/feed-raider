namespace FeedRaider.Engine.Raider;

/// <summary>
/// The speed burst: a pickup that fires the moment it is flown over and is never stored. For <see cref="BurstTotalFrames"/> frames
/// the page scrolls up fast (<see cref="CardField.Rush"/>), the stars streak, the stage clock runs ahead, and the ship flies
/// through every bolt and shell and past every small enemy and mine; a boss, a mini-boss or a lesser gunship still hurts it.
/// A burst never runs during a boss fight. The hooks elsewhere are <see cref="UpdateBurst"/> in <c>Step</c>,
/// <see cref="HurtsInBurst"/> in <c>Collide</c>, <see cref="ProgressPace"/> in the stage clock and the boss fight's
/// <see cref="EaseBurstOut"/> and <see cref="EndBurst"/>.
/// </summary>
public sealed partial class RaiderGame
{
    public const int BurstTotalFrames = Tuning.BurstInFrames + Tuning.BurstFrames + Tuning.BurstOutFrames;

    /// <summary>True for a real run: the first burst pickup comes at <see cref="Tuning.OpeningBurstFrame"/>. The start page
    /// tour and the tests that build a game for one purpose leave it off. Set once, when the game is made.</summary>
    public bool OpeningBurst { get; init; }

    /// <summary>Frames of the running burst still to go; 0 when none runs.</summary>
    public int BurstLeft { get; private set; }

    public bool Bursting => BurstLeft > 0;

    private int _burstAge;
    private bool _openingDropped;
    private long _burstTakenTick = NoBurstYet;
    private const long NoBurstYet = long.MinValue / 2;

    /// <summary>How hard the burst pulls, 0 to 1: rising over the ease-in, 1 at full, falling over the ease-out; 0 when none runs.</summary>
    public double BurstStrength => Bursting ? Math.Min(1, Math.Min(_burstAge / (double)Tuning.BurstInFrames, BurstLeft / (double)Tuning.BurstOutFrames)) : 0;

    /// <summary>The page's frames per row the burst asks for at its present strength: <see cref="Tuning.BurstFramesPerRow"/> at full,
    /// and one, two, ... steps of it slower on the way in and out.</summary>
    internal int BurstFramesPerRow
    {
        get
        {
            int steps = Tuning.BurstEaseSteps;
            int slower = Math.Clamp(steps - (int)Math.Floor(BurstStrength * steps), 1, steps);
            return Tuning.BurstFramesPerRow * slower;
        }
    }

    /// <summary>How far the stars fall this frame, in frames of their usual drift.</summary>
    private double StarPace => 1 + (Tuning.BurstStarsMul - 1) * BurstStrength;

    /// <summary>Frames the stage clock moves this frame.</summary>
    private int ProgressPace => Bursting ? Tuning.BurstProgressMul : 1;

    /// <summary>False during a boss fight (<see cref="Gate"/> past <see cref="GateState.None"/>): no burst starts, none drops, and one on the field cannot be taken.</summary>
    private bool BurstMayStart => !(BossGates && Gate != GateState.None);

    /// <summary>Whether a burst pickup may drop now: none is on the field or running, the last was taken long enough ago, and no boss fight is on.</summary>
    private bool BurstMayDrop =>
        BurstMayStart && !Bursting && Tick - _burstTakenTick >= Tuning.BurstRepeatFrames && !Pickups.Exists(p => p.Kind == PickupKind.Burst);

    /// <summary>A body that still hurts the ship during a burst: the gate's boss, any boss kind (a lesser gunship is one), and the mini-bosses.</summary>
    private bool HurtsInBurst(Enemy e) => ReferenceEquals(e, Boss) || BehaviourFor(e.Kind).Boss || IsVisitor(e);

    /// <summary>One tick of the burst, before the cards move: drops the run's opening pickup when it is due, and runs the burst's clock.</summary>
    private void UpdateBurst()
    {
        if (OpeningBurst && !_openingDropped && Tick >= Tuning.OpeningBurstFrame)
        {
            _openingDropped = true;
            if (Deaths == 0 && Alive && BurstMayStart) DropOpeningBurst();
        }
        if (!Bursting) return;
        _burstAge++;
        if (--BurstLeft == 0) { EndBurst(); return; }
        Cards.Rush(BurstFramesPerRow);
    }

    /// <summary>The opening pickup: straight ahead of the ship, falling fast enough to reach it in
    /// <see cref="Tuning.OpeningBurstReachFrames"/> frames if it has not moved. Ahead of the ship and not at the field's middle,
    /// because a host's later layout keeps the ship's column from the first, which can sit wide of the new middle.</summary>
    private void DropOpeningBurst()
    {
        double y = PH * Tuning.OpeningBurstRow;
        var p = SpawnPickup(PickupKind.Burst, PX, y);
        p.Drift = Math.Max(Tuning.PickupDrift, (PY - y) / Tuning.OpeningBurstReachFrames);
    }

    /// <summary>The ring flashed for a single frame as the burst starts, from and to radii in pixels.</summary>
    private const int BurstRingFrom = 4, BurstRingTo = 14, BurstRingFrames = 1;

    private void StartBurst()
    {
        _burstTakenTick = Tick;
        _burstAge = 0;
        BurstLeft = BurstTotalFrames;
        Cards.Rush(BurstFramesPerRow);
        AddRing(PX, PY, BurstRingFrom, BurstRingTo, Palette.White, BurstRingFrames);
    }

    /// <summary>A boss fight starting on a burst at full speed: the burst slows to its end at once.</summary>
    private void EaseBurstOut()
    {
        BurstLeft = Math.Min(BurstLeft, Tuning.BurstOutFrames);
    }

    /// <summary>Ends the burst: the page goes back to the player's speed, a falling tone plays, and a ship left inside an enemy gets
    /// <see cref="Tuning.BurstExitGraceFrames"/> of grace, so the end never kills it on the spot.</summary>
    private void EndBurst()
    {
        BurstLeft = 0;
        Cards.Rush(null);
        if (!Alive || Hp <= 0) return;
        Sound(SfxNames.BurstEnd, PX);
        if (Invuln < Tuning.BurstExitGraceFrames && Enemies.Exists(e => e.Hp > 0 && !e.Untouchable && TouchesShip(e)))
        {
            Invuln = Tuning.BurstExitGraceFrames;
            InvulnIsShield = false;
        }
    }

    /// <summary>A burst pickup on the field when a boss fight starts blinks out, and cannot be taken meanwhile.</summary>
    private void FadeBurstPickups()
    {
        foreach (var p in Pickups)
            if (p.Kind == PickupKind.Burst) p.Age = Math.Max(p.Age, Tuning.PickupLifeFrames - Tuning.PickupBlinkFrames);
    }
}
