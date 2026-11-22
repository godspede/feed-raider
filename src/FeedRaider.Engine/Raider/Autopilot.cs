namespace FeedRaider.Engine.Raider;

/// <summary>
/// The start page's tour of the game: a script and the pilot that flies it. The script is a fixed list of times, the same on
/// every loop: a first wave, a like made by a click on a heart, a speed burst, a mini-boss, then three of the ten
/// bosses with a later stage's waves after each. The pilot goes where the scene needs it, steps out of the way of what it can,
/// and fires all the while. It only ever presses the movement, fire and bomb keys, and clicks the heart with a pointer mark that glides to it
/// and clicks; the like it lands is the signed-out kind: a mark on a built-in
/// post, with nothing sent and no account. The tour's game is <see cref="RaiderGame.Charmed"/>, so the ship never dies
/// and the tour always reaches its end. The same seed against the same game gives the same tour, tick for tick.
/// </summary>
public sealed class Autopilot(ulong seed)
{
    /// <summary>One boss scene: when its warning starts, which stage's boss it is, and when it is cut short if still up.</summary>
    public readonly record struct Scene(int At, int Stage, int Until);

    private const int TicksPerSecond = Tuning.Fps;

    /// <summary>The length of one loop of the tour, in ticks.</summary>
    public const int MaxRunTicks = 84 * TicksPerSecond;
    /// <summary>The opening wave: a line of the plainest enemy, so the first seconds are a fight.</summary>
    public const int FirstWaveAt = TicksPerSecond / 2;
    /// <summary>From here the pointer looks for a heart to click, and gives up at <see cref="LikeUntil"/> if no post came by.</summary>
    public const int LikeFrom = 4 * TicksPerSecond, LikeUntil = 20 * TicksPerSecond;
    /// <summary>The tick the speed burst pickup drops in the pilot's path: after the like, before the mini-boss, with no boss fight on.</summary>
    public const int BurstAt = 9 * TicksPerSecond;
    /// <summary>The tick of the tour loop the mini-boss comes on at.</summary>
    public const int MiniBossAt = 13 * TicksPerSecond;
    /// <summary>The three boss scenes. A boss killed early leaves the next stage's waves on the field until the next scene.</summary>
    public static readonly Scene[] Scenes =
    [
        new(22 * TicksPerSecond, 2, 38 * TicksPerSecond),
        new(41 * TicksPerSecond, 4, 57 * TicksPerSecond),
        new(60 * TicksPerSecond, 7, 76 * TicksPerSecond),
    ];
    /// <summary>Weapon pickups the script drops in the pilot's path, so the bosses come apart in the time they have.</summary>
    private static readonly int[] WeaponAt = [15 * TicksPerSecond, 19 * TicksPerSecond, 23 * TicksPerSecond, 43 * TicksPerSecond, 62 * TicksPerSecond];
    /// <summary>How long the pointer mark takes to glide to the heart, how long it rests on it before the click, and how long it stays after
    /// the heart has lit, in ticks: long enough to be seen to click, short enough that the like is not the whole scene.</summary>
    private const int PointerGlideTicks = 36, PointerRestTicks = 8, PointerLingerTicks = 24;
    /// <summary>How far into a boss scene the pilot bombs, if the boss is still up.</summary>
    private static readonly int[] BombAfter = [8 * TicksPerSecond, 12 * TicksPerSecond];

    /// <summary>How many ticks ahead a falling bolt is followed to where it will cross the ship's row.</summary>
    private const int DodgeLookaheadTicks = 22;
    /// <summary>How near a falling bolt's landing point must be to the ship for the pilot to step aside, in pixels.</summary>
    private const double BoltBandPx = 5;
    /// <summary>How near a gunning hull must be sideways to the ship for the pilot to step aside, in pixels.</summary>
    private const double HullBandPx = 8;
    /// <summary>How close to a side wall, or to the HUD rows, the pilot goes before it turns inward, in pixels.</summary>
    private const int EdgeMarginPx = 8;
    /// <summary>How far to the side of the ship a scripted weapon pickup drops, and how far it keeps from the walls, in pixels.</summary>
    private const int PickupOffsetPx = 14, PickupMarginPx = 10;
    /// <summary>The pilot's home row as a fraction of the field height, and the sweep's half width and period (in ticks per radian) when nothing needs it.</summary>
    private const double HomeRow = 0.82, SweepAmp = 0.34, SweepTicksPerRadian = 38.0;

    /// <summary>How many distinct starting phases the sweep has across one full turn.</summary>
    private const int PhaseSteps = 628;
    /// <summary>The opening line of grunts: how many, where the first stands and the gap between them, as fractions of the field width.</summary>
    private const int FirstWaveGrunts = 5;
    private const double FirstWaveLeft = 0.18, FirstWaveGap = 0.16;
    /// <summary>The height a scripted weapon pickup drops from, as a fraction of the field height.</summary>
    private const double PickupDropRow = 0.35;
    /// <summary>How far above the ship an enemy may be and still count as a clear target to line up on, in pixels.</summary>
    private const int TargetClearancePx = 6;
    /// <summary>The highest row a heart's button may sit on for the pilot to go for it, as a fraction of the field height.</summary>
    private const double HeartReachRow = 0.6;
    /// <summary>A falling bolt slower than this, in pixels per tick, is not tracked.</summary>
    private const double MinBoltFallSpeed = 0.1;
    /// <summary>The band of rows around the ship, above and below, in which a gunning hull makes the pilot step aside, in pixels.</summary>
    private const int HullBandAbovePx = 16, HullBandBelowPx = 4;
    /// <summary>How far the ship may be from where the pilot wants it before a key is pressed, in pixels.</summary>
    private const double AlignDeadZonePx = 1;

    private readonly double _phase = Hashing.Mix(seed) % PhaseSteps * (2 * Math.PI / PhaseSteps);
    private int _t;
    private Card? _heart;
    private (double X, double Y) _pointerFrom;
    private int _glide, _linger;

    /// <summary>Ticks flown so far.</summary>
    public int Ticks => _t;
    /// <summary>True once the pilot's like has landed.</summary>
    public bool Liked { get; private set; }
    /// <summary>Bosses the tour has brought on so far.</summary>
    public int BossesMet { get; private set; }

    /// <summary>Plays the script's part for the coming tick and sets the keys. Call it once before each <see cref="RaiderGame.Step"/>.</summary>
    public void Drive(RaiderGame g)
    {
        if (g.PW <= 0 || g.PH <= 0) return;
        Script(g);
        ClickHeart(g);
        PressKeysToward(g, Target(g));
        _t++;
    }

    /// <summary>Sets the keys for the coming tick the way <see cref="Drive"/> does, without the tour: it plays none of the script
    /// (no wave, mini-boss or weapon pickup arrives, no scene is cut to and no bomb is dropped) and likes nothing. It goes for
    /// a pickup on the field, lines up on the boss or the lowest enemy, sweeps when nothing needs it, steps aside and fires. Its
    /// ticks count in <see cref="Ticks"/> and <see cref="Finished"/> as <see cref="Drive"/>'s do. Call it once before each
    /// <see cref="RaiderGame.Step"/>, on a pilot that is never also given <see cref="Drive"/>.</summary>
    public void Fly(RaiderGame g)
    {
        if (g.PW <= 0 || g.PH <= 0) return;
        PressKeysToward(g, Target(g));
        _t++;
    }

    /// <summary>Presses the keys that move the ship toward <paramref name="target"/>, or aside when a bolt or a hull is about
    /// to reach it, and holds fire.</summary>
    private void PressKeysToward(RaiderGame g, (double X, double Y) target)
    {
        var (tx, ty) = target;
        int dodge = Dodge(g);
        var inp = g.Input;
        inp.Key(GameKey.Left, dodge < 0 || dodge == 0 && g.PX > tx + AlignDeadZonePx);
        inp.Key(GameKey.Right, dodge > 0 || dodge == 0 && g.PX < tx - AlignDeadZonePx);
        inp.Key(GameKey.Up, g.PY > ty + AlignDeadZonePx);
        inp.Key(GameKey.Down, g.PY < ty - AlignDeadZonePx);
        inp.Key(GameKey.Fire, true);
    }

    /// <summary>True when the loop is over and a fresh one should begin.</summary>
    public bool Finished() => _t >= MaxRunTicks;

    private void Script(RaiderGame g)
    {
        if (_t == FirstWaveAt)
            for (int i = 0; i < FirstWaveGrunts; i++) g.SpawnKind(EnemyKind.Grunt, FirstWaveLeft + FirstWaveGap * i);
        if (_t == MiniBossAt) g.SpawnKind(EnemyKind.Warden, 0.5);
        foreach (int at in WeaponAt)
            if (_t == at) g.SpawnPickup(PickupKind.Weapon, Math.Clamp(g.PX + (at / TicksPerSecond % 2 == 0 ? PickupOffsetPx : -PickupOffsetPx), PickupMarginPx, g.PW - PickupMarginPx), g.PH * PickupDropRow);
        if (_t == BurstAt) g.SpawnPickup(PickupKind.Burst, Math.Clamp(g.PX + PickupOffsetPx, PickupMarginPx, g.PW - PickupMarginPx), g.PH * PickupDropRow);
        foreach (var scene in Scenes)
        {
            if (_t == scene.At) { g.TourCut(scene.Stage, boss: true); BossesMet++; }
            if (_t == scene.Until && g.Gate != GateState.None) g.TourCut(scene.Stage + 1, boss: false);
            if (g.Gate == GateState.Fighting && g.Bombs > 0)
                foreach (int after in BombAfter)
                    if (_t == scene.At + after) g.Input.Bomb();
        }
    }

    /// <summary>Where the pilot wants to be this tick.</summary>
    private (double X, double Y) Target(RaiderGame g)
    {
        double home = g.PH * HomeRow;
        foreach (var p in g.Pickups)
            if (p.Y > 0 && p.Y < g.PH) return (p.X, p.Y);
        if (g.Boss is { Leaving: false } boss)
        {
            double x = boss.X;
            if (boss.Parts is { } parts)
            {
                var part = parts.Find(p => p.State == PartState.Open) ?? parts.Find(p => p.State != PartState.Dead);
                if (part is not null) x += part.OX;
            }
            return (x, home);
        }
        Enemy? low = null;
        foreach (var e in g.Enemies)
            if (!e.Leaving && e.Y > 0 && e.Y < g.PY - TargetClearancePx && (low is null || e.Y > low.Y)) low = e;
        if (low is not null) return (low.X, home);
        return (g.PW * (0.5 + SweepAmp * Math.Sin(_t / SweepTicksPerRadian + _phase)), home);
    }

    /// <summary>Plays the scripted click: while the script wants a like and a heart is in reach, a pointer mark glides from the ship to
    /// the heart's button, rests on it and clicks it, the way a player's click does, so the heart lights at once; the mark stays a moment
    /// and goes. The ship flies on meanwhile.</summary>
    private void ClickHeart(RaiderGame g)
    {
        if (Liked)
        {
            if (_linger > 0 && --_linger == 0) g.TourPointer = null;
            return;
        }
        if (_t < LikeFrom || _t >= LikeUntil) return;
        if (_heart is not null && _heart.Marks.Like == ButtonState.Marked) { Liked = true; _heart = null; _linger = PointerLingerTicks; return; }
        if (_heart is null || !Reachable(g, _heart))
        {
            _heart = null;
            g.TourPointer = null;
            foreach (var c in g.Cards.Cards)
                if (Reachable(g, c) && g.Cards.ButtonBox(c, PostAction.Like).Y < g.PH * HeartReachRow) { _heart = c; break; }
            if (_heart is null) return;
            _pointerFrom = (g.PX, g.PY);
            _glide = 0;
        }
        var button = g.Cards.ButtonBox(_heart, PostAction.Like);
        double x = button.X + button.W / 2.0, y = button.Y + button.H / 2.0;
        double along = Math.Min(1, ++_glide / (double)PointerGlideTicks), eased = along * along * (3 - 2 * along);
        g.TourPointer = (_pointerFrom.X + (x - _pointerFrom.X) * eased, _pointerFrom.Y + (y - _pointerFrom.Y) * eased);
        if (_glide == PointerGlideTicks + PointerRestTicks) g.Input.Click(g.Layout.Col0 + x / CardField.PixelsPerCell, y / CardField.PixelsPerCell);
    }

    private static bool Reachable(RaiderGame g, Card c)
    {
        if (!g.CanPress(c, PostAction.Like)) return false;
        var button = g.Cards.ButtonBox(c, PostAction.Like);
        return button.Y >= CardPainter.HudPixelRows + EdgeMarginPx && button.Y + button.H < g.PH - EdgeMarginPx;
    }

    /// <summary>-1 or 1 to step left or right out of the way of a bolt or a hull about to reach the ship; 0 when nothing is.</summary>
    private int Dodge(RaiderGame g)
    {
        foreach (var b in g.Bullets)
        {
            if (b.Friendly || b.VY <= MinBoltFallSpeed) continue;
            double dt = (g.PY - b.Y) / b.VY;
            if (dt < 0 || dt > DodgeLookaheadTicks) continue;
            double at = b.X + b.VX * dt;
            if (Math.Abs(at - g.PX) < BoltBandPx) return Away(g, at);
        }
        foreach (var e in g.Enemies)
            if (e.Armed && Math.Abs(e.X - g.PX) < HullBandPx && e.Y > g.PY - HullBandAbovePx && e.Y < g.PY + HullBandBelowPx) return Away(g, e.X);
        return 0;
    }

    /// <summary>The side to step to: away from <paramref name="x"/>, and inward when the ship is at an edge.</summary>
    private static int Away(RaiderGame g, double x)
    {
        if (g.PX < EdgeMarginPx) return 1;
        if (g.PX > g.PW - EdgeMarginPx) return -1;
        return g.PX >= x ? 1 : -1;
    }
}
