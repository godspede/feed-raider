using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// The score line, the power bar, the stage colour and the text a carrier hauls, read off the screen the view draws.
public class HudAndCarrierTextTests
{
    private const uint HudBg = RaiderView.HudBg, Pink = Palette.Pink, Ice = Palette.Ice, PostText = RaiderView.PostText, CarrierTextBg = RaiderView.PostBg;
    // the bar sits this far from the right edge of the HUD, and the text read runs on over the rail cell beside the field
    private const int PowerBarOffset = RaiderView.PowerLabelWidth, PowerBarWidth = PowerBarOffset + 1;
    private static readonly string PowerLabel = RaiderView.PowerHeart + " POWER ";
    private const int OneFrameLeft = 1;

    // the HUD's two rows: the score and the stage on top, the power bar under them
    private const int ScoreRow = 0, PowerBarRow = 1;
    // the score starts one cell in from the HUD's left edge, and the stage readout ends two in from its right edge
    private const int ScoreCol = 1, StageEndFromRight = 2;

    // the carrier's height on the field in pixels, well under the HUD, and the line it hauls
    private const double CarrierY = 70;
    private const string SampleLine = "a sample line";

    private static RaiderGame EmptyGame()
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(HoldRig.ScrCols, HoldRig.ScrRows));
        game.Enemies.Clear(); game.Bullets.Clear(); game.Particles.Clear(); game.Rings.Clear(); game.Pickups.Clear();
        return game;
    }

    private static Screen Render(RaiderGame game, out FieldLayout lay)
    {
        var scr = new Screen(HoldRig.ScrCols, HoldRig.ScrRows);
        new RaiderView().FitAndRender(game, scr);
        lay = FieldLayout.Compute(HoldRig.ScrCols, HoldRig.ScrRows);
        return scr;
    }

    private static string TextAt(Screen screen, int row, int col, int len) => new(screen.Ch.AsSpan(row * screen.Cols + col, len));

    [Fact]
    public void The_score_is_six_digits_wide_and_a_big_score_is_not_cut()
    {
        var game = EmptyGame();
        const int smallScore = 42, bigScore = 987654;
        HoldRig.Force(game, nameof(RaiderGame.Score), smallScore);
        var scr = Render(game, out var lay);
        string small = $"SCORE {smallScore:000000}", big = $"SCORE {bigScore}";
        Assert.Equal(small, TextAt(scr, ScoreRow, lay.Col0 + ScoreCol, small.Length));
        HoldRig.Force(game, nameof(RaiderGame.Score), bigScore);
        scr = Render(game, out lay);
        Assert.Equal(big, TextAt(scr, ScoreRow, lay.Col0 + ScoreCol, big.Length));
    }

    [Fact]
    public void The_stage_readout_is_ice_until_the_last_stage_and_pink_on_it()
    {
        var game = EmptyGame();
        game.StartAtStage(Tuning.StageCap - 1);
        var scr = Render(game, out var lay);
        int col = ScoreRow * scr.Cols + lay.Col0 + lay.PCols - StageEndFromRight;
        Assert.Equal(Ice, scr.Fg[col]);
        game.StartAtStage(Tuning.StageCap);
        Assert.True(game.Stage >= Tuning.StageCap);
        scr = Render(game, out lay);
        Assert.Equal(Pink, scr.Fg[col]);
        Assert.Equal(HudBg, scr.Bg[col]);
    }

    private static string PowerRow(int framesLeft, out Screen scr, out FieldLayout lay)
    {
        var game = EmptyGame();
        HoldRig.Force(game, LikeHoldTests.PowerUntilField, game.Tick + framesLeft);
        Assert.Equal(framesLeft, game.PowerLeft);
        scr = Render(game, out lay);
        return TextAt(scr, PowerBarRow, lay.Col0 + lay.PCols - PowerBarOffset, PowerBarWidth);
    }

    [Fact]
    public void The_power_bar_is_full_at_the_start_and_shortens_with_the_time_left()
    {
        Assert.Equal($"{PowerLabel}████████", PowerRow(Tuning.PowerFrames, out _, out _));
        Assert.Equal($"{PowerLabel}████   │", PowerRow(Tuning.PowerFrames / 2, out _, out _));
        Assert.Equal($"{PowerLabel}█      │", PowerRow(OneFrameLeft, out _, out _));
    }

    [Fact]
    public void The_power_bar_is_pink_and_shows_one_block_when_nearly_out_and_nothing_when_out()
    {
        string row = PowerRow(OneFrameLeft, out var scr, out var lay);
        Assert.StartsWith($"{PowerLabel}█ ", row);
        int col = lay.Col0 + lay.PCols - PowerBarOffset;
        Assert.Equal(Pink, scr.Fg[PowerBarRow * scr.Cols + col]);
        Assert.Equal(HudBg, scr.Bg[PowerBarRow * scr.Cols + col]);
        var game = EmptyGame();
        scr = Render(game, out lay);
        Assert.DoesNotContain("POWER", TextAt(scr, PowerBarRow, lay.Col0, lay.PCols));
    }

    /// <summary>Spawns a carrier hauling <paramref name="text"/> at this pixel spot, already settled: no grace, wind-up or flash.</summary>
    private static Enemy PlaceCarrier(RaiderGame game, double x, double y, string text)
    {
        var enemy = game.SpawnKind(EnemyKind.Carrier, x / game.PW);
        enemy.X = x; enemy.Y = y; enemy.Grace = 0; enemy.Age = 0; enemy.Windup = 0; enemy.Flash = 0; enemy.Text = text; enemy.Header = null;
        return enemy;
    }

    private static int RowOf(Screen scr, string text)
    {
        for (int row = 0; row < scr.Rows; row++)
            if (new string(scr.Ch.AsSpan(row * scr.Cols, scr.Cols)).Contains(text)) return row;
        return -1;
    }

    [Fact]
    public void A_carriers_text_sits_on_the_cell_row_above_its_top_and_in_the_post_colour()
    {
        var game = EmptyGame();
        var enemy = PlaceCarrier(game, game.PW / 2.0, CarrierY, SampleLine);
        var scr = Render(game, out var lay);
        int row = (int)((enemy.Y - enemy.Sprite.H / 2.0) / CardField.PixelsPerCell) - 1;
        Assert.Equal(row, RowOf(scr, SampleLine));
        int col = lay.Col0 + (int)(enemy.X / CardField.PixelsPerCell) - SampleLine.Length / 2;
        Assert.Equal(SampleLine, TextAt(scr, row, col, SampleLine.Length));
        Assert.Equal(PostText, scr.Fg[row * scr.Cols + col]);
    }

    [Fact]
    public void A_carriers_text_never_rises_into_the_hud_rows()
    {
        var game = EmptyGame();
        const double atTheTop = 5;
        PlaceCarrier(game, game.PW / 2.0, atTheTop, SampleLine);
        var scr = Render(game, out _);
        // the carrier's own ship is drawn over some of its cells, so look for the text's background rather than for the whole line
        int TextCellsOnRow(int row) => Enumerable.Range(0, scr.Cols).Count(col => scr.Bg[row * scr.Cols + col] == CarrierTextBg && scr.Fg[row * scr.Cols + col] == PostText);
        const int mostOfTheLine = 5;   // cells of the text's ground showing past the ship drawn over it
        Assert.True(TextCellsOnRow(EnemySpeechBox.HudRows) >= mostOfTheLine, "the text is on the first row under the HUD");
        for (int row = 0; row < EnemySpeechBox.HudRows; row++) Assert.Equal(0, TextCellsOnRow(row));
        Assert.Equal(0, TextCellsOnRow(EnemySpeechBox.HudRows + 1));
    }

    [Fact]
    public void A_carriers_text_is_kept_inside_the_field_at_both_edges()
    {
        var game = EmptyGame();
        const double edgeInset = 1;   // pixels in from the field's edge
        const string leftLine = "left sample";
        PlaceCarrier(game, edgeInset, CarrierY, leftLine);
        var scr = Render(game, out var lay);
        int row = RowOf(scr, leftLine);
        Assert.Equal(lay.Col0, Array.IndexOf(scr.Ch, leftLine[0], row * scr.Cols + lay.Col0 - 1) - row * scr.Cols);
        game = EmptyGame();
        const string rightLine = "right sample";
        PlaceCarrier(game, lay.PW - edgeInset, CarrierY, rightLine);
        scr = Render(game, out lay);
        row = RowOf(scr, rightLine);
        Assert.Equal(rightLine, TextAt(scr, row, lay.Col0 + lay.PCols - rightLine.Length, rightLine.Length));
    }
}
