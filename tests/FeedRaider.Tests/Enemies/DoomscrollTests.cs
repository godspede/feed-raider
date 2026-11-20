using System.Runtime.CompilerServices;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// Doomscroll, boss 10: the last boss, very big, three fights in one. These tests give its layout on the four fields (against
/// the drawings), each phase beginning on its event, the quiet while the crown falls, the blink before every bolt, the scroll
/// bar and its marker, the heart's hops and rings, the voice, and the fight (a bot at weapon level 4).
/// </summary>
public sealed class DoomscrollTests(ITestOutputHelper output)
{
    private const int Crown = 0, CrownFalls = 1, MawShut = 2, MawOpen = 3, HuskFalls = 4, Heart = 5;

    /// <summary>Frames the crown falls for, the maw stays shut and open, and the husk falls for.</summary>
    private const int CrownFallFrames = 60, ShutFrames = 70, OpenFrames = 60, HuskFallFrames = 45;

    /// <summary>A muzzle blinks for <see cref="Blink"/> frames before its bolt, solid white for the last <see cref="SolidBlink"/>;
    /// the guns fire left to right <see cref="GunGap"/> apart, then rest <see cref="GunRest"/>.</summary>
    private const int Blink = 24, SolidBlink = 8, GunGap = 20, GunRest = 60;

    /// <summary>The heart rests <see cref="HopRest"/> frames, then hops for <see cref="HopFrames"/>, and throws a scroll bar after
    /// every <see cref="BarEvery"/>th hop.</summary>
    private const int HopRest = 50, HopFrames = 12, HopCycle = HopRest + HopFrames, BarEvery = 3, RingBolts = 10;

    /// <summary>The ages of a shut maw from which the gold marker shows, and on which the bar is spat.</summary>
    private const int MarkerFrom = 4, BarFrame = 40;

    private const double GunSpeed = 1.15, BarSpeed = 0.8, RingSpeed = 1.0;

    /// <summary>A scroll bar's bolts stand <see cref="BarSpacing"/> apart but for one gap of <see cref="BarGap"/>, and the bar
    /// reaches within <see cref="BarEdge"/> of either side of the field.</summary>
    private const int BarSpacing = 6, BarGap = 14, BarEdge = 9;

    /// <summary>The fewest bolts a scroll bar brings into being at once, and the fewest gold dots a marker column shows.</summary>
    private const int LeastBarBolts = 7, LeastMarkerDots = 4;

    /// <summary>The row a scroll bar from the heart starts on.</summary>
    private const int HeartBarRow = 6;

    /// <summary>The hit points of the crown's guns, the maw and the heart before the band, and the least share of them the band leaves.</summary>
    private const double GunsHp = 126, MawHp = 84, HeartHp = 54, LeastHpShare = 0.3;

    private const int BossScore = 20000, GunsScore = 3600, MawScore = 3000;

    private const int HeartRadius = 7;

    /// <summary>Damage that kills any part in one shot.</summary>
    private const double KillingDamage = 1000;

    /// <summary>Decimal places a position, a speed or hit points must agree to, and the fewer a ring bolt's speed and angle agree to,
    /// since the test works those out from the bolt's position and velocity.</summary>
    private const int Places = 6, RingPlaces = 5;

    /// <summary>How far apart two values that are equal but for rounding may be, and the same for the spacing of a bar's bolts.</summary>
    private const double RoundingSlack = 1e-9, SpacingSlack = 1e-6;

    /// <summary>The width with no guns (two edges of <see cref="EdgeW"/> and the maw's <see cref="MawCols"/>), each gun's segment on a
    /// side, and the most guns on a side.</summary>
    private const int EdgeW = 3, MawCols = 31, CoreWidth = Sides * EdgeW + MawCols, GunPitch = 9, MaxSegments = 5;

    /// <summary>The two sides of the body, each with its edge and its guns; and the two parts every field has, the crown and the maw.</summary>
    private const int Sides = 2, CrownAndMaw = 2;

    /// <summary>The body's rows, tall and short: the short form leaves out the top rows of spikes.</summary>
    private const int TallBodyRows = 29, ShortBodyRows = 15, SpikeRows = 2;

    private const int GunW = 7, GunH = 6, MawW = 27, MawH = 7, JawTop = 15, ShortCrownRows = 5;

    /// <summary>A gun's muzzle, its red tip, is this many rows down from the gun's top row.</summary>
    private const int MuzzleRow = 4;

    /// <summary>The rows of the mouth's rim between the crown's bottom and the maw's box.</summary>
    private const int MawRimRows = 1;

    /// <summary>Guns on a side in each drawing.</summary>
    private const int TallDrawnGuns = 3, ShortDrawnGuns = 1;

    private const int BossStage = 10, FightLevel = Tuning.MaxWeaponLevel;

    /// <summary>The terminal field, in columns and rows: short and wide, so the boss is in its short form.</summary>
    private const int TerminalCols = 80, TerminalRows = 24;

    /// <summary>The phone field, in columns and rows: narrow and tall.</summary>
    private const int PhoneCols = 48, PhoneRows = 60;

    /// <summary>Half a minute of frames: long enough for the gate to bring the boss, and for the boss to kill a ship that stands still.</summary>
    private const int HalfMinute = 30 * Tuning.Fps;

    /// <summary>The boss's top row on every field but the smallest.</summary>
    private const int TopRow = 6;

    /// <summary>Frames a test waits for the boss to come to rest, to change mode, to say its first words and to have its box read out.</summary>
    private const int ArrivalWait = 400, ModeWait = 400, SpeakWait = 600, ReadOutWait = 1500;

    /// <summary>The names of its parts.</summary>
    private const string CrownPart = "crown", MawPart = "maw", JawPart = "jaw", HeartPart = "heart";

    /// <summary>The name on its box, and its own lines: on arriving, while the maw is shut, when the maw opens and on dying.</summary>
    private const string BossName = "DOOMSCROLL", ArriveLine = "Keep scrolling.", ShutLine = "Just one more.", OpenLine = "You can stop any time.", DeathLine = "Refresh.";

    /// <summary>The share of the field's width the boss arrives at: its middle.</summary>
    private const double MidField = 0.5;

    /// <summary>The middle column of the field, in pixels.</summary>
    private static double MidX(RaiderGame g) => g.PW / 2.0;

    private static EnemyBehaviour Doom => RaiderGame.TableBehaviour(EnemyKind.Doomscroll)!;

    // ---------------------------------------------------------------- the drawings

    private static readonly string[] TallDrawing =
    [
        "#......#........#........#.................#.#.#.................#........#........#......#",
        "##....###......###......###..............#########..............###......###......###....##",
        "######ooo######ooo######ooo######ooooooooooooooooooooooooo######ooo######ooo######ooo######",
        "#ooooo+ooo+oooo+ooo+oooo+ooo+oooo+ooooo+ooooooooooo+ooooo+oooo+ooo+oooo+ooo+oooo+ooo+ooooo#",
        "#o+o+++o+++oo+++o+++oo+++o+++oo+++o+o+++o+o+o+o+o+++o+o+++o+oo+++o+++oo+++o+++oo+++o+++o+o#",
        "#ooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooo#",
        ".#########################################################################################.",
        "....#ooroo#..#ooroo#..#ooroo#.#ooooooooooooooooooooooooooooo#.#ooroo#..#ooroo#..#ooroo#....",
        "....#oryro#..#oryro#..#oryro#.#o###########################o#.#oryro#..#oryro#..#oryro#....",
        "....#orrro#..#orrro#..#orrro#.#o#o#o#o#o#o#o#o#o#o#o#o#o#o#o#.#orrro#..#orrro#..#orrro#....",
        ".....#oro#....#oro#....#oro#..#oo#o#o#o#o#o#o#o#o#o#o#o#o#oo#..#oro#....#oro#....#oro#.....",
        "......#r#......#r#......#r#...#o###########################o#...#r#......#r#......#r#......",
        ".......#........#........#....#oo#o#o#o#o#o#o#o#o#o#o#o#o#oo#....#........#........#.......",
        "..............................#o#o#o#o#o#o#o#o#o#o#o#o#o#o#o#..............................",
        "..............................#o###########################o#..............................",
        "..............................#ooooooooooooooooooooooooooooo#..............................",
        "...............................#ooo+++++++++++++++++++++ooo#...............................",
        "...............................#oooo+ooooooooooooooooo+oooo#...............................",
        "................................#oooo+++++++++++++++++oooo#................................",
        "................................#ooooo+ooooooooooooo+ooooo#................................",
        ".................................#ooooo+++++++++++++ooooo#.................................",
        "..................................#ooooo+ooooooooo+ooooo#..................................",
        "...................................#ooooo+++++++++ooooo#...................................",
        "....................................##ooooo+ooo+ooooo##....................................",
        "......................................##ooooooooooo##......................................",
        "........................................##ooooooo##........................................",
        "..........................................##ooo##..........................................",
        "............................................###............................................",
        ".............................................#.............................................",
    ];

    private static readonly string[] ShortDrawing =
    [
        "######ooo######ooooooooooooooooooooooooo######ooo######",
        "#ooooo+ooo+oooo+ooooo+ooooooooooo+ooooo+oooo+ooo+ooooo#",
        "#o+o+++o+++oo+++o+o+++o+o+o+o+o+++o+o+++o+oo+++o+++o+o#",
        "#ooooooooooooooooooooooooooooooooooooooooooooooooooooo#",
        ".#####################################################.",
        "....#ooroo#.#ooooooooooooooooooooooooooooo#.#ooroo#....",
        "....#oryro#.#o###########################o#.#oryro#....",
        "....#orrro#.#o#o#o#o#o#o#o#o#o#o#o#o#o#o#o#.#orrro#....",
        ".....#oro#..#oo#o#o#o#o#o#o#o#o#o#o#o#o#oo#..#oro#.....",
        "......#r#...#o###########################o#...#r#......",
        ".......#....#oo#o#o#o#o#o#o#o#o#o#o#o#o#oo#....#.......",
        "............#o#o#o#o#o#o#o#o#o#o#o#o#o#o#o#............",
        "............#o###########################o#............",
        ".............#ooooooooooooooooooooooooooo#.............",
        "..............###########################..............",
    ];

    // ---------------------------------------------------------------- the layout, the table

    /// <summary>What the table states for one field: guns on a side, tall or short, the row of the top.</summary>
    private sealed record Lay(int Pw, int Ph, int GunsPerSide, bool Tall, int Top)
    {
        public int W => CoreWidth + Sides * GunPitch * GunsPerSide;
        public int H => Tall ? TallBodyRows : ShortBodyRows;
        public int Left => (Pw - W) / 2;
        public int CrownH => (Tall ? SpikeRows : 0) + ShortCrownRows;
        public int GunTop => CrownH;
        public int MawTop => CrownH + MawRimRows;
        public int Floor => (int)EnemyKit.BossFloor(Ph);
        public double CentreX => Left + W / 2.0;
        public double CentreY => Top + H / 2.0;
        public int Parts => CrownAndMaw + Sides * GunsPerSide + (Tall ? 1 : 0);   // and the jaw on a tall body
        public int GunLeft(bool right, int i) => (right ? EdgeW + GunPitch * GunsPerSide + MawCols : EdgeW) + (GunPitch - GunW) / 2 + GunPitch * i;
        /// <summary>Every gun's muzzle: the left side's guns from the left, then the right side's.</summary>
        public IEnumerable<(int X, int Y)> Muzzles() =>
            from right in new[] { false, true }
            from i in Enumerable.Range(0, GunsPerSide)
            select (Left + GunLeft(right, i) + GunW / 2, Top + GunTop + MuzzleRow);

        /// <summary>The boxes, relative to the boss's top left: name, left, top, width, height.</summary>
        public IEnumerable<(string Name, int L, int T, int Wd, int Ht)> Boxes()
        {
            yield return (CrownPart, 0, 0, W, CrownH);
            for (int i = 0; i < GunsPerSide; i++) yield return ($"gun-l-{i}", GunLeft(false, i), GunTop, GunW, GunH);
            for (int i = 0; i < GunsPerSide; i++) yield return ($"gun-r-{i}", GunLeft(true, i), GunTop, GunW, GunH);
            yield return (MawPart, EdgeW + GunPitch * GunsPerSide + (MawCols - MawW) / 2, MawTop, MawW, MawH);
            if (Tall) yield return (JawPart, EdgeW + GunPitch * GunsPerSide, JawTop, MawCols, TallBodyRows - JawTop);
        }
    }

    public static IEnumerable<object[]> Fields() =>
    [
        [30, 16, 1, false, 5],                                       // 60x32: the smallest
        [TerminalCols, TerminalRows, MaxSegments, false, TopRow],    // 128x48: a terminal
        [PhoneCols, PhoneRows, 3, true, TopRow],                     // 96x120: a phone
        [EnemyKit.BigCols, EnemyKit.BigRows, MaxSegments, true, TopRow],   // 128x92: a desktop
    ];

    private static RaiderGame NewGame(int cols, int rows)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        EnemyKit.Clear(g);
        return g;
    }

    private static Lay LayOf(RaiderGame g, int gunsPerSide, bool tall, int top) => new(g.PW, g.PH, gunsPerSide, tall, top);

    private static Part PartOf(Enemy e, string name) => e.Parts!.Single(p => p.Name == name);

    private static IEnumerable<Part> Guns(Enemy e) => e.Parts!.Where(p => p.Name.StartsWith("gun-"));

    private static void Run(RaiderGame g, int frames, bool pilot = true)
    {
        for (int i = 0; i < frames; i++)
        {
            if (pilot) Pilot.Frame(g, shoot: false);
            EnemyKit.StepSweeping(g);
        }
    }

    /// <summary>Makes the boss arrive and steps until it has come to rest on its rows (the step on which it stops is the first of its fight).</summary>
    private static Enemy Settled(RaiderGame g, Lay lay)
    {
        var e = g.SpawnKind(EnemyKind.Doomscroll, MidField);
        for (int i = 0; i < ArrivalWait && e.Y < lay.CentreY - RoundingSlack; i++) { EnemyKit.StepSweeping(g); Assert.Empty(Hostile(g)); }   // it fires nothing on its way in
        Assert.Equal(lay.CentreY, e.Y, Places);
        return e;
    }

    private static void Shoot(RaiderGame g, Enemy e, Part p, double dmg = KillingDamage) => EnemyKit.Shot(g, e.X + p.OX, e.Y + p.OY, dmg);

    /// <summary>Shoots every living gun in one step: the last one's death brings the crown down on that very step.</summary>
    private static void KillGuns(RaiderGame g, Enemy e)
    {
        foreach (var p in Guns(e).Where(p => p.State != PartState.Dead)) Shoot(g, e, p);
        EnemyKit.StepSweeping(g);
    }

    private static void StepToMode(RaiderGame g, Enemy e, int mode)
    {
        for (int i = 0; i < ModeWait && e.Mode != mode; i++) { Pilot.Frame(g, shoot: false); EnemyKit.StepSweeping(g); }
        Assert.Equal(mode, e.Mode);
    }

    /// <summary>Gets the boss to the maw's first opening: guns down, the crown's fall over, the maw shut and then open.</summary>
    private static void ToOpenMaw(RaiderGame g, Enemy e)
    {
        KillGuns(g, e);
        StepToMode(g, e, MawShut);
        StepToMode(g, e, MawOpen);
    }

    /// <summary>Gets the boss to the heart: the maw shot to pieces the step it opens, and the husk's fall over.</summary>
    private static void ToHeart(RaiderGame g, Enemy e)
    {
        ToOpenMaw(g, e);
        Shoot(g, e, PartOf(e, MawPart));
        EnemyKit.StepSweeping(g);
        Assert.Equal(HuskFalls, e.Mode);
        StepToMode(g, e, Heart);
    }

    /// <summary>The hits the ship has taken: its shield takes the first, then a hit kills.</summary>
    private static int Taken(RaiderGame g) => Tuning.ShieldHits - g.Shield + g.Deaths;

    private static List<Bullet> Hostile(RaiderGame g) => g.Bullets.Where(b => !b.Friendly).ToList();

    private static bool IsBar(Bullet b) => !b.Friendly && !b.Aimed && b.VX == 0 && Math.Abs(b.VY - BarSpeed) < RoundingSlack;

    /// <summary>Adds this frame's new hostile bolts to <paramref name="seen"/>, then is true when they make a whole bar (the few bolts held behind the jaw and let go later are not a new bar).</summary>
    private static bool RecordNewBoltsAndSpotBar(RaiderGame g, HashSet<Bullet> seen) => g.Bullets.Where(b => !b.Friendly && seen.Add(b)).Count(IsBar) >= LeastBarBolts;

    private static bool IsRing(Bullet b) => !b.Friendly && !b.Aimed && Math.Abs(double.Hypot(b.VX, b.VY) - RingSpeed) < RoundingSlack;

    /// <summary>Columns below row <paramref name="y0"/> with a dotted gold line in them (<see cref="LeastMarkerDots"/> gold pixels at least, none touching another in the column).</summary>
    private static List<int> MarkerColumns(RaiderGame g, Surface f, int y0)
    {
        var cols = new List<int>();
        for (int x = 0; x < g.PW; x++)
        {
            int dots = 0; bool touching = false, prev = false;
            for (int y = y0; y < g.PH; y++)
            {
                bool gold = (f.Get(x, y) & EnemyKit.RgbMask) == Palette.Gold;
                if (gold) { dots++; if (prev) touching = true; }
                prev = gold;
            }
            if (dots >= LeastMarkerDots && !touching) cols.Add(x);
        }
        return cols;
    }

    /// <summary>The middle of a scroll bar's one gap, from the x of each of its bolts.</summary>
    private static double GapCentre(RaiderGame g, IEnumerable<double> boltXs)
    {
        var xs = boltXs.Order().ToList();
        double left = double.NaN;
        int gaps = 0;
        for (int i = 1; i < xs.Count; i++)
        {
            double apart = xs[i] - xs[i - 1];
            if (Math.Abs(apart - BarSpacing) < SpacingSlack) continue;
            Assert.True(Math.Abs(apart - BarGap) < SpacingSlack, $"bolts {apart:0.###} apart at x {xs[i - 1]:0.###}");
            gaps++; left = xs[i - 1];
        }
        Assert.Equal(1, gaps);
        Assert.True(xs[0] < BarEdge && xs[^1] > g.PW - BarEdge, "the bar runs across the whole field");
        return left + BarGap / 2.0;
    }

    /// <summary>A plain pilot: it dodges what it can see coming and, when told, fires and keeps under the lowest open part.</summary>
    private static class Pilot
    {
        /// <summary>How many steps of the ship either side the pilot weighs as goals, and how near an edge it will go.</summary>
        private const int Steps = 40, EdgeMargin = 2;

        /// <summary>Open parts within this many rows of each other count as level, and the nearer one is aimed at.</summary>
        private const double LevelRows = 2;

        /// <summary>The cost of a goal: per pixel off the aim (scaled down when not shooting), per pixel walked, and per pixel
        /// a goal comes inside <see cref="EdgeRoom"/> of an edge or stands off the column it waits in for a bar.</summary>
        private const double AimCost = 0.02, IdleAimShare = 0.2, WalkCost = 0.004, EdgeRoom = 5, EdgeCost = 0.5, WaitCost = 5;

        /// <summary>The ship keeps this many rows off the bottom.</summary>
        private const double FloorRows = 4;

        /// <summary>The age of a shut maw from which its bar has gone by.</summary>
        private const int BarGoneAt = 60;

        /// <summary>The cost of a bolt that would hit, falling off with the frames until it hits.</summary>
        private const double HitCost = 1000, HitCostFalloff = 0.05;

        public static void Frame(RaiderGame g, bool shoot)
        {
            g.Input.Key(GameKey.Fire, shoot);
            double px = g.PX, py = g.PY, aim = MidX(g), low = double.MinValue;
            foreach (var e in g.Enemies)
                foreach (var p in e.Parts ?? [])
                    if (p.State == PartState.Open && (e.Y + p.OY > low + LevelRows || (e.Y + p.OY > low - LevelRows && Math.Abs(e.X + p.OX - px) < Math.Abs(aim - px)))) { low = Math.Max(low, e.Y + p.OY); aim = e.X + p.OX; }
            double best = double.MaxValue, goal = px;
            var maw = g.Enemies.FirstOrDefault(q => q.Kind == EnemyKind.Doomscroll);
            var mem = Memory.GetOrCreateValue(g);
            WatchTheMaw(g, maw, mem);
            double? wait = WaitColumn(maw, mem);
            for (int k = -Steps; k <= Steps; k++)
            {
                double cx = Math.Clamp(px + k * Tuning.ShipSpeed, EdgeMargin, g.PW - EdgeMargin);
                double cost = Math.Abs(cx - aim) * AimCost * (shoot ? 1 : IdleAimShare) + Math.Abs(cx - px) * WalkCost + Risk(g, px, py, cx) + Math.Max(0, EdgeRoom - Math.Min(cx, g.PW - cx)) * EdgeCost;
                if (wait is { } at) cost += Math.Abs(cx - at) * WaitCost;
                if (cost < best) { best = cost; goal = cx; }
            }
            g.Input.Key(GameKey.Down, py < g.PH - FloorRows);   // as low as it goes: the boss never reaches there
            double halfStep = Tuning.ShipSpeed / 2;   // near enough the goal that one more step would overshoot it
            g.Input.Key(GameKey.Left, px > goal + halfStep);
            g.Input.Key(GameKey.Right, px < goal - halfStep);
        }

        private sealed class Seen { public double Gap = double.NaN, Park = double.NaN; public RaiderView View = new(); }
        private static readonly ConditionalWeakTable<RaiderGame, Seen> Memory = new();

        /// <summary>
        /// The column to wait in for a bar still out of sight, or null when none is coming: beside the gold line until the bar is
        /// spat, then the line itself, the bar's gap. The bolts in the mouth's columns are hidden behind the jaw until they clear the
        /// chin, so no pilot can see them; a player follows the dotted gold line, which this pilot remembers through
        /// <see cref="WatchTheMaw"/> and waits beside, because the ship and its shield would hide the line.
        /// </summary>
        private static double? WaitColumn(Enemy? maw, Seen mem)
        {
            if (maw is null || maw.Mode != MawShut || double.IsNaN(mem.Gap) || maw.ModeAge >= BarGoneAt) return null;
            return maw.ModeAge < BarFrame ? mem.Park : mem.Gap;
        }

        /// <summary>Forgets the gold line in <paramref name="mem"/> on any frame the maw is not shut, and while the line shows
        /// clears the pickups and reads it off the drawn field through <see cref="RememberMarker"/>.</summary>
        private static void WatchTheMaw(RaiderGame g, Enemy? maw, Seen mem)
        {
            if (maw is null || maw.Mode != MawShut) mem.Gap = mem.Park = double.NaN;
            else if (maw.ModeAge >= MarkerFrom && maw.ModeAge < BarFrame) RememberMarker(g, maw, mem);
        }

        /// <summary>Clears the pickups and draws the field, reads the gold line below the boss off it into <paramref name="mem"/>'s gap, and the first time
        /// the line is seen picks the column beside it to wait in, on the ship's side unless that is past an edge.</summary>
        private static void RememberMarker(RaiderGame g, Enemy e, Seen mem)
        {
            var f = EnemyKit.ClearPickupsAndDraw(g, mem.View, new Screen(g.Layout.Cols, g.Layout.Rows));
            var cols = MarkerColumns(g, f, (int)Math.Ceiling(e.Y + e.Sprite.H / 2.0));
            if (cols.Count == 0) return;
            mem.Gap = cols.Average();
            if (!double.IsNaN(mem.Park)) return;
            double side = g.PX < mem.Gap ? -1 : 1, park = mem.Gap + side * ParkOffset;
            if (park < EdgeMargin || park > g.PW - EdgeMargin) park = mem.Gap - side * ParkOffset;
            mem.Park = Math.Clamp(park, EdgeMargin, g.PW - EdgeMargin);
        }

        private const double ShipHalfHeight = 7, HitHalfWidth = 2.6, HitHalfHeight = 3.2;

        /// <summary>How far from the gold line the pilot waits for the bar to be spat.</summary>
        private const double ParkOffset = 11;

        private const int Horizon = 40;   // frames of look-ahead: a human sidesteps a bolt late, not a second before

        /// <summary>A bolt whose vertical speed is below this flies level.</summary>
        private const double StillSpeed = 1e-6;

        /// <summary>How soon a bolt in flight, at whatever slope, would meet a ship that walks to <paramref name="cx"/> at the game's speed and waits there, as a cost.</summary>
        private static double Risk(RaiderGame g, double px, double py, double cx)
        {
            double risk = 0, reach = Math.Abs(cx - px), dir = Math.Sign(cx - px);
            foreach (var b in g.Bullets)
            {
                if (b.Friendly) continue;
                int t0 = 1, t1 = Horizon;
                if (Math.Abs(b.VY) > StillSpeed)
                {
                    double toTop = (py - ShipHalfHeight - b.Y) / b.VY, toBottom = (py + ShipHalfHeight - b.Y) / b.VY;
                    t0 = Math.Max(1, (int)Math.Floor(Math.Min(toTop, toBottom))); t1 = Math.Min(Horizon, (int)Math.Ceiling(Math.Max(toTop, toBottom)));
                }
                else if (Math.Abs(b.Y - py) > ShipHalfHeight) continue;
                for (int t = t0; t <= t1; t++)
                {
                    double sx = px + dir * Math.Min(reach, Tuning.ShipSpeed * t);
                    if (Math.Abs(b.X + b.VX * t - sx) <= HitHalfWidth && Math.Abs(b.Y + b.VY * t - py) <= HitHalfHeight) { risk += HitCost / (1 + t * HitCostFalloff); break; }
                }
            }
            return risk;
        }
    }

    /// <summary>The drawing rebuilt by its own rule (a cap, gunsPerSide gun segments, the maw, the mirror), so every field can be checked pixel by pixel.</summary>
    private static char DrawingChar(bool tall, int gunsPerSide, int w, int x, int y)
    {
        const int chinRows = 2, upperChin = MawCols - 2, lowerChin = MawCols - 4;
        const int chinTop = ShortBodyRows - chinRows, drawnMaw = EdgeW + GunPitch * TallDrawnGuns;
        if (!tall && y >= chinTop)   // the short form closes the mouth with two rows of chin
        {
            int len = y == chinTop ? upperChin : lowerChin, cx = x - (w - len) / 2;
            if (cx < 0 || cx >= len) return '.';
            return y == chinTop ? (cx == 0 || cx == len - 1 ? '#' : 'o') : '#';
        }
        int ty = tall ? y : y + SpikeRows;
        bool right = x > (w - 1) / 2;   // the right half reads the drawing's right half: the maw is not quite its own mirror
        int xl = right ? w - 1 - x : x;
        int sx = xl < EdgeW ? xl : xl < EdgeW + GunPitch * gunsPerSide ? EdgeW + (xl - EdgeW) % GunPitch : drawnMaw + (xl - EdgeW - GunPitch * gunsPerSide);
        return TallDrawing[ty][right ? TallDrawing[ty].Length - 1 - sx : sx];
    }

    [Fact]
    public void The_rule_rebuilds_the_drawings()
    {
        for (int y = 0; y < TallDrawing.Length; y++)
            for (int x = 0; x < TallDrawing[y].Length; x++)
                Assert.True(TallDrawing[y][x] == DrawingChar(true, TallDrawnGuns, TallDrawing[y].Length, x, y), $"tall drawing at {x},{y}");
        for (int y = 0; y < ShortDrawing.Length; y++)
            for (int x = 0; x < ShortDrawing[y].Length; x++)
                Assert.True(ShortDrawing[y][x] == DrawingChar(false, ShortDrawnGuns, ShortDrawing[y].Length, x, y), $"short drawing at {x},{y}");
    }

    // ---------------------------------------------------------------- what it is

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.True(Doom.Boss);
        Assert.True(Doom.Speaks);
        Assert.Equal(BossName, Doom.Name);
        Assert.True(Doom.Name.Length <= EnemySpeechBox.MaxLabel);
        const double total = GunsHp + MawHp + HeartHp;
        Assert.InRange(Doom.Hp, total * LeastHpShare, total);
        Assert.Equal(BossScore, Doom.Score);
    }

    [Theory, MemberData(nameof(Fields))]
    public void It_lays_out_as_the_table_says_and_is_drawn_as_the_drawing(int cols, int rows, int gunsPerSide, bool tall, int top)
    {
        var g = NewGame(cols, rows);
        var lay = LayOf(g, gunsPerSide, tall, top);
        Assert.Equal(lay.GunsPerSide, Math.Clamp((g.PW - CoreWidth) / (Sides * GunPitch), 1, MaxSegments));
        var e = Settled(g, lay);
        Assert.Equal((lay.W, lay.H), (e.Sprite.W, e.Sprite.H));
        Assert.Equal(lay.CentreX, e.X, Places);
        Assert.Equal(lay.Parts, e.Parts!.Count);
        Assert.True(EnemyKit.InsideWidth(g, e));
        Assert.True(EnemyKit.LowestPixel(e) <= lay.Floor + 1, "its lowest row stays on or above the floor row");
        Assert.True(lay.Top + lay.H - 1 <= lay.Floor);

        const double halfPixel = 0.5001;
        foreach (var (name, bl, bt, bw, bh) in lay.Boxes())
        {
            var p = PartOf(e, name);
            Assert.Equal(bw / 2, p.HW);
            Assert.Equal(bh / 2, p.HH);
            double boxX = lay.Left + bl + bw / 2.0, boxY = lay.Top + bt + bh / 2.0;
            Assert.InRange(e.X + p.OX, boxX - halfPixel, boxX + halfPixel);
            Assert.InRange(e.Y + p.OY, boxY - halfPixel, boxY + halfPixel);
        }

        var muzzles = lay.Muzzles().ToHashSet();
        var f = EnemyKit.ClearPickupsAndDraw(g, new RaiderView(), new Screen(cols, rows));
        for (int y = 0; y < lay.H; y++)
            for (int x = 0; x < lay.W; x++)
            {
                char c = gunsPerSide == TallDrawnGuns && tall ? TallDrawing[y][x] : gunsPerSide == ShortDrawnGuns && !tall ? ShortDrawing[y][x] : DrawingChar(tall, gunsPerSide, lay.W, x, y);
                if (c == '.' || muzzles.Contains((lay.Left + x, lay.Top + y))) continue;   // a muzzle may be blinking white
                Assert.True((f.Get(lay.Left + x, lay.Top + y) & EnemyKit.RgbMask) == Art.DarkPal[c], $"pixel {x},{y} ('{c}') on {cols}x{rows}");
            }
    }

    [Theory, MemberData(nameof(Fields))]
    public void Its_hit_points_and_scores_are_shared_as_the_table_says(int cols, int rows, int gunsPerSide, bool tall, int top)
    {
        var g = NewGame(cols, rows);
        var e = Settled(g, LayOf(g, gunsPerSide, tall, top));
        var guns = Guns(e).ToList();
        Assert.Equal(Sides * gunsPerSide, guns.Count);
        double scale = guns.Sum(p => p.MaxHp) / GunsHp;
        Assert.True(scale > 0);
        Assert.All(guns, p => { Assert.Equal(GunsHp * scale / (Sides * gunsPerSide), p.MaxHp, Places); Assert.Equal(PartState.Open, p.State); Assert.True(p.Vital); });
        Assert.Equal(GunsScore, guns.Sum(p => p.Score));
        var maw = PartOf(e, MawPart);
        Assert.Equal(MawHp * scale, maw.MaxHp, Places);
        Assert.Equal(MawScore, maw.Score);
        Assert.Equal(PartState.Armoured, maw.State);
        Assert.False(PartOf(e, CrownPart).Vital);
        Assert.Equal(PartState.Armoured, PartOf(e, CrownPart).State);
        if (tall) { Assert.False(PartOf(e, JawPart).Vital); Assert.Equal(PartState.Armoured, PartOf(e, JawPart).State); }
        Assert.Equal((GunsHp + MawHp) * scale, e.Hp, Places);   // the crown and the maw: the heart comes only when the husk falls
        Assert.DoesNotContain(e.Parts!, p => p.Name == HeartPart);
    }

    // ---------------------------------------------------------------- the three phases

    [Theory, MemberData(nameof(Fields))]
    public void Each_phase_begins_on_its_event(int cols, int rows, int gunsPerSide, bool tall, int top)
    {
        var g = NewGame(cols, rows);
        var lay = LayOf(g, gunsPerSide, tall, top);
        var e = Settled(g, lay);
        Assert.Equal(Crown, e.Mode);

        // the crown falls on the step the last gun dies, and not before
        var guns = Guns(e).ToList();
        for (int i = 0; i < guns.Count - 1; i++)
        {
            Shoot(g, e, guns[i]);
            EnemyKit.StepSweeping(g);
            Assert.Equal(PartState.Dead, guns[i].State);
            Assert.Equal(Crown, e.Mode);
        }
        Shoot(g, e, guns[^1]);
        EnemyKit.StepSweeping(g);
        Assert.Equal(CrownFalls, e.Mode);
        Assert.Equal(0, e.ModeAge);
        Assert.Equal(tall ? new[] { JawPart, MawPart } : new[] { MawPart }, e.Parts!.Select(p => p.Name).Order().ToArray());

        for (int i = 1; i < CrownFallFrames; i++) { Run(g, 1); Assert.Equal(CrownFalls, e.Mode); }
        Run(g, 1);
        Assert.Equal(MawShut, e.Mode);
        Assert.Equal(0, e.ModeAge);

        // the maw is armoured while shut and takes nothing
        const int beforeShot = 10;
        var maw = PartOf(e, MawPart);
        Run(g, beforeShot);
        Shoot(g, e, maw);
        Run(g, 1);
        Assert.Equal(maw.MaxHp, maw.Hp);
        Assert.Equal(PartState.Armoured, maw.State);
        int shut = beforeShot + 2;   // the step it shut on, the steps before the shot, and the shot's
        while (true) { Run(g, 1); if (e.Mode != MawShut) break; shut++; }
        Assert.Equal(ShutFrames, shut);
        Assert.Equal(MawOpen, e.Mode);
        Assert.Equal(PartState.Open, maw.State);
        int open = 1;
        while (true) { Run(g, 1); if (e.Mode != MawOpen) break; open++; }
        Assert.Equal(OpenFrames, open);
        Assert.Equal(MawShut, e.Mode);
        StepToMode(g, e, MawOpen);

        // the husk falls on the step the maw dies
        const double scratch = 30;
        double mawHp = maw.Hp;
        Shoot(g, e, maw, scratch);
        Run(g, 1);
        Assert.Equal(mawHp - scratch, maw.Hp, Places);
        Assert.Equal(MawOpen, e.Mode);
        Shoot(g, e, maw);
        Run(g, 1);
        Assert.Equal(HuskFalls, e.Mode);
        Assert.Equal(0, e.ModeAge);
        Assert.Equal(new[] { HeartPart }, e.Parts!.Select(p => p.Name).ToArray());
        int fall = 1;
        while (true) { Run(g, 1); if (e.Mode != HuskFalls) break; fall++; }
        Assert.Equal(HuskFallFrames, fall);
        Assert.Equal(Heart, e.Mode);
        Assert.False(e.Untouchable);

        // the kill is the heart's
        var heart = PartOf(e, HeartPart);
        Assert.True(heart.Vital);
        Assert.Equal(PartState.Open, heart.State);
        Assert.Equal((HeartRadius, HeartRadius), (heart.HW, heart.HH));
        int kills = g.Kills;
        long score = g.Score;
        Shoot(g, e, heart);
        EnemyKit.StepSweeping(g);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal(kills + 1, g.Kills);
        Assert.True(g.Score - score >= BossScore, $"the kill pays its {BossScore}");
    }

    [Theory, MemberData(nameof(Fields))]
    public void Nothing_can_be_hurt_and_nothing_fires_while_the_crown_falls(int cols, int rows, int gunsPerSide, bool tall, int top)
    {
        var g = NewGame(cols, rows);
        var e = Settled(g, LayOf(g, gunsPerSide, tall, top));
        KillGuns(g, e);
        Assert.Equal(CrownFalls, e.Mode);
        var maw = PartOf(e, MawPart);
        double hp = e.Hp;
        const int leastDebris = 40;
        int most = 0;
        for (int i = 1; i < CrownFallFrames; i++)
        {
            var before = g.Bullets.Where(b => !b.Friendly).ToHashSet();
            Shoot(g, e, maw);
            EnemyKit.Shot(g, e.X, e.Y, KillingDamage);
            Pilot.Frame(g, shoot: false);
            EnemyKit.StepSweeping(g);
            Assert.Equal(CrownFalls, e.Mode);
            Assert.True(e.Untouchable);
            Assert.Equal(hp, e.Hp, Places);
            Assert.Equal(maw.MaxHp, maw.Hp);
            Assert.All(g.Bullets.Where(b => !b.Friendly), b => Assert.Contains(b, before));   // not one new hostile bolt
            most = Math.Max(most, g.Particles.Count);
        }
        Assert.InRange(most, leastDebris, Tuning.MaxParticles);   // the plates come down as debris, capped at the limit
        Run(g, 1);
        Assert.Equal(MawShut, e.Mode);
        Assert.False(e.Untouchable);
    }

    // ---------------------------------------------------------------- the crown: a muzzle blinks 24 frames before its bolt

    [Theory, MemberData(nameof(Fields))]
    public void Every_muzzle_blinks_for_24_frames_before_its_bolt_left_to_right_20_apart_then_rests_60(int cols, int rows, int gunsPerSide, bool tall, int top)
    {
        var g = NewGame(cols, rows);
        var lay = LayOf(g, gunsPerSide, tall, top);
        var e = Settled(g, lay);
        int muzzleCount = Sides * gunsPerSide;
        var muzzles = lay.Muzzles().ToList();
        var view = new RaiderView();
        var scr = new Screen(cols, rows);
        var white = Enumerable.Range(0, muzzleCount).Select(_ => new List<int>()).ToList();
        var fired = Enumerable.Range(0, muzzleCount).Select(_ => new List<int>()).ToList();
        var known = new HashSet<Bullet>();
        const double muzzleSlack = 1.2, boltStartRows = 2;
        int cycle = GunGap * (muzzleCount - 1) + Blink + GunRest;
        for (int j = 1; j <= cycle + Blink; j++)
        {
            Pilot.Frame(g, shoot: false);
            EnemyKit.StepSweeping(g);
            var f = EnemyKit.ClearPickupsAndDraw(g, view, scr);
            for (int k = 0; k < muzzleCount; k++)
                if ((f.Get(muzzles[k].X, muzzles[k].Y) & EnemyKit.RgbMask) == Palette.White) white[k].Add(j);
            foreach (var b in g.Bullets.Where(b => !b.Friendly && b.Aimed && known.Add(b)))
            {
                int k = Enumerable.Range(0, muzzleCount).OrderBy(q => Math.Abs(muzzles[q].X - b.X)).First();
                Assert.InRange(b.X, muzzles[k].X - muzzleSlack, muzzles[k].X + muzzleSlack);
                Assert.Equal(GunSpeed, double.Hypot(b.VX, b.VY), Places);
                Assert.InRange(b.Y, lay.Top + lay.GunTop + GunH, lay.Top + lay.GunTop + GunH + boltStartRows);   // from the gun's bottom edge
                fired[k].Add(j);
            }
        }
        Assert.Equal(0, Taken(g));
        const int firstRound = 30, latestStart = 22;
        for (int k = 0; k < muzzleCount; k++)
        {
            Assert.Equal(k == 0 ? new[] { Blink, cycle + Blink } : new[] { Blink + GunGap * k }, fired[k].ToArray());
            int firedAt = fired[k][0], flicker = firedAt - SolidBlink - 1;
            Assert.All(white[k].Where(j => j < cycle - firstRound), j => Assert.InRange(j, firedAt - Blink, firedAt - 1));
            for (int j = firedAt - SolidBlink; j < firedAt; j++) Assert.Contains(j, white[k]);   // the last frames are solid
            Assert.Contains(white[k], j => j <= flicker);                         // and before them it flickers
            Assert.DoesNotContain(white[k], j => j <= flicker && white[k].Contains(j + 1) && j + 1 <= flicker);
            Assert.True(white[k].Min() <= firedAt - latestStart, $"gun {k} starts blinking by {firedAt - latestStart}");
        }
    }

    // ---------------------------------------------------------------- the maw: the scroll bar

    [Theory, MemberData(nameof(Fields))]
    public void A_scroll_bar_has_one_14_pixel_gap_on_the_far_side_from_the_last_marked_36_frames_ahead(int cols, int rows, int gunsPerSide, bool tall, int top)
    {
        var g = NewGame(cols, rows);
        var lay = LayOf(g, gunsPerSide, tall, top);
        var e = Settled(g, lay);
        KillGuns(g, e);
        StepToMode(g, e, MawShut);
        var view = new RaiderView();
        var scr = new Screen(cols, rows);
        var maw = PartOf(e, MawPart);
        const int mouth = ShutFrames + OpenFrames;
        const double gapRoom = 12, markerSlack = 1.01, chinSlack = 0.01;
        int mawLeft = (int)Math.Round(e.X + maw.OX - MawCols / 2), mawRight = mawLeft + MawCols - 1;
        var bars = new List<HashSet<double>> { new(), new() };
        var markers = new List<List<int>>();
        var which = new Dictionary<Bullet, int>();
        for (int j = 0; j < mouth + BarFrame + OpenFrames; j++)
        {
            int age = j % mouth;
            Assert.Equal(age < ShutFrames ? MawShut : MawOpen, e.Mode);
            var f = EnemyKit.ClearPickupsAndDraw(g, view, scr);
            markers.Add(MarkerColumns(g, f, lay.Top + lay.H));
            if (age < ShutFrames) Assert.Equal(age, e.ModeAge);
            Pilot.Frame(g, shoot: false);
            EnemyKit.StepSweeping(g);
            foreach (var b in g.Bullets.Where(b => !b.Friendly))
            {
                if (!IsBar(b)) Assert.Fail($"a hostile bolt that is no part of a bar at {b.X}, {b.Y}");
                if (!which.TryGetValue(b, out int bar)) { which[b] = bar = j < mouth ? 0 : 1; Assert.True(b.Y >= lay.Top + lay.CrownH, "a bar starts at the crown's bottom row"); }
                bars[bar].Add(b.X);
                if (b.X >= mawLeft && b.X <= mawRight)
                    Assert.True(b.Y >= lay.Top + lay.H - chinSlack, $"a bolt behind the jaw shows only below the chin ({b.X}, {b.Y})");
            }
        }
        Assert.Equal(0, Taken(g));

        var gaps = bars.Select(b => GapCentre(g, b)).ToList();
        Assert.NotEqual(gaps[0] < MidX(g), gaps[1] < MidX(g));   // the far side from the last
        foreach (double centre in gaps) Assert.InRange(centre, gapRoom, g.PW - gapRoom);

        // dotted gold column at the gap, from the moment the maw shuts, until the bar is spat
        for (int i = 0; i < gaps.Count; i++)
        {
            for (int age = MarkerFrom; age < BarFrame; age++)
            {
                var marked = markers[i * mouth + age];
                Assert.True(marked.Any(c => Math.Abs(c - gaps[i]) <= markerSlack), $"bar {i}: marker at age {age} (gap {gaps[i]}, columns {string.Join(",", marked)})");
            }
        }
    }

    // ---------------------------------------------------------------- the heart

    private static (double X, double Y) HeartAt(Enemy e) => (e.X + PartOf(e, HeartPart).OX, e.Y + PartOf(e, HeartPart).OY);

    [Theory, MemberData(nameof(Fields))]
    public void The_heart_hops_and_throws_rings_on_the_stated_frames_and_keeps_above_the_floor(int cols, int rows, int gunsPerSide, bool tall, int top)
    {
        var g = NewGame(cols, rows);
        var lay = LayOf(g, gunsPerSide, tall, top);
        var e = Settled(g, lay);
        ToHeart(g, e);
        Assert.Equal(0, e.ModeAge);
        var pos = new List<(double X, double Y)> { HeartAt(e) };
        var known = new HashSet<Bullet>(g.Bullets);
        var rings = new List<(int Age, List<(double X, double Y, double VX, double VY)> Bolts)>();
        var barFrames = new List<int>();
        var bars = new List<HashSet<double>>();
        const int cycles = 7, anchorsSeen = 5, AnchorPlaces = 3;
        int frames = HopCycle * cycles;
        for (int m = 1; m <= frames; m++)
        {
            Pilot.Frame(g, shoot: false);
            EnemyKit.StepSweeping(g);
            Assert.Equal(Heart, e.Mode);
            Assert.Equal(m, e.ModeAge);
            pos.Add(HeartAt(e));
            var fresh = g.Bullets.Where(b => !b.Friendly && known.Add(b)).ToList();
            if (fresh.Any(IsRing)) rings.Add((m, fresh.Where(IsRing).Select(b => (b.X, b.Y, b.VX, b.VY)).ToList()));   // as they stand on this frame: they fly on
            if (fresh.Any(IsBar)) { barFrames.Add(m); bars.Add(fresh.Where(IsBar).Select(b => b.X).ToHashSet()); Assert.All(fresh.Where(IsBar), b => Assert.InRange(b.Y, HeartBarRow, HeartBarRow + 1)); }
        }

        // a hop lands on the last frame of its cycle, with a ring thrown as it lands
        Assert.Equal(Enumerable.Range(0, cycles).Select(k => HopCycle - 1 + HopCycle * k).ToArray(), rings.Select(r => r.Age).ToArray());
        foreach (var (age, bolts) in rings)
        {
            Assert.Equal(RingBolts, bolts.Count);
            var centre = pos[age];
            foreach (var b in bolts) Assert.Equal(RingSpeed, double.Hypot(b.X - centre.X, b.Y - centre.Y), RingPlaces);
            var angles = bolts.Select(b => Math.Atan2(b.VY, b.VX)).Order().ToList();
            for (int i = 1; i < angles.Count; i++) Assert.Equal(Math.Tau / RingBolts, angles[i] - angles[i - 1], RingPlaces);
        }

        // a rest at an anchor, then a hop
        for (int c = 0; c < cycles; c++)
        {
            int from = c == 0 ? 0 : HopCycle * c - 1, rested = HopCycle * c + HopRest - 1, landed = HopCycle * (c + 1) - 1;
            for (int m = from; m <= rested; m++) Assert.Equal(pos[from], pos[m]);
            for (int m = rested + 1; m <= landed; m++) Assert.NotEqual(pos[m - 1], pos[m]);
            if (c < cycles - 1) Assert.NotEqual(pos[rested], pos[landed]);
        }
        var anchors = Enumerable.Range(0, cycles).Select(c => c == 0 ? pos[0] : pos[HopCycle * c]).ToList();
        Assert.Equal(anchorsSeen, anchors.Select(a => (Math.Round(a.X, AnchorPlaces), Math.Round(a.Y, AnchorPlaces))).Distinct().Count());

        // above the floor, below the HUD, inside the field
        const double leastRow = 11, floorRoom = 8, lowerRow = 14, hudRows = 4;
        const int upperAnchors = 3, lowerAnchors = anchorsSeen - upperAnchors;
        double row = EnemyKit.BossRow(g.PH);
        if (!tall) row = Math.Max(leastRow, Math.Min(row, lay.Floor - floorRoom));
        foreach (var (x, y) in pos)
        {
            Assert.InRange(y + HeartRadius, 0, lay.Floor);
            Assert.True(y - HeartRadius >= hudRows - RoundingSlack, $"heart top {y - HeartRadius}");
            Assert.InRange(x, HeartRadius, g.PW - HeartRadius);
        }
        var rowsSeen = anchors.Select(a => (X: Math.Round(a.X), Y: Math.Round(a.Y))).Distinct().Select(a => a.Y).GroupBy(y => y).ToDictionary(gr => gr.Key, gr => gr.Count());
        if (tall) Assert.Equal(new SortedDictionary<double, int> { [Math.Round(row)] = upperAnchors, [Math.Round(row + lowerRow)] = lowerAnchors }, new SortedDictionary<double, int>(rowsSeen));
        else Assert.Equal(new[] { Math.Round(row) }, rowsSeen.Keys.ToArray());

        // after every third hop, a scroll bar, with its gap on the far side from the last
        Assert.Equal(Enumerable.Range(1, cycles / BarEvery).Select(k => BarEvery * k * HopCycle - 1), barFrames);
        var gaps = bars.Select(b => GapCentre(g, b)).ToList();
        Assert.NotEqual(gaps[0] < MidX(g), gaps[1] < MidX(g));
        Assert.Equal(0, Taken(g));
    }

    [Theory, MemberData(nameof(Fields))]
    public void A_hop_shows_the_hearts_outline_and_the_rings_radius_at_its_destination_for_24_frames(int cols, int rows, int gunsPerSide, bool tall, int top)
    {
        const int landing = HopCycle - 1, ahead = 25, warnLeft = 16, lateLeft = 4, leastMarks = 10;
        const int box = 14, ringInner = 11, ringOuter = 13, heartClear = 9;   // the ring of the warning, around the heart's destination
        (int Dots, int White, (double X, double Y) Dest) Look(int age, (double X, double Y)? dest)
        {
            var g = NewGame(cols, rows);
            var e = Settled(g, LayOf(g, gunsPerSide, tall, top));
            ToHeart(g, e);
            Run(g, age);
            if (dest is null)
            {
                Run(g, landing - age);
                return (0, 0, HeartAt(e));
            }
            var to = dest.Value;
            var heart = HeartAt(e);
            var f = EnemyKit.ClearPickupsAndDraw(g, new RaiderView(), new Screen(cols, rows));
            int dots = 0, white = 0;
            for (int y = (int)to.Y - box; y <= (int)to.Y + box; y++)
                for (int x = (int)to.X - box; x <= (int)to.X + box; x++)
                {
                    double fromDest = double.Hypot(x - to.X, y - to.Y);
                    if (fromDest < ringInner || fromDest > ringOuter || double.Hypot(x - heart.X, y - heart.Y) <= heartClear) continue;
                    uint px = f.Get(x, y) & EnemyKit.RgbMask;
                    if (px == Palette.Crimson) dots++;
                    if (px == Palette.White) white++;
                }
            return (dots, white, to);
        }
        var dest = Look(0, null).Dest;
        var early = Look(landing - ahead, dest);       // before the warning: nothing yet
        var warn = Look(landing - warnLeft, dest);
        var late = Look(landing - lateLeft, dest);            // the last third of the warning is white
        Assert.True(warn.Dots - early.Dots >= leastMarks, $"{warn.Dots} dots in the warning against {early.Dots} {ahead} frames ahead");
        Assert.True(late.White - early.White >= leastMarks, $"{late.White} white against {early.White}");
    }

    // ---------------------------------------------------------------- the voice

    /// <summary>Steps until the box on show has been typed out and held to its end, so the next turn is not held back by it.</summary>
    private static void ReadOut(RaiderGame g, Enemy e)
    {
        bool Read() => e.SpeechLines is not null && e.SpeechBoxAge >= EnemySpeechBox.Frames(e);
        for (int i = 0; i < ReadOutWait && !Read(); i++) { Pilot.Frame(g, shoot: false); EnemyKit.StepSweeping(g); }
        Assert.True(Read(), "the box was never read out");
    }

    /// <summary>Steps until the boss is at rest on its rows with its first words said.</summary>
    private static void SettleAndSpeak(RaiderGame g, Enemy e, Lay lay)
    {
        for (int i = 0; i < SpeakWait && (e.Text is null || e.Y < lay.CentreY - RoundingSlack); i++) { Pilot.Frame(g, shoot: false); EnemyKit.StepSweeping(g); }
        Assert.Equal(lay.CentreY, e.Y, Places);
        Assert.NotNull(e.Text);
    }

    [Fact]
    public void With_the_store_empty_it_says_its_own_line_for_each_cue_under_its_name()
    {
        var g = NewGame(EnemyKit.BigCols, EnemyKit.BigRows);
        var lay = LayOf(g, MaxSegments, true, TopRow);
        var e = g.SpawnKind(EnemyKind.Doomscroll, MidField);
        SettleAndSpeak(g, e, lay);
        Assert.Equal(ArriveLine, e.Text);          // Arrive
        Assert.Equal(BossName, e.Header);
        ReadOut(g, e);

        KillGuns(g, e);                                    // Phase: the crown comes down
        Assert.Equal(ShutLine, e.Text);
        Assert.Equal(BossName, e.Header);
        StepToMode(g, e, MawOpen);                         // a scroll bar is spat meanwhile: no post, no line, so it says nothing new
        Assert.Equal(ShutLine, e.Text);
        ReadOut(g, e);

        Assert.Equal(MawOpen, e.Mode);
        Shoot(g, e, PartOf(e, MawPart));                     // Opens: the maw gives way and the heart is bare
        EnemyKit.StepSweeping(g);
        Assert.Equal(OpenLine, e.Text);
        Assert.Equal(BossName, e.Header);
        StepToMode(g, e, Heart);
        ReadOut(g, e);
        Assert.Equal(OpenLine, e.Text);    // nothing else cuts into the heart's turn

        Shoot(g, e, PartOf(e, HeartPart));                   // Dying
        EnemyKit.StepSweeping(g);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal(DeathLine, e.Text);
        Assert.Equal(BossName, e.Header);
    }

    [Fact]
    public void Given_four_posts_that_differ_it_takes_them_oldest_first_and_each_scroll_bar_cuts_the_post_on_show()
    {
        var g = NewGame(EnemyKit.BigCols, EnemyKit.BigRows);
        var lay = LayOf(g, MaxSegments, true, TopRow);
        const int longPostWords = 40;
        const string FirstAuthor = "alice";
        var offered = new[]
        {
            new DarkPost(DarknessKind.Rude, 11, "scroll scroll scroll scroll", DarkSource.Label, FirstAuthor),                                   // short, quiet
            new DarkPost(DarknessKind.Rude, 12, "YOU CALL THAT A FEED?!! " + string.Join(' ', Enumerable.Repeat("ratio", longPostWords)), DarkSource.Hidden, null),  // long, loud
            new DarkPost(DarknessKind.Rude, 13, "meh, the same stuff in a different hat and so on and so on", DarkSource.Demo, "a demo"),     // middling
            new DarkPost(DarknessKind.Rude, 14, "no", DarkSource.Label, null),                                                              // tiny
        };
        const int apart = 7;
        foreach (var p in offered) { g.OfferDark(p); EnemyKit.StepSweeping(g, apart); }   // each one a little older than the next
        var waiting = g.Rude.Waiting.ToList();
        Assert.Equal(offered.Length, waiting.Count);
        for (int i = 1; i < waiting.Count; i++) Assert.True(waiting[i - 1].Arrived < waiting[i].Arrived);

        var e = g.SpawnKind(EnemyKind.Doomscroll, MidField);
        SettleAndSpeak(g, e, lay);
        Assert.Equal(waiting[0].Text, e.Text);              // Arrive takes the oldest
        Assert.Equal(EnemySpeechBox.Header(FirstAuthor, DarknessKind.Rude, DarkSource.Label), e.Header);
        ReadOut(g, e);

        KillGuns(g, e);                                      // Phase takes the next oldest
        var order = new List<RudePost> { waiting[0], waiting[1] };
        Assert.Equal(waiting[1].Text, e.Text);
        Assert.Equal(EnemySpeechBox.Header(null, DarknessKind.Rude, DarkSource.Hidden), e.Header);

        const int frames = 700, nextBarWait = 400;
        var seen = new HashSet<Bullet>(g.Bullets);
        for (int i = 0; i < frames && order.Count < waiting.Count; i++)
        {
            Pilot.Frame(g, shoot: false);
            string? before = e.Text;
            bool unread = e.SpeechLines is not null && e.SpeechBoxAge + 1 < EnemySpeechBox.Frames(e);
            EnemyKit.StepSweeping(g);
            bool bar = RecordNewBoltsAndSpotBar(g, seen);
            if (e.Text == before) { Assert.False(bar && waiting.Count > order.Count, "a scroll bar passed without taking a post"); continue; }
            Assert.True(bar, "its words changed on a frame without a scroll bar");
            Assert.True(unread, "the post on show was cut before it was read");
            order.Add(waiting[order.Count]);
            Assert.Equal(waiting[order.Count - 1].Text, e.Text);
        }
        Assert.Equal(waiting.Count, order.Count);
        Assert.Equal(EnemySpeechBox.Header(null, DarknessKind.Rude, DarkSource.Label), e.Header);
        Assert.Empty(g.Rude.Waiting);

        // nothing waits: the next bar says one of the four heard posts again, never the one already on show
        string last = e.Text!;
        for (int i = 0; i < nextBarWait && e.Text == last; i++) { Pilot.Frame(g, shoot: false); EnemyKit.StepSweeping(g); }
        Assert.NotEqual(last, e.Text);
        Assert.Contains(e.Text, waiting.Select(p => p.Text));
        Assert.Equal(0, Taken(g));
    }

    [Fact]
    public void The_heart_reads_a_post_whole_with_a_scroll_bar_going_by_and_dies_with_its_own_word()
    {
        var g = NewGame(EnemyKit.BigCols, EnemyKit.BigRows);
        var e = Settled(g, LayOf(g, MaxSegments, true, TopRow));
        ToOpenMaw(g, e);
        const int stillReadingWords = 90;   // long enough that the heart is still reading it when a scroll bar goes by
        string first = "first " + string.Join(' ', Enumerable.Repeat("stop", stillReadingWords));
        g.OfferDark(new DarkPost(DarknessKind.Rude, 21, first, DarkSource.Label, "bob"));
        g.OfferDark(new DarkPost(DarknessKind.Rude, 22, "second", DarkSource.Label, "carol"));
        string wanted = g.Rude.Waiting[0].Text, second = g.Rude.Waiting[1].Text;

        Shoot(g, e, PartOf(e, MawPart));                       // Opens: a post is taken on this step or as soon as the line on show is read
        EnemyKit.StepSweeping(g);
        var texts = new List<string?> { e.Text };
        var seen = new HashSet<Bullet>(g.Bullets);
        const int frames = 320;
        bool barWhileReading = false;
        for (int i = 0; i < frames; i++)
        {
            Pilot.Frame(g, shoot: false);
            EnemyKit.StepSweeping(g);
            if (texts[^1] != e.Text) texts.Add(e.Text);
            bool bar = RecordNewBoltsAndSpotBar(g, seen);
            if (bar && e.Text == wanted && e.Mode == Heart) barWhileReading = true;
        }
        Assert.Equal(Heart, e.Mode);
        Assert.Equal(wanted, texts[^1]);
        Assert.All(texts.SkipLast(1), t => Assert.Contains(t, new[] { ArriveLine, ShutLine, OpenLine }));   // only the kind's own lines before it
        Assert.True(barWhileReading, "a scroll bar went by while the heart was reading, and did not cut it");
        Assert.True(e.SpeechBoxAge < EnemySpeechBox.Frames(e), "the post is still being read");
        Assert.Equal(second, Assert.Single(g.Rude.Waiting).Text);

        Shoot(g, e, PartOf(e, HeartPart));                     // its death cuts in with its sign-off, and the next post is left for another speaker
        EnemyKit.StepSweeping(g);
        Assert.Equal(DeathLine, e.Text);
        Assert.Equal(second, Assert.Single(g.Rude.Waiting).Text);
        Assert.Equal(0, Taken(g));
    }

    // ---------------------------------------------------------------- sounds

    [Fact]
    public void It_plays_the_sounds_and_none_of_the_lob_the_hatch_or_the_tick()
    {
        const int volley = 120;
        var g = NewGame(EnemyKit.BigCols, EnemyKit.BigRows);
        var played = new HashSet<string>();
        g.Sfx = new SfxBus((name, _, _, _) => played.Add(name));
        var e = Settled(g, LayOf(g, MaxSegments, true, TopRow));
        Shoot(g, e, PartOf(e, CrownPart));                     // armour: a ping
        Run(g, volley);                                      // the muzzles blink (charge) and fire (beam)
        ToOpenMaw(g, e);                                     // the crown falls (part), a bar is spat (blast), the maw shuts and opens
        Shoot(g, e, PartOf(e, MawPart));
        EnemyKit.StepSweeping(g);
        StepToMode(g, e, Heart);
        Run(g, HopCycle);                                    // a hop is announced
        Shoot(g, e, PartOf(e, HeartPart));
        EnemyKit.StepSweeping(g);
        foreach (var name in new[] { SfxNames.Warn, SfxNames.Ping, SfxNames.Open, SfxNames.Shut, SfxNames.Charge, SfxNames.Beam, SfxNames.Blast, SfxNames.Part })   // boss_down is the gate's
            Assert.Contains(name, played);
        Assert.DoesNotContain(SfxNames.Lob, played);
        Assert.DoesNotContain(SfxNames.Hatch, played);
        Assert.DoesNotContain(SfxNames.Tick, played);
    }

    // ---------------------------------------------------------------- the gate, and the same seed

    [Fact]
    public void The_gate_at_stage_ten_warns_for_its_frames_and_then_brings_doomscroll()
    {
        const int gateWait = 200;
        Assert.Equal(EnemyKind.Doomscroll, Ramp.Boss(BossStage));
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(BossStage);
        Assert.Equal(GateState.Warning, g.Gate);
        Assert.Null(g.Boss);
        int warned = 0;
        for (; warned < gateWait && g.Gate == GateState.Warning; warned++) { g.Step(); g.Bullets.RemoveAll(b => !b.Friendly); }
        Assert.Equal(Tuning.BossWarnFrames, warned);
        Assert.Equal(GateState.Fighting, g.Gate);
        Assert.Equal(EnemyKind.Doomscroll, g.Boss!.Kind);
        Assert.True(g.Boss.Parts!.Count > 0);
    }

    [Fact]
    public void The_same_seed_gives_the_same_run_with_doomscroll_in_it()
    {
        const ulong seed = 11;
        (string, bool) Go()
        {
            var g = new RaiderGame(seed) { SignedIn = true };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtBoss(BossStage);
            bool seen = false;
            for (int f = 0; f < HalfMinute; f++)
            {
                EnemyKit.BotFrame(g);
                g.Step();
                seen |= g.Enemies.Any(e => e.Kind == EnemyKind.Doomscroll);
            }
            var state = string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}:{e.Mode}:{e.ModeAge}:{string.Join(',', (e.Parts ?? []).Select(p => $"{p.Name}{p.State}{p.Hp:0.0}"))}"))
                + $"#{g.Bullets.Count}:{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}";
            return (state, seen);
        }
        var (first, brought) = Go();
        var (second, _) = Go();
        Assert.Equal(first, second);
        Assert.True(brought, $"the gate brings Doomscroll inside {HalfMinute} frames");
    }

    // ---------------------------------------------------------------- the fight

    /// <summary>The boss's stage with no gate, the ship at the fight's weapon level and the field clear.</summary>
    private static RaiderGame FightGame(int cols, int rows)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtStage(BossStage);
        EnemyKit.Clear(g);
        while (g.WeaponLevel < FightLevel) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); EnemyKit.Clear(g); }
        return g;
    }

    /// <summary>The boss as the gate brings it (its hit points are the boss's own, never scaled by the stage), with the ship at the fight's weapon level and the field clear.</summary>
    private static (RaiderGame G, Enemy Boss) GateFight(ulong seed, int cols, int rows)
    {
        var g = new RaiderGame(seed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtBoss(BossStage);
        while (g.WeaponLevel < FightLevel) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); }
        while (g.Gate == GateState.Warning) g.Step();
        return (g, g.Boss!);
    }

    /// <summary>Frames from the boss's first appearance to its death for a pilot that fires always, keeps under the lowest open part and dodges, or -1.</summary>
    private static int Fight(ulong seed, int cols, int rows)
    {
        var (g, e) = GateFight(seed, cols, rows);
        const int fiveMinutes = 5 * 60 * Tuning.Fps;   // a fight has no clock short of five minutes
        for (int f = 0; f < fiveMinutes; f++)
        {
            Pilot.Frame(g, shoot: true);
            g.Step();
            if (!g.Enemies.Contains(e)) return g.Kills > 0 ? f + 1 : -1;
        }
        return -1;
    }

    [Theory]
    [InlineData(EnemyKit.BigCols, EnemyKit.BigRows)]
    [InlineData(TerminalCols, TerminalRows)]
    public void A_pilot_at_weapon_level_four_beats_it_in_thirty_to_forty_eight_seconds_over_six_seeds(int cols, int rows)
    {
        const int seeds = 6, fastest = 30, slowest = 48, walkover = 20, grind = 90;   // seconds
        var times = new List<int>();
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            int took = Fight(seed, cols, rows);
            Assert.True(took > 0, $"seed {seed}: not dead inside five minutes");
            times.Add(took);
        }
        times.Sort();
        int median = (times[seeds / 2 - 1] + times[seeds / 2]) / 2;
        output.WriteLine($"doomscroll fight on {cols}x{rows} at level {FightLevel}, frames to kill: {string.Join(" ", times)}; median {median} ({median / (double)Tuning.Fps:0.0} s)");
        Assert.InRange(median, fastest * Tuning.Fps, slowest * Tuning.Fps);
        Assert.All(times, t => Assert.InRange(t, walkover * Tuning.Fps, grind * Tuning.Fps));   // no seed is a walkover or a grind
    }

    [Theory]
    [InlineData(EnemyKit.BigCols, EnemyKit.BigRows)]
    [InlineData(PhoneCols, PhoneRows)]
    public void A_ship_that_never_moves_dies(int cols, int rows)
    {
        var g = FightGame(cols, rows);
        g.SpawnKind(EnemyKind.Doomscroll, MidField);
        for (int f = 0; f < HalfMinute && g.Deaths == 0; f++) { g.Input.Key(GameKey.Fire, true); EnemyKit.StepSweeping(g); }
        Assert.True(g.Deaths > 0, "a ship that stands still lives through half a minute of the boss");
    }

    [Fact]
    public void A_boss_told_to_leave_rises_eight_pixels_in_ten_frames()
    {
        const int frames = 10;
        const double rise = 8;
        var g = NewGame(TerminalCols, TerminalRows);
        var e = Settled(g, LayOf(g, MaxSegments, false, TopRow));
        double start = e.Y;
        e.Leaving = true;
        Run(g, frames, pilot: false);
        Assert.Equal(start - rise, e.Y, Places);
    }

    private static RaiderGame.PixelDust Dust(int left, int top, int width, int frames)
    {
        const uint plateColour = 0xFF336699u;
        var plate = new Sprite(width, 1);
        Array.Fill(plate.Px, plateColour);
        return new RaiderGame.PixelDust(left, top, width, 1, plate, (_, _) => true, frames);
    }

    [Fact]
    public void Debris_is_shed_on_the_last_frame_of_the_fall_and_not_on_the_one_after_or_before_the_first()
    {
        const int frames = 3, left = 10, top = 4, width = 40;
        var g = NewGame(TerminalCols, TerminalRows);
        var dust = Dust(left, top, width, frames);
        dust.Shed(g, frames);
        dust.Shed(g, -1);
        Assert.Empty(g.Particles);
        dust.Shed(g, frames - 1);
        Assert.NotEmpty(g.Particles);
    }

    [Fact]
    public void Debris_starts_in_the_middle_of_each_pixel_it_comes_from()
    {
        const int left = 100, top = 50, width = 4;
        const double PixelMiddle = 0.5;
        var g = NewGame(TerminalCols, TerminalRows);
        Dust(left, top, width, 1).Shed(g, 0);
        Assert.Equal(Enumerable.Range(left, width).Select(x => x + PixelMiddle), g.Particles.Select(p => p.X).Order());
        Assert.All(g.Particles, p => Assert.Equal(top + PixelMiddle, p.Y));
    }
}
