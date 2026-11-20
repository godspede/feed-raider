using System.Runtime.CompilerServices;
using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Everything that touches the game draws above an enemy's speech box (a darkness enemy's): one test per object class,
// plus the enemy that must not sit under its own post. The rendered frames of a busy scene are saved when
// FR_SCREENS names a directory.
public class UnitsOverAllPostsTests
{
    private const int Cols = 60, Rows = 60;

    private const int GameSeed = 5;

    /// <summary>Pixels the ship sits in from the field's left and bottom edges: a corner far from the speech box.</summary>
    private const double ShipInset = 8;

    /// <summary>Where the gunship sits, as fractions of the field's width and height.</summary>
    private const double GunshipAcross = 0.25, GunshipDown = 0.55;
    private const int GunshipHp = 8;

    /// <summary>Frames the speech box has been open: a few frames into its typing, so its first letters show.</summary>
    private const int BoxAge = 5;

    /// <summary>The health of every other enemy the tests place, full.</summary>
    private const int UnitHp = 4;

    /// <summary>A wind-up under way, which draws a turret's reticle and a diver's aim line, and no wind-up at all.</summary>
    private const int WindingUp = 10, NotWindingUp = -1;

    /// <summary>The box's first text row, under its top border and title.</summary>
    private const int FirstTextRow = 2;

    /// <summary>The box's frame is one cell thick, and the text keeps half of <see cref="EnemySpeechBox.TextSide"/> clear on each side.</summary>
    private const int Border = 1, TextInset = EnemySpeechBox.TextSide / 2;

    /// <summary>The middle of a cell in pixels, where a thing aimed at the cell is put.</summary>
    private const int CellMiddle = CardField.PixelsPerCell / 2;

    /// <summary>A test ring's frames left and its whole life in frames: half way through it.</summary>
    private const int RingLife = 10, RingMax = 20;

    private sealed class Scene
    {
        public RaiderGame Game = null!;
        public Enemy Enemy = null!;
        public CellRect EnemyBox;
        public Screen Control = null!;
    }

    private static Scene Make()
    {
        var game = new RaiderGame(GameSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(Cols, Rows));
        SetShip(game, ShipInset, game.Layout.PH - ShipInset);
        game.Enemies.Clear(); game.Bullets.Clear(); game.Pickups.Clear(); game.Particles.Clear(); game.Rings.Clear();
        var enemy = new Enemy { Kind = EnemyKind.Gunship, Sprite = Art.Gunship, X = game.Layout.PW * GunshipAcross, Y = game.Layout.PH * GunshipDown, Hp = GunshipHp, MaxHp = GunshipHp, Header = "RUDE", Live = true, Text = "sample text standing in for a rude post, aimed at nobody" };
        game.Enemies.Add(enemy);
        EnemySpeechBox.Update(enemy, game.Layout.PCols, game.Layout.Rows);
        enemy.SpeechBoxAge = BoxAge;
        var scene = new Scene { Game = game, Enemy = enemy, EnemyBox = EnemySpeechBox.Box(enemy, game.Layout) };
        Assert.True(EnemySpeechBox.IsOpen(enemy));
        scene.Control = Render(game);
        return scene;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PX")]
    private static extern void SetPX(RaiderGame game, double value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PY")]
    private static extern void SetPY(RaiderGame game, double value);

    private static void SetShip(RaiderGame game, double x, double y)
    {
        SetPX(game, x);
        SetPY(game, y);
    }

    private static Screen Render(RaiderGame game)
    {
        var scr = new Screen(Cols, Rows);
        new RaiderView().FitAndRender(game, scr);
        return scr;
    }

    /// <summary>A cell of the box that carries a letter of its text.</summary>
    private static (int col, int row) LetterCell(Screen scr, CellRect box)
    {
        for (int row = box.R + FirstTextRow; row < box.Bottom - Border; row++)
            for (int col = box.C + TextInset; col < box.Right - TextInset; col++)
                if (char.IsLetter(scr.CharAt(col, row))) return (col, row);
        throw new Xunit.Sdk.XunitException("no letter in the box");
    }

    private static bool Hidden(Screen scr, Screen control, int col, int row) =>
        !char.IsLetter(scr.CharAt(col, row)) && (scr.Fg[row * Cols + col] != control.Fg[row * Cols + col] || scr.Bg[row * Cols + col] != control.Bg[row * Cols + col]);

    private static (CellRect box, int col, int row, double px, double py) Target(Scene scene)
    {
        var box = scene.EnemyBox;
        var (col, row) = LetterCell(scene.Control, box);
        return (box, col, row, (col - scene.Game.Layout.Col0) * CardField.PixelsPerCell + CellMiddle, row * CardField.PixelsPerCell + CellMiddle);
    }

    private static string AThing(object kind) => $"a {kind}";

    private static void Shows(Scene scene, Action<RaiderGame, double, double> place, string what)
    {
        var (_, col, row, px, py) = Target(scene);
        place(scene.Game, px, py);
        var scr = Render(scene.Game);
        Assert.True(Hidden(scr, scene.Control, col, row), $"{what} is drawn over the speech box");
    }

    [Fact]
    public void A_friendly_shot_shows() => Shows(Make(), (game, x, y) => game.Bullets.Add(new Bullet { X = x, Y = y, Friendly = true }), "a shot");

    [Fact]
    public void A_bolt_shows() => Shows(Make(), (game, x, y) => game.Bullets.Add(new Bullet { X = x, Y = y, Friendly = false }), "a bolt");

    [Fact]
    public void An_aimed_bolt_shows() => Shows(Make(), (game, x, y) => game.Bullets.Add(new Bullet { X = x, Y = y, Friendly = false, Aimed = true }), "an aimed bolt");

    [Fact]
    public void The_ship_shows() => Shows(Make(), SetShip, "the ship");

    [Fact]
    public void A_pickup_shows()
    {
        foreach (var kind in Enum.GetValues<PickupKind>())
            Shows(Make(), (game, x, y) => game.Pickups.Add(new Pickup { Kind = kind, X = x, Y = y, BaseX = x }), AThing(kind) + " pickup");
    }

    [Fact]
    public void Another_enemy_shows()
    {
        foreach (var kind in new[] { EnemyKind.Grunt, EnemyKind.Mine, EnemyKind.Diver, EnemyKind.Splitter, EnemyKind.Turret })
            Shows(Make(), (game, x, y) =>
            {
                var sprite = kind switch { EnemyKind.Diver => Art.Diver, EnemyKind.Turret => Art.Turret, _ => Art.Grunt };
                game.Enemies.Add(new Enemy { Kind = kind, Sprite = sprite, X = x, BaseX = x, Y = y, Hp = UnitHp, MaxHp = UnitHp, Windup = NotWindingUp, Grace = 0 });
            }, AThing(kind));
    }

    /// <summary>The ring's radius, start and end alike; it is centred one radius left of the target, so its rim passes over it.</summary>
    private const double TestRingRadius = 6;

    [Fact]
    public void A_blast_ring_shows() =>
        Shows(Make(), (game, x, y) => game.Rings.Add(new Ring { X = x - TestRingRadius, Y = y, R0 = TestRingRadius, R1 = TestRingRadius, Life = RingLife, Max = RingMax, Colour = Palette.White }), "a ring");

    [Fact]
    public void A_turret_reticle_shows() =>
        Shows(Make(), (game, x, y) => game.Enemies.Add(new Enemy { Kind = EnemyKind.Turret, Sprite = Art.Turret, X = TurretX, Y = game.Layout.PH - TurretAboveBottom, Hp = UnitHp, MaxHp = UnitHp, Windup = WindingUp, AimX = x, AimY = y }), "a turret reticle");

    /// <summary>Pixels the turret sits in from the field's left edge and up from its bottom: far from the speech box, so only its reticle is near.</summary>
    private const double TurretX = 4, TurretAboveBottom = 20;

    /// <summary>Pixels the diver starts left of the target and above the box, so its aim line runs down across the box.</summary>
    private const double DiverLeftOfTarget = 10, DiverAboveBox = 20;

    [Fact]
    public void A_diver_aim_line_and_crosshair_show()
    {
        var scene = Make();
        var (box, col, row, px, py) = Target(scene);
        scene.Game.Enemies.Add(new Enemy { Kind = EnemyKind.Diver, Sprite = Art.Diver, X = px - DiverLeftOfTarget, Y = box.R * CardField.PixelsPerCell - DiverAboveBox, BaseX = px, Hp = UnitHp, MaxHp = UnitHp, Windup = WindingUp, AimX = px, AimY = py });
        var scr = Render(scene.Game);
        int changed = 0;
        for (int y = box.R; y < box.Bottom; y++)
            for (int x = box.C; x < box.Right; x++) if (Hidden(scr, scene.Control, x, y)) changed++;
        Assert.True(Hidden(scr, scene.Control, col, row), "the crosshair cell");
        const int CrosshairAndALineCell = 2;
        Assert.True(changed >= CrosshairAndALineCell, "the aim line crosses the box too");
    }

    [Fact]
    public void An_enemys_own_bolt_shows_over_its_own_box()
    {
        var scene = Make();
        var (_, col, row, px, py) = Target(scene);
        scene.Game.Bullets.Add(new Bullet { X = px, Y = py, Friendly = false });
        Assert.True(Hidden(Render(scene.Game), scene.Control, col, row));
    }

    /// <summary>Enemy heights in pixels near the field's top, where the box no longer fits above the sprite.</summary>
    private static readonly double[] NearTopYs = [7, 10, 14, 20];

    [Fact]
    public void An_enemy_near_the_top_gets_its_box_below_so_neither_hides_the_other()
    {
        var scene = Make();
        var enemy = scene.Enemy;
        foreach (double y in NearTopYs)
        {
            enemy.Y = y;
            var box = EnemySpeechBox.Box(enemy, scene.Game.Layout);
            int top = (int)Math.Floor((enemy.Y - enemy.Sprite.H / 2.0) / CardField.PixelsPerCell), bottom = (int)Math.Ceiling((enemy.Y + enemy.Sprite.H / 2.0) / CardField.PixelsPerCell);
            Assert.True(box.Bottom <= top || box.R >= bottom, $"box rows {box.R}-{box.Bottom} clear of the sprite rows {top}-{bottom} at y={y}");
            var scr = Render(scene.Game);
            int centreCol = scene.Game.Layout.Col0 + (int)(enemy.X / CardField.PixelsPerCell), centreRow = (int)(enemy.Y / CardField.PixelsPerCell);
            Assert.False(box.C <= centreCol && centreCol < box.Right && box.R <= centreRow && centreRow < box.Bottom, "the sprite's centre is outside the box");
            if (EnemySpeechBox.IsBelow(enemy, box)) Assert.Equal('▲', scr.CharAt(box.C + box.W / 2, box.R));
        }
    }

    [Fact]
    public void Nothing_in_the_box_changes_when_no_unit_is_near()
    {
        var scene = Make();
        const double farX = 2, farAboveBottom = 30;   // low on the left, far from the speech box
        scene.Game.Bullets.Add(new Bullet { X = farX, Y = scene.Game.Layout.PH - farAboveBottom, Friendly = true });
        var scr = Render(scene.Game);
        var box = scene.EnemyBox;
        for (int y = box.R; y < box.Bottom; y++)
            for (int x = box.C; x < box.Right; x++) Assert.Equal(scene.Control.CharAt(x, y), scr.CharAt(x, y));
    }

    [Fact]
    public void A_busy_scene_renders_to_pictures_saved_in_FR_SCREENS_when_it_is_set()
    {
        // with FR_SCREENS set the pictures are kept there to look at; otherwise they go to a folder removed at the end
        var kept = Environment.GetEnvironmentVariable("FR_SCREENS");
        var dir = string.IsNullOrEmpty(kept) ? Path.Combine(Path.GetTempPath(), "fr-screens-" + Guid.NewGuid().ToString("N")) : kept;
        Directory.CreateDirectory(dir);
        try
        {
            SaveBusyScenes(dir);
            foreach (var name in new[] { BusyScenePng, ControlPng, NearTopPng })
                Assert.True(new FileInfo(Path.Combine(dir, name)).Length > 0, $"{name} was written");
        }
        finally
        {
            if (string.IsNullOrEmpty(kept)) Directory.Delete(dir, true);
        }
    }

    private const string BusyScenePng = "busy-scene-enemy-box.png", ControlPng = "control-box-only.png", NearTopPng = "enemy-near-top-box-below.png";

    /// <summary>The height in pixels the near-top picture's enemy is moved to, so its box sits below it.</summary>
    private const double NearTopPictureY = 9;

    /// <summary>The busy scene's ring colour: the pink of a small kill's ring.</summary>
    private const uint BusyRingColour = 0xff8fb1u;

    /// <summary>Pixels in from the box's top left corner to the busy scene's corner.</summary>
    private const double BusyInset = 4;

    /// <summary>Where each thing of the busy scene sits against the scene's corner, in pixels.</summary>
    private static readonly (double X, double Y) BusyShot = (0, 2), BusyBolt = (6, 4), BusyAimedBolt = (12, 4),
        BusyPickup = (26, 6), BusyRing = (20, 8), BusyGrunt = (36, 8);

    /// <summary>Pixels the busy scene's diver starts right of the target and above the box, and where its ship sits against the target.</summary>
    private const double BusyDiverRightOfTarget = 20, BusyDiverAboveBox = 14;
    private static readonly (double X, double Y) BusyShip = (4, 3);

    private const double BusyRingRadius = 9;
    private const int BusyGruntHp = 2;

    private static void SaveBusyScenes(string dir)
    {
        var scene = Make();
        var game = scene.Game;
        {
            var box = scene.EnemyBox;
            double x0 = (box.C - game.Layout.Col0) * CardField.PixelsPerCell + BusyInset, y0 = box.R * CardField.PixelsPerCell + BusyInset;
            game.Bullets.Add(new Bullet { X = x0 + BusyShot.X, Y = y0 + BusyShot.Y, Friendly = true });
            game.Bullets.Add(new Bullet { X = x0 + BusyBolt.X, Y = y0 + BusyBolt.Y, Friendly = false });
            game.Bullets.Add(new Bullet { X = x0 + BusyAimedBolt.X, Y = y0 + BusyAimedBolt.Y, Friendly = false, Aimed = true });
            game.Pickups.Add(new Pickup { Kind = PickupKind.Shield, X = x0 + BusyPickup.X, Y = y0 + BusyPickup.Y, BaseX = x0 + BusyPickup.X });
            game.Enemies.Add(new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = x0 + BusyGrunt.X, Y = y0 + BusyGrunt.Y, Hp = BusyGruntHp, MaxHp = BusyGruntHp, Windup = NotWindingUp });
            game.Rings.Add(new Ring { X = x0 + BusyRing.X, Y = y0 + BusyRing.Y, R0 = BusyRingRadius, R1 = BusyRingRadius, Life = RingLife, Max = RingMax, Colour = BusyRingColour });
        }
        var (targetBox, _, _, targetX, targetY) = Target(scene);
        game.Enemies.Add(new Enemy { Kind = EnemyKind.Diver, Sprite = Art.Diver, X = targetX + BusyDiverRightOfTarget, Y = targetBox.R * CardField.PixelsPerCell - BusyDiverAboveBox, BaseX = targetX, Hp = UnitHp, MaxHp = UnitHp, Windup = WindingUp, AimX = targetX, AimY = targetY });
        SetShip(game, targetX + BusyShip.X, targetY + BusyShip.Y);
        FrameImage.Write(Render(game), Path.Combine(dir, BusyScenePng));
        FrameImage.Write(scene.Control, Path.Combine(dir, ControlPng));
        scene = Make();
        scene.Enemy.Y = NearTopPictureY;
        FrameImage.Write(Render(scene.Game), Path.Combine(dir, NearTopPng));
    }
}
