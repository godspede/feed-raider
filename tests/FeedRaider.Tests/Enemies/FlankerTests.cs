using System.Runtime.CompilerServices;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Flanker (stage 4): it locks the ship's row, marks it with a dotted lane and a chevron at the edge it will come in from, and
/// then flies the row once, fast, from one side. A second one follows from the other side, never within 10 pixels of the first's row.
/// These tests give its numbers, the lock (the row does not follow the ship, and a ship 8 pixels off it lives), the pair, the speeds
/// (the same at every stage), its warning, its pixels, the live-post rule and its fit on the four fields.
/// </summary>
public sealed class FlankerTests(ITestOutputHelper output)
{
    private const uint Coral = 0xff7043;

    /// <summary>Its score, hit points and drawing size in the enemy table.</summary>
    private const int TableScore = 350, TableHp = 3, BodyW = 9, BodyH = 5;

    /// <summary>The stage the flanker first comes in on.</summary>
    private const int FlankerStage = 4;

    /// <summary>How far a frame it comes down while it marks its row, and how far a frame it flies the row.</summary>
    private const double MarkSpeed = 1.2, CrossSpeed = 2.4;

    /// <summary>The fewest pixels between the rows of a pair, and the frames between the first's arrival and the second's.</summary>
    private const double MinRowGap = 10;
    private const int CallAge = 20;

    /// <summary>The fewest frames it marks its row for, and the last frames before it crosses, in which the chevron is white.</summary>
    private const int MinMarkFrames = 40, WarnFrames = 13;

    /// <summary>Frames a test watches it mark its row for, inside the fewest frames it marks for.</summary>
    private const int MarkingFrames = 35;

    /// <summary>How far off the locked row a ship may move after the lock and live.</summary>
    private const int ClearOfTheLock = 8;

    /// <summary>The ship's column and row in most tests on the largest field, and so the row the flanker locks; and the row the tests
    /// that move the ship off its row start it on.</summary>
    private const double ShipX = 60, LaneRow = 70, MiddleRow = 60;

    /// <summary>Frames a flight may take before the test gives up on it: on the largest field, and on any field.</summary>
    private const int MaxFlyFrames = 400, MaxFlyFramesAnyField = 600;

    /// <summary>Frames <see cref="Fly"/> runs past before a field with no flanker left on it ends the flight.</summary>
    private const int MinFlyFrames = 5;

    /// <summary>A shot far heavier than a flanker's hit points.</summary>
    private const double KillingShot = 100;

    /// <summary>Decimal places two positions must agree to, and the rounding a sum of positions may carry.</summary>
    private const int Places = 6;
    private const double Rounding = 1e-9;

    /// <summary>The flankers one call makes: the first and the second it calls.</summary>
    private const int PairSize = 2;

    /// <summary>The share of the field's width an enemy arrives at: its middle.</summary>
    private const double MidField = 0.5;

    /// <summary>The middle column of the field, in pixels.</summary>
    private static double MidX(RaiderGame g) => g.PW / 2.0;

    /// <summary>Every field size the tests try, as theory rows.</summary>
    public static TheoryData<int, int> AllFieldSizes()
    {
        var data = new TheoryData<int, int>();
        foreach (var (cols, rows) in EnemyKit.FieldSizes) data.Add(cols, rows);
        return data;
    }

    private static EnemyBehaviour Flanker => RaiderGame.TableBehaviour(EnemyKind.Flanker)!;

    private static RaiderGame FieldGame(int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows, ulong seed = EnemyKit.PlainSeed, int stage = FlankerStage)
    {
        var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtStage(stage);
        EnemyKit.Clear(g);
        return g;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PX")]
    private static extern void SetPX(RaiderGame g, double value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PY")]
    private static extern void SetPY(RaiderGame g, double value);

    private static void PlaceShip(RaiderGame g, double x, double y)
    {
        SetPX(g, x);
        SetPY(g, y);
    }

    /// <summary>The enemies a test put on each game, and the flankers they called.</summary>
    private static readonly ConditionalWeakTable<RaiderGame, HashSet<Enemy>> OwnEnemies = new();

    /// <summary>One frame, then every enemy a test did not make (a stage's own waves, with their own flankers) is swept away.</summary>
    private static void StepOwnOnly(RaiderGame g)
    {
        g.Step();
        var mine = OwnEnemies.GetOrCreateValue(g);
        foreach (var e in g.Enemies.Where(e => e.Kind == EnemyKind.Flanker && e.Parent is not null && mine.Contains(e.Parent))) mine.Add(e);
        g.Enemies.RemoveAll(e => !mine.Contains(e));
    }

    private static List<Enemy> Flankers(RaiderGame g) => g.Enemies.Where(e => e.Kind == EnemyKind.Flanker).ToList();

    /// <summary>One flanker's path: where it was on each frame, and the first frame it moved sideways, which ends the marking.</summary>
    private sealed class Track
    {
        public Enemy Enemy = null!;
        public readonly List<(int Frame, double X, double Y)> Pos = [];
        public int CrossFrame = -1;
        public double Row => Pos.Max(p => p.Y);
        public int Dir => Math.Sign(Pos[^1].X - Pos[0].X);
    }

    /// <summary>Runs frames with the ship held where <paramref name="ship"/> says, recording every flanker's path, until none is left (or the cap).</summary>
    private static List<Track> Fly(RaiderGame g, Func<int, (double X, double Y)> ship, int frames, Action<int>? each = null)
    {
        var tracks = new Dictionary<Enemy, Track>();
        for (int f = 0; f < frames; f++)
        {
            var (x, y) = ship(f);
            PlaceShip(g, x, y);
            StepOwnOnly(g);
            each?.Invoke(f);
            foreach (var e in Flankers(g))
            {
                if (!tracks.TryGetValue(e, out var track)) tracks[e] = track = new Track { Enemy = e };
                if (track.CrossFrame < 0 && track.Pos.Count > 0 && Math.Abs(e.X - track.Pos[^1].X) > Rounding) track.CrossFrame = f;
                track.Pos.Add((f, e.X, e.Y));
            }
            if (f > MinFlyFrames && tracks.Count > 0 && Flankers(g).Count == 0) break;
        }
        return [.. tracks.Values];
    }

    /// <summary>A ship locked on <see cref="LaneRow"/> that then keeps clear of both rows (a ship rammed into a flanker kills it): on one
    /// row for the second's lock, then on another.</summary>
    private static (double X, double Y) Dodge(int f)
    {
        const int FirstLockFrames = 3, SecondLockFrames = 25;
        const double SecondLockRow = 40, ClearRow = 55;
        return (ShipX, f < FirstLockFrames ? LaneRow : f < SecondLockFrames ? SecondLockRow : ClearRow);
    }

    private static Enemy SpawnOwnFlanker(RaiderGame g)
    {
        var e = g.SpawnKind(EnemyKind.Flanker, MidField);
        OwnEnemies.GetOrCreateValue(g).Add(e);
        return e;
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Flanker, Flanker.Kind);
        Assert.Equal("FLANKER", Flanker.Name);
        Assert.False(Flanker.Boss);
        Assert.False(Flanker.Speaks);
        Assert.Equal(TableHp, Flanker.Hp);
        Assert.Equal(TableScore, Flanker.Score);
        Assert.Equal(Coral, Flanker.Colour);
        Assert.Equal(BodyW, Flanker.Body.W);
        Assert.Equal(BodyH, Flanker.Body.H);
    }

    [Fact]
    public void It_has_three_hit_points_loses_one_to_a_hit_of_one_and_scores_three_hundred_and_fifty()
    {
        const int UnrampedStage = 1;   // the stage whose hit points are the table's own
        const double ShipRow = 80, FlankerX = 100, FlankerRow = 40, OneHit = 1, FinishingShot = 5;
        var g = FieldGame(stage: UnrampedStage);
        PlaceShip(g, ShipX, ShipRow);
        var e = SpawnOwnFlanker(g);
        e.X = FlankerX; e.BaseX = FlankerX; e.Y = FlankerRow;
        Assert.Equal(TableHp, e.Hp, Places);
        int before = g.Score;
        EnemyKit.Shot(g, e.X, e.Y, OneHit);
        StepOwnOnly(g);
        Assert.Equal(TableHp - OneHit, e.Hp, Places);
        EnemyKit.Shot(g, e.X, e.Y, FinishingShot);
        StepOwnOnly(g);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal(before + TableScore, g.Score);
    }

    [Fact]
    public void It_says_nothing_when_it_arrives()
    {
        const int ArrivalFrames = 10;
        var g = FieldGame();
        var e = SpawnOwnFlanker(g);
        Assert.Null(e.Text);
        for (int i = 0; i < ArrivalFrames; i++) StepOwnOnly(g);
        Assert.Null(e.Text);
        Assert.Null(e.SpeechLines);
    }

    // ---------------------------------------------------------------- the lock

    [Fact]
    public void The_locked_row_is_the_ships_row_at_the_lock_and_does_not_follow_the_ship()
    {
        const int WanderWidth = 40, FramesOnEachRow = 20, DownOnItsRowBy = 70;
        double[] wanderRows = [LaneRow, 50, 30, 85];   // the ship wanders all over the field
        var g = FieldGame();
        PlaceShip(g, ShipX, LaneRow);
        var e = SpawnOwnFlanker(g);
        var rows = new List<double>();
        for (int f = 0; f < MaxFlyFrames && Flankers(g).Contains(e); f++)
        {
            PlaceShip(g, ShipX + (f % WanderWidth), wanderRows[Math.Min(f / FramesOnEachRow, wanderRows.Length - 1)]);
            StepOwnOnly(g);
            if (f > DownOnItsRowBy) rows.Add(e.Y);
        }
        Assert.NotEmpty(rows);
        Assert.All(rows, y => Assert.Equal(LaneRow, y, Places));
    }

    [Theory]
    [InlineData(-ClearOfTheLock)]
    [InlineData(ClearOfTheLock)]
    public void A_ship_that_moves_eight_pixels_up_or_down_after_the_lock_lives(int dy)
    {
        var g = FieldGame();
        PlaceShip(g, MidX(g), MiddleRow);
        var e = SpawnOwnFlanker(g);
        StepOwnOnly(g);                                   // the lock is made the frame it arrives
        for (int f = 0; f < MaxFlyFrames && Flankers(g).Contains(e); f++)
        {
            PlaceShip(g, MidX(g), MiddleRow + dy);
            StepOwnOnly(g);
            g.Enemies.RemoveAll(x => x != e);      // the second takes its own lock of the moved ship; this is about the first
            Assert.Equal(0, g.Invuln);
            Assert.True(g.Alive);
        }
        Assert.DoesNotContain(e, g.Enemies);      // it flew the whole row
        Assert.Equal(0, g.Invuln);
    }

    [Fact]
    public void A_ship_left_on_the_row_is_hit()
    {
        var g = FieldGame();
        PlaceShip(g, MidX(g), MiddleRow);
        SpawnOwnFlanker(g);
        bool hit = false;
        for (int f = 0; f < MaxFlyFrames && Flankers(g).Count > 0 && !hit; f++)
        {
            PlaceShip(g, MidX(g), MiddleRow);
            StepOwnOnly(g);
            hit = g.Invuln > 0 || !g.Alive;
        }
        Assert.True(hit);
    }

    [Fact]
    public void It_cannot_touch_the_ship_while_it_marks_even_in_the_column_it_comes_down()
    {
        const double EntryColumn = BodyW / 2.0;
        var g = FieldGame();
        PlaceShip(g, EntryColumn, MiddleRow);
        var e = SpawnOwnFlanker(g);
        for (int f = 0; f < MarkingFrames; f++)
        {
            PlaceShip(g, EntryColumn, MiddleRow);
            StepOwnOnly(g);
            if (!Flankers(g).Contains(e)) break;
            Assert.True(g.Alive);
        }
    }

    // ---------------------------------------------------------------- the pair

    [Theory]
    [InlineData(LaneRow)]
    [InlineData(88)]
    [InlineData(12)]
    [InlineData(40)]
    public void A_pair_flies_rows_at_least_ten_pixels_apart_from_opposite_sides_twenty_frames_apart(double shipY)
    {
        var g = FieldGame();
        PlaceShip(g, ShipX, shipY);
        SpawnOwnFlanker(g);
        var tracks = Fly(g, _ => (ShipX, shipY), MaxFlyFrames);
        Assert.Equal(PairSize, tracks.Count);
        var (first, second) = (tracks[0], tracks[1]);
        Assert.True(Math.Abs(first.Row - second.Row) >= MinRowGap - Rounding, $"rows {first.Row:0.0} and {second.Row:0.0}");
        Assert.NotEqual(first.Dir, second.Dir);
        Assert.Equal(CallAge, second.Pos[0].Frame - first.Pos[0].Frame);
        Assert.All(tracks, t => Assert.InRange(t.Row, 0, g.PH - 1));
    }

    [Fact]
    public void The_second_takes_its_own_lock_when_the_ship_has_moved_on()
    {
        const int MovesAfter = 10;
        const double NewRow = 40;
        var g = FieldGame();
        PlaceShip(g, ShipX, LaneRow);
        SpawnOwnFlanker(g);
        var tracks = Fly(g, f => (ShipX, f < MovesAfter ? LaneRow : NewRow), MaxFlyFrames);
        Assert.Equal(PairSize, tracks.Count);
        Assert.Equal(LaneRow, tracks[0].Row, Places);
        Assert.Equal(NewRow, tracks[1].Row, Places);
    }

    [Fact]
    public void If_the_first_is_killed_before_it_has_called_the_second_there_is_no_second()
    {
        const int ShotAfter = 4, WatchedFrames = 80;
        var g = FieldGame();
        PlaceShip(g, ShipX, LaneRow);
        var e = SpawnOwnFlanker(g);
        for (int f = 0; f < ShotAfter; f++) StepOwnOnly(g);
        EnemyKit.Shot(g, e.X, e.Y, KillingShot);
        for (int f = 0; f < WatchedFrames; f++) StepOwnOnly(g);
        Assert.Empty(Flankers(g));
    }

    // ---------------------------------------------------------------- speeds

    [Theory]
    [InlineData(FlankerStage)]
    [InlineData(Tuning.StageCap)]
    public void Its_speeds_are_the_same_at_every_stage(int stage)
    {
        var g = FieldGame(stage: stage);
        PlaceShip(g, ShipX, LaneRow);
        SpawnOwnFlanker(g);
        var track = Fly(g, Dodge, MaxFlyFrames)[0];
        double mark = track.Pos.Zip(track.Pos.Skip(1), (p, q) => (q.Y - p.Y)).Where(d => d > 0).Max();
        Assert.Equal(MarkSpeed, mark, Rounding);
        double cross = track.Pos.Where(p => p.Frame > track.CrossFrame).Zip(track.Pos.Where(p => p.Frame >= track.CrossFrame), (q, p) => Math.Abs(q.X - p.X)).Max();
        Assert.Equal(CrossSpeed, cross, Rounding);
        Assert.Equal(CrossSpeed, Math.Abs(track.Pos[^1].X - track.Pos[^2].X), Rounding);
    }

    [Fact]
    public void It_marks_the_row_for_at_least_forty_frames_before_it_crosses()
    {
        foreach (var (cols, rows) in EnemyKit.FieldSizes)
            foreach (double frac in new[] { 0.1, 0.5, 0.95 })
            {
                var g = FieldGame(cols, rows);
                PlaceShip(g, MidX(g), g.PH * frac);
                SpawnOwnFlanker(g);
                var track = Fly(g, _ => (MidX(g), g.PH * frac), MaxFlyFramesAnyField)[0];
                Assert.True(track.CrossFrame >= MinMarkFrames, $"{cols}x{rows} row {g.PH * frac}: crossed at frame {track.CrossFrame}");
            }
    }

    [Fact]
    public void It_flies_the_whole_width_in_a_straight_line()
    {
        const double MostOfTheWidth = 0.9;
        var g = FieldGame();
        PlaceShip(g, ShipX, LaneRow);
        SpawnOwnFlanker(g);
        var track = Fly(g, Dodge, MaxFlyFrames)[0];
        var crossing = track.Pos.Where(p => p.Frame >= track.CrossFrame).ToList();
        Assert.All(crossing, p => Assert.Equal(LaneRow, p.Y, Places));
        Assert.True(Math.Abs(crossing[^1].X - crossing[0].X) > g.PW * MostOfTheWidth);
        Assert.Equal(Math.Sign(crossing[^1].X - crossing[0].X), track.Dir);
    }

    // ---------------------------------------------------------------- warning and pixels

    private static int CountPixels(Surface surface, int x0, int x1, int y0, int y1, uint colour)
    {
        int count = 0;
        for (int x = Math.Max(0, x0); x <= Math.Min(surface.W - 1, x1); x++)
            for (int y = Math.Max(0, y0); y <= Math.Min(surface.H - 1, y1); y++)
                if ((surface.Get(x, y) & EnemyKit.RgbMask) == colour) count++;
        return count;
    }

    [Fact]
    public void The_dotted_lane_is_drawn_along_the_locked_row_from_the_lock_and_goes_when_it_has_flown()
    {
        const int LaneMargin = 20;              // kept clear of the flanker and the chevron at either edge
        const int DotsAtLeast = 20, DotsAtMost = 40;   // a pixel every three, in a row across the field
        const double OffTheRow = 10;
        var g = FieldGame();
        PlaceShip(g, ShipX, LaneRow);
        SpawnOwnFlanker(g);
        StepOwnOnly(g);
        var seen = new List<int>();
        int row = (int)LaneRow;
        for (int f = 0; f < MarkingFrames; f++)
        {
            PlaceShip(g, ShipX, LaneRow);
            StepOwnOnly(g);
            seen.Add(CountPixels(EnemyKit.ClearPickupsAndDraw(g), LaneMargin, g.PW - 1 - LaneMargin, row, row, Coral));
        }
        Assert.All(seen.Skip(1), n => Assert.InRange(n, DotsAtLeast, DotsAtMost));
        for (int f = 0; f < MaxFlyFrames && Flankers(g).Count > 0; f++) { PlaceShip(g, ShipX, LaneRow + OffTheRow); StepOwnOnly(g); }
        Assert.Empty(Flankers(g));
        Assert.Equal(0, CountPixels(EnemyKit.ClearPickupsAndDraw(g), 0, g.PW - 1, row, row, Coral));
    }

    [Fact]
    public void The_chevron_at_the_entry_edge_is_white_for_the_last_thirteen_frames_and_not_before()
    {
        // the box the chevron is counted in: this many columns in from the edge, and this many rows either side of the lane
        const int ChevronBoxW = 13, ChevronBoxHalfH = 8;
        var g = FieldGame();
        PlaceShip(g, ShipX, LaneRow);
        var lead = SpawnOwnFlanker(g);
        var whites = new List<(int Frame, int N)>();
        int cross = -1;
        double startX = lead.X;
        int top = (int)LaneRow - ChevronBoxHalfH, bottom = (int)LaneRow + ChevronBoxHalfH;
        for (int f = 0; f < MaxFlyFrames && cross < 0; f++)
        {
            PlaceShip(g, ShipX, LaneRow);
            StepOwnOnly(g);
            if (Math.Abs(lead.X - startX) > Rounding) { cross = f; break; }
            bool left = lead.X < MidX(g);
            var screen = EnemyKit.ClearPickupsAndDraw(g);
            whites.Add((f, left
                ? CountPixels(screen, 0, ChevronBoxW - 1, top, bottom, Palette.White)
                : CountPixels(screen, g.PW - ChevronBoxW, g.PW - 1, top, bottom, Palette.White)));
        }
        Assert.True(cross >= MinMarkFrames);
        Assert.All(whites.Where(w => w.Frame < cross - WarnFrames), w => Assert.Equal(0, w.N));
        Assert.All(whites.Where(w => w.Frame > cross - WarnFrames), w => Assert.True(w.N > 0, $"no white at frame {w.Frame} of {cross}"));
    }

    [Fact]
    public void One_flies_right_facing_right_and_the_other_left_facing_left_as_a_mirror()
    {
        // the gold nose leads: it sits on the middle row, one cell in from the side it flies toward
        const int NoseRow = BodyH / 2, NoseInset = 1;
        var g = FieldGame();
        SpawnOwnFlanker(g);
        var tracks = Fly(g, _ => (ShipX, LaneRow), MaxFlyFrames);
        var right = tracks.Single(t => t.Dir > 0).Enemy.Sprite;
        var left = tracks.Single(t => t.Dir < 0).Enemy.Sprite;
        Assert.Equal(BodyW, right.W);
        Assert.Equal(BodyH, right.H);
        Assert.Equal(BodyW, left.W);
        for (int y = 0; y < BodyH; y++)
            for (int x = 0; x < BodyW; x++)
                Assert.Equal(right.Px[y * BodyW + x], left.Px[y * BodyW + BodyW - 1 - x]);
        Assert.NotEqual(right.Px, left.Px);
        Assert.NotEqual(0u, right.Px[NoseRow * BodyW + BodyW - 1 - NoseInset]);
        Assert.NotEqual(0u, left.Px[NoseRow * BodyW + NoseInset]);
    }

    // ---------------------------------------------------------------- live posts

    /// <summary>Where the tests put down a flanker a grunt brings.</summary>
    private const double AddX = 40, AddY = 10;

    [Fact]
    public void A_flanker_a_live_wave_would_carry_is_a_grunt_with_a_grunts_hit_points()
    {
        var g = FieldGame();
        var parent = g.SpawnKind(EnemyKind.Grunt, MidField);
        parent.Live = true;
        var e = g.SpawnAdd(EnemyKind.Flanker, AddX, AddY, parent);
        Assert.Equal(EnemyKind.Grunt, e.Kind);
        Assert.Same(Art.Grunt, e.Sprite);
        Assert.Equal(RaiderGame.TableBehaviour(EnemyKind.Grunt).Hp * Ramp.HpMul(g.Stage), e.Hp, Places);
        Assert.True(e.Live);
    }

    [Fact]
    public void A_flanker_with_no_live_post_stays_a_flanker()
    {
        var g = FieldGame();
        var parent = g.SpawnKind(EnemyKind.Grunt, MidField);
        var e = g.SpawnAdd(EnemyKind.Flanker, AddX, AddY, parent);
        Assert.Equal(EnemyKind.Flanker, e.Kind);
    }

    // ---------------------------------------------------------------- the fields and the machine

    [Theory]
    [MemberData(nameof(AllFieldSizes))]
    public void It_comes_in_whole_and_flies_a_row_on_the_field_on_every_field(int cols, int rows)
    {
        const double ShipTopRow = 10, ShipBottomGap = 6;
        const int HighestRow = 2, ComingInFrames = 30;
        var g = FieldGame(cols, rows);
        foreach (double frac in new[] { 0.0, 0.5, 1.0 })
        {
            EnemyKit.Clear(g);
            double shipY = Math.Clamp(g.PH * frac, ShipTopRow, g.PH - ShipBottomGap);
            SpawnOwnFlanker(g);
            var tracks = Fly(g, _ => (MidX(g), shipY), MaxFlyFramesAnyField, _ =>
            {
                foreach (var e in Flankers(g).Where(e => e.Y >= 0 && e.Age < ComingInFrames))
                    Assert.True(EnemyKit.InsideWidth(g, e), $"{cols}x{rows}: x {e.X}");
            });
            Assert.Equal(PairSize, tracks.Count);
            Assert.All(tracks, t => Assert.InRange(t.Row, HighestRow, g.PH - 1));
            Assert.All(tracks, t => Assert.True(t.CrossFrame > 0));
        }
    }

    [Fact]
    public void It_never_sits_on_the_field_after_it_has_flown_and_leaves_no_bolt()
    {
        var g = FieldGame();
        SpawnOwnFlanker(g);
        Fly(g, _ => (ShipX, LaneRow), MaxFlyFramesAnyField);
        Assert.Empty(Flankers(g));
        Assert.DoesNotContain(g.Bullets, b => !b.Friendly);
    }

    [Fact]
    public void The_same_seed_gives_the_same_pair()
    {
        const ulong RunSeed = 3;
        // the ship wanders over a box with its top left corner here and this size
        const int WanderLeft = 40, WanderTop = 30, WanderWidth = 50, WanderHeight = 40;
        string Go()
        {
            var g = FieldGame(seed: RunSeed);
            SpawnOwnFlanker(g);
            var tracks = Fly(g, f => (WanderLeft + f % WanderWidth, WanderTop + f % WanderHeight), MaxFlyFrames);
            return string.Join("|", tracks.Select(t => $"{t.Dir}:{t.Row:0.000}:{t.CrossFrame}:{t.Pos[0].X:0.0}"));
        }
        Assert.Equal(Go(), Go());
        output.WriteLine(Go());
    }

    [Fact]
    public void The_side_it_comes_from_varies_with_the_seed()
    {
        const ulong SeedsTried = 12;
        const int BothSides = 2;
        var dirs = new HashSet<int>();
        for (ulong seed = 1; seed <= SeedsTried; seed++)
        {
            var g = FieldGame(seed: seed);
            SpawnOwnFlanker(g);
            dirs.Add(Fly(g, _ => (ShipX, LaneRow), MaxFlyFrames)[0].Dir);
        }
        Assert.Equal(BothSides, dirs.Count);
    }
}
