using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

/// <summary>The stage banner, the hints, the boss warning and the pop-ups each read whole wherever they land: a banner is one solid block that no post card shows through, and no two pop-ups share a cell.</summary>
public class LegibleTextTests
{
    public static IEnumerable<object[]> Fields() => EnemyKit.FieldSizes.Select(size => new object[] { size.Cols, size.Rows });

    private const int GuardFrames = 400;
    private const int StageWithTwoHints = 2, HintsAtThatStage = 2, PostId = 1;
    private const string ShieldUp = PickupTests.ShieldUp, PowerUp = PickupTests.PowerUp;
    /// <summary>The pop-up a shot-down enemy makes with its score.</summary>
    private const string Score = "+100";

    private static string RowText(Screen scr, int row) => new(Enumerable.Range(0, scr.Cols).Select(col => scr.CharAt(col, row)).ToArray());

    private static Screen Render(RaiderGame game, int cols, int rows)
    {
        var scr = new Screen(cols, rows);
        new RaiderView().FitAndRender(game, scr);
        return scr;
    }

    /// <summary>A game with one post card laid behind the banner's rows.</summary>
    private static RaiderGame GameWithCard(int cols, int rows, Action<RaiderGame> setUp)
    {
        var game = EnemyKit.Game(cols, rows);
        game.Posts.Offer(HoldRig.Post(PostId));
        game.Step();
        setUp(game);
        var card = game.Cards.Cards.Single();
        card.Top = Math.Max(0, RaiderView.BannerRow(game.Layout) - 1);
        EnemyKit.Clear(game);
        return game;
    }

    /// <summary>Checks the block whose first line is <paramref name="lines"/>[0]: each line whole, on consecutive rows, and every cell from the block's left edge to its right edge on the backing.</summary>
    private static void AssertSolidBlock(Screen scr, FieldLayout lay, IReadOnlyList<string> lines, uint back)
    {
        int first = Enumerable.Range(RaiderView.HudRows, scr.Rows - RaiderView.HudRows).First(row => RowText(scr, row).Contains(lines[0]));
        int width = lines.Max(line => line.Length) + RaiderView.BlockPad;
        int left = lay.Col0 + (lay.PCols - width) / 2;
        Assert.True(width <= lay.PCols, "the block is wider than the field");
        for (int i = 0; i < lines.Count; i++)
        {
            Assert.Contains(lines[i], RowText(scr, first + i));
            for (int col = left; col < left + width; col++)
                Assert.True(scr.BgAt(col, first + i) == back, $"cell {col},{first + i} of '{lines[i]}' is not on the block's backing");
        }
    }

    private static string[] HintLines(EnemyKind kind, FieldLayout lay) =>
        ShortLines.Format(Ramp.HintFor(kind)!, lay.PCols - RaiderView.BlockPad, RaiderView.HintWrapLines);

    [Theory, MemberData(nameof(Fields))]
    public void The_stage_banner_and_its_hints_are_one_solid_block_over_a_post_card(int cols, int rows)
    {
        var game = GameWithCard(cols, rows, fresh => fresh.StartAtStage(StageWithTwoHints));
        Assert.True(game.StageBannerLeft > 0);
        var lay = FieldLayout.Compute(cols, rows);
        var scr = Render(game, cols, rows);
        var kinds = Ramp.Introduced(StageWithTwoHints);
        Assert.Equal(HintsAtThatStage, kinds.Length);
        var lines = new List<string> { RaiderView.StageTitle(StageWithTwoHints) };
        foreach (var kind in kinds)
        {
            var hint = HintLines(kind, lay);
            Assert.Equal(Ramp.HintFor(kind), string.Join(' ', hint));
            lines.AddRange(hint);
        }
        AssertSolidBlock(scr, lay, lines, Palette.BannerBack);
    }

    [Theory, MemberData(nameof(Fields))]
    public void The_mid_stage_hint_is_a_padded_block_over_a_post_card(int cols, int rows)
    {
        var game = GameWithCard(cols, rows, fresh =>
        {
            fresh.SetProgress((long)(Ramp.UnlockShare(EnemyKind.Mine) * Tuning.StageFrames) - 1);
            for (int i = 0; i < GuardFrames && fresh.KindHint is null; i++) EnemyKit.StepSweeping(fresh);
        });
        Assert.Equal(EnemyKind.Mine, game.KindHint);
        var lay = FieldLayout.Compute(cols, rows);
        var scr = Render(game, cols, rows);
        var hint = HintLines(EnemyKind.Mine, lay);
        Assert.Equal(Ramp.HintFor(EnemyKind.Mine), string.Join(' ', hint));
        AssertSolidBlock(scr, lay, hint, Palette.BannerBack);
    }

    [Theory, MemberData(nameof(Fields))]
    public void The_warning_and_the_boss_name_are_one_padded_block_over_a_post_card(int cols, int rows)
    {
        for (int stage = 1; stage <= Tuning.StageCap; stage++)
        {
            var game = GameWithCard(cols, rows, fresh => fresh.StartAtBoss(stage));
            Assert.Equal(GateState.Warning, game.Gate);
            var lay = FieldLayout.Compute(cols, rows);
            var scr = Render(game, cols, rows);
            var lines = new List<string> { "WARNING" };
            lines.AddRange(ShortLines.Format(game.BossName, lay.PCols - RaiderView.BlockPad, RaiderView.WrapMaxLines));
            Assert.Equal(game.BossName, string.Join(' ', lines.Skip(1)));
            AssertSolidBlock(scr, lay, lines, Palette.WarningBack);
        }
    }

    [Fact]
    public void Every_hint_is_drawn_in_a_colour_that_reads_on_its_backing_and_only_a_dim_kind_changes()
    {
        foreach (var kind in Enum.GetValues<EnemyKind>().Where(kind => Ramp.HintFor(kind) is not null))
        {
            Assert.True(Surface.Contrast(RaiderView.HintColour(kind), Palette.BannerBack) >= Palette.HintMinContrast, kind.ToString());
            if (RaiderView.HintColour(kind) != RaiderGame.TableBehaviour(kind).Colour) Assert.Equal(EnemyKind.Lurk, kind);
        }
        Assert.True(Surface.Contrast(Palette.Gold, Palette.WarningBack) >= Palette.HintMinContrast);
        Assert.True(Surface.Contrast(Palette.White, Palette.WarningBack) >= Palette.HintMinContrast);
        Assert.True(Surface.Contrast(Palette.White, Palette.BannerBack) >= Palette.HintMinContrast);
    }

    private static Popup PopupAt(string text, double x, double y, uint colour = Palette.White) =>
        new() { Text = text, X = x, Y = y, Life = Tuning.PopupFrames, Colour = colour };

    private static List<int> RowsOf(Screen scr, string text) => Enumerable.Range(0, scr.Rows).Where(row => RowText(scr, row).Contains(text)).ToList();

    [Theory, MemberData(nameof(Fields))]
    public void Three_pop_ups_added_at_one_point_stay_whole_and_on_rows_of_their_own_for_their_whole_life(int cols, int rows)
    {
        var game = EnemyKit.Game(cols, rows);
        string[] texts = [Score, ShieldUp, PowerUp];
        foreach (var text in texts) game.Popups.Add(PopupAt(text, game.PW / 2.0, game.PH / 2.0));
        int frames = 0;
        for (; game.Popups.Count > 0 && frames < GuardFrames; frames++)
        {
            var live = game.Popups.Select(popup => popup.Text).ToList();
            var scr = Render(game, cols, rows);
            var found = live.Select(text => Assert.Single(RowsOf(scr, text))).ToList();
            Assert.Equal(found.Count, found.Distinct().Count());
            EnemyKit.StepSweeping(game);
        }
        Assert.Equal(Tuning.PopupFrames, frames);
    }

    [Fact]
    public void A_pop_up_that_would_overlap_moves_to_the_nearest_free_row_above_and_below_only_when_none_is_free_above()
    {
        int ColOf(double x) => (int)(x / CardField.PixelsPerCell);
        int RowOf(double y) => (int)(y / CardField.PixelsPerCell);
        const int Row = 3, Width = 100, Centre = 40, LeftOne = 10, RightOne = 60;
        const string First = "AAAA", Second = "BBBB";
        const double RowY = Row * CardField.PixelsPerCell;
        var same = new[] { PopupAt(First, Centre, RowY), PopupAt(Second, Centre, RowY), PopupAt("CCCC", Centre, RowY) };
        Assert.Equal(new[] { Row, Row - 1, Row + 1 }, RaiderView.PopupSpots(same, 0, Width, ColOf, RowOf).Select(spot => spot.Row).ToArray());
        var apart = RaiderView.PopupSpots([PopupAt(First, LeftOne, RowY), PopupAt(Second, RightOne, RowY)], 0, Width, ColOf, RowOf);
        Assert.All(apart, spot => Assert.Equal(Row, spot.Row));
    }

    [Fact]
    public void A_gold_like_makes_pop_ups_that_are_whole_in_the_screen_on_the_frame_after()
    {
        var (game, card, _) = HoldRig.Scene(post: HoldRig.Post(PostId, likes: Tuning.GoldLikes));
        EnemyKit.ScriptedThreeParts(game);
        HoldRig.Force(game, nameof(RaiderGame.Shield), LikeHoldTests.NoShield);
        for (int i = 0; i < GuardFrames && !card.Marks.Paid; i++)
        {
            game.Bullets.Clear();
            int top = card.Top;
            HoldRig.Park(game, card, PostAction.Like);
            game.Step();
            card.Top = top;
        }
        Assert.True(card.Marks.Paid);
        game.Step();
        var scr = Render(game, HoldRig.ScrCols, HoldRig.ScrRows);
        string[] made = [RaiderGame.GoldPopup, ShieldUp, PowerUp, RaiderGame.PartsPopup];
        foreach (var text in made) Assert.Contains(game.Popups, popup => popup.Text == text);
        var rowsSeen = made.Select(text => Assert.Single(RowsOf(scr, text))).ToList();
        Assert.Equal(rowsSeen.Count, rowsSeen.Distinct().Count());
    }

    [Theory, MemberData(nameof(Fields))]
    public void No_pop_up_cell_is_drawn_outside_the_fields_columns(int cols, int rows)
    {
        var game = EnemyKit.Game(cols, rows);
        var lay = FieldLayout.Compute(cols, rows);
        game.Popups.Add(PopupAt(RaiderGame.GoldPopup, 0, game.PH / 2.0));
        game.Popups.Add(PopupAt(ShieldUp, game.PW, game.PH / 2.0));
        var spots = RaiderView.PopupSpots(game.Popups, lay.Col0, lay.PCols, x => lay.Col0 + (int)(x / CardField.PixelsPerCell), y => (int)(y / CardField.PixelsPerCell));
        Assert.All(spots, spot => Assert.True(spot.Col >= lay.Col0 && spot.Col + spot.Popup.Text.Length <= lay.Col0 + lay.PCols));
    }

    /// <summary>A screen of one colour wide enough for a pop-up or two, which <see cref="DrawPopups"/> draws by cell: a pop-up's
    /// centre is its column and its row.</summary>
    private const int PopupScreenCols = 40, PopupScreenRows = 10, PopupRow = 5, PopupLeft = 10;

    private static Screen PopupScreen(uint back)
    {
        var scr = new Screen(PopupScreenCols, PopupScreenRows);
        scr.Clear(back);
        return scr;
    }

    private static Popup PopupFrom(string text, int left, uint colour = Palette.White) => PopupAt(text, left + text.Length / 2, PopupRow, colour);

    private static void DrawPopups(Screen scr, params Popup[] popups) =>
        RaiderView.DrawPopups(scr, popups, 0, scr.Cols, x => (int)x, y => (int)y);

    [Fact]
    public void A_pop_up_beside_a_card_digit_leaves_a_clear_cell_between_them()
    {
        const char Clear = ' ';
        var scr = PopupScreen(CardColours.Slab);
        string text = RaiderGame.PartsPopup;
        int before = PopupLeft - 1, after = PopupLeft + text.Length;
        scr.Put(before, PopupRow, '2', Palette.White);
        scr.Put(after, PopupRow, 'K', Palette.White);
        DrawPopups(scr, PopupFrom(text, PopupLeft));
        Assert.Equal(Clear, scr.CharAt(before, PopupRow));
        Assert.Equal(text, RowText(scr, PopupRow).Substring(PopupLeft, text.Length));
        Assert.Equal(Clear, scr.CharAt(after, PopupRow));
    }

    [Fact]
    public void A_pop_up_that_ends_where_another_starts_keeps_its_letters_and_the_others()
    {
        var scr = PopupScreen(CardColours.Slab);
        DrawPopups(scr, PopupFrom(Score, PopupLeft), PopupFrom(PowerUp, PopupLeft + Score.Length));
        Assert.Equal(Score + PowerUp, RowText(scr, PopupRow).Substring(PopupLeft, Score.Length + PowerUp.Length));
    }

    [Theory]
    [InlineData(Palette.Gold)]
    [InlineData(Palette.Pink)]
    [InlineData(Palette.White)]
    [InlineData(Palette.Ice)]
    public void A_pop_ups_letters_read_on_a_background_of_their_own_colour(uint colour)
    {
        var scr = PopupScreen(colour);
        const string Text = "+BOMB";
        DrawPopups(scr, PopupFrom(Text, PopupLeft, colour));
        for (int col = PopupLeft; col < PopupLeft + Text.Length; col++)
        {
            int cell = PopupRow * scr.Cols + col;
            Assert.Equal(Text[col - PopupLeft], scr.Ch[cell]);
            Assert.True(Surface.Contrast(scr.Fg[cell], scr.Bg[cell]) >= Surface.BodyTextContrast, $"cell {col} of '{Text}' on its own colour");
        }
    }

    private const int PartTrials = 2000;
    private const double PartDropBand = 0.05;

    [Fact]
    public void About_a_third_of_destroyed_boss_parts_drop_a_pickup()
    {
        var game = EnemyKit.Game(seed: 7);
        int drops = 0;
        for (int i = 0; i < PartTrials; i++)
        {
            EnemyKit.Clear(game);
            var boss = EnemyKit.ScriptedThreeParts(game);
            var pod = boss.Parts!.Single(part => part.Name == "pod-l");
            EnemyKit.Shot(game, boss.X + pod.OX, boss.Y + pod.OY, pod.Hp);
            game.Step();
            Assert.Equal(PartState.Dead, pod.State);
            if (game.Pickups.Count > 0) drops++;
        }
        Assert.InRange((double)drops / PartTrials, Tuning.PartDropChance - PartDropBand, Tuning.PartDropChance + PartDropBand);
    }
}
