using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Lobber (stage 5): an old mortar that throws one slow shell where the ship stood and lets it fall 50 frames later. These tests
/// give its numbers, its throw cadence, the one place a shell lands and the one radius it hurts in, the pair a later stage brings,
/// its pixels, its fit on the four fields and its run being the same for the same seed.
/// </summary>
public sealed class LobberTests
{
    private const uint Rust = 0xd9773c, Shade = 0x7a3f1c, White = 0xffffff;
    private const int Flight = 50, Window = 4, Radius = 9;

    /// <summary>The lobber's hit points, score and body size.</summary>
    private const double LobberHp = 4;
    private const int LobberScore = 500, BodyWidth = 9, BodyHeight = 8;

    /// <summary>The shells one lobber throws, the steps between its throws, and the steps between the throws of a pair.</summary>
    private const int Throws = 3, Cadence = 110, PairGap = 55;

    /// <summary>The stage the lobber first comes in, and the stage from which waves bring it as a pair.</summary>
    private const int LobberStage = 5, PairFromStage = 7;

    /// <summary>Steps between the two lobbers of a wave's pair.</summary>
    private const int PairStagger = 12;

    private static EnemyBehaviour Lob => RaiderGame.TableBehaviour(EnemyKind.Lobber)!;

    /// <summary>Decimal places a position or hit points must match to.</summary>
    private const int Precision = 6;

    /// <summary>Where the ship stands: its column, and its height above the bottom of the field.</summary>
    private const double ShipX = 40, ShipRise = 14;

    /// <summary>A column far enough from the ship's spot that a shell landing there misses it.</summary>
    private const double AsideX = 120;

    /// <summary>The column a lone lobber is placed at, and the columns of a pair.</summary>
    private const double LobberX = 100, PairLeftX = 60, PairRightX = 110;

    /// <summary>The row the lobber holds: this share of the field's height down from the top, and never less than the minimum.</summary>
    private const double HoldShare = 0.12, HoldMin = 8;

    /// <summary>The step on which the first shell is thrown, counting from the lobber's placement.</summary>
    private const int ThrowStep = 41;

    /// <summary>Steps long enough for the lobber to throw all its shells and leave.</summary>
    private const int LongRun = 700;

    /// <summary>Frames the dodging ship walks after each throw.</summary>
    private const int DodgeFrames = 16;

    /// <summary>Frames of the seeded run that must bring a lobber.</summary>
    private const int RunFrames = 900;

    /// <summary>The seed both copies of the seeded run use.</summary>
    private const ulong RunSeed = 11;

    /// <summary>The red byte every pixel of the blast's disc exceeds.</summary>
    private const int BlastRedFloor = 0xe0;

    private static double Hold(RaiderGame g) => Math.Max(HoldShare * g.PH, HoldMin);

    private static int Red(uint colour) => (int)(colour >> 16 & 0xff);

    /// <summary>The steps of a run of sounds: the first, then one every <paramref name="gap"/> steps.</summary>
    private static int[] Every(int first, int gap, int count) => Enumerable.Range(0, count).Select(i => first + i * gap).ToArray();

    private static void PlaceShip(RaiderGame g, double x, double y)
    {
        typeof(RaiderGame).GetProperty("PX")!.GetSetMethod(true)!.Invoke(g, [x]);
        typeof(RaiderGame).GetProperty("PY")!.GetSetMethod(true)!.Invoke(g, [y]);
    }

    /// <summary>One step, then the enemies of every kind before the Gnat and every hostile bolt removed; the Lobber and the later kinds stay.</summary>
    private static void StepDroppingKindsBeforeGnat(RaiderGame g)
    {
        g.Step();
        g.Enemies.RemoveAll(e => e.Kind < EnemyKind.Gnat);
        g.Bullets.RemoveAll(b => !b.Friendly);
    }

    /// <summary>A game with the ship below and aside, and the steps that played each sound counted from the first.</summary>
    private sealed class Rig
    {
        public readonly RaiderGame G = EnemyKit.Game();
        public readonly List<(string Name, int Step)> Sounds = [];
        public int Steps;
        /// <summary>When on, the ship walks off after each throw, so no blast hurts it and freezes the game for a hit.</summary>
        public bool Dodge = true;
        private int _walk, _dir = -1;
        public Rig()
        {
            G.Sfx = new SfxBus((n, _, _, _) => Sounds.Add((n, Steps)));
            PlaceShip(G, ShipX, G.PH - ShipRise);
        }
        public Enemy Add(double x)
        {
            var e = EnemyKit.Place(G, EnemyKind.Lobber, x, Hold(G));
            e.VY = 0;
            return e;
        }
        public void Tick(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                if (Dodge)
                {
                    G.Input.Key(GameKey.Left, _walk > 0 && _dir < 0);
                    G.Input.Key(GameKey.Right, _walk > 0 && _dir > 0);
                    if (_walk > 0) _walk--;
                }
                Steps++;
                int lobs = Sounds.Count(x => x.Name == SfxNames.Lob);
                StepDroppingKindsBeforeGnat(G);
                if (Dodge && Sounds.Count(x => x.Name == SfxNames.Lob) > lobs) { _walk = DodgeFrames; _dir = -_dir; }
            }
        }
        public int[] At(string name) => Sounds.Where(s => s.Name == name).Select(s => s.Step).ToArray();
        public string[] Alternation() => Sounds.Where(s => s.Name is SfxNames.Lob or SfxNames.Blast).Select(s => s.Name).ToArray();
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal("LOBBER", Lob.Name);
        Assert.Equal(LobberHp, Lob.Hp);
        Assert.Equal(LobberScore, Lob.Score);
        Assert.False(Lob.Boss);
        Assert.False(Lob.Speaks);
        Assert.Equal(BodyWidth, Lob.Body.W);
        Assert.Equal(BodyHeight, Lob.Body.H);
        Assert.Contains(EnemyKind.Lobber, Ramp.Unlocks(LobberStage));
        Assert.Equal(LobberStage, Ramp.UnlockStage(EnemyKind.Lobber));
    }

    /// <summary>The seed of the game whose new lobber's hit points are checked.</summary>
    private const ulong RampSeed = 3;

    [Fact]
    public void Its_hp_follows_the_ramp_of_the_stage()
    {
        var g = new RaiderGame(RampSeed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtStage(LobberStage);
        var e = g.SpawnKind(EnemyKind.Lobber, EnemyKit.MidWidth);
        Assert.Equal(LobberHp * Ramp.HpMul(LobberStage), e.Hp, Precision);
    }

    // ---------------------------------------------------------------- the throw

    [Fact]
    public void The_first_shell_goes_40_frames_after_it_settles_then_every_110_and_the_third_is_the_last()
    {
        var r = new Rig();
        var e = r.Add(LobberX);
        r.Tick(LongRun);
        Assert.Equal(Every(ThrowStep, Cadence, Throws), r.At(SfxNames.Lob));
        Assert.Equal(Every(ThrowStep + Flight, Cadence, Throws), r.At(SfxNames.Blast));
        Assert.True(e.Leaving || !r.G.Enemies.Contains(e), "after the third shell it goes");
    }

    /// <summary>The most steps a new lobber may take to reach its row, and the steps it is then watched for not moving.</summary>
    private const int MaxSettleSteps = 400, HoldWatchSteps = 20;

    [Fact]
    public void It_comes_down_from_the_top_at_a_walk_and_settles_on_its_row()
    {
        var g = EnemyKit.Game();
        var e = g.SpawnKind(EnemyKind.Lobber, EnemyKit.MidWidth);
        Assert.Equal(-e.Sprite.H, e.Y, Precision);
        for (int i = 0; i < MaxSettleSteps && e.Y < Hold(g); i++) StepDroppingKindsBeforeGnat(g);
        Assert.Equal(Hold(g), e.Y, Precision);
        double before = e.Y;
        for (int i = 0; i < HoldWatchSteps; i++) StepDroppingKindsBeforeGnat(g);
        Assert.Equal(before, e.Y, Precision);
    }

    [Fact]
    public void The_shell_lands_where_the_ship_was_at_the_throw_not_where_it_is_after()
    {
        Assert.True(BlastTouches(0, 0, Flight), "the ship back on the spot is under it");
        Assert.False(BlastTouches(0, 0, Flight - 1), "a frame early is no blast");
        Assert.False(BlastTouches(FarShift, 0, Flight), "where the ship is now is not where it lands");
    }

    /// <summary>A shift of the ship from the throw spot far past the blast's radius.</summary>
    private const double FarShift = 60;

    /// <summary>
    /// Whether the blast touches a ship that stands aside while the shell flies and steps onto the throw spot, shifted by
    /// (<paramref name="dx"/>, <paramref name="dy"/>), on the <paramref name="count"/>th step after the throw. Each call plays its own
    /// game, since a hurt ship would freeze it.
    /// </summary>
    private static bool BlastTouches(double dx, double dy, int count)
    {
        var r = new Rig { Dodge = false };
        var e = r.Add(LobberX);
        r.Tick(ThrowStep);
        double sx = r.G.PX, sy = r.G.PY;
        PlaceShip(r.G, AsideX, sy);
        r.Tick(count - 1);
        PlaceShip(r.G, sx + dx, sy + dy);
        r.Tick();
        return Lob.Touches(r.G, e);
    }

    /// <summary>Steps after the throw searched for the lethal frames: well past the landing and the blast.</summary>
    private const int LethalSearchSteps = 70;

    /// <summary>Diagonal shifts just inside and just outside the blast's radius, and straight shifts just and well outside it.</summary>
    private const double InsideDiagonal = 6, OutsideDiagonal = 7, JustOutside = 9.5, WellOutside = 12;

    [Fact]
    public void The_blast_is_lethal_inside_radius_9_and_safe_outside_it_and_lasts_four_frames()
    {
        var lethal = Enumerable.Range(1, LethalSearchSteps).Where(c => BlastTouches(0, 0, c)).ToArray();
        Assert.Equal(Enumerable.Range(Flight, Window), lethal);

        foreach (var (dx, dy, want) in new[]
                 {
                     (Radius, 0.0, true), (0.0, Radius, true), (InsideDiagonal, InsideDiagonal, true), (JustOutside, 0.0, false),
                     (OutsideDiagonal, OutsideDiagonal, false), (0.0, -WellOutside, false), (WellOutside, 0.0, false),
                 })
            Assert.Equal(want, BlastTouches(dx, dy, Flight));
    }

    /// <summary>Steps before the landing the walking ship sets off, and steps after the landing the hurt is counted for.</summary>
    private const int WalkLead = 8, WatchAfterLanding = 8;

    [Fact]
    public void A_ship_that_stands_still_is_hurt_and_one_that_walks_eight_frames_clear_is_not()
    {
        int HpLost(bool walks)
        {
            var r = new Rig { Dodge = false };
            r.Add(LobberX);
            r.Tick(ThrowStep);
            int hp = r.G.Hp + r.G.Shield;   // the shield takes the first hit
            for (int c = 1; c <= Flight + WatchAfterLanding; c++)
            {
                r.G.Input.Key(GameKey.Left, walks && c > Flight - WalkLead);
                r.Tick();
            }
            r.G.Input.Key(GameKey.Left, false);
            return hp - (r.G.Hp + r.G.Shield);
        }
        Assert.True(HpLost(false) > 0, "standing still under the shell hurts");
        Assert.Equal(0, HpLost(true));
    }

    [Fact]
    public void A_shell_in_flight_hurts_nothing()
    {
        var r = new Rig { Dodge = false };
        var e = r.Add(LobberX);
        r.Tick(ThrowStep);
        for (int count = 1; count < Flight; count++)
        {
            r.Tick();
            Assert.False(Lob.Touches(r.G, e), $"count {count}");
        }
    }

    /// <summary>Steps that take in the first throw and its blast and end before the second throw.</summary>
    private const int FirstShellSteps = 100;

    [Fact]
    public void It_sounds_the_lob_at_the_throw_and_the_blast_at_the_landing()
    {
        var r = new Rig();
        r.Add(LobberX);
        r.Tick(FirstShellSteps);
        Assert.Equal([ThrowStep], r.At(SfxNames.Lob));
        Assert.Equal([ThrowStep + Flight], r.At(SfxNames.Blast));
    }

    /// <summary>Shell ages at which the ring is still rust, and inside its last 16 frames, when it shows white.</summary>
    private const int EarlyRingAge = 20, LateRingAge = 40;

    /// <summary>Shot damage beyond the lobber's hit points, so one shot kills it.</summary>
    private const double KillingDamage = 99;

    /// <summary>Steps that would carry a live shell past its landing.</summary>
    private const int PastLandingSteps = 80;

    [Fact]
    public void Killing_the_lobber_takes_its_shell_with_it()
    {
        var r = new Rig();
        var e = r.Add(LobberX);
        r.Tick(ThrowStep);
        int sx = (int)Math.Round(r.G.PX), sy = (int)Math.Round(r.G.PY);
        r.Tick(EarlyRingAge);
        Assert.Equal(Rust, EnemyKit.ClearPickupsAndDraw(r.G).Get(sx + Radius, sy));
        EnemyKit.Shot(r.G, e.X, e.Y, KillingDamage);
        r.Tick();
        Assert.DoesNotContain(e, r.G.Enemies);
        Assert.NotEqual(Rust, EnemyKit.ClearPickupsAndDraw(r.G).Get(sx + Radius, sy));
        r.Tick(PastLandingSteps);
        Assert.Empty(r.At(SfxNames.Blast));
    }

    // ---------------------------------------------------------------- one at a time, and a pair

    [Fact]
    public void Two_lobbers_never_have_two_shells_in_the_air_and_the_pair_throws_55_frames_apart()
    {
        var r = new Rig();
        r.Add(PairLeftX);
        r.Add(PairRightX);
        r.Tick(LongRun);
        Assert.Equal(Every(ThrowStep, PairGap, 2 * Throws), r.At(SfxNames.Lob));
        var seq = r.Alternation();
        for (int i = 0; i < seq.Length; i++) Assert.Equal(i % 2 == 0 ? SfxNames.Lob : SfxNames.Blast, seq[i]);
    }

    /// <summary>Steps the first lobber plays alone before the second comes, and the fewest lob and blast sounds the two must make.</summary>
    private const int LateArrivalSteps = 30, MinPairSounds = 8;

    [Fact]
    public void A_second_lobber_that_arrives_later_keeps_the_one_shell_at_a_time_too()
    {
        var r = new Rig();
        r.Add(PairLeftX);
        r.Tick(LateArrivalSteps);
        r.Add(PairRightX);
        r.Tick(LongRun);
        var seq = r.Alternation();
        Assert.True(seq.Length >= MinPairSounds, string.Join(",", seq));
        for (int i = 0; i < seq.Length; i++) Assert.Equal(i % 2 == 0 ? SfxNames.Lob : SfxNames.Blast, seq[i]);
    }

    /// <summary>Seeds tried for each stage's waves, and waves tried for each seed.</summary>
    private const int WaveSeeds = 60, WavesPerSeed = 4;

    /// <summary>The widest gap two equal phases may show after the sum.</summary>
    private const double PhaseTolerance = 1e-9;

    [Fact]
    public void Waves_bring_one_lobber_at_a_time_in_stages_5_and_6_and_a_pair_from_stage_7()
    {
        static IEnumerable<EnemySpawn[]> Waves(int stage) =>
            Enumerable.Range(1, WaveSeeds).SelectMany(s => Enumerable.Range(0, WavesPerSeed).Select(w =>
                WaveSpawner.Filler((ulong)s, stage, w).Enemies.Where(x => x.Kind == EnemyKind.Lobber).ToArray()));
        static bool HasPair(EnemySpawn[] l) =>
            l.Any(a => l.Any(b => b.Delay - a.Delay == PairStagger && Math.Abs(b.Phase - a.Phase - 1) < PhaseTolerance));

        for (int stage = LobberStage; stage < PairFromStage; stage++)
        {
            var any = Waves(stage).Where(l => l.Length > 0).ToArray();
            if (stage == LobberStage) Assert.NotEmpty(any);
            Assert.All(any, l => Assert.False(HasPair(l), $"stage {stage}"));
        }
        var paired = Waves(PairFromStage).Where(l => l.Length > 0).ToArray();
        Assert.NotEmpty(paired);
        Assert.Contains(paired, HasPair);
    }

    // ---------------------------------------------------------------- pixels

    /// <summary>A point inside the ring, right of and above the landing spot, whose fill grows redder as the shell falls.</summary>
    private const int FillDX = 6, FillDY = 3;

    /// <summary>How much redder that point must grow between the early and the late look.</summary>
    private const int FillRedRise = 20;

    /// <summary>The fill point's shift right of the landing spot: <see cref="FillDX"/>, or a column nearer the spot if a star crosses
    /// the sky under it between the early look and the late one, so the change measured is the ring's and not the star's.</summary>
    private static int StarFreeFillDX(RaiderGame game, double earlyScroll, int sx, int sy)
    {
        var sky = new Background(game.PW, game.PH, RaiderView.DefaultBackgroundSeed);
        var (early, late) = (new Surface(game.PW, game.PH), new Surface(game.PW, game.PH));
        sky.Draw(early, earlyScroll);
        sky.Draw(late, game.ScrollFrames);
        for (int dx = FillDX; dx > 1; dx--)
            if (early.Get(sx + dx, sy - FillDY) == late.Get(sx + dx, sy - FillDY)) return dx;
        Assert.Fail("a star crosses every column of the fill");
        return FillDX;
    }

    [Fact]
    public void The_landing_is_shown_by_a_rust_ring_that_fills_and_turns_white_for_the_last_16_frames()
    {
        var r = new Rig();
        r.Add(LobberX);
        r.Tick(ThrowStep);
        int sx = (int)Math.Round(r.G.PX), sy = (int)Math.Round(r.G.PY);
        r.Tick(EarlyRingAge);
        double earlyScroll = r.G.ScrollFrames;
        var early = EnemyKit.ClearPickupsAndDraw(r.G);
        Assert.Equal(Rust, early.Get(sx + Radius, sy));
        Assert.Equal(Rust, early.Get(sx - Radius, sy));
        r.Tick(LateRingAge - EarlyRingAge);
        var late = EnemyKit.ClearPickupsAndDraw(r.G);
        Assert.Equal(White, late.Get(sx + Radius, sy));
        Assert.Equal(White, late.Get(sx - Radius, sy));
        int fx = sx + StarFreeFillDX(r.G, earlyScroll, sx, sy);
        int redEarly = Red(early.Get(fx, sy - FillDY));
        int redLate = Red(late.Get(fx, sy - FillDY));
        Assert.True(redLate > redEarly + FillRedRise, $"{redEarly} -> {redLate}");
    }

    /// <summary>A point inside the blast near its edge, given as a shift from the landing spot; its mirror across the spot is checked against it.</summary>
    private const int BlastDX = 8, BlastDY = 2;

    [Fact]
    public void The_blast_fills_its_whole_circle_for_its_four_frames_and_then_is_gone()
    {
        var r = new Rig();
        r.Add(LobberX);
        r.Tick(ThrowStep);
        int sx = (int)Math.Round(r.G.PX), sy = (int)Math.Round(r.G.PY);
        r.Tick(Flight);
        for (int i = 0; i < Window; i++)
        {
            var s = EnemyKit.ClearPickupsAndDraw(r.G);
            Assert.Equal(s.Get(sx + BlastDX, sy - BlastDY), s.Get(sx - BlastDX, sy - BlastDY));
            Assert.NotEqual(Rust, s.Get(sx + BlastDX, sy - BlastDY));
            Assert.True(Red(s.Get(sx + BlastDX, sy - BlastDY)) > BlastRedFloor, $"frame {i}");
            r.Tick();
        }
        var after = EnemyKit.ClearPickupsAndDraw(r.G);
        Assert.NotEqual(Rust, after.Get(sx + Radius, sy));
        Assert.NotEqual(White, after.Get(sx + Radius, sy));
    }

    /// <summary>Where the lobber and the ship stand while the body is drawn.</summary>
    private const double BodyLobberX = 60, BodyShipX = 20;

    /// <summary>The body's eye row in its drawing, and on it the columns of the rim's shade, the rust beside the eye, and the eye.</summary>
    private const int EyeRow = 3, RimCol = 1, RustCol = 2, EyeCol = 4;

    [Fact]
    public void The_body_is_rust_with_a_gold_eye()
    {
        var g = EnemyKit.Game();
        var e = EnemyKit.Place(g, EnemyKind.Lobber, BodyLobberX, Hold(g));
        e.VY = 0;
        PlaceShip(g, BodyShipX, g.PH - ShipRise);
        StepDroppingKindsBeforeGnat(g);
        var s = EnemyKit.ClearPickupsAndDraw(g);
        int x0 = (int)Math.Round(e.X - e.Sprite.W / 2.0), y0 = (int)Math.Round(e.Y - e.Sprite.H / 2.0);
        Assert.Equal(Rust, s.Get(x0 + RustCol, y0 + EyeRow));
        Assert.Equal(Shade, s.Get(x0 + RimCol, y0 + EyeRow));
        Assert.Equal(Palette.Gold, s.Get(x0 + EyeCol, y0 + EyeRow));
    }

    // ---------------------------------------------------------------- sizes and the run

    /// <summary>Steps each new lobber is watched for, long enough to come down and settle.</summary>
    private const int FitSteps = 150;

    [Fact]
    public void It_stays_inside_the_field_and_above_the_floor_on_all_four_sizes()
    {
        foreach (var (cols, rows) in EnemyKit.FieldSizes)
        {
            var g = EnemyKit.Game(cols, rows);
            foreach (double fx in new[] { 0.0, EnemyKit.MidWidth, 1.0 })
            {
                var e = g.SpawnKind(EnemyKind.Lobber, fx);
                for (int f = 0; f < FitSteps; f++)
                {
                    StepDroppingKindsBeforeGnat(g);
                    Assert.True(EnemyKit.InsideWidth(g, e), $"{cols}x{rows} at {fx}: width");
                }
                double floor = EnemyKit.BossFloor(g.PH);
                Assert.True(EnemyKit.LowestPixel(e) <= floor, $"{cols}x{rows}: {EnemyKit.LowestPixel(e)} > {floor}");
                Assert.True(EnemyKit.Extent(e).T >= 0, $"{cols}x{rows}: top clipped");
                g.Enemies.Clear();
            }
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_run_with_lobbers_in_it()
    {
        (string, bool) Run(ulong seed)
        {
            var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtStage(LobberStage);
            bool seen = false;
            for (int f = 0; f < RunFrames; f++)
            {
                EnemyKit.BotFrame(g);
                g.Step();
                seen |= g.Enemies.Any(e => e.Kind == EnemyKind.Lobber);
            }
            var state = string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}";
            return (state, seen);
        }
        var (a, seenA) = Run(RunSeed);
        var (b, _) = Run(RunSeed);
        Assert.Equal(a, b);
        Assert.True(seenA, $"stage {LobberStage} brings a lobber within {RunFrames} frames");
    }
}
