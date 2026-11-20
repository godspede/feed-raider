using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Pile-On (stage 8's boss) and the Brick it throws. The wall of bricks never moves; the instigator on top paces in whole cells
/// and fires only down a column with every brick gone. These tests give the layout table on the four fields, what a shot meets, the
/// cost of a column, the core's volley, the throws, the regrowth, the voice and the fight (a bot at stage 8's weapon level 3).
/// </summary>
public sealed class PileOnTests(ITestOutputHelper output)
{
    private const int MaxHostileBolts = 48;
    private const int PileOnStage = 8;

    /// <summary>Decimal places two positions or hit points must agree to.</summary>
    private const int Places = 6;

    /// <summary>A brick's sprite and the cell it sits in, its hit points and score, and the hit points every column holds however deep.</summary>
    private const int BrickW = 7, BrickH = 5, CellW = 8, BrickHp = 2, BrickScore = 20, ColumnHp = 14;
    private const double BrickHalfW = BrickW / 2.0;

    /// <summary>The wall's top row: a row higher on a field shorter than <see cref="CompactFieldH"/>.</summary>
    private const int CompactFieldH = 48, TopRowCompact = 5, TopRow = 6;

    /// <summary>The brick rows of a three-deep wall, top to bottom.</summary>
    private const int TopDepth = 0, MiddleDepth = 1, LowestDepth = 2;

    /// <summary>Pixels the core paces and the wall enters by in one step.</summary>
    private const int CoreStep = 2, EntryStep = 2;

    /// <summary>The core's volley: frames it waits lit over a hole (crimson first, then white), frames between its bolts, how many, the rest
    /// after them, and the bolts' speed.</summary>
    private const int SlitWait = 24, WarnCrimsonFrames = 16, SlitGap = 5, SlitBolts = 3, SlitRest = 60;
    private const double SlitBoltSpeed = 1.5;

    /// <summary>The throws: frames a brick shakes, frames between throws above and below half the core's hit points, the frames with no
    /// column open that force a whole column down, frames between that column's bricks, and a thrown brick's fall in pixels a frame.</summary>
    private const int ShakeFrames = 30, ThrowEvery = 50, ThrowFast = 35, OpenForcedAfter = 300, ForcedGap = 10;
    private const double BrickFallSpeed = 1;

    /// <summary>The blinking outline a regrowing brick shows, its colour and its frames.</summary>
    private const uint GrowOutline = 0xcdbfe8;
    private const int OutlineFrames = 45;

    /// <summary>Frames a counted timing may be off by: the step that first sees a change can lag it.</summary>
    private const int FrameSlack = 2;

    /// <summary>Frames a shot put on a part takes to meet it and be spent.</summary>
    private const int ShotFlightFrames = 4;

    /// <summary>A shot too light to break a brick or end the core in one hit.</summary>
    private const double LightShot = 1;

    /// <summary>Frames the wall gets to come down and hold its row.</summary>
    private const int EntryFrameCap = 600;

    /// <summary>A column in the middle of the widest wall that the tests open.</summary>
    private const int OpenCol = 8;

    /// <summary>Frames given to the core to pace over an open column and fire down it.</summary>
    private const int BoltWatchFrames = 900;

    /// <summary>How near the core's middle must be to a column's to stand over it.</summary>
    private const double SameSpot = 0.01;

    /// <summary>Where a test puts a thrown brick: left of the middle and below the wall.</summary>
    private const double BrickSpawnX = 40, BrickSpawnY = 40;

    private static EnemyBehaviour PileOn => RaiderGame.TableBehaviour(EnemyKind.PileOn)!;

    /// <summary>The points the pile-on is worth, as the table gives them.</summary>
    private const int PileOnScore = 8000;

    /// <summary>Where across the field the pile-on arrives in a test, as a share of its width: the middle.</summary>
    private const double SpawnAcross = 0.5;

    /// <summary>The name the pile-on goes by, and the line it speaks when no post is waiting.</summary>
    private const string PileOnName = "PILE-ON", OwnArriveLine = "We are all saying it.";

    /// <summary>The seeds of the replayed fight, the gate's fight and the ship that never moves.</summary>
    private const ulong ReplaySeed = 3, GateSeed = 2, StillShipSeed = 4;
    private static EnemyBehaviour BrickBehaviour => RaiderGame.TableBehaviour(EnemyKind.Brick)!;

    public static IEnumerable<object[]> Fields() => EnemyKit.FieldSizes.Select(f => new object[] { f.Cols, f.Rows });

    // ---------------------------------------------------------------- what the layout table says

    /// <summary>The field's width and height, then columns, bricks deep, instigator width and height, wall width and height, and the lowest part count, field by field.</summary>
    private record Layout(int PW, int PH, int Cols, int Deep, int InstW, int InstH, int Width, int Height, int Parts);

    private static readonly Layout[] Table =
    [
        new(96, 120, 12, 3, 21, 13, 96, 29, 37),
        new(128, 92, 16, 3, 21, 13, 128, 29, 49),
        new(128, 48, 16, 1, 21, 13, 128, 19, 17),
        new(60, 32, 7, 1, 13, 9, 56, 15, 8),
    ];

    private static Layout Expected(RaiderGame g) => Table.Single(t => t.PW == g.PW && t.PH == g.PH);

    private static int Top(RaiderGame g) => g.PH < CompactFieldH ? TopRowCompact : TopRow;

    private static double Floor(RaiderGame g) => EnemyKit.BossFloor(g.PH);

    /// <summary>The middle column of the field, in pixels.</summary>
    private static double MidX(RaiderGame g) => g.PW / 2.0;

    /// <summary>The top and the bottom of the wall's drawing, in pixels.</summary>
    private static double WallTop(Enemy e) => e.Y - e.Sprite.H / 2.0;

    private static double WallBottom(Enemy e) => e.Y + e.Sprite.H / 2.0;

    private static Part PartOf(Enemy e, string name) => e.Parts!.Single(p => p.Name == name);

    private const string CoreName = "core";

    // a brick part is named brick-<depth>-<column>
    private const string BrickPrefix = "brick-";
    private const char NameSeparator = '-';
    private const int DepthField = 1, ColumnField = 2;

    private static Part Brick(Enemy e, int deep, int col) => PartOf(e, $"{BrickPrefix}{deep}{NameSeparator}{col}");

    private static bool IsBrick(Part p) => p.Name.StartsWith(BrickPrefix);

    private static int BrickDepth(Part p) => int.Parse(p.Name.Split(NameSeparator)[DepthField]);

    private static int BrickCol(Part p) => int.Parse(p.Name.Split(NameSeparator)[ColumnField]);

    private static IEnumerable<Part> Column(Enemy e, int col) => e.Parts!.Where(p => IsBrick(p) && BrickCol(p) == col);

    /// <summary>Breaks a part outright, as if shot off.</summary>
    private static void Break(Part p)
    {
        p.State = PartState.Dead;
        p.Hp = 0;
    }

    /// <summary>Breaks every brick of a column, so a shot or a bolt passes down it.</summary>
    private static void OpenColumn(Enemy e, int col)
    {
        foreach (var p in Column(e, col)) Break(p);
    }

    private static double ColX(RaiderGame g, int col) => (g.PW - CellW * Expected(g).Cols) / 2 + CellW * col + BrickHalfW;

    /// <summary>Makes a Pile-On arrive the way the gate does and steps until it holds its row; the sweep keeps only it (and what it throws).</summary>
    private static Enemy Settled(RaiderGame g)
    {
        var e = g.SpawnKind(EnemyKind.PileOn, SpawnAcross);
        for (int f = 0; f < EntryFrameCap && WallTop(e) < Top(g); f++) StepKeepingOnlyWall(g);
        Assert.Equal(Top(g), WallTop(e), Places);
        return e;
    }

    /// <summary>One frame with every enemy but the Pile-On and its bricks swept away.</summary>
    private static void StepKeepingOnlyWall(RaiderGame g)
    {
        g.Step();
        g.Enemies.RemoveAll(x => x.Kind != EnemyKind.PileOn && x.Kind != EnemyKind.Brick);
    }

    private static List<Bullet> Hostile(RaiderGame g) => g.Bullets.Where(b => !b.Friendly).ToList();

    // ---------------------------------------------------------------- the kinds

    [Fact]
    public void The_pile_on_is_a_boss_with_its_numbers()
    {
        Assert.True(PileOn.Boss);
        Assert.Equal(PileOnName, PileOn.Name);
        Assert.Equal(PileOnScore, PileOn.Score);
        Assert.True(PileOn.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.True(PileOn.Hp > 0);
    }

    [Fact]
    public void The_brick_is_a_two_point_enemy_worth_twenty_in_a_seven_by_five_sprite()
    {
        Assert.False(BrickBehaviour.Boss);
        Assert.Equal("BRICK", BrickBehaviour.Name);
        Assert.Equal(BrickHp, BrickBehaviour.Hp);
        Assert.Equal(BrickScore, BrickBehaviour.Score);
        Assert.Equal((BrickW, BrickH), (BrickBehaviour.Body.W, BrickBehaviour.Body.H));
        Assert.Equal(0, BrickBehaviour.DropChance);
    }

    // ---------------------------------------------------------------- the layout

    [Theory, MemberData(nameof(Fields))]
    public void The_layout_table_holds_on_every_field(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var want = Expected(g);
        var e = Settled(g);
        var parts = e.Parts!;
        Assert.Equal(want.Parts, parts.Count);
        Assert.Equal(want.Cols * want.Deep, parts.Count(IsBrick));
        var core = PartOf(e, CoreName);
        Assert.Equal((want.InstW, want.InstH), (core.Closed.W, core.Closed.H));
        Assert.True(core.Vital);
        Assert.Equal(PartState.Open, core.State);
        Assert.Equal(want.Width, e.Sprite.W);
        Assert.Equal(want.Height, e.Sprite.H);
        var extent = EnemyKit.Extent(e);
        Assert.Equal(want.Width, extent.R - extent.L, Places);
        Assert.Equal(want.Height, extent.B - extent.T, Places);
        Assert.Equal(Top(g), extent.T, Places);
        // rows are inclusive: the last one is the lowest pixel row, and it stands on the floor or above it
        Assert.True(extent.B - 1 <= Floor(g), $"lowest row {extent.B - 1} against the floor {Floor(g)}");
        Assert.True(EnemyKit.InsideWidth(g, e));
        Assert.Equal(MidX(g), e.X, Places);
        output.WriteLine($"{cols}x{rows} cells = {g.PW}x{g.PH} px: the wall is {extent.R - extent.L:0}x{extent.B - extent.T:0} px, rows {extent.T:0} to {extent.B - 1:0}, {parts.Count} parts, floor row {Floor(g)}");
    }

    [Theory, MemberData(nameof(Fields))]
    public void Every_brick_is_seven_by_five_in_an_eight_by_five_cell_and_no_two_boxes_overlap(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var e = Settled(g);
        foreach (var p in e.Parts!.Where(IsBrick))
        {
            Assert.Equal((BrickW, BrickH), (p.Closed.W, p.Closed.H));
            Assert.Equal((CellW / 2, BrickH / 2), (p.HW, p.HH));   // the box fills the cell, so no slit between two columns lets a shot through
            Assert.Equal(PartState.Open, p.State);
            Assert.False(p.Vital);
            Assert.Null(p.Wreck);
            Assert.Equal(BrickScore, p.Score);
        }
        for (int a = 0; a < e.Parts!.Count; a++)
            for (int b = a + 1; b < e.Parts.Count; b++)
            {
                var (al, at, ar, ab) = EnemyKit.Box(e, e.Parts[a]);
                var (bl, bt, br, bb) = EnemyKit.Box(e, e.Parts[b]);
                Assert.False(al < br && bl < ar && at < bb && bt < ab, $"{e.Parts[a].Name} overlaps {e.Parts[b].Name}");
            }
        // the bricks sit on whole pixels: the view rounds a half to the even pixel, so a brick drawn on a half would wander
        foreach (var p in e.Parts)
        {
            var art = p.Closed;
            Assert.Equal(0, (e.X + p.OX - art.W / 2.0) % 1, Places);
            Assert.Equal(0, (e.Y + p.OY - art.H / 2.0) % 1, Places);
        }
    }

    [Theory, MemberData(nameof(Fields))]
    public void A_column_costs_fourteen_to_open_whether_it_is_one_deep_or_three(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var e = Settled(g);
        var want = Expected(g);
        for (int col = 0; col < want.Cols; col++)
        {
            var column = Column(e, col).ToList();
            Assert.Equal(want.Deep, column.Count);
            Assert.Equal(ColumnHp, column.Sum(p => p.MaxHp), Places);
            Assert.Equal(ColumnHp, column.Sum(p => p.Hp), Places);
        }
    }

    [Theory, MemberData(nameof(Fields))]
    public void The_core_is_one_pool_of_the_enemies_hit_points_and_the_bar_counts_it_alone(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var e = Settled(g);
        var core = PartOf(e, CoreName);
        Assert.Equal(core.Hp, e.Hp, Places);
        Assert.Equal(core.MaxHp, e.MaxHp, Places);
        // the ramp's boss multiplier reaches the core, not the bricks
        var plain = EnemyKit.Game(cols, rows);
        var ramped = plain.SpawnKind(EnemyKind.PileOn, SpawnAcross);
        Assert.Equal(PileOn.Hp * Ramp.HpMul(plain.Stage), PartOf(ramped, CoreName).MaxHp, Places);
        Assert.Equal(ColumnHp, Column(ramped, 0).Sum(p => p.MaxHp), Places);
    }

    // ---------------------------------------------------------------- the wall stands, the core paces in cells

    [Theory, MemberData(nameof(Fields))]
    public void The_wall_never_moves_the_core_steps_in_whole_cells_and_nothing_hurtful_sits_below_the_floor(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var e = Settled(g);
        var core = PartOf(e, CoreName);
        double startX = e.X, startY = e.Y, last = e.X + core.OX;
        var seen = new HashSet<double> { last };
        var brickOffsets = e.Parts!.Where(IsBrick).ToDictionary(p => p.Name, p => p.OX);
        const int watchFrames = 700, leastSpotsPaced = 6;
        g.Input.Key(GameKey.Left, true);
        for (int f = 0; f < watchFrames; f++)
        {
            StepKeepingOnlyWall(g);
            if (!g.Enemies.Contains(e)) break;
            Assert.Equal(startX, e.X, Places);
            Assert.Equal(startY, e.Y, Places);
            foreach (var p in e.Parts!.Where(p => IsBrick(p) && p.Timer <= 0 && p.State == PartState.Open))
                Assert.True(Math.Abs(p.OX - brickOffsets[p.Name]) <= 1, "a brick shakes by a pixel at most");
            double now = e.X + core.OX, step = Math.Abs(now - last);
            Assert.True(step == 0 || step == CoreStep, $"the core moved {step} px in a frame");
            last = now;
            seen.Add(now);
            Assert.True(EnemyKit.InsideWidth(g, e));
            var extent = EnemyKit.Extent(e);
            Assert.True(extent.B - 1 <= Floor(g));
            Assert.True(Hostile(g).Count <= MaxHostileBolts, $"{Hostile(g).Count} hostile bullets");
            if (g.Deaths > 0) break;
        }
        Assert.True(seen.Count >= leastSpotsPaced, $"the core paced over {seen.Count} spots");
    }

    [Fact]
    public void It_enters_by_whole_cells_and_nothing_hurts_before_it_holds_its_row()
    {
        var g = EnemyKit.Game();
        var e = g.SpawnKind(EnemyKind.PileOn, SpawnAcross);
        double last = e.Y;
        Assert.True(WallBottom(e) <= 0, "it starts wholly above the field");
        const int fewestEntryFrames = 20, mostEntryFrames = 200;
        int frames = 0;
        for (; frames < EntryFrameCap && WallTop(e) < Top(g); frames++)
        {
            StepKeepingOnlyWall(g);
            double dy = e.Y - last;
            Assert.True(dy == 0 || dy == EntryStep, $"it entered {dy} px in a frame");
            last = e.Y;
            Assert.Empty(Hostile(g));
            Assert.DoesNotContain(g.Enemies, x => x.Kind == EnemyKind.Brick);
        }
        Assert.InRange(frames, fewestEntryFrames, mostEntryFrames);
    }

    // ---------------------------------------------------------------- what a shot meets

    [Fact]
    public void A_shot_stops_on_a_brick_and_flies_through_a_hole_to_the_core()
    {
        var g = EnemyKit.Game();
        var e = Settled(g);
        var core = PartOf(e, CoreName);
        var lowest = Brick(e, LowestDepth, OpenCol);
        double x = e.X + lowest.OX;
        EnemyKit.Shot(g, x, e.Y + lowest.OY, LightShot);
        for (int i = 0; i < ShotFlightFrames; i++) StepKeepingOnlyWall(g);
        Assert.True(lowest.Hp < lowest.MaxHp);
        Assert.Equal(core.MaxHp, core.Hp, Places);
        Assert.DoesNotContain(g.Bullets, b => b.Friendly && Math.Abs(b.X - x) < 1 && Math.Abs(b.Y - (e.Y + lowest.OY)) < 1);
        OpenColumn(e, OpenCol);
        EnemyKit.Shot(g, x, e.Y + core.OY, LightShot);
        for (int i = 0; i < ShotFlightFrames; i++) StepKeepingOnlyWall(g);
        Assert.True(core.Hp < core.MaxHp);
        Assert.Equal(core.Hp, e.Hp, Places);
    }

    [Fact]
    public void A_shot_that_passes_down_a_hole_is_stopped_by_the_next_brick_row_only_where_one_stands()
    {
        var g = EnemyKit.Game();
        var e = Settled(g);
        var core = PartOf(e, CoreName);
        const int col = 5;
        Break(Brick(e, LowestDepth, col));   // the lowest of the column: the one above it still blocks
        double x = e.X + Brick(e, TopDepth, col).OX;
        EnemyKit.Shot(g, x, e.Y + Brick(e, LowestDepth, col).OY, LightShot);   // through the gap, into the brick above it
        for (int i = 0; i < ShotFlightFrames; i++) StepKeepingOnlyWall(g);
        Assert.Equal(core.MaxHp, core.Hp, Places);
        Assert.True(Brick(e, MiddleDepth, col).Hp < Brick(e, MiddleDepth, col).MaxHp, "the second brick took the shot");
        Assert.DoesNotContain(g.Bullets, b => b.Friendly && Math.Abs(b.X - x) < 1 && b.Y < e.Y + Brick(e, LowestDepth, col).OY);
    }

    [Fact]
    public void A_shot_outside_the_bounding_box_of_the_living_parts_tests_no_part()
    {
        var g = EnemyKit.Game();
        var e = Settled(g);
        const double belowTheWall = 30, aboveTheWall = 4;
        int before = g.PartBoxTests;
        EnemyKit.Shot(g, e.X, WallBottom(e) + belowTheWall, LightShot);
        EnemyKit.Shot(g, e.X, WallTop(e) - aboveTheWall, LightShot);
        // the filler wave could be in the way: the count is of this enemy's parts only
        g.Step();
        int after = g.PartBoxTests;
        Assert.Equal(before, after);
        EnemyKit.Shot(g, e.X, e.Y, LightShot);
        g.Step();
        Assert.True(g.PartBoxTests > after);
    }

    [Fact]
    public void The_box_follows_the_parts_a_wreck_keeps_it_and_a_gone_part_with_no_wreck_does_not()
    {
        // a shell in the middle, a pod below it, and two side parts far enough out that each alone widens the box
        const double ex = 60, ey = 30, sideOffset = 20, podOffset = 6;
        const int shellHalf = 4, sideHalf = 3, wreckSize = 2 * sideHalf + 1, shellHp = 30, sideHp = 5;
        const uint wreckColour = 0x010203;
        var g = EnemyKit.Game();
        var shell = EnemyKit.MakePart("shell", 0, 0, shellHalf, shellHalf, shellHp, PartState.Armoured, vital: false);
        var gone = EnemyKit.MakePart("gone", sideOffset, 0, sideHalf, sideHalf, sideHp, vital: false);
        var wreck = EnemyKit.MakePart("wreck", -sideOffset, 0, sideHalf, sideHalf, sideHp, vital: false, wreck: EnemyKit.Solid(wreckSize, wreckSize, wreckColour));
        var pod = EnemyKit.MakePart("pod", 0, podOffset, sideHalf, sideHalf, sideHp);
        var e = g.SpawnParts(EnemyKind.Bulwark, ex, ey, shell, gone, wreck, pod);
        gone.State = PartState.Dead; wreck.State = PartState.Dead;
        g.Step();
        g.Enemies.RemoveAll(x => x != e);
        int before = g.PartBoxTests;
        EnemyKit.Shot(g, ex + sideOffset, ey, LightShot);   // where the gone part was, and outside the others: tests nothing
        g.Step();
        g.Enemies.RemoveAll(x => x != e);
        Assert.Equal(before, g.PartBoxTests);
        before = g.PartBoxTests;
        EnemyKit.Shot(g, ex - sideOffset, ey, LightShot);   // on the wreck: it stops the shot
        g.Step();
        Assert.True(g.PartBoxTests > before);
    }

    // ---------------------------------------------------------------- the core's volley

    [Fact]
    public void The_core_stops_over_a_column_with_every_brick_gone_waits_twenty_four_frames_and_fires_three_bolts_five_apart()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((name, _, _, _) => played.Add(name));
        var e = Settled(g);
        var core = PartOf(e, CoreName);
        const int col = OpenCol;
        OpenColumn(e, col);
        double cx = ColX(g, col);
        g.Input.Key(GameKey.Left, true);   // the ship keeps to the far side, out of the hole's line
        int arrive = -1;
        var bolts = new List<(int Frame, Bullet Bolt)>();
        var seen = new HashSet<Bullet>();
        for (int f = 0; f < BoltWatchFrames && bolts.Count < SlitBolts; f++)
        {
            StepKeepingOnlyWall(g);
            if (arrive < 0 && Math.Abs(e.X + core.OX - cx) < SameSpot) arrive = f;
            foreach (var b in Hostile(g).Where(b => seen.Add(b))) bolts.Add((f, b));
        }
        Assert.True(arrive >= 0, "the core paces over the open column");
        Assert.Equal(SlitBolts, bolts.Count);
        Assert.InRange(bolts[0].Frame - arrive, SlitWait, SlitWait + 1);
        Assert.Equal(SlitGap, bolts[1].Frame - bolts[0].Frame);
        Assert.Equal(SlitGap, bolts[2].Frame - bolts[1].Frame);
        foreach (var (_, b) in bolts)
        {
            Assert.Equal(0, b.VX, Places);
            Assert.Equal(SlitBoltSpeed, b.VY, Places);
            Assert.Equal(cx, b.X, Places);
        }
        Assert.Contains(SfxNames.Charge, played);
        Assert.Equal(cx, e.X + core.OX, Places);
    }

    [Fact]
    public void After_its_three_bolts_the_core_waits_sixty_frames_before_it_moves_on()
    {
        var g = EnemyKit.Game();
        var e = Settled(g);
        var core = PartOf(e, CoreName);
        const int col = OpenCol;
        OpenColumn(e, col);
        double cx = ColX(g, col);
        g.Input.Key(GameKey.Left, true);
        const int restWatchFrames = 1200;
        int third = -1, moved = -1, bolts = 0;
        var seen = new HashSet<Bullet>();
        for (int f = 0; f < restWatchFrames && moved < 0; f++)
        {
            StepKeepingOnlyWall(g);
            foreach (var _ in Hostile(g).Where(b => seen.Add(b))) if (++bolts == SlitBolts) third = f;
            if (third >= 0 && Math.Abs(e.X + core.OX - cx) > SameSpot) moved = f;
        }
        Assert.True(third > 0 && moved > 0);
        Assert.InRange(moved - third, SlitRest, SlitRest + FrameSlack);
    }

    [Theory, MemberData(nameof(Fields))]
    public void The_core_fires_nothing_while_every_column_still_has_a_brick(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var e = Settled(g);
        g.Input.Key(GameKey.Left, true);
        int bolts = 0;
        const int beforeFirstThrowLands = 100;
        for (int f = 0; f < beforeFirstThrowLands; f++)   // before the first throw has had time to open a one-deep column
        {
            StepKeepingOnlyWall(g);
            bolts += Hostile(g).Count;
        }
        Assert.Equal(0, bolts);
    }

    [Fact]
    public void The_hole_is_lit_crimson_then_white_for_the_twenty_four_frames_before_the_volley()
    {
        var g = EnemyKit.Game();
        var e = Settled(g);
        var core = PartOf(e, CoreName);
        const int col = OpenCol;
        OpenColumn(e, col);
        double cx = ColX(g, col);
        g.Input.Key(GameKey.Left, true);
        int arrive = -1;
        var colours = new List<uint>();
        int edgeX = (int)Math.Floor(cx + BrickHalfW), edgeY = (int)Math.Floor(e.Y + Brick(e, MiddleDepth, col).OY);
        for (int f = 0; f < BoltWatchFrames; f++)
        {
            StepKeepingOnlyWall(g);
            if (arrive < 0 && Math.Abs(e.X + core.OX - cx) < SameSpot) arrive = f;
            if (arrive >= 0)
            {
                colours.Add(EnemyKit.ClearPickupsAndDraw(g).Get(edgeX, edgeY));
                if (f - arrive >= SlitWait + FrameSlack) break;
            }
        }
        Assert.True(arrive >= 0);
        const int crimsonWhiteOverlap = 3;   // the step from crimson to white may blink the two for a frame or so
        int crimson = colours.Take(SlitWait).Count(c => c == Palette.Crimson), white = colours.Take(SlitWait).Count(c => c == Palette.White);
        Assert.True(crimson >= WarnCrimsonFrames - FrameSlack, $"{crimson} crimson frames");
        Assert.True(white >= SlitWait - WarnCrimsonFrames - FrameSlack, $"{white} white frames");
        Assert.Equal(Palette.Crimson, colours[1]);
        Assert.Equal(Palette.White, colours[SlitWait - FrameSlack]);
        Assert.True(Array.FindIndex([.. colours], c => c == Palette.White) > Array.FindLastIndex([.. colours.Take(SlitWait)], c => c == Palette.Crimson) - crimsonWhiteOverlap, "white comes after the crimson");
    }

    // ---------------------------------------------------------------- the throws

    [Fact]
    public void The_lowest_brick_of_the_column_nearest_the_ship_shakes_thirty_frames_with_lob_and_then_falls_at_one()
    {
        var g = EnemyKit.Game();
        var played = new List<(string, int)>();
        g.Sfx = new SfxBus((name, _, _, _) => played.Add((name, (int)g.Frame)));
        var e = Settled(g);
        g.Input.Key(GameKey.Left, true);
        Part? shaking = null;
        const int throwWatchFrames = 400;
        int shakeStart = -1, release = -1, lob = -1;
        for (int f = 0; f < throwWatchFrames && release < 0; f++)
        {
            StepKeepingOnlyWall(g);
            if (shaking is null)
            {
                shaking = e.Parts!.FirstOrDefault(p => IsBrick(p) && p.Timer > 0);
                if (shaking is not null) { shakeStart = f; lob = played.FindIndex(p => p.Item1 == SfxNames.Lob); }
            }
            else if (shaking.State == PartState.Dead && g.Enemies.Any(x => x.Kind == EnemyKind.Brick)) release = f;
        }
        Assert.NotNull(shaking);
        Assert.True(release > 0);
        Assert.InRange(release - shakeStart, ShakeFrames - 1, ShakeFrames + FrameSlack);
        Assert.True(lob >= 0, "the lob sound marks the shake");
        // the lowest brick of the column nearest the ship, which keeps to the left
        Assert.Equal(LowestDepth, BrickDepth(shaking!));
        var column = Column(e, BrickCol(shaking)).ToList();
        Assert.True(column.Count(p => p.State == PartState.Open) == Expected(g).Deep - 1, "the other two stand");
        var brick = g.Enemies.First(x => x.Kind == EnemyKind.Brick);
        Assert.Equal(e.X + Brick(e, LowestDepth, BrickCol(shaking)).OX, brick.X, Places);
        double y = brick.Y;
        StepKeepingOnlyWall(g);
        Assert.Equal(BrickFallSpeed, brick.Y - y, Places);
        Assert.Same(e, brick.Parent);
        Assert.True(brick.Y > e.Y);
    }

    [Fact]
    public void A_throw_comes_every_fifty_frames_and_every_thirty_five_below_half_the_cores_hit_points()
    {
        const double belowHalf = 0.4;
        const int gapWatchFrames = 420, throwsToSee = 4;
        int Gap(bool low)
        {
            var g = EnemyKit.Game();
            var e = Settled(g);
            var core = PartOf(e, CoreName);
            if (low) { core.Hp = core.MaxHp * belowHalf; }
            g.Input.Key(GameKey.Right, true);
            var starts = new List<int>();
            var shaking = new HashSet<Part>();
            for (int f = 0; f < gapWatchFrames && starts.Count < throwsToSee; f++)
            {
                StepKeepingOnlyWall(g);
                foreach (var p in e.Parts!.Where(p => IsBrick(p) && p.Timer > 0 && p.State == PartState.Open))
                    if (shaking.Add(p)) starts.Add(f);
                foreach (var p in shaking.ToArray()) if (p.Timer <= 0) shaking.Remove(p);
                if (g.Deaths > 0) break;
            }
            const int second = 1, third = 2;   // the first throw counts from the settle, so the gap is read after it
            Assert.True(starts.Count > third, $"{starts.Count} throws seen");
            return starts[third] - starts[second];
        }
        Assert.Equal(ThrowEvery, Gap(false));
        Assert.Equal(ThrowFast, Gap(true));
    }

    [Fact]
    public void A_thrown_brick_belongs_to_its_pile_on_has_two_hit_points_and_scores_twenty()
    {
        var g = EnemyKit.Game();
        var parent = Settled(g);
        var brick = g.SpawnAdd(EnemyKind.Brick, BrickSpawnX, BrickSpawnY, parent);
        Assert.Same(parent, brick.Parent);
        Assert.Equal(BrickHp, brick.Hp / Ramp.HpMul(g.Stage), Places);
        int score = g.Score;
        EnemyKit.Shot(g, brick.X, brick.Y + 1, brick.Hp + 1);
        StepKeepingOnlyWall(g);
        Assert.DoesNotContain(brick, g.Enemies);
        Assert.Equal(score + BrickScore, g.Score);
    }

    [Fact]
    public void A_fallen_brick_is_culled_below_the_field_and_when_its_pile_on_is_gone()
    {
        var g = EnemyKit.Game();
        const int rowsAboveTheBottom = 3, fallOutFrames = 40, cullFrames = 3;
        EnemyKit.Place(g, EnemyKind.Brick, BrickSpawnX, g.PH - rowsAboveTheBottom);
        for (int f = 0; f < fallOutFrames; f++) { g.Step(); g.Enemies.RemoveAll(x => x.Kind != EnemyKind.Brick); }
        Assert.Empty(g.Enemies);
        var parent = Settled(g);
        var orphan = g.SpawnAdd(EnemyKind.Brick, BrickSpawnX, BrickSpawnY, parent);
        g.Enemies.Remove(parent);
        for (int f = 0; f < cullFrames; f++) { g.Step(); g.Enemies.RemoveAll(x => x.Kind != EnemyKind.Brick); }
        Assert.DoesNotContain(orphan, g.Enemies);
    }

    [Fact]
    public void A_falling_brick_hits_a_ship_that_does_not_move()
    {
        var g = EnemyKit.Game();
        const int fallHeight = 30, fallFrames = 80;
        var brick = EnemyKit.Place(g, EnemyKind.Brick, g.PX, g.PY - fallHeight);
        brick.Parent = null;
        for (int f = 0; f < fallFrames && g.Deaths == 0; f++) { g.Step(); g.Enemies.RemoveAll(x => x != brick && x.Kind < EnemyKind.Gnat); }
        Assert.True(g.Deaths > 0 || g.Hp < Tuning.ShipHp || g.Shield < Tuning.ShieldHits, "the brick hit the ship");
    }

    // ---------------------------------------------------------------- regrowth and the whole column

    [Fact]
    public void A_column_short_of_a_brick_regains_one_in_its_highest_empty_place_after_a_blinking_outline_of_forty_five_frames()
    {
        var g = EnemyKit.Game();
        var e = Settled(g);
        g.Input.Key(GameKey.Left, true);
        // one brick of a far column is gone: the throws go to the column near the ship, so this one is the one that has been short longest
        const int farCol = 14, regrowWatchFrames = 400, fewestOutlineFrames = 20;
        var lowest = Brick(e, LowestDepth, farCol);
        Break(lowest);
        var up = Brick(e, MiddleDepth, farCol);
        Break(up);   // two gone: the highest empty place is depth 1
        int back = -1, outline = -1;
        var litFrames = new List<int>();
        int simFrames = 0;
        for (int f = 0; f < regrowWatchFrames && back < 0; f++)
        {
            if (g.HitStop == 0) simFrames++;
            StepKeepingOnlyWall(g);
            if (up.State == PartState.Open) back = simFrames;
            else if (outline < 0 || back < 0)
            {
                var screen = EnemyKit.ClearPickupsAndDraw(g);
                int left = (int)Math.Round(e.X + up.OX - BrickW / 2), top = (int)Math.Round(e.Y + up.OY - BrickH / 2);
                bool lit = false;
                for (int x = 0; x < BrickW; x++) lit |= (screen.Get(left + x, top) & EnemyKit.RgbMask) == GrowOutline;
                if (lit) { if (outline < 0) outline = simFrames; litFrames.Add(simFrames); }
            }
        }
        Assert.True(back > 0, "the brick regrows");
        Assert.Equal(PartState.Dead, lowest.State);
        Assert.Equal(up.MaxHp, up.Hp, Places);
        Assert.True(outline > 0 && back - outline <= OutlineFrames && back - outline >= fewestOutlineFrames, $"outline at {outline}, brick at {back} {string.Join(",", litFrames)}");
    }

    [Fact]
    public void A_dead_cell_lets_a_shot_through_to_the_brick_above_it()
    {
        var g = EnemyKit.Game();
        var e = Settled(g);
        const int col = 6;
        var lowest = Brick(e, LowestDepth, col);
        Break(lowest);
        EnemyKit.Shot(g, e.X + lowest.OX, e.Y + lowest.OY, LightShot);
        for (int i = 0; i < ShotFlightFrames; i++) StepKeepingOnlyWall(g);
        Assert.True(Brick(e, MiddleDepth, col).Hp < Brick(e, MiddleDepth, col).MaxHp);
    }

    [Fact]
    public void When_no_column_has_been_open_for_three_hundred_frames_the_next_throw_takes_a_whole_column_ten_frames_apart()
    {
        var g = EnemyKit.Game();
        var e = Settled(g);
        var drops = new List<int>();
        var seen = new HashSet<Enemy>();
        // the ship goes to a different column before each throw, so no column is ever worn through by the throws one at a time
        const int forcedWatchFrames = 560, fewestSingleThrows = 4;
        int[] cols = [1, 3, 5, 7, 9, 11, 13, 15];   // sixteen pixels a leg: the ship is under each column when its throw comes
        for (int f = 0; f < forcedWatchFrames; f++)
        {
            int throwNo = f / ThrowEvery;
            double target = ColX(g, cols[throwNo % cols.Length]);
            g.Input.Key(GameKey.Left, g.PX > target + EnemyKit.SteerDeadZone);
            g.Input.Key(GameKey.Right, g.PX < target - EnemyKit.SteerDeadZone);
            StepKeepingOnlyWall(g);
            if (g.Deaths > 0) break;
            foreach (var _ in g.Enemies.Where(x => x.Kind == EnemyKind.Brick && seen.Add(x))) drops.Add(f);
        }
        Assert.True(g.Deaths == 0, "the ship dodges by staying away from the fall line");
        // one brick each throw, fifty frames apart, until the wall has gone three hundred frames with no column open: then a column goes,
        // a brick every ten frames
        int forced = -1;
        for (int i = 0; i + 1 < drops.Count && forced < 0; i++) if (drops[i + 1] - drops[i] == ForcedGap) forced = i;
        Assert.True(forced >= fewestSingleThrows, $"the forced column came after the singles: {string.Join(",", drops)}");
        for (int i = 0; i < forced; i++) Assert.Equal(ThrowEvery, drops[i + 1] - drops[i]);
        Assert.True(drops[forced] >= OpenForcedAfter, $"not before three hundred frames: {drops[forced]}");
    }

    // ---------------------------------------------------------------- the voice

    /// <summary>The start and the multiplier of the string hash that gives each waiting post its id.</summary>
    private const ulong PostIdSeed = 17, PostIdPrime = 31;

    private static void Post(RaiderGame g, string text) => g.OfferDark(new DarkPost(DarknessKind.Rude, text.Aggregate(PostIdSeed, (hash, ch) => hash * PostIdPrime + ch), text));

    [Fact]
    public void The_pile_on_speaks_a_waiting_post_else_its_own_words_under_its_header()
    {
        var g = EnemyKit.Game();
        const int speakWatchFrames = 400;
        const string waiting = "grr you again";
        Post(g, waiting);
        var e = g.SpawnKind(EnemyKind.PileOn, SpawnAcross);
        for (int f = 0; f < speakWatchFrames && e.Text is null; f++) StepKeepingOnlyWall(g);
        Assert.Equal(waiting, e.Text);
        Assert.True(EnemySpeechBox.Speaks(e));

        var nothingWaiting = EnemyKit.Game();
        var own = nothingWaiting.SpawnKind(EnemyKind.PileOn, SpawnAcross);
        for (int f = 0; f < speakWatchFrames && own.Text is null; f++) StepKeepingOnlyWall(nothingWaiting);
        Assert.Equal(OwnArriveLine, own.Text);
        Assert.Equal(PileOnName, own.Header);
    }

    [Fact]
    public void Its_turns_come_on_arriving_at_the_first_hole_below_half_and_at_the_end_in_its_own_words()
    {
        Assert.Equal(OwnArriveLine, PileOn.Line(EnemyKit.Speaker(EnemyKind.PileOn), VoiceCue.Arrive));
        Assert.Equal("So true. So true. So true.", PileOn.Line(EnemyKit.Speaker(EnemyKind.PileOn), VoiceCue.PartLost));
        Assert.Equal("Screenshot this.", PileOn.Line(EnemyKit.Speaker(EnemyKind.PileOn), VoiceCue.Phase));
        Assert.Equal("It was only a joke.", PileOn.Line(EnemyKit.Speaker(EnemyKind.PileOn), VoiceCue.Dying));
        Assert.True(PileOn.Speaks);
        var g = EnemyKit.Game();
        var e = Settled(g);
        Assert.Equal(Turn.Stack, PileOn.TurnFor(g, e, VoiceCue.Arrive));
        Assert.Equal(Turn.Stack, PileOn.TurnFor(g, e, VoiceCue.Phase));
        Assert.Equal(Turn.Stack, PileOn.TurnFor(g, e, VoiceCue.Opens));
        Assert.Equal(Turn.Cut, PileOn.TurnFor(g, e, VoiceCue.Dying));
        // a brick shot off is not a turn by itself
        Assert.Equal(Turn.Skip, PileOn.TurnFor(g, e, VoiceCue.PartLost));
    }

    [Fact]
    public void Three_boxes_stack_the_live_one_and_two_left_behind_with_four_posts_waiting()
    {
        var g = EnemyKit.Game();
        const int speakWatchFrames = 200, holeCol = 4, turnWatchFrames = 10;
        string[] posts = ["one one one", "two two two", "three three", "four four"];
        foreach (var post in posts) Post(g, post);
        var e = Settled(g);
        for (int f = 0; f < speakWatchFrames && e.Text is null; f++) StepKeepingOnlyWall(g);
        Assert.Equal(posts[0], e.Text);
        // the wall takes a first hole: the turn stacks the next post beside the last
        OpenColumn(e, holeCol);
        g.Input.Key(GameKey.Left, true);
        for (int f = 0; f < turnWatchFrames && !g.Echoes.Any(x => x.Kind == EnemyKind.PileOn); f++) StepKeepingOnlyWall(g);
        Assert.Equal(posts[1], e.Text);
        Assert.Single(g.Echoes, x => x.Kind == EnemyKind.PileOn);
        // two more turns: the kind cues one every third brick it throws, by when the boxes left behind are gone, so the test cues them
        StepKeepingOnlyWall(g); StepKeepingOnlyWall(g);   // a box left behind must have begun to be read
        g.Cue(e, VoiceCue.Opens);
        Assert.Equal(posts[2], e.Text);
        StepKeepingOnlyWall(g); StepKeepingOnlyWall(g);
        g.Cue(e, VoiceCue.Opens);
        Assert.Equal(posts[3], e.Text);
        StepKeepingOnlyWall(g);
        Assert.Equal(posts.Length - 1, g.Echoes.Count(x => x.Kind == EnemyKind.PileOn));
        var visible = EnemySpeechBox.Placed(g, g.Layout, Tuning.MaxSpeechBoxes).Select(shown => shown.Enemy).ToList();
        Assert.Equal(Tuning.MaxSpeechBoxes, visible.Count);   // the live one and the two newest left behind
        Assert.Contains(e, visible);
        Assert.DoesNotContain(visible, v => v.Text == posts[0]);
        var spots = visible.Select(v => EnemySpeechBox.Box(v, g.Layout).C).Distinct().ToList();
        Assert.Equal(visible.Count, spots.Count);
    }

    // ---------------------------------------------------------------- same seed, same run; the gate

    [Fact]
    public void The_same_seed_gives_the_same_pile_on_fight()
    {
        const int replayCols = 80, replayRows = 24;   // a common terminal
        string Run()
        {
            var g = new RaiderGame(ReplaySeed) { SignedIn = true };
            g.SetLayout(FieldLayout.Compute(replayCols, replayRows));
            g.StartAtBoss(PileOnStage);
            bool met = false;
            const int runFrames = 900;
            for (int f = 0; f < runFrames; f++) { BotFrame(g); g.Step(); met |= g.Enemies.Any(x => x.Kind == EnemyKind.PileOn); }
            var boss = g.Boss;
            return $"{met}:{boss?.Kind}:{boss?.X:0.000}:{boss?.Y:0.000}:{boss?.Hp:0.000}|{string.Join(",", boss?.Parts?.Select(p => p.State == PartState.Dead ? "x" : p.Hp.ToString("0.0")) ?? [])}#{g.Score}:{g.Deaths}:{g.Bullets.Count}:{g.Enemies.Count}";
        }
        Assert.Equal(Run(), Run());
        Assert.StartsWith("True:", Run());
    }

    [Fact]
    public void The_gate_brings_it_to_fight_with_its_table_hit_points()
    {
        var g = new RaiderGame(GateSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(PileOnStage);
        const int arriveFrames = 3;
        for (int f = 0; f < Tuning.BossWarnFrames + arriveFrames; f++) g.Step();
        Assert.Equal(GateState.Fighting, g.Gate);
        Assert.NotNull(g.Boss);
        Assert.Equal(EnemyKind.PileOn, g.Boss!.Kind);
        Assert.Equal(PileOn.Hp, g.Boss.Hp, Places);
        Assert.Equal(PileOn.Hp, g.Boss.MaxHp, Places);
    }

    // ---------------------------------------------------------------- the fight

    /// <summary>The bot counts a hole as lit when the core is this near it, sees danger in a bolt or a brick this near its line, steps this
    /// far aside from a lit hole, looks this far up for a falling brick, and steps this far aside from one.</summary>
    private const double BotOverHoleWithin = 0.6, BotDangerHalfWidth = 6, BotHoleStepAside = 9, BotBrickLookUp = 32, BotBrickStepAside = 8;

    private const int BotWeaponLevel = 3;

    /// <summary>A bot: fires always; goes to a column with every brick gone if there is one (and steps off it while it is lit), else under
    /// the column with the fewest brick hit points.</summary>
    private static void BotFrame(RaiderGame g)
    {
        g.Input.Key(GameKey.Fire, true);
        var e = g.Enemies.FirstOrDefault(x => x.Kind == EnemyKind.PileOn);
        double target = MidX(g);
        if (e is not null)
        {
            var core = PartOf(e, CoreName);
            var bricks = e.Parts!.Where(IsBrick).ToList();
            var groups = bricks.GroupBy(BrickCol).Select(c => (Col: c.Key, Hp: c.Where(p => p.State != PartState.Dead).Sum(p => p.Hp), X: e.X + c.First().OX)).ToList();
            var open = groups.Where(c => c.Hp <= 0).ToList();
            double coreX = e.X + core.OX;
            // a hole the core is waiting over is dangerous: the bolts come down it
            var lit = open.Where(c => Math.Abs(c.X - coreX) < BotOverHoleWithin).ToList();
            if (lit.Count > 0)
            {
                var hole = lit[0];
                var danger = Hostile(g).Any(b => Math.Abs(b.X - hole.X) < BotDangerHalfWidth);
                target = hole.X + (g.PX < hole.X ? -BotHoleStepAside : BotHoleStepAside);
                if (!danger) target = hole.X;
            }
            else if (open.Count > 0) target = open.OrderBy(c => Math.Abs(c.X - coreX)).First().X;
            else target = groups.OrderBy(c => c.Hp).ThenBy(c => Math.Abs(c.X - g.PX)).First().X;
            // keep out of the way of a brick that is coming down the ship's column
            foreach (var b in g.Enemies.Where(x => x.Kind == EnemyKind.Brick && x.Y < g.PY && g.PY - x.Y < BotBrickLookUp && Math.Abs(x.X - g.PX) < BotDangerHalfWidth))
                target = g.PX + (g.PX < b.X ? -BotBrickStepAside : BotBrickStepAside);
        }
        g.Input.Key(GameKey.Left, g.PX > target + EnemyKit.SteerDeadZone);
        g.Input.Key(GameKey.Right, g.PX < target - EnemyKit.SteerDeadZone);
    }

    /// <summary>Frames from the gate to the boss's death for the bot at its weapon level, or -1.</summary>
    private static int Fight(ulong seed, int cols, int rows)
    {
        var g = new RaiderGame(seed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtBoss(PileOnStage);
        while (g.WeaponLevel < BotWeaponLevel) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); }
        for (int f = 0; f < Tuning.BossStayFrames; f++)
        {
            BotFrame(g);
            g.Step();
            if (g.Gate == GateState.None && g.BossesBeaten >= PileOnStage) return g.Kills > 0 && g.Boss is null && !g.Enemies.Any(x => x.Kind == EnemyKind.PileOn) ? f + 1 : -1;
        }
        return -1;
    }

    [Fact]
    public void A_bot_at_weapon_level_three_kills_it_in_the_short_end_of_the_band_over_several_seeds()
    {
        const int bandSeeds = 7;
        const double fastestSeconds = 21, slowestSeconds = 33;
        var times = new List<int>();
        for (ulong seed = 1; seed <= bandSeeds; seed++)
        {
            int took = Fight(seed, EnemyKit.BigCols, EnemyKit.BigRows);
            Assert.True(took > 0, $"seed {seed}: not down inside the stay");
            times.Add(took);
        }
        times.Sort();
        int median = times[times.Count / 2];
        output.WriteLine($"pile-on fight at level {BotWeaponLevel}, frames to kill: {string.Join(" ", times)}; median {median} ({median / (double)Tuning.Fps:0.0} s)");
        Assert.InRange(median / (double)Tuning.Fps, fastestSeconds, slowestSeconds);
    }

    [Theory, MemberData(nameof(Fields))]
    public void The_fight_ends_on_every_field(int cols, int rows)
    {
        const int fieldSeeds = 3;
        var times = new List<int>();
        for (ulong seed = 1; seed <= fieldSeeds; seed++) times.Add(Fight(seed, cols, rows));
        output.WriteLine($"{cols}x{rows}: frames to kill {string.Join(" ", times)}");
        Assert.True(times.Count(t => t > 0) >= fieldSeeds - 1, $"{string.Join(" ", times)}");
    }

    [Fact]
    public void A_ship_that_never_moves_dies_to_the_thrown_bricks()
    {
        var g = new RaiderGame(StillShipSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(PileOnStage);
        const int hitWithinFrames = 900;
        for (int f = 0; f < hitWithinFrames && g.Deaths == 0; f++) g.Step();
        Assert.True(g.Deaths > 0, $"a ship under the wall is hit within {hitWithinFrames} frames");
    }

    [Fact]
    public void The_wall_comes_down_as_debris_at_the_particle_cap_and_the_boss_sound_plays()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((name, _, _, _) => played.Add(name));
        var e = Settled(g);
        var core = PartOf(e, CoreName);
        const double overkill = 5;
        EnemyKit.Shot(g, e.X + core.OX, e.Y + core.OY, core.Hp + overkill);
        foreach (var p in e.Parts!.Where(IsBrick)) Break(p);
        StepKeepingOnlyWall(g);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.True(g.Particles.Count > 0);
        Assert.True(g.Particles.Count <= Tuning.MaxParticles, $"{g.Particles.Count} particles");
        Assert.Contains(SfxNames.Boom, played);
    }
}
