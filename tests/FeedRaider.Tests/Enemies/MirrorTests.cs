using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Mirror (boss 9): one wide wall of glass, two panes of facets either side of an eye. These tests give its kind and numbers, its
/// size on every field, its art (the drawings exactly), the machine (a 240-frame cycle: each pane rests, warns in white and burns),
/// the rule that it hurts a ship for where it stands and never for firing, what a lit and a dull facet do with a shot, the eye's
/// rhythm and its spread, its voice (it says back the run's echoes) and the fight (a bot with the starting gun and at weapon level 3).
/// </summary>
public sealed class MirrorTests(ITestOutputHelper output)
{
    private const int Entering = 0, LeftRest = 1, LeftTell = 2, LeftBurn = 3, RightRest = 4, RightTell = 5, RightBurn = 6;
    /// <summary>One pane's turn: it rests, warns and burns. Two turns make the cycle.</summary>
    private const int RestFrames = 35, TellFrames = 40, BurnFrames = 45, TurnFrames = RestFrames + TellFrames + BurnFrames, CycleFrames = 2 * TurnFrames;
    private const int FacetW = 11, EyeW = 11;
    /// <summary>How far to the side of a facet's centre its glare still burns.</summary>
    private const double GlareReach = 6;
    /// <summary>The warning's dots and the glare's ice stripes are this many pixels apart.</summary>
    private const int DotPitch = 2;
    private const int MaxFightFrames = Tuning.BossStayFrames;
    /// <summary>A phone's field: 80 pixels wide.</summary>
    private const int PhoneCols = 40, PhoneRows = 58;
    private const uint Shade = 0x5f7f9f, Silver = 0x8f8a9a, Sheen = 0xd8d4e0;
    /// <summary>Every colour inside a facet a shot can break: the silver glass and its gold mark on crimson.</summary>
    private static readonly uint[] DullColours = [Silver, Sheen, Palette.WeakBody, Palette.WeakMark];

    /// <summary>A game with the field emptied and the boss gates off, so a mirror placed by hand is never cleared away by the stage.</summary>
    private static RaiderGame Field(int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        EnemyKit.Clear(g);
        return g;
    }

    private static EnemyBehaviour Mirror => RaiderGame.TableBehaviour(EnemyKind.Mirror)!;

    private static Part PartOf(Enemy e, string name) => e.Parts!.Single(p => p.Name == name);

    /// <summary>One frame with only these enemies kept; the eye's spread is swept away too, so the glare is the only thing that can hurt the ship.</summary>
    private static void StepKeepingAndClearingHostileBolts(RaiderGame g, params Enemy[] keep)
    {
        g.Step();
        g.Enemies.RemoveAll(e => !keep.Contains(e));
        g.Bullets.RemoveAll(b => !b.Friendly);
    }

    /// <summary>One frame with only these enemies kept, the spread left to fly.</summary>
    private static void StepLive(RaiderGame g, params Enemy[] keep)
    {
        g.Step();
        g.Enemies.RemoveAll(e => !keep.Contains(e));
    }

    /// <summary>A mirror on a field, stepped until it has settled in its place: the first frame of the left pane's rest.</summary>
    private static Enemy Settled(RaiderGame g)
    {
        const int enterLimit = 200;
        var m = g.SpawnKind(EnemyKind.Mirror, 0.5);
        for (int i = 0; i < enterLimit && m.Mode == Entering; i++) StepKeepingAndClearingHostileBolts(g, m);
        Assert.Equal(LeftRest, m.Mode);
        return m;
    }

    /// <summary>Steps until the mirror has just come into a mode, then this many frames more.</summary>
    private static void StepToMode(RaiderGame g, Enemy m, int mode, int into = 0)
    {
        for (int i = 0; i < 2 * CycleFrames && m.Mode != mode; i++) StepKeepingAndClearingHostileBolts(g, m);
        Assert.Equal(mode, m.Mode);
        for (int i = 0; i < into; i++) StepKeepingAndClearingHostileBolts(g, m);
    }

    /// <summary>Holds the keys that take the ship toward a column, to within half of one frame's travel.</summary>
    private static void Steer(RaiderGame g, double x)
    {
        const double slack = Tuning.ShipSpeed / 2;
        g.Input.Key(GameKey.Left, g.PX > x + slack);
        g.Input.Key(GameKey.Right, g.PX < x - slack);
    }

    /// <summary>One frame of a ship that keeps this far to the side of the mirror's middle, firing or not.</summary>
    private static void Follow(RaiderGame g, Enemy m, double offset, bool firing)
    {
        g.Input.Key(GameKey.Fire, firing);
        Steer(g, m.X + offset);
        StepKeepingAndClearingHostileBolts(g, m);
    }

    /// <summary>Steps a ship that holds its fire and keeps this far to the side of the mirror's middle until the mirror has just come into a mode.</summary>
    private static void FollowToMode(RaiderGame g, Enemy m, int mode, double offset = 0)
    {
        for (int i = 0; i < 2 * CycleFrames && m.Mode != mode; i++) Follow(g, m, offset, false);
        Assert.Equal(mode, m.Mode);
    }

    private static void Walk(RaiderGame g, double x, params Enemy[] keep)
    {
        const int walkLimit = 400;
        for (int i = 0; i < walkLimit && Math.Abs(g.PX - x) > Tuning.ShipSpeed / 2; i++)
        {
            Steer(g, x);
            StepKeepingAndClearingHostileBolts(g, keep);
        }
        g.Input.Key(GameKey.Left, false);
        g.Input.Key(GameKey.Right, false);
    }

    private static int HostileBoltCount(RaiderGame g) => g.Bullets.Count(b => !b.Friendly);

    /// <summary>What the ship has lost (its shield, its hit points) and its deaths.</summary>
    private static (int Shield, int Hp, int Deaths) Harm(RaiderGame g) => (g.Shield, g.Hp, g.Deaths);

    private static readonly (int Shield, int Hp, int Deaths) Unharmed = (Tuning.ShieldHits, Tuning.ShipHp, 0);

    /// <summary>True when the ship has lost something between two readings of its harm.</summary>
    private static bool Hurt((int Shield, int Hp, int Deaths) before, (int Shield, int Hp, int Deaths) after) =>
        after.Shield < before.Shield || after.Hp < before.Hp || after.Deaths > before.Deaths;

    private static Part Facet(Enemy m, bool left, int i) => PartOf(m, (left ? "facet-l-" : "facet-r-") + i);

    private static Part Eye(Enemy m) => PartOf(m, "eye");

    /// <summary>Breaks these parts with a heavy shot each, in one frame. Only while the parts are open.</summary>
    private static void BreakAndStep(RaiderGame g, Enemy m, IEnumerable<Part> parts)
    {
        const double heavy = 999;
        foreach (var p in parts.ToList()) EnemyKit.Shot(g, m.X + p.OX, m.Y + p.OY, heavy);
        StepKeepingAndClearingHostileBolts(g, m);
    }

    private static IEnumerable<Part> Pane(Enemy m, bool left) => m.Parts!.Where(p => p.Name.StartsWith(left ? "facet-l-" : "facet-r-", StringComparison.Ordinal));

    /// <summary>The left column and the top row of a part's art on the field.</summary>
    private static (int X, int Y) Corner(Enemy m, Part p)
    {
        var art = p.OpenArt ?? p.Closed;
        return ((int)Math.Round(m.X + p.OX - art.W / 2.0), (int)Math.Round(m.Y + p.OY - art.H / 2.0));
    }

    private static uint Pixel(Surface f, int x, int y) => f.Get(x, y) & EnemyKit.RgbMask;

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_is_a_boss_and_has_its_numbers()
    {
        const int hp = 72, score = 8000, place = 9;
        Assert.True(Mirror.Boss);
        Assert.True(Mirror.Speaks);
        Assert.Equal(EnemyKind.Mirror, Mirror.Kind);
        Assert.Equal("MIRROR", Mirror.Name);
        Assert.Equal(hp, Mirror.Hp);
        Assert.Equal(score, Mirror.Score);
        Assert.True(Mirror.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(EnemyKind.Mirror, Ramp.Boss(place));
    }

    [Fact]
    public void The_gate_at_the_end_of_stage_nine_brings_it_in_as_the_boss()
    {
        const int place = 9, settle = 5;
        var g = EnemyKit.Game();
        g.StartAtBoss(place);
        for (int i = 0; i < Tuning.BossWarnFrames + settle; i++) g.Step();
        Assert.NotNull(g.Boss);
        Assert.Equal(EnemyKind.Mirror, g.Boss!.Kind);
        Assert.Equal(Mirror.Hp, g.Boss.Hp, 6);   // never scaled by the stage
        Assert.Equal("MIRROR", g.BossName);
    }

    // ---------------------------------------------------------------- its size

    [Theory]
    [InlineData(30, 16, 2, 55, 12, 5)]
    [InlineData(40, 58, 2, 55, 18, 6)]
    [InlineData(48, 60, 3, 77, 20, 6)]
    [InlineData(80, 24, 4, 99, 22, 6)]
    [InlineData(120, 46, 4, 99, 22, 6)]
    public void Its_size_rows_and_parts_follow_the_layout_rule_on_every_field(int cols, int rows, int k, int w, int h, int top)
    {
        const int watched = 3 * CycleFrames;
        var g = Field(cols, rows);
        var m = Settled(g);
        Assert.Equal(2 * k + 1, m.Parts!.Count);
        Assert.Equal(2 * k, m.Parts.Count(p => p.Name.StartsWith("facet", StringComparison.Ordinal)));
        Assert.Equal(w, m.Sprite.W);
        Assert.Equal(h, m.Sprite.H);
        Assert.Equal(top, (int)Math.Round(m.Y - h / 2.0));
        double floor = EnemyKit.BossFloor(g.PH);
        for (int f = 0; f < watched; f++)
        {
            StepKeepingAndClearingHostileBolts(g, m);
            Assert.True(EnemyKit.InsideWidth(g, m), $"frame {f}: outside the width");
            Assert.True(EnemyKit.LowestPixel(m) <= floor, $"frame {f}: lowest pixel {EnemyKit.LowestPixel(m)} under the floor {floor}");
            Assert.True(top == (int)Math.Round(EnemyKit.Extent(m).T), $"frame {f}: top {EnemyKit.Extent(m).T} y {m.Y} PH {g.PH}");
        }
        output.WriteLine($"{cols}x{rows}: field {g.PW}x{g.PH} px, mirror {w}x{h} px, {m.Parts.Count} parts");
    }

    [Fact]
    public void It_is_drawn_at_least_half_as_wide_as_the_field_at_desktop_at_phone_and_on_every_other_field()
    {
        (int Cols, int Rows)[] fields = [.. EnemyKit.FieldSizes, (PhoneCols, PhoneRows)];
        foreach (var (cols, rows) in fields)
        {
            var g = Field(cols, rows);
            var m = Settled(g);
            var x = EnemyKit.Extent(m);
            double drawn = x.R - x.L;
            output.WriteLine($"{cols}x{rows}: drawn {drawn}x{x.B - x.T} px on a field {g.PW}x{g.PH} px, {drawn / g.PW:P0} of the width");
            Assert.True(drawn >= g.PW / 2.0, $"{cols}x{rows}: {drawn} of {g.PW}");
        }
    }

    [Fact]
    public void It_is_one_body_with_no_empty_column_from_edge_to_edge_even_with_every_facet_broken()
    {
        (int Cols, int Rows)[] fields = [.. EnemyKit.FieldSizes, (PhoneCols, PhoneRows)];
        foreach (var (cols, rows) in fields)
        {
            var g = Field(cols, rows);
            var m = Settled(g);
            Assert.All(Columns(Drawn(m)), filled => Assert.True(filled > 0));
            BreakAndStep(g, m, Pane(m, true).Concat(Pane(m, false)));
            Assert.All(Pane(m, true).Concat(Pane(m, false)), p => Assert.Equal(PartState.Dead, p.State));
            Assert.All(Columns(Frames(m)), filled => Assert.True(filled > 0));
        }
    }

    [Fact]
    public void At_phone_size_a_burning_pane_leaves_more_than_half_the_field_free_of_its_glare()
    {
        var g = Field(PhoneCols, PhoneRows);
        var m = Settled(g);
        foreach (bool left in new[] { true, false })
        {
            var boxes = Pane(m, left).Select(p => (L: m.X + p.OX - GlareReach, R: m.X + p.OX + GlareReach)).ToList();
            double burnt = boxes.Max(b => b.R) - boxes.Min(b => b.L);
            output.WriteLine($"phone: a burning pane covers {burnt} px of {g.PW}");
            Assert.True(g.PW - burnt > g.PW / 2.0, $"{burnt} of {g.PW} burnt");
        }
    }

    // ---------------------------------------------------------------- the art

    private static readonly string[] DrawingNarrow =
    [
        "###########.................................###########",
        "#IIIIIiiII############...........############IIiiIIIII#",
        "#IIIIiiIII##IIIIIiiII#############IIiiIIIII##IIIiiIIII#",
        "#IIIrrrIII##IIIIiiIII##o+++++++o##IIIiiIIII##IIIrrrIII#",
        "#IIiryrIII##IIIrrrIII##+orrrrro+##IIIrrrIII##IIIryriII#",
        "#IiirrrIIi##IIiryrIII##+orryrro+##IIIryriII##iIIrrriiI#",
        "#iiIIIIIii##IiirrrIIi##+oryyyro+##iIIrrriiI##iiIIIIIii#",
        "#iIIIIIiiI##iiIIIIIii##+orryrro+##iiIIIIIii##IiiIIIIIi#",
        "#IIIIIiiII##iIIIIIiiI##+orrrrro+##IiiIIIIIi##IIiiIIIII#",
        "############IIIIIiiII##o+++++++o##IIiiIIIII############",
        "...........###########.#o+++++o#.###########...........",
        "........................#######........................",
    ];

    /// <summary>The field the three-facet drawing below is made on.</summary>
    private const int ThreeFacetCols = 48, ThreeFacetRows = 60;

    private static readonly string[] DrawingThreeFacets =
    [
        "###########.......................................................###########",
        "#IIIIIiiII#.......................................................#IIiiIIIII#",
        "#IIIIiiIII############.................................############IIIiiIIII#",
        "#IIIiiIIII##IIIIIiiII#.................................#IIiiIIIII##IIIIiiIII#",
        "#IIiiIIIII##IIIIiiIII############...........############IIIiiIIII##IIIIIiiII#",
        "#IiirrrIIi##IIIiiIIII##IIIIIiiII#...........#IIiiIIIII##IIIIiiIII##iIIrrriiI#",
        "#iiIryrIii##IIiiIIIII##IIIIiiIII#############IIIiiIIII##IIIIIiiII##iiIryrIii#",
        "#iIIrrriiI##IiirrrIIi##IIIiiIIII##o+++++++o##IIIIiiIII##iIIrrriiI##IiirrrIIi#",
        "#IIIIIiiII##iiIryrIii##IIiiIIIII##+ooooooo+##IIIIIiiII##iiIryrIii##IIiiIIIII#",
        "#IIIIiiIII##iIIrrriiI##IiirrrIIi##+orrrrro+##iIIrrriiI##IiirrrIIi##IIIiiIIII#",
        "#IIIiiIIII##IIIIIiiII##iiIryrIii##+orrrrro+##iiIryrIii##IIiiIIIII##IIIIiiIII#",
        "#IIiiIIIII##IIIIiiIII##iIIrrriiI##+orryrro+##IiirrrIIi##IIIiiIIII##IIIIIiiII#",
        "#IiiIIIIIi##IIIiiIIII##IIIIIiiII##+oryyyro+##IIiiIIIII##IIIIiiIII##iIIIIIiiI#",
        "############IIiiIIIII##IIIIiiIII##+orryrro+##IIIiiIIII##IIIIIiiII############",
        "...........#IiiIIIIIi##IIIiiIIII##+orrrrro+##IIIIiiIII##iIIIIIiiI#...........",
        "...........############IIiiIIIII##+orrrrro+##IIIIIiiII############...........",
        "......................#IiiIIIIIi##+ooooooo+##iIIIIIiiI#......................",
        "......................############o+++++++o############......................",
        "..................................#o+++++o#..................................",
        "...................................#######...................................",
    ];

    /// <summary>The eye's two lid colours: the dark rim and the lighter lid.</summary>
    private const uint LidRim = 0x4b2a78, LidFace = 0x7a3fb0;

    private static readonly Dictionary<uint, char> Letters = new()
    {
        [Palette.Plum] = '#',
        [LidRim] = 'o',
        [LidFace] = '+',
        [Palette.Crimson] = 'r',
        [Palette.Gold] = 'y',
        [Silver] = 'I',
        [Sheen] = 'i',
    };

    /// <summary>The whole drawing as the view lays it: the frames, then every part's dull art with the eye open, in the palette's letters.</summary>
    private static string[] Drawn(Enemy m)
    {
        var layers = new List<(Sprite Art, int X, int Y)> { (m.Sprite, 0, 0) };
        foreach (var p in m.Parts!)
        {
            var art = p.OpenArt ?? p.Closed;
            layers.Add((art, (int)Math.Round(m.Sprite.W / 2.0 + p.OX - art.W / 2.0), (int)Math.Round(m.Sprite.H / 2.0 + p.OY - art.H / 2.0)));
        }
        return Lettered(m.Sprite.W, m.Sprite.H, layers);
    }

    /// <summary>What is left to see with every facet broken: the frames and the eye.</summary>
    private static string[] Frames(Enemy m)
    {
        var eye = Eye(m);
        var art = eye.Closed;
        return Lettered(m.Sprite.W, m.Sprite.H,
            [(m.Sprite, 0, 0), (art, (int)Math.Round(m.Sprite.W / 2.0 + eye.OX - art.W / 2.0), (int)Math.Round(m.Sprite.H / 2.0 + eye.OY - art.H / 2.0))]);
    }

    private static string[] Lettered(int w, int h, List<(Sprite Art, int X, int Y)> layers)
    {
        var rows = new string[h];
        for (int y = 0; y < h; y++)
        {
            var chars = new char[w];
            for (int x = 0; x < w; x++)
            {
                chars[x] = '.';
                foreach (var (art, x0, y0) in layers)
                {
                    if (x < x0 || x >= x0 + art.W || y < y0 || y >= y0 + art.H) continue;
                    uint v = art.Px[(y - y0) * art.W + x - x0];
                    if (v != 0 && Letters.TryGetValue(v & EnemyKit.RgbMask, out char letter)) chars[x] = letter;
                }
            }
            rows[y] = new string(chars);
        }
        return rows;
    }

    /// <summary>How many pixels each column of a drawing has.</summary>
    private static int[] Columns(string[] rows) => [.. Enumerable.Range(0, rows[0].Length).Select(x => rows.Count(r => r[x] != '.'))];

    [Theory]
    [InlineData(ThreeFacetCols, ThreeFacetRows)]
    [InlineData(30, 16)]
    public void Its_dull_drawing_is_the_drawing_exactly(int cols, int rows)
    {
        var m = Settled(Field(cols, rows));
        var drawn = Drawn(m);
        foreach (string row in drawn) output.WriteLine(row);
        Assert.Equal(cols == ThreeFacetCols ? DrawingThreeFacets : DrawingNarrow, drawn);
    }

    [Fact]
    public void A_lit_facet_is_all_ice_with_a_shield_blue_edge_and_a_dull_one_is_silver_with_a_gold_mark_on_crimson()
    {
        var m = Settled(Field());
        var p = Facet(m, true, 1);
        var lit = p.Closed; var dull = p.OpenArt!;
        Assert.Equal(Palette.ShieldGlow, lit.Px[0] & EnemyKit.RgbMask);
        Assert.Equal(Palette.Plum, dull.Px[0] & EnemyKit.RgbMask);
        Assert.All(lit.Px.Where(v => v != 0), v => Assert.Contains(v & EnemyKit.RgbMask, new[] { Palette.Ice, Palette.ShieldGlow }));
        Assert.Contains(dull.Px, v => (v & EnemyKit.RgbMask) == Silver);
        Assert.Contains(dull.Px, v => (v & EnemyKit.RgbMask) == Sheen);
        Assert.Contains(dull.Px, v => (v & EnemyKit.RgbMask) == Palette.WeakMark);
        Assert.Contains(dull.Px, v => (v & EnemyKit.RgbMask) == Palette.WeakBody);
        Assert.DoesNotContain(dull.Px, v => (v & EnemyKit.RgbMask) is Palette.Ice or Palette.ShieldGlow or Shade);
        Assert.Null(p.Wreck);
    }

    [Fact]
    public void The_eye_is_gold_on_crimson_when_open_and_blue_under_its_lid_when_shut()
    {
        var eye = Eye(Settled(Field()));
        Assert.Contains(eye.OpenArt!.Px, v => (v & EnemyKit.RgbMask) == Palette.Gold);
        Assert.Contains(eye.OpenArt.Px, v => (v & EnemyKit.RgbMask) == Palette.Crimson);
        Assert.DoesNotContain(eye.Closed.Px, v => (v & EnemyKit.RgbMask) is Palette.Gold or Palette.Crimson);
        Assert.Contains(eye.Closed.Px, v => (v & EnemyKit.RgbMask) == Shade);
    }

    [Fact]
    public void The_parts_land_on_whole_pixels_over_the_body_while_the_boss_steps()
    {
        const double shipX = 20;
        var g = Field(ThreeFacetCols, ThreeFacetRows);
        var m = Settled(g);
        Walk(g, shipX, m);
        for (int i = 0; i < CycleFrames; i++)
        {
            Assert.Equal(0, (m.X - m.Sprite.W / 2.0) % 1.0, 9);
            Assert.Equal(0, (m.Y - m.Sprite.H / 2.0) % 1.0, 9);
            StepKeepingAndClearingHostileBolts(g, m);
        }
    }

    [Fact]
    public void A_burning_pane_is_drawn_all_ice_and_a_resting_one_is_not()
    {
        var g = Field();
        var m = Settled(g);
        var p = Facet(m, true, 1);
        int midX = FacetW / 2, midY = p.Closed.H / 2;
        StepToMode(g, m, LeftBurn, 1);
        var (x0, y0) = Corner(m, p);
        var lit = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(Palette.Ice, Pixel(lit, x0 + midX, y0 + midY));
        Assert.Equal(Palette.ShieldGlow, Pixel(lit, x0, y0));
        StepToMode(g, m, RightRest, 1);
        (x0, y0) = Corner(m, p);
        var dull = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Contains(Pixel(dull, x0 + midX, y0 + midY), DullColours);
        Assert.Equal(Palette.Plum, Pixel(dull, x0, y0));
    }

    // ---------------------------------------------------------------- the warning

    [Fact]
    public void A_white_warning_stands_under_every_facet_of_the_pane_for_forty_frames_before_it_burns_and_hurts_nothing()
    {
        var g = Field();
        var m = Settled(g);
        StepToMode(g, m, LeftTell);
        int warned = 0;
        while (m.Mode == LeftTell)
        {
            var f = EnemyKit.ClearPickupsAndDraw(g);
            foreach (var p in Pane(m, true))
            {
                var (x0, y0) = Corner(m, p);
                int under = y0 + p.Closed.H;
                Assert.Equal(Palette.White, Pixel(f, x0, under));
                Assert.Equal(Palette.White, Pixel(f, x0 + FacetW - 1, under));
                Assert.Equal(Palette.White, Pixel(f, x0, g.PH - 1 - (g.PH - 1 - under) % DotPitch));
                Assert.Equal(PartState.Open, p.State);
            }
            Assert.False(Mirror.Touches(g, m));
            Assert.Equal(Unharmed, Harm(g));
            warned++;
            StepKeepingAndClearingHostileBolts(g, m);
        }
        Assert.Equal(TellFrames, warned);
        Assert.Equal(LeftBurn, m.Mode);
        Assert.NotEqual(Unharmed, Harm(g));   // the ship stood under it to the end, and the first frame of the burn has it
    }

    [Fact]
    public void The_warning_grows_whiter_in_three_steps_and_the_pane_itself_pales()
    {
        var g = Field();
        var m = Settled(g);
        var p = Facet(m, true, 0);
        var (x0, y0) = Corner(m, p);
        int under = y0 + p.Closed.H;
        int White(Surface f)
        {
            int n = 0;
            for (int y = under; y < g.PH; y++)
                for (int x = x0; x < x0 + FacetW; x++)
                    if (Pixel(f, x, y) == Palette.White) n++;
            return n;
        }
        int resting = White(EnemyKit.ClearPickupsAndDraw(g));
        uint glass = Pixel(EnemyKit.ClearPickupsAndDraw(g), x0 + 1, y0 + 1);
        var counts = new List<int>();
        StepToMode(g, m, LeftTell);
        (x0, y0) = Corner(m, p);
        while (m.Mode == LeftTell)
        {
            var f = EnemyKit.ClearPickupsAndDraw(g);
            int n = White(f);
            if (counts.Count == 0 || counts[^1] != n) counts.Add(n);
            Assert.NotEqual(glass, Pixel(f, x0 + 1, y0 + 1));
            StepKeepingAndClearingHostileBolts(g, m);
        }
        const int steps = 3;
        Assert.Equal(steps, counts.Count);
        Assert.True(resting < counts[0] && counts[0] < counts[1] && counts[1] < counts[2], $"{resting} then {string.Join(" ", counts)}");
    }

    [Fact]
    public void The_glare_is_drawn_from_every_lit_facet_down_to_the_floor_and_nowhere_else()
    {
        var g = Field();
        var m = Settled(g);
        StepToMode(g, m, LeftBurn, 1);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        foreach (var p in Pane(m, true))
        {
            var (x0, y0) = Corner(m, p);
            foreach (int y in new[] { y0 + p.Closed.H, g.PH - 1 })
            {
                Assert.Equal(Palette.White, Pixel(f, x0, y));
                Assert.Equal(Palette.White, Pixel(f, x0 + FacetW - 1, y));
                Assert.Equal(Palette.Ice, Pixel(f, x0 + DotPitch, y));
            }
        }
        foreach (var p in Pane(m, false))
        {
            var (x0, y0) = Corner(m, p);
            int under = y0 + p.Closed.H;
            Assert.NotEqual(Palette.White, Pixel(f, x0, under));
            Assert.NotEqual(Palette.Ice, Pixel(f, x0 + DotPitch, under));
        }
    }

    [Fact]
    public void It_sounds_open_as_a_pane_warns_the_beam_as_it_burns_and_shut_as_it_dulls()
    {
        var g = Field();
        var played = new List<(string Name, int Mode)>();
        Enemy? m = null;
        g.Sfx = new SfxBus((n, _, _, _) => { if (m is not null) played.Add((n, m.Mode)); });
        m = Settled(g);
        for (int f = 0; f < CycleFrames; f++) Follow(g, m, 0, false);
        string[] own = [SfxNames.Open, SfxNames.Beam, SfxNames.Shut];
        // a sound is made as the mode changes, so it is heard with the mode that is ending
        (string, int)[] expected =
        [
            (SfxNames.Open, LeftRest), (SfxNames.Beam, LeftTell), (SfxNames.Shut, LeftBurn),
            (SfxNames.Open, RightRest), (SfxNames.Beam, RightTell), (SfxNames.Shut, RightBurn),
        ];
        Assert.Equal(expected, played.Where(s => own.Contains(s.Name)).Take(expected.Length));
    }

    // ---------------------------------------------------------------- the machine

    [Fact]
    public void A_cycle_is_two_hundred_and_forty_frames_each_pane_resting_thirty_five_warning_forty_and_burning_forty_five()
    {
        var g = Field();
        var m = Settled(g);
        StepToMode(g, m, LeftTell);
        var runs = new List<(int Mode, int Frames)>();
        for (int f = 0; f < 2 * CycleFrames; f++)
        {
            if (runs.Count > 0 && runs[^1].Mode == m.Mode) runs[^1] = (m.Mode, runs[^1].Frames + 1);
            else runs.Add((m.Mode, 1));
            Follow(g, m, 0, false);
        }
        (int, int)[] cycle =
        [
            (LeftTell, TellFrames), (LeftBurn, BurnFrames), (RightRest, RestFrames),
            (RightTell, TellFrames), (RightBurn, BurnFrames), (LeftRest, RestFrames),
        ];
        Assert.Equal([.. cycle, .. cycle], runs);
        Assert.Equal(Tuning.InvulnFrames, BurnFrames);   // one burn hurts a ship once at most
    }

    [Fact]
    public void A_pane_is_armoured_as_one_only_while_it_burns_and_the_eye_is_shut_only_while_the_mirror_rests()
    {
        var g = Field();
        var m = Settled(g);
        for (int f = 0; f < CycleFrames; f++)
        {
            bool leftBurns = m.Mode == LeftBurn, rightBurns = m.Mode == RightBurn, rests = m.Mode is LeftRest or RightRest;
            Assert.All(Pane(m, true), p => Assert.Equal(leftBurns ? PartState.Armoured : PartState.Open, p.State));
            Assert.All(Pane(m, false), p => Assert.Equal(rightBurns ? PartState.Armoured : PartState.Open, p.State));
            Assert.Equal(rests ? PartState.Armoured : PartState.Open, Eye(m).State);
            StepKeepingAndClearingHostileBolts(g, m);
        }
    }

    [Fact]
    public void The_eye_stops_a_shot_while_shut_and_is_hurt_by_one_while_open()
    {
        const double dmg = 5;
        var g = Field();
        var m = Settled(g);
        var eye = Eye(m);
        EnemyKit.Shot(g, m.X + eye.OX, m.Y + eye.OY, dmg);
        StepKeepingAndClearingHostileBolts(g, m);
        Assert.Equal(Mirror.Hp, m.Hp);
        StepToMode(g, m, LeftTell);
        EnemyKit.Shot(g, m.X + eye.OX, m.Y + eye.OY, dmg);
        StepKeepingAndClearingHostileBolts(g, m);
        Assert.Equal(Mirror.Hp - dmg, m.Hp);
        StepToMode(g, m, LeftBurn);
        EnemyKit.Shot(g, m.X + eye.OX, m.Y + eye.OY, dmg);
        StepKeepingAndClearingHostileBolts(g, m);
        Assert.Equal(Mirror.Hp - 2 * dmg, m.Hp);
    }

    [Fact]
    public void A_pane_broken_in_part_still_burns_under_what_is_left_and_a_wholly_broken_pane_stays_dark_in_its_turn()
    {
        const double underThePane = -(FacetW + EyeW);
        var g = Field();
        var played = new List<string>();
        var m = Settled(g);
        BreakAndStep(g, m, [Facet(m, true, 0)]);
        FollowToMode(g, m, LeftBurn);
        Assert.Equal(PartState.Dead, Facet(m, true, 0).State);
        Assert.All(Pane(m, true).Skip(1), p => Assert.Equal(PartState.Armoured, p.State));
        FollowToMode(g, m, RightRest);
        BreakAndStep(g, m, Pane(m, true).Skip(1));
        FollowToMode(g, m, LeftRest);
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        FollowToMode(g, m, LeftBurn, underThePane);
        Assert.True(Math.Abs(g.PX - (m.X + underThePane)) <= Tuning.ShipSpeed, "the ship is under the broken pane");
        while (m.Mode == LeftBurn)
        {
            Assert.All(Pane(m, true), p => Assert.Equal(PartState.Dead, p.State));
            Assert.All(Pane(m, false), p => Assert.Equal(PartState.Open, p.State));
            Assert.False(Mirror.Touches(g, m));
            Follow(g, m, underThePane, false);
        }
        Assert.DoesNotContain(SfxNames.Beam, played);
        Assert.Equal(Unharmed, Harm(g));
    }

    [Fact]
    public void While_it_rests_it_steps_two_pixels_every_four_frames_to_put_the_pane_that_lights_next_over_the_ship_and_never_leaves_the_field()
    {
        const int stepPixels = 2, stepEvery = 4, near = 2;
        foreach (double shipX in new[] { 20.0, 100.0 })
        {
            var g = Field();
            var m = g.SpawnKind(EnemyKind.Mirror, 0.5);
            Walk(g, shipX, m);   // while the mirror is still coming on
            Assert.Equal(Entering, m.Mode);
            while (m.Mode == Entering) StepKeepingAndClearingHostileBolts(g, m);
            int k = Pane(m, true).Count();
            double PaneAt(Enemy b) => b.X - (FacetW * k + EyeW) / 2.0;
            double before = Math.Abs(PaneAt(m) - g.PX);
            double x = m.X; int moves = 0, frames = 0;
            while (m.Mode == LeftRest)
            {
                StepKeepingAndClearingHostileBolts(g, m);
                frames++;
                double d = m.X - x;
                Assert.True(d == 0 || Math.Abs(d) == stepPixels, $"a step is two pixels, saw {d}");
                if (d != 0) { moves++; Assert.Equal(Math.Sign(g.PX - (PaneAt(m) - d)), Math.Sign(d)); }
                x = m.X;
                Assert.True(EnemyKit.InsideWidth(g, m));
            }
            double after = Math.Abs(PaneAt(m) - g.PX);
            output.WriteLine($"ship at {shipX}: {moves} steps in {frames} frames, pane {before:0.0} -> {after:0.0} from the ship");
            Assert.InRange(moves, 1, frames / stepEvery + 1);
            Assert.True(moves >= frames / stepEvery - 1 || after <= near, $"{moves} steps and still {after} away");
            Assert.True(after < before);
        }
    }

    [Fact]
    public void It_stands_still_from_the_first_white_of_the_warning_to_the_end_of_the_burn()
    {
        const double shipX = 10;
        var g = Field();
        var m = Settled(g);
        Walk(g, shipX, m);
        foreach (int tell in new[] { LeftTell, RightTell })
        {
            StepToMode(g, m, tell);
            double x = m.X;
            for (int f = 0; f < TellFrames + BurnFrames - 1; f++)
            {
                StepKeepingAndClearingHostileBolts(g, m);
                Assert.Equal(x, m.X);
            }
        }
    }

    // ---------------------------------------------------------------- where the ship stands

    /// <summary>Gives every part its full strength back, so that what a firing ship breaks is no part of what the two ships are compared on.</summary>
    private static void Mend(Enemy m)
    {
        foreach (var p in m.Parts!) p.Hp = p.MaxHp;
    }

    /// <summary>A ship keeps this far to the side of the mirror's middle for one pane's turn, the glass mended every frame. Gives what the ship
    /// had lost after every frame, and how many shots it fired.</summary>
    private static (List<(int, int, int)> Harm, int Fired) StandThroughTurn(RaiderGame g, Enemy m, double offset, bool firing)
    {
        var harm = new List<(int, int, int)>();
        int fired = 0, mode = m.Mode;
        Assert.True(mode is LeftRest or RightRest);
        while (m.Mode == mode || m.Mode == mode + 1 || m.Mode == mode + 2)
        {
            Mend(m);
            int before = g.Bullets.Count(b => b.Friendly);
            Follow(g, m, offset, firing);
            fired += Math.Max(0, g.Bullets.Count(b => b.Friendly) - before);
            harm.Add(Harm(g));
        }
        return (harm, fired);
    }

    [Theory]
    [InlineData(-44, true, false)]
    [InlineData(-33, true, false)]
    [InlineData(-22, true, false)]
    [InlineData(-11, true, false)]
    [InlineData(0, false, false)]
    [InlineData(11, false, true)]
    [InlineData(22, false, true)]
    [InlineData(33, false, true)]
    [InlineData(44, false, true)]
    public void A_ship_that_never_fires_and_one_that_always_fires_take_the_same_harm_from_the_same_place(double offset, bool leftBurns, bool rightBurns)
    {
        var quiet = Field(); var loud = Field();
        var q = Settled(quiet); var l = Settled(loud);
        foreach (bool burnt in new[] { leftBurns, rightBurns })
        {
            var start = Harm(quiet);
            Assert.Equal(start, Harm(loud));
            var (quietHarm, quietFired) = StandThroughTurn(quiet, q, offset, false);
            var (loudHarm, loudFired) = StandThroughTurn(loud, l, offset, true);
            Assert.Equal(0, quietFired);
            Assert.True(loudFired > 0, "the firing ship fired");
            Assert.Equal(quietHarm, loudHarm);
            Assert.Equal(burnt, Hurt(start, Harm(quiet)));
            Assert.Equal(burnt, Hurt(start, Harm(loud)));
        }
    }

    [Theory]
    [InlineData(-22, true)]
    [InlineData(-11, true)]
    [InlineData(0, false)]
    [InlineData(11, false)]
    [InlineData(22, false)]
    public void At_phone_size_too_the_two_ships_are_burnt_alike_under_the_lit_pane_and_safe_alike_under_the_eye_and_the_dull_pane(double offset, bool burnt)
    {
        var quiet = Field(PhoneCols, PhoneRows); var loud = Field(PhoneCols, PhoneRows);
        var q = Settled(quiet); var l = Settled(loud);
        var (quietHarm, _) = StandThroughTurn(quiet, q, offset, false);
        var (loudHarm, loudFired) = StandThroughTurn(loud, l, offset, true);
        Assert.True(loudFired > 0, "the firing ship fired");
        Assert.Equal(quietHarm, loudHarm);
        Assert.Equal(burnt, Hurt(Unharmed, Harm(quiet)));
        Assert.Equal(burnt, Hurt(Unharmed, Harm(loud)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ship_that_keeps_under_the_eye_is_never_burnt_whether_it_fires_or_not(bool firing)
    {
        const int cycles = 2;
        var g = Field();
        var m = Settled(g);
        for (int f = 0; f < cycles * CycleFrames && m.Hp > 0; f++) Follow(g, m, 0, firing);
        Assert.Equal(Unharmed, Harm(g));
        Assert.Equal(firing, m.Hp < Mirror.Hp);
    }

    [Theory]
    [InlineData(120, 46)]
    [InlineData(40, 58)]
    public void A_ship_under_any_facet_when_the_white_shows_has_time_to_reach_the_eye(int cols, int rows)
    {
        int k = Pane(Settled(Field(cols, rows)), true).Count();
        for (int i = 0; i < k; i++)
        {
            var g = Field(cols, rows);
            var m = Settled(g);
            double offset = Facet(m, true, i).OX;
            while (m.Mode == LeftRest) Follow(g, m, offset, false);
            Assert.True(Math.Abs(g.PX - (m.X + offset)) <= GlareReach, "the ship starts under the facet");
            int walked = 0;
            while (m.Mode is LeftTell or LeftBurn)
            {
                if (Math.Abs(g.PX - m.X) > Tuning.ShipSpeed && m.Mode == LeftTell) walked++;
                Follow(g, m, 0, false);
            }
            output.WriteLine($"{cols}x{rows}: from facet {i} the eye is {walked} frames away; the warning lasts {TellFrames}");
            Assert.True(walked < TellFrames);
            Assert.Equal(Unharmed, Harm(g));
        }
    }

    [Fact]
    public void A_ship_that_never_moves_is_burnt_by_the_first_pane_that_lights_over_it()
    {
        var g = Field();
        var m = Settled(g);
        StepToMode(g, m, LeftTell, TellFrames - 1);
        Assert.Equal(LeftTell, m.Mode);
        Assert.Equal(Unharmed, Harm(g));
        StepKeepingAndClearingHostileBolts(g, m);
        Assert.Equal(LeftBurn, m.Mode);
        Assert.Equal(Tuning.ShieldHits - 1, g.Shield);
    }

    // ---------------------------------------------------------------- what a shot does

    [Fact]
    public void A_shot_on_a_lit_facet_only_pings_costs_the_facet_nothing_and_sends_nothing_back()
    {
        const double dmg = 5;
        var g = Field();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        var m = Settled(g);
        StepToMode(g, m, LeftBurn, 1);
        var p = Facet(m, true, 1);
        var shot = EnemyKit.Shot(g, m.X + p.OX + 1, m.Y + p.OY, dmg);
        StepLive(g, m);
        Assert.DoesNotContain(shot, g.Bullets);
        Assert.True(shot.Friendly);
        Assert.False(shot.Reflected);
        Assert.Equal(0, HostileBoltCount(g));
        Assert.Equal(p.MaxHp, p.Hp);
        Assert.Contains(SfxNames.Ping, played);
    }

    [Fact]
    public void Firing_at_a_lit_pane_for_the_whole_of_its_burn_never_hurts_the_ship()
    {
        var g = Field();
        var m = Settled(g);
        FollowToMode(g, m, LeftBurn);
        int struck = 0;
        for (int f = 0; f < BurnFrames - 1; f++)
        {
            foreach (var p in Pane(m, true)) EnemyKit.Shot(g, m.X + p.OX, m.Y + p.OY);
            struck += Pane(m, true).Count();
            g.Input.Key(GameKey.Fire, true);
            Steer(g, m.X);
            StepLive(g, m);
            Assert.DoesNotContain(g.Bullets, b => b.Reflected);
        }
        Assert.True(struck > BurnFrames);
        Assert.All(Pane(m, true), p => Assert.Equal(p.MaxHp, p.Hp));
        Assert.Equal(Unharmed, Harm(g));
    }

    [Fact]
    public void A_shot_on_a_dull_facet_is_taken_by_the_facet()
    {
        const double dmg = 3;
        var g = Field();
        var m = Settled(g);
        var p = Facet(m, false, 2);
        var shot = EnemyKit.Shot(g, m.X + p.OX, m.Y + p.OY, dmg);
        StepKeepingAndClearingHostileBolts(g, m);
        Assert.Equal(p.MaxHp - dmg, p.Hp);
        Assert.DoesNotContain(shot, g.Bullets);
        Assert.Equal(Mirror.Hp, m.Hp);   // the glass is not the Mirror's life
    }

    [Fact]
    public void A_panes_thirty_hit_points_are_shared_evenly_by_its_facets_rounded_up()
    {
        const int paneHp = 30;
        foreach (var (cols, rows) in EnemyKit.FieldSizes)
        {
            var m = Settled(Field(cols, rows));
            int k = Pane(m, true).Count();
            Assert.All(m.Parts!.Where(p => p.Name.StartsWith("facet", StringComparison.Ordinal)), p => Assert.Equal((paneHp + k - 1) / k, p.MaxHp));
        }
    }

    [Fact]
    public void A_broken_facets_column_lets_a_shot_through_and_is_never_burnt_again()
    {
        var g = Field();
        var m = Settled(g);
        var p = Facet(m, true, 1);
        BreakAndStep(g, m, [p]);
        while (m.Mode == LeftRest) Follow(g, m, p.OX, false);
        var shot = EnemyKit.Shot(g, m.X + p.OX, m.Y + p.OY);
        while (m.Mode is LeftTell or LeftBurn)
        {
            Follow(g, m, p.OX, false);
            Assert.False(Mirror.Touches(g, m));
        }
        Assert.Equal(Unharmed, Harm(g));
        Assert.True(shot.Y < m.Y + p.OY - p.HH, "the shot flew on through the hole");
        Assert.All(Pane(m, true).Where(q => q != p), q => Assert.Equal(q.MaxHp, q.Hp));
    }

    [Fact]
    public void A_broken_facet_leaves_its_empty_frame_and_scores_its_share_of_the_pane()
    {
        const int paneScore = 720;
        var g = Field();
        var m = Settled(g);
        int before = g.Score;
        var p = Facet(m, false, 1);
        var (x0, y0) = Corner(m, p);
        int midX = FacetW / 2, midY = p.Closed.H / 2;
        Assert.Contains(Pixel(EnemyKit.ClearPickupsAndDraw(g), x0 + midX, y0 + midY), DullColours);
        BreakAndStep(g, m, [p]);
        Assert.Equal(before + paneScore / Pane(m, false).Count(), g.Score);
        Assert.Equal(1, m.Parts!.Count(q => q.State == PartState.Dead));
        Assert.Equal(0, g.Kills);
        Assert.Contains(m, g.Enemies);
        Assert.Null(p.Wreck);
        (x0, y0) = Corner(m, p);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        Assert.DoesNotContain(Pixel(f, x0 + midX, y0 + midY), DullColours);
        Assert.Equal(Palette.Plum, Pixel(f, x0, y0));
    }

    // ---------------------------------------------------------------- the eye's spread

    [Fact]
    public void A_white_ring_blinks_round_the_pupil_for_twenty_four_frames_then_the_eye_fires_three_aimed_bolts_at_speed_one_point_one_five()
    {
        const int watchLimit = 200, fireGap = 70, blink = 24, slack = 4;
        // the blink and its charge sound may begin this many frames after, or before, the moment that is blink frames ahead of the volley
        const int blinkLateBy = 2, blinkEarlyBy = 1;
        const int beforeTheBlink = 40;
        const int inTheWhite = 2;   // two frames before the volley, inside the white last third of the warning
        const int pupilMiddleAbove = 1;
        const int ringAbove = 3;   // the ring's top row, one above the three-row pupil whose middle is one above the eye's centre
        const double speed = 1.15;
        var g = Field();
        var played = new List<(string Name, int Frame)>();
        int frame = 0;
        g.Sfx = new SfxBus((n, _, _, _) => played.Add((n, frame)));
        var m = Settled(g);
        var eye = Eye(m);
        int fire = -1;
        var ring = new List<uint>();
        var rest = new List<uint>();
        for (frame = 0; frame < watchLimit && fire < 0; frame++)
        {
            Steer(g, m.X);
            StepLive(g, m);
            var f = EnemyKit.ClearPickupsAndDraw(g);
            ring.Add(Pixel(f, (int)Math.Round(m.X), (int)Math.Round(m.Y + eye.OY) - ringAbove));
            rest.Add(eye.State == PartState.Open ? Palette.WeakBody : Shade);
            if (eye.State == PartState.Open) Assert.Equal(Palette.WeakMark, Pixel(f, (int)Math.Round(m.X), (int)Math.Round(m.Y + eye.OY) - pupilMiddleAbove));
            if (HostileBoltCount(g) > 0) fire = frame;
        }
        Assert.InRange(fire, fireGap - slack, fireGap + slack);
        var spread = g.Bullets.Where(b => !b.Friendly).ToList();
        const int bolts = 3;
        Assert.Equal(bolts, spread.Count);
        Assert.All(spread, b => Assert.Equal(speed, Math.Sqrt(b.VX * b.VX + b.VY * b.VY), 3));
        var charge = played.Where(p => p.Name == SfxNames.Charge).Select(p => p.Frame).ToList();
        Assert.Contains(charge, c => fire - c is >= blink - blinkLateBy and <= blink + blinkEarlyBy);
        Assert.Equal(rest[fire - beforeTheBlink], ring[fire - beforeTheBlink]);
        Assert.Equal(Palette.Tell, ring[fire - inTheWhite]);
        var blinking = ring.Skip(fire - blink + blinkLateBy).Take(blink * 2 / 3 - blinkLateBy).ToList();   // the first two thirds of the warning
        Assert.Contains(blinking, v => v == Palette.Tell);
        Assert.Contains(blinking, v => v != Palette.Tell);
    }

    [Fact]
    public void Nothing_is_hostile_until_the_blink_ends_and_nothing_burns_until_the_first_warning_has()
    {
        const int quiet = 60;
        var g = Field();
        var m = Settled(g);
        for (int f = 0; f < RestFrames + TellFrames - 1; f++)
        {
            StepLive(g, m);
            if (f < quiet) Assert.Equal(0, HostileBoltCount(g));
            Assert.False(Mirror.Touches(g, m));
        }
        Assert.Equal(Unharmed, Harm(g));
    }

    [Fact]
    public void A_ship_under_the_eye_that_steps_to_the_dull_side_of_the_spread_lives()
    {
        const int watchLimit = 200, aside = 6;
        var g = Field();
        var m = Settled(g);
        for (int f = 0; f < watchLimit && HostileBoltCount(g) == 0; f++) { Steer(g, m.X); StepLive(g, m); }
        Assert.True(HostileBoltCount(g) > 0);
        bool leftTurn = m.Mode is LeftRest or LeftTell or LeftBurn;
        double dull = m.X + (leftTurn ? FacetW : -FacetW);
        while (HostileBoltCount(g) > 0)
        {
            Steer(g, dull);
            StepLive(g, m);
        }
        Assert.True(Math.Abs(g.PX - m.X) >= aside);
        Assert.Equal(Unharmed, Harm(g));
    }

    [Fact]
    public void The_eye_fires_every_seventy_frames_and_every_fifty_once_both_panes_are_broken()
    {
        const int gap = 70, angryGap = 50, watched = 300, bolts = 3;
        var g = Field();
        var m = Settled(g);
        var volleys = new List<int>();
        void Run(int frames)
        {
            for (int f = 0; f < frames; f++)
            {
                g.Bullets.RemoveAll(b => !b.Friendly);   // keep the ship out of it
                Steer(g, m.X);
                StepLive(g, m);
                if (HostileBoltCount(g) == bolts) volleys.Add((int)g.Frame);
            }
        }
        Run(watched);
        Assert.True(volleys.Count >= watched / gap - 1);
        for (int i = 1; i < volleys.Count; i++) Assert.Equal(gap, volleys[i] - volleys[i - 1]);
        Assert.Equal(Unharmed, Harm(g));
        g.Bullets.RemoveAll(b => !b.Friendly);
        StepToMode(g, m, LeftRest);
        BreakAndStep(g, m, Pane(m, true).Concat(Pane(m, false)));
        volleys.Clear();
        Run(watched);
        Assert.True(volleys.Count >= watched / angryGap - 1);
        for (int i = 1; i < volleys.Count; i++) Assert.Equal(angryGap, volleys[i] - volleys[i - 1]);
    }

    // ---------------------------------------------------------------- the voice

    /// <summary>The seed of a fresh post, and the first of the echoes' seeds, far enough above it that the two never meet.</summary>
    private const ulong FreshPostSeed = 7, FirstEchoSeed = 100;

    /// <summary>Gives the run these posts as its echoes, each taken from the store as soon as it lands there.</summary>
    private static void Echo(RaiderGame g, params string[] texts)
    {
        ulong seed = FirstEchoSeed;
        foreach (var t in texts)
        {
            g.Rude.Add(new RudePost(seed++, t, DarkSource.Label, "someone", 0));
            g.Rude.Take(g.Rude.Waiting.Count - 1);
        }
    }

    /// <summary>Long enough for a speech box to be read and the next to come up.</summary>
    private const int SpeechFrames = 600;

    [Fact]
    public void With_an_empty_store_it_says_its_own_line_under_its_name()
    {
        var g = Field();
        var m = g.SpawnKind(EnemyKind.Mirror, 0.5);
        for (int i = 0; i < SpeechFrames && m.Text is null; i++) StepKeepingAndClearingHostileBolts(g, m);
        Assert.Equal("No, you.", m.Text);
        Assert.Equal("MIRROR", m.Header);
        Assert.StartsWith("MIRROR", EnemySpeechBox.Title(m));
    }

    [Fact]
    public void It_says_the_runs_echoes_newest_first_with_RE_before_a_fresh_post()
    {
        var g = Field();
        Echo(g, "older echo", "newest echo");
        g.Rude.Add(new RudePost(FreshPostSeed, "a fresh post", DarkSource.Label, "fresh", 0));
        var m = g.SpawnKind(EnemyKind.Mirror, 0.5);
        for (int i = 0; i < SpeechFrames && m.Text is null; i++) StepKeepingAndClearingHostileBolts(g, m);
        Assert.Equal("newest echo", m.Text);
        Assert.StartsWith("RE:", EnemySpeechBox.Title(m));
        Assert.Single(g.Rude.Waiting);
    }

    [Fact]
    public void A_facet_that_breaks_makes_it_say_the_next_echo()
    {
        var g = Field();
        Echo(g, "one", "two");
        var m = g.SpawnKind(EnemyKind.Mirror, 0.5);
        var seen = new List<string>();
        void Watch(int frames, string until)
        {
            for (int i = 0; i < frames && !seen.Contains(until); i++) { StepKeepingAndClearingHostileBolts(g, m); if (m.Text is { } t && !seen.Contains(t)) seen.Add(t); }
        }
        Watch(SpeechFrames, "two");
        Assert.Equal(["two"], seen);
        for (int i = 0; i < SpeechFrames; i++) StepKeepingAndClearingHostileBolts(g, m);
        StepToMode(g, m, LeftRest);
        BreakAndStep(g, m, [Facet(m, true, 0)]);
        Watch(SpeechFrames, "one");
        Assert.Equal(["two", "one"], seen);
    }

    [Fact]
    public void A_wholly_broken_pane_makes_it_say_the_discourse_line_with_no_echoes_and_both_make_it_say_fine_me_then()
    {
        var g = Field();
        var m = g.SpawnKind(EnemyKind.Mirror, 0.5);
        var seen = new List<string>();
        void Watch(int frames) { for (int i = 0; i < frames; i++) { StepKeepingAndClearingHostileBolts(g, m); if (m.Text is { } t && (seen.Count == 0 || seen[^1] != t)) seen.Add(t); } }
        Watch(SpeechFrames);
        Assert.Equal("No, you.", seen[0]);
        StepToMode(g, m, LeftRest);
        BreakAndStep(g, m, Pane(m, true));
        Watch(SpeechFrames);
        Assert.Contains("You broke the discourse.", seen);
        StepToMode(g, m, LeftRest);
        BreakAndStep(g, m, Pane(m, false));
        Watch(SpeechFrames);
        Assert.Contains("Fine. Me, then.", seen);
    }

    [Fact]
    public void It_says_no_me_as_it_dies()
    {
        const double fatal = 9999;
        var g = Field();
        var m = Settled(g);
        for (int i = 0; i < SpeechFrames; i++) StepKeepingAndClearingHostileBolts(g, m);
        StepToMode(g, m, LeftTell);
        EnemyKit.Shot(g, m.X, m.Y + Eye(m).OY, fatal);
        g.Step();
        Assert.Equal("No. Me.", m.Text);
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_fight()
    {
        const int limit = 4000, watched = 300, place = 9;
        const ulong seed = 11;
        string Run()
        {
            var g = new RaiderGame(seed) { SignedIn = true };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtBoss(place);
            int seen = 0;
            for (int f = 0; f < limit && seen < watched; f++) { EnemyKit.BotFrame(g, DodgeTarget); g.Step(); if (g.Boss is not null) seen++; }
            return string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}";
        }
        string a = Run();
        Assert.Contains("Mirror", a);
        Assert.Equal(a, Run());
    }

    // ---------------------------------------------------------------- the fight

    /// <summary>The bot dodges a bolt within this many pixels above the ship and this many across, ending a step away from it.</summary>
    private const double BoltLookAhead = 40, BoltReach = 9, DodgeStep = 11;

    /// <summary>The bot's way of living: it steps out of the way of the nearest hostile bolt above it, to the dull side while a pane
    /// warns or burns. Where it stands otherwise is the bot's own rule, under the lowest open part: the eye whenever a pane warns or burns.</summary>
    private static double? DodgeTarget(RaiderGame g)
    {
        var hostile = g.Bullets.Where(b => !b.Friendly && b.Y < g.PY && b.Y > g.PY - BoltLookAhead).OrderByDescending(b => b.Y).FirstOrDefault();
        if (hostile is null || Math.Abs(hostile.X - g.PX) >= BoltReach) return null;
        int mode = g.Boss?.Mode ?? Entering;
        if (mode is LeftTell or LeftBurn) return hostile.X + DodgeStep;
        if (mode is RightTell or RightBurn) return hostile.X - DodgeStep;
        return hostile.X + (g.PX >= hostile.X ? DodgeStep : -DodgeStep);
    }

    private static (int Frames, int Deaths) Fight(ulong seed, int level, int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows)
    {
        const int place = 9;
        var g = new RaiderGame(seed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtBoss(place);
        EnemyKit.Clear(g);
        while (g.WeaponLevel < level) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); EnemyKit.Clear(g); }
        Assert.Equal(level, g.WeaponLevel);
        int start = -1;
        for (int f = 0; f < MaxFightFrames; f++)
        {
            if (g.Boss is not null && start < 0) start = f;
            EnemyKit.BotFrame(g, DodgeTarget);
            EnemyKit.StepSweeping(g);
            if (start >= 0 && g.Boss is null) return (f - start, g.Deaths);
        }
        return (-1, g.Deaths);
    }

    [Fact]
    public void A_bot_that_never_stops_firing_wins_inside_the_band_at_weapon_level_three()
    {
        const int level = 3, fastest = 630, slowest = 990, seeds = 6;
        var times = new List<int>();
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            var (t, deaths) = Fight(seed, level);
            output.WriteLine($"seed {seed}: {t} frames, {deaths} deaths");
            Assert.True(t > 0, $"seed {seed}: no win");
            times.Add(t);
        }
        times.Sort();
        double median = (times[seeds / 2 - 1] + times[seeds / 2]) / 2.0;
        output.WriteLine($"mirror fight at level {level}, frames: {string.Join(" ", times)}; median {median} ({median / Tuning.Fps:0.0} s)");
        Assert.InRange(median, fastest, slowest);
    }

    [Theory]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(40, 58)]
    public void The_bot_wins_on_the_other_fields_inside_the_five_minutes_it_stays(int cols, int rows)
    {
        const int level = 3;
        const ulong seed = 3;
        var (t, deaths) = Fight(seed, level, cols, rows);
        output.WriteLine($"{cols}x{rows}: won in {t} frames ({(double)t / Tuning.Fps:0.0} s), {deaths} deaths");
        Assert.InRange(t, 1, MaxFightFrames);
    }

    [Theory]
    [InlineData(120, 46)]
    [InlineData(40, 58)]
    public void The_starting_gun_wins_inside_the_five_minutes_it_stays(int cols, int rows)
    {
        const int startingGun = 1, seeds = 3;
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            var (t, deaths) = Fight(seed, startingGun, cols, rows);
            output.WriteLine($"{cols}x{rows} seed {seed}: the starting gun won in {t} frames ({(double)t / Tuning.Fps:0.0} s), {deaths} deaths");
            Assert.InRange(t, 1, MaxFightFrames);
        }
    }
}
