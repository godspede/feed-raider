using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The pass that lays units over the speech boxes draws only inside the boxes; the cells it gives must be the ones a full-field pass gave.
public class SpeechBoxPassPixelsTests
{
    private const int Cols = 48, Rows = 60;
    private const string Post = "this is a perfectly ordinary post with enough words in it to run well past three short lines of speech box text";

    private const int GameSeed = 5;

    /// <summary>Pixels the ship sits in from the field's right and bottom edges.</summary>
    private const double ShipInset = 8;

    /// <summary>The gunship's height as a share of the field's, and its hit points out of its full health.</summary>
    private const double GunshipDown = 0.55;
    private const int GunshipHp = 4, GunshipMaxHp = 8;

    /// <summary>Frames the speech box has been open: a few frames into its typing.</summary>
    private const int BoxAge = 5;

    private static (RaiderGame game, Enemy enemy) Game()
    {
        var game = new RaiderGame(GameSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(Cols, Rows));
        typeof(RaiderGame).GetProperty(nameof(RaiderGame.PX))!.GetSetMethod(true)!.Invoke(game, [(double)game.Layout.PW - ShipInset]);
        typeof(RaiderGame).GetProperty(nameof(RaiderGame.PY))!.GetSetMethod(true)!.Invoke(game, [(double)game.Layout.PH - ShipInset]);
        game.Enemies.Clear(); game.Bullets.Clear(); game.Pickups.Clear(); game.Particles.Clear(); game.Rings.Clear();
        var enemy = new Enemy { Kind = EnemyKind.Gunship, Sprite = Art.Gunship, X = game.Layout.PW / 2.0, Y = game.Layout.PH * GunshipDown, Hp = GunshipHp, MaxHp = GunshipMaxHp, FireCd = Tuning.GunshipVolleyFrames, Header = "SAMPLE", Live = true, Text = Post };
        game.Enemies.Add(enemy);
        EnemySpeechBox.Update(enemy, game.Layout.PCols, game.Layout.Rows);
        enemy.SpeechBoxAge = BoxAge;
        return (game, enemy);
    }

    private const ulong FnvOffset = 1469598103934665603UL, FnvPrime = 1099511628211UL;

    private static ulong Hash(Screen scr)
    {
        ulong hash = FnvOffset;
        for (int row = 0; row < scr.Rows; row++)
            for (int col = 0; col < scr.Cols; col++)
            {
                int index = row * scr.Cols + col;
                foreach (ulong value in new ulong[] { scr.CharAt(col, row), scr.Fg[index], scr.Bg[index] }) hash = (hash ^ value) * FnvPrime;
            }
        return hash;
    }

    private static ulong FrameHash(RaiderGame game)
    {
        var scr = new Screen(Cols, Rows);
        new RaiderView().FitAndRender(game, scr);
        return Hash(scr);
    }

    /// <summary>Where the shot, the bolt and the ring sit in the box: cells in from its left edge, and pixels down from its top.</summary>
    private const int ShotCellsIn = 4, ShotPixelsDown = 3, BoltCellsIn = 9, BoltPixelsDown = 5, RingCellsIn = 6, RingPixelsDown = 4;

    /// <summary>The ring's start and end radius, its frames left and its whole life in frames.</summary>
    private const double RingStartRadius = 3, RingEndRadius = 9;
    private const int RingLife = 6, RingMax = 12;

    /// <summary>The field pixel the given cells in from the box's left edge starts at.</summary>
    private static double BoxX(RaiderGame game, CellRect box, int cellsIn) => (box.C + cellsIn - game.Layout.Col0) * CardField.PixelsPerCell;

    /// <summary>The field pixel the given pixels down from the box's top sits at.</summary>
    private static double BoxY(CellRect box, int pixelsDown) => box.R * CardField.PixelsPerCell + pixelsDown;

    [Fact]
    public void A_frame_with_a_shot_a_ring_and_the_ship_over_boxes_is_unchanged()
    {
        var (game, enemy) = Game();
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        game.Bullets.Add(new Bullet { X = BoxX(game, box, ShotCellsIn), Y = BoxY(box, ShotPixelsDown), Friendly = true });
        game.Bullets.Add(new Bullet { X = BoxX(game, box, BoltCellsIn) + 1, Y = BoxY(box, BoltPixelsDown), Friendly = false });
        game.Rings.Add(new Ring { X = BoxX(game, box, RingCellsIn), Y = BoxY(box, RingPixelsDown), R0 = RingStartRadius, R1 = RingEndRadius, Life = RingLife, Max = RingMax, Colour = Palette.Ice });
        Assert.Equal(OverBoxes, FrameHash(game));
    }

    /// <summary>Pixels in from the field's top left corner: far from the box.</summary>
    private const double FarCorner = 6;

    [Fact]
    public void A_frame_with_no_unit_near_a_box_is_unchanged()
    {
        var (game, _) = Game();
        game.Bullets.Add(new Bullet { X = FarCorner, Y = FarCorner, Friendly = true });
        Assert.Equal(NoUnitNear, FrameHash(game));
    }

    private const ulong OverBoxes = 8406509250988379653UL, NoUnitNear = 622736293825532887UL;
}
