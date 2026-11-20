using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// Ships, enemies, pickups and bullets draw above an enemy's speech box: where their pixels cross a box the cell shows them, not the text.
public class UnitsOverPostsTests
{
    private const int Cols = 48, Rows = 60;
    private const string Post = "this is a perfectly ordinary post with enough words in it to run well past three short lines of speech box text";
    /// <summary>Pixels from the field's bottom-right corner where the ship waits, clear of the enemy's speech box.</summary>
    private const int ShipCornerInset = 8;
    /// <summary>How far down the field the enemy sits, as a share of its height.</summary>
    private const double EnemyDepth = 0.55;
    /// <summary>Full hit points, so no health bar is drawn under the enemy.</summary>
    private const int EnemyHp = 8;
    /// <summary>Frames the speech box has been typing: past its first frame and well short of its last.</summary>
    private const int SpeechBoxAgeOpen = 5;
    /// <summary>Cells skipped at the box's top and sides when looking for a letter of the post: the edge and the header or margin inside it.</summary>
    private const int TextInset = 2;
    /// <summary>The box's bottom edge, which carries no text.</summary>
    private const int BottomEdgeRows = 1;
    /// <summary>A friendly shot is drawn from two pixels above its Y, so this offset puts it over the whole cell.</summary>
    private const int ShotTopOffset = 2;
    /// <summary>An enemy bolt is a square drawn from one pixel up and left of its centre, so this offset keeps it inside the cell.</summary>
    private const int BoltCentreOffset = 1;
    /// <summary>The letters a shot may cover: its own cell and a neighbour or so.</summary>
    private const int LettersAShotMayCover = 4;
    /// <summary>A shot far enough right of and below the box that none of its pixels reach it.</summary>
    private const int FarRightPixels = 40, FarDownPixel = 100;

    private static int PixelX(RaiderGame game, int col) => (col - game.Layout.Col0) * CardField.PixelsPerCell;

    private static int PixelY(int row) => row * CardField.PixelsPerCell;

    private static (RaiderGame game, Enemy enemy) Game()
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(Cols, Rows));
        SetShip(game, game.Layout.PW - ShipCornerInset, game.Layout.PH - ShipCornerInset);
        EnemyKit.Clear(game);
        var enemy = new Enemy { Kind = EnemyKind.Gunship, Sprite = Art.Gunship, X = game.Layout.PW / 2.0, Y = game.Layout.PH * EnemyDepth, Hp = EnemyHp, MaxHp = EnemyHp, Header = "RUDE", Live = true, Text = Post };
        game.Enemies.Add(enemy);
        EnemySpeechBox.Update(enemy, game.Layout.PCols, game.Layout.Rows);
        enemy.SpeechBoxAge = SpeechBoxAgeOpen;
        Assert.True(EnemySpeechBox.IsOpen(enemy));
        return (game, enemy);
    }

    private static void SetShip(RaiderGame game, double x, double y)
    {
        typeof(RaiderGame).GetProperty(nameof(RaiderGame.PX))!.GetSetMethod(true)!.Invoke(game, [x]);
        typeof(RaiderGame).GetProperty(nameof(RaiderGame.PY))!.GetSetMethod(true)!.Invoke(game, [y]);
    }

    private static Screen Render(RaiderGame game)
    {
        var scr = new Screen(Cols, Rows);
        new RaiderView().FitAndRender(game, scr);
        return scr;
    }

    /// <summary>A cell inside the box that carries a letter of the post.</summary>
    private static (int col, int row) LetterCell(Screen scr, CellRect box)
    {
        for (int row = box.R + TextInset; row < box.Bottom - BottomEdgeRows; row++)
            for (int col = box.C + TextInset; col < box.Right - TextInset; col++)
                if (char.IsLetterOrDigit(scr.CharAt(col, row))) return (col, row);
        throw new Xunit.Sdk.XunitException("no letter in the box");
    }

    private static int Letters(Screen scr, CellRect box)
    {
        int letters = 0;
        for (int row = box.R; row < box.Bottom; row++)
            for (int col = box.C; col < box.Right; col++)
                if (char.IsLetterOrDigit(scr.CharAt(col, row))) letters++;
        return letters;
    }

    [Fact]
    public void A_friendly_shot_over_a_speech_box_shows_instead_of_the_text()
    {
        var (game, enemy) = Game();
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        var before = Render(game);
        var (col, row) = LetterCell(before, box);
        int letters = Letters(before, box);
        game.Bullets.Add(new Bullet { X = PixelX(game, col), Y = PixelY(row) + ShotTopOffset, Friendly = true });
        var scr = Render(game);
        Assert.False(char.IsLetterOrDigit(scr.CharAt(col, row)), "the shot's cell is not a letter");
        Assert.True(scr.Fg[row * Cols + col] == Palette.Ice || scr.Bg[row * Cols + col] == Palette.Ice, "the cell carries the shot's colour");
        Assert.True(Letters(scr, box) >= letters - LettersAShotMayCover, "the rest of the text is still there");
    }

    [Fact]
    public void An_enemy_bolt_over_a_speech_box_shows_instead_of_the_text()
    {
        var (game, enemy) = Game();
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        var (col, row) = LetterCell(Render(game), box);
        game.Bullets.Add(new Bullet { X = PixelX(game, col) + BoltCentreOffset, Y = PixelY(row) + BoltCentreOffset, Friendly = false });
        var scr = Render(game);
        Assert.False(char.IsLetterOrDigit(scr.CharAt(col, row)));
        Assert.True(scr.Fg[row * Cols + col] == Palette.Crimson || scr.Bg[row * Cols + col] == Palette.Crimson);
    }

    [Fact]
    public void The_ship_over_a_speech_box_shows_instead_of_the_text()
    {
        var (game, enemy) = Game();
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        var control = Render(game);
        var (col, row) = LetterCell(control, box);
        SetShip(game, PixelX(game, col) + CardField.PixelsPerCell / 2, PixelY(row) + CardField.PixelsPerCell / 2);
        var scr = Render(game);
        Assert.False(char.IsLetterOrDigit(scr.CharAt(col, row)), "the ship's cell is not a letter");
        Assert.NotEqual(control.Fg[row * Cols + col], scr.Fg[row * Cols + col]);
        // an enemy's box gives way to the ship (EnemySpeechBox.Placed), so what shows there is the ship
    }

    [Fact]
    public void Cells_with_no_unit_in_them_keep_the_text()
    {
        var (game, enemy) = Game();
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        var control = Render(game);
        var (col, _) = LetterCell(control, box);
        game.Bullets.Add(new Bullet { X = PixelX(game, col) + FarRightPixels, Y = FarDownPixel, Friendly = true });
        var scr = Render(game);
        for (int y = box.R; y < box.Bottom; y++)
            for (int x = box.C; x < box.Right; x++) Assert.Equal(control.CharAt(x, y), scr.CharAt(x, y));
    }
}
