using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Chorus (stage 8's mini-boss): a row of mouths that all fire on one frame. The number of voices follows the field's width, a
/// dead voice leaves a gap the ship fits through, and every volley is announced for 36 frames before it can hurt.
/// </summary>
public sealed class ChorusTests(ITestOutputHelper output)
{
    private static EnemyBehaviour Chorus => RaiderGame.TableBehaviour(EnemyKind.Chorus)!;

    public static IEnumerable<object[]> Fields() => EnemyKit.FieldSizes.Select(f => new object[] { f.Cols, f.Rows });

    /// <summary>One voice for each this many pixels of field width, kept between the fewest and the most the chorus has.</summary>
    private const int PixelsPerVoice = 16, FewestVoices = 3, MostVoices = 6;

    /// <summary>The row it holds is this share of the field's height, and never above this row.</summary>
    private const double HoldShare = 0.12, HoldTopRow = 8;

    /// <summary>Its colour and score in the enemy table, and the table's hit points, which every voice gets.</summary>
    private const uint Sea = 0x4fbf8f;
    private const int ChorusScore = 500, HpPerVoice = 3;

    /// <summary>A voice's drawing size, the distance from one voice's centre to the next, and what killing one scores.</summary>
    private const int VoiceW = 7, VoiceH = 6, VoiceSpacing = 12, VoiceScore = 150;

    /// <summary>A volley every this many frames once it holds; the last frames of each period are the charge, the last few of those white.</summary>
    private const int VolleyPeriod = 120, ChargeFrames = 36, WhiteFrames = 12, WaitFrames = VolleyPeriod - ChargeFrames;

    /// <summary>The step <see cref="Held"/> takes is the period's first frame, so the first volley comes one frame short of a whole
    /// period after it; it may come up to two frames late.</summary>
    private const int FirstVolleyFrom = VolleyPeriod - 1, FirstVolleyBy = VolleyPeriod + 2;

    /// <summary>The last frame after <see cref="Held"/> on which nothing has been fired.</summary>
    private const int LastQuietFrame = FirstVolleyFrom - 1;

    /// <summary>How far the measured charge may be from <see cref="ChargeFrames"/>.</summary>
    private const int ChargeSlack = 2;

    /// <summary>The volleys it sings before it leaves.</summary>
    private const int Volleys = 3;

    /// <summary>Each living voice drops this many bolts, one down its middle and one this far to either side.</summary>
    private const int BoltsPerVoice = 3, Lane = 4;

    /// <summary>The speed its bolts fall at.</summary>
    private const double BoltSpeed = 0.8;

    /// <summary>How far a frame it drifts toward the ship's column.</summary>
    private const double DriftSpeed = 0.2;

    /// <summary>The dotted column is read from this many rows below the middle of a mouth, for this many rows.</summary>
    private const int BelowMouth = 4, ColumnRows = 12;

    /// <summary>A row above the top of the field, where a chorus is put to come in from.</summary>
    private const double AboveField = -10;

    /// <summary>A shot far heavier than a voice's hit points.</summary>
    private const double KillingShot = 100;

    /// <summary>Decimal places two positions must agree to, and the rounding a sum of positions may carry.</summary>
    private const int Places = 6;
    private const double Rounding = 1e-6;

    /// <summary>Frames a wait may take before the test gives up on the state it waits for.</summary>
    private const int MaxWaitFrames = 300;

    /// <summary>The stage whose unlocks bring the chorus.</summary>
    private const int ChorusStage = 8;

    private static int VoicesOn(RaiderGame g) => Math.Clamp(g.PW / PixelsPerVoice, FewestVoices, MostVoices);

    private static double HoldRow(RaiderGame g) => Math.Round(Math.Max(HoldShare * g.PH, HoldTopRow));

    private static List<Part> Voices(Enemy e) => e.Parts!.Where(p => p.Name.StartsWith("voice-")).ToList();

    /// <summary>Puts a chorus on its row at once, as it stands after entering, and keeps the filler wave off the field.</summary>
    private static Enemy Held(RaiderGame g)
    {
        var e = EnemyKit.Place(g, EnemyKind.Chorus, g.PW / 2.0, HoldRow(g));
        EnemyKit.StepSweeping(g);
        g.Enemies.RemoveAll(x => x != e);
        return e;
    }

    private static void StepOnly(RaiderGame g, Enemy e, int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            g.Step();
            g.Enemies.RemoveAll(x => x != e);
        }
    }

    private static List<Bullet> Hostile(RaiderGame g) => g.Bullets.Where(b => !b.Friendly).ToList();

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void The_chorus_is_a_mini_boss_with_its_numbers()
    {
        Assert.False(Chorus.Boss);
        Assert.Equal("CHORUS", Chorus.Name);
        Assert.Equal(Sea, Chorus.Colour);
        Assert.Equal(ChorusScore, Chorus.Score);
        Assert.Equal(HpPerVoice, Chorus.Hp);
        Assert.True(Chorus.Speaks);
        Assert.True(Chorus.Name.Length <= EnemySpeechBox.MaxLabel);
    }

    // ---------------------------------------------------------------- the row of voices

    [Theory, MemberData(nameof(Fields))]
    public void The_number_of_voices_follows_the_width_of_the_field(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var e = Held(g);
        var voices = Voices(e);
        Assert.Equal(VoicesOn(g), voices.Count);
        Assert.Equal(voices.Count, e.Parts!.Count);
        Assert.Equal(g.PW >= MostVoices * PixelsPerVoice ? MostVoices : FewestVoices, voices.Count);
        for (int i = 0; i < voices.Count; i++)
        {
            var v = voices[i];
            Assert.Equal($"voice-{i}", v.Name);
            Assert.Equal((VoiceW, VoiceH), (v.Closed.W, v.Closed.H));
            Assert.Equal(PartState.Open, v.State);
            Assert.True(v.Vital);
            Assert.Null(v.Wreck);
            Assert.Equal(VoiceScore, v.Score);
            Assert.Equal(e.Hp / voices.Count, v.Hp, Places);
            if (i > 0) Assert.Equal(VoiceSpacing, v.OX - voices[i - 1].OX, Places);
        }
        double width = (voices.Count - 1) * VoiceSpacing + VoiceW;
        var x = EnemyKit.Extent(e);
        Assert.Equal(width, x.R - x.L, Places);
        Assert.Equal(voices.Count * HpPerVoice, e.Hp / Ramp.HpMul(g.Stage), Places);
        output.WriteLine($"{cols}x{rows}: {voices.Count} voices, {width:0} px wide, row {e.Y:0}");
    }

    [Theory, MemberData(nameof(Fields))]
    public void It_holds_at_twelve_hundredths_of_the_height_never_above_row_eight_and_stays_inside_the_field(int cols, int rows)
    {
        const double FarLeft = 2;
        const int BeforeThirdVolley = 330;   // after the third volley the chorus leaves
        var g = EnemyKit.Game(cols, rows);
        var e = EnemyKit.Place(g, EnemyKind.Chorus, FarLeft, AboveField);
        for (int f = 0; f < BeforeThirdVolley; f++)
        {
            StepOnly(g, e);
            Assert.True(EnemyKit.InsideWidth(g, e), $"frame {f}: x {e.X}");
        }
        Assert.Equal(HoldRow(g), e.Y, Places);
        Assert.True(e.Y >= HoldTopRow);
        var x = EnemyKit.Extent(e);
        Assert.True(x.B <= EnemyKit.BossFloor(g.PH), "the row stays well above the floor");
        Assert.True(Voices(e).Count > 0);
    }

    [Fact]
    public void It_drifts_toward_the_ships_column_at_a_fifth_of_a_pixel_a_frame_and_the_nearest_voice_aims_at_it()
    {
        const int FirstWatch = 10, SteadyFrames = 30, CatchUpFrames = 40;
        const double FarFromShip = 40;
        var g = EnemyKit.Game();
        var e = Held(g);
        double x0 = e.X;
        StepOnly(g, e, FirstWatch);
        Assert.True(Math.Abs(e.X - x0) <= DriftSpeed * FirstWatch + Rounding);
        e.X = FarFromShip; e.BaseX = FarFromShip;
        double last = e.X;
        for (int f = 0; f < SteadyFrames; f++)
        {
            StepOnly(g, e);
            Assert.InRange(e.X - last, -Rounding, DriftSpeed + Rounding);
            last = e.X;
        }
        StepOnly(g, e, CatchUpFrames);
        double near = Voices(e).Min(v => Math.Abs(e.X + v.OX - g.PX));
        Assert.True(near < 1, $"the nearest voice is {near} px from the ship");
    }

    // ---------------------------------------------------------------- the volley

    [Fact]
    public void A_volley_comes_after_eighty_four_frames_of_waiting_and_thirty_six_of_charging_from_every_living_voice_on_one_frame()
    {
        const int MaxFrames = 200;
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        var e = Held(g);
        var voices = Voices(e);
        int firstOpen = -1, volley = -1;
        for (int f = 1; f < MaxFrames && volley < 0; f++)
        {
            StepOnly(g, e);
            if (firstOpen < 0 && voices.Any(v => v.OpenArt is not null)) firstOpen = f;
            if (Hostile(g).Count > 0) volley = f;
        }
        Assert.InRange(volley, FirstVolleyFrom, FirstVolleyBy);
        Assert.InRange(volley - firstOpen, ChargeFrames - ChargeSlack, ChargeFrames + ChargeSlack);
        var bolts = Hostile(g);
        Assert.Equal(voices.Count * BoltsPerVoice, bolts.Count);
        Assert.All(bolts, b => { Assert.Equal(0, b.VX, Places); Assert.Equal(BoltSpeed, b.VY, Places); });
        Assert.Single(bolts.Select(b => Math.Round(b.Y, Places)).Distinct());
        foreach (var v in voices)
        {
            double x = e.X + v.OX;
            Assert.Equal(BoltsPerVoice, bolts.Count(b => Math.Abs(b.X - x) <= Lane + Rounding));
            Assert.Contains(bolts, b => Math.Abs(b.X - (x - Lane)) < Rounding);
            Assert.Contains(bolts, b => Math.Abs(b.X - x) < Rounding);
            Assert.Contains(bolts, b => Math.Abs(b.X - (x + Lane)) < Rounding);
        }
        Assert.Contains(SfxNames.Charge, played);
        Assert.Contains(SfxNames.Sting, played);
    }

    [Fact]
    public void Nothing_can_hurt_before_the_volley_and_the_charge_is_drawn_below_every_living_mouth_white_for_the_last_twelve_frames()
    {
        // most of the frames each colour is shown on (23 tinted, 12 white), with a margin
        const int TintFramesAtLeast = 18, WhiteFramesAtLeast = 9;
        const int BeforeTheCharge = 40;
        var g = EnemyKit.Game();
        var e = Held(g);
        var voices = Voices(e);
        int white = 0, tint = 0;
        for (int f = 1; f <= LastQuietFrame; f++)
        {
            StepOnly(g, e);
            Assert.Empty(Hostile(g));
            if (f < WaitFrames) continue;
            int x = (int)Math.Round(e.X + voices[0].OX), y0 = (int)Math.Round(e.Y + VoiceH / 2.0) + BelowMouth;
            var s = EnemyKit.ClearPickupsAndDraw(g);
            var col = new List<uint>();
            for (int dy = 0; dy < ColumnRows; dy++) col.Add(s.Get(x, y0 + dy));
            if (f > LastQuietFrame - WhiteFrames) white += col.Any(c => c == Palette.White) ? 1 : 0;
            else tint += col.Any(c => c == Chorus.Colour) ? 1 : 0;
        }
        Assert.True(tint >= TintFramesAtLeast, $"{tint} frames with the dotted column in the tell colour");
        Assert.True(white >= WhiteFramesAtLeast, $"{white} white frames");
        var g2 = EnemyKit.Game();
        var e2 = Held(g2);
        StepOnly(g2, e2, BeforeTheCharge);
        var s2 = EnemyKit.ClearPickupsAndDraw(g2);
        int x2 = (int)Math.Round(e2.X + Voices(e2)[0].OX), y2 = (int)Math.Round(e2.Y + VoiceH / 2.0) + BelowMouth;
        for (int dy = 0; dy < ColumnRows; dy++) Assert.NotEqual(Chorus.Colour, s2.Get(x2, y2 + dy));
    }

    [Fact]
    public void A_dead_voice_fires_nothing_and_leaves_a_gap_the_ship_fits_through_while_the_row_without_one_has_none()
    {
        const int PastFirstVolley = 125, MiddleVoice = 2;
        const double SameColumn = 0.01;
        var g = EnemyKit.Game();
        var e = Held(g);
        // the row whole: the nearest bolts of neighbours are four pixels apart, narrower than the ship
        StepOnly(g, e, PastFirstVolley);
        var xs = Hostile(g).Select(b => b.X).OrderBy(x => x).ToList();
        double narrowest = xs.Zip(xs.Skip(1), (a, b) => b - a).Where(d => d > SameColumn).Min();
        Assert.True(narrowest < g.Ship.W, $"the narrowest gap {narrowest} is under the ship's width {g.Ship.W}");
        var g2 = EnemyKit.Game();
        var e2 = Held(g2);
        var v2 = Voices(e2);
        var dead = v2[MiddleVoice];
        dead.State = PartState.Dead; dead.Hp = 0;
        StepOnly(g2, e2, PastFirstVolley);
        var bolts = Hostile(g2);
        Assert.Equal((v2.Count - 1) * BoltsPerVoice, bolts.Count);
        double deadX = e2.X + dead.OX;
        Assert.DoesNotContain(bolts, b => Math.Abs(b.X - deadX) < Lane + 1);
        var xs2 = bolts.Select(b => b.X).OrderBy(x => x).ToList();
        var gaps = xs2.Zip(xs2.Skip(1), (a, b) => b - a).Max();
        Assert.True(gaps >= g2.Ship.W, $"the gap {gaps} fits a ship {g2.Ship.W} wide");
    }

    [Fact]
    public void It_leaves_after_three_volleys_and_the_field_is_clear_of_it()
    {
        const int MaxFrames = 900;
        const int SameVolleyWithin = 20;   // bolts this close in time are one volley; volleys are a period apart
        var g = EnemyKit.Game();
        var e = Held(g);
        int volleys = 0, last = 0;
        var seen = new HashSet<Bullet>();
        for (int f = 1; f < MaxFrames && g.Enemies.Contains(e); f++)
        {
            StepOnly(g, e);
            var fresh = Hostile(g).Where(seen.Add).ToList();
            if (fresh.Count > 0 && f - last > SameVolleyWithin) { volleys++; last = f; }
            g.Bullets.RemoveAll(b => !b.Friendly);   // a ship that never meets one: only the count of volleys matters
        }
        Assert.Equal(Volleys, volleys);
        Assert.DoesNotContain(e, g.Enemies);
    }

    // ---------------------------------------------------------------- hits and score

    [Fact]
    public void Each_voice_scores_a_hundred_and_fifty_with_the_part_sound_and_the_last_ends_the_chorus_for_five_hundred_more()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        var e = Held(g);
        var voices = Voices(e);
        int score = g.Score;
        for (int i = 0; i < voices.Count - 1; i++)
        {
            EnemyKit.Shot(g, e.X + voices[i].OX, e.Y + voices[i].OY, KillingShot);
            StepOnly(g, e);
            Assert.Equal(PartState.Dead, voices[i].State);
            Assert.Equal(score + VoiceScore * (i + 1), g.Score);
            Assert.Contains(e, g.Enemies);
        }
        Assert.Contains(SfxNames.Part, played);
        var last = voices[^1];
        EnemyKit.Shot(g, e.X + last.OX, e.Y + last.OY, KillingShot);
        StepOnly(g, e);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal(score + VoiceScore * voices.Count + ChorusScore, g.Score);
        Assert.Contains(SfxNames.Kill, played);   // a mini-boss ends with the kill sound; only a boss has the boom
        Assert.DoesNotContain(SfxNames.Boom, played);
    }

    [Fact]
    public void A_shot_between_two_voices_passes_through_the_five_pixel_gap()
    {
        var g = EnemyKit.Game();
        var e = Held(g);
        var voices = Voices(e);
        double x = e.X + (voices[0].OX + voices[1].OX) / 2;
        EnemyKit.Shot(g, x, e.Y, KillingShot);
        StepOnly(g, e);
        Assert.All(voices, v => Assert.Equal(PartState.Open, v.State));
        Assert.Contains(g.Bullets, b => b.Friendly);
    }

    // ---------------------------------------------------------------- the voice

    [Fact]
    public void It_says_a_waiting_post_else_all_together_now()
    {
        const ulong AnyPostSeed = 1;
        var g = EnemyKit.Game();
        g.OfferDarkness(new DarknessPost(DarknessKind.Rude, AnyPostSeed, "sing it back"));
        var e = EnemyKit.Place(g, EnemyKind.Chorus, g.PW / 2.0, AboveField);
        for (int f = 0; f < MaxWaitFrames && e.Text is null; f++) StepOnly(g, e);
        Assert.Equal("sing it back", e.Text);
        Assert.True(EnemySpeechBox.Speaks(e));

        var g2 = EnemyKit.Game();
        var e2 = EnemyKit.Place(g2, EnemyKind.Chorus, g2.PW / 2.0, AboveField);
        for (int f = 0; f < MaxWaitFrames && e2.Text is null; f++) StepOnly(g2, e2);
        Assert.Equal("All together now.", e2.Text);
        Assert.Equal("CHORUS", e2.Header);
        Assert.Equal("All together now.", Chorus.Line(EnemyKit.Speaker(EnemyKind.Chorus), VoiceCue.Arrive));
    }

    // ---------------------------------------------------------------- same seed, same run; the ship

    [Fact]
    public void The_same_seed_gives_the_same_chorus_run()
    {
        const int RunCols = 80, RunRows = 24, RunFrames = 300, SampleEvery = 25;
        const ulong RunSeed = 9;
        string Run()
        {
            var g = EnemyKit.Game(RunCols, RunRows, RunSeed);
            var e = Held(g);
            var sb = new System.Text.StringBuilder();
            for (int f = 0; f < RunFrames; f++)
            {
                StepOnly(g, e);
                if (f % SampleEvery == 0) sb.Append($"{e.X:0.000}/{Hostile(g).Count}/{e.Hp:0.0};");
            }
            return sb.ToString();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void A_ship_that_never_moves_is_hit_by_the_volley_that_follows_it()
    {
        const int MaxFrames = 500;
        var g = EnemyKit.Game();
        var e = Held(g);
        for (int f = 0; f < MaxFrames && g.Deaths == 0; f++) StepOnly(g, e);
        Assert.True(g.Deaths > 0, "the nearest voice aims at the ship, so a ship that stays is hit");
    }

    [Fact]
    public void The_stage_eight_unlock_table_can_bring_it_and_it_gets_a_sting()
    {
        Assert.Contains(EnemyKind.Chorus, Ramp.Unlocks(ChorusStage));
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        g.SpawnKind(EnemyKind.Chorus, EnemyKit.MidWidth);
        Assert.Contains(SfxNames.Sting, played);
    }
}
