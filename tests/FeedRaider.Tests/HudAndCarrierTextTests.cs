using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// The score line, the stage colour and the text a carrier hauls, read off the screen the view draws.
public class HudAndCarrierTextTests
{
    private const uint HudBg = RaiderView.HudBg, Pink = Palette.Pink, Ice = Palette.Ice, PostText = RaiderView.PostText, CarrierTextBg = RaiderView.PostBg;

    // the HUD's two rows: the score and the stage on top, the wave and the weapon under them
    private const int ScoreRow = 0, WaveRow = 1;
    // the score starts one cell in from the HUD's left edge, and the stage readout ends two in from its right edge
    private const int ScoreCol = 1, StageEndFromRight = 2;

    // the carrier's height on the field in pixels, well under the HUD, and the line it hauls
    private const double CarrierY = 70;
    private const string SampleLine = "a sample line";

    private static RaiderGame EmptyGame()
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(PressRig.ScrCols, PressRig.ScrRows));
        game.Enemies.Clear(); game.Bullets.Clear(); game.Particles.Clear(); game.Rings.Clear(); game.Pickups.Clear();
        return game;
    }

    private static Screen Render(RaiderGame game, out FieldLayout lay)
    {
        var scr = new Screen(PressRig.ScrCols, PressRig.ScrRows);
        new RaiderView().FitAndRender(game, scr);
        lay = FieldLayout.Compute(PressRig.ScrCols, PressRig.ScrRows);
        return scr;
    }

    private static string TextAt(Screen screen, int row, int col, int len) => new(screen.Ch.AsSpan(row * screen.Cols + col, len));

    [Fact]
    public void The_score_is_six_digits_wide_and_a_big_score_is_not_cut()
    {
        var game = EmptyGame();
        const int smallScore = 42, bigScore = 987654;
        PressRig.Force(game, nameof(RaiderGame.Score), smallScore);
        var scr = Render(game, out var lay);
        string small = $"SCORE {smallScore:000000}", big = $"SCORE {bigScore}";
        Assert.Equal(small, TextAt(scr, ScoreRow, lay.Col0 + ScoreCol, small.Length));
        PressRig.Force(game, nameof(RaiderGame.Score), bigScore);
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

    [Fact]
    public void A_like_leaves_the_wave_label_on_the_second_hud_row()
    {
        var game = EmptyGame();
        PressRig.Force(game, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        game.Posts.Offer(PressRig.Post(1));
        PressRig.Until(game, () => game.Cards.Cards.Count >= 1);
        PressRig.Press(game, game.Cards.Cards[0], PostAction.Like);
        var scr = Render(game, out var lay);
        string wave = game.WaveLabel();
        Assert.Equal(wave, TextAt(scr, WaveRow, lay.Col0 + ScoreCol, wave.Length));
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
