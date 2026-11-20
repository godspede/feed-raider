using FeedRaider.Engine.Raider;
using FeedRaider.Engine;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// What RaiderView puts on the field, pixel for pixel. The screen only shows 2x2 cells reduced to two colours, so these
// render through FitAndRender and then read the field the view drew into, before the cells were made of it.
public class RaiderViewPixelTests
{
    /// <summary>A spark's colour and the life it starts with; any colour serves.</summary>
    private const uint SparkColour = 0xff00ff;
    private const int SparkLife = 10;
    /// <summary>The row two sparks are put on, side by side, and where on it each one is.</summary>
    private const int SparkY = 40, LeftSparkX = 40, RightSparkX = 80;
    /// <summary>A field spot clear of the HUD, the ship and the edges, where a test puts the one thing it draws.</summary>
    private const int SpotX = 60, SpotY = 60;
    /// <summary>Where a test puts an enemy whose sprite it reads. One that aims sits higher, at <see cref="AimerY"/>, and aims
    /// straight down at <see cref="AimBelowY"/> or off to the side at (<see cref="SideAimX"/>, <see cref="SpotY"/>).</summary>
    private const int EnemyX = 40, EnemyY = 30, AimerY = 20, AimBelowY = 80, SideAimX = 90;
    /// <summary>The height a gunship is put at, with room above it for its health bar.</summary>
    private const int GunshipY = 50;

    /// <summary>A game stepped to <paramref name="frame"/> with the field emptied, so a test places exactly what it draws.</summary>
    private static RaiderGame Scene(int frame = 0)
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        for (int step = 0; step < frame; step++) game.Step();
        EnemyKit.Clear(game);
        Assert.Equal(frame, game.Frame);
        return game;
    }

    /// <summary>Renders and returns the field surface the view drew, before the screen shake and the cell reduction.</summary>
    private static Surface Draw(RaiderGame game, Screen? scr = null)
    {
        var view = new RaiderView();
        view.FitAndRender(game, scr ?? new Screen(EnemyKit.BigCols, EnemyKit.BigRows));
        return view.Field!;
    }

    /// <summary>The field pixel the top-left of <paramref name="enemy"/>'s sprite is drawn at.</summary>
    private static (int X, int Y) SpriteCorner(Enemy enemy) =>
        ((int)Math.Round(enemy.X - enemy.Sprite.W / 2.0), (int)Math.Round(enemy.Y - enemy.Sprite.H / 2.0));

    /// <summary>The first sprite pixel where the lit and the cold mine differ, as a field position and the two colours.</summary>
    private static (int Dx, int Dy, uint Lit, uint Cold) MineProbe()
    {
        for (int at = 0; at < Art.Mine.Px.Length; at++)
            if (Art.Mine.Px[at] != 0 && Art.MineCold.Px[at] != 0 && Art.Mine.Px[at] != Art.MineCold.Px[at])
                return (at % Art.Mine.W, at / Art.Mine.W, Art.Mine.Px[at] & EnemyKit.RgbMask, Art.MineCold.Px[at] & EnemyKit.RgbMask);
        throw new InvalidOperationException("the lit and cold mine are the same picture");
    }

    /// <summary>Where <paramref name="enemy"/>'s first opaque sprite pixel lands on the field, and its colour unlit.</summary>
    private static (int X, int Y, uint Colour) FirstSpritePixel(Enemy enemy)
    {
        var (x0, y0) = SpriteCorner(enemy);
        int at = Array.FindIndex(enemy.Sprite.Px, pixel => pixel != 0);
        return (x0 + at % enemy.Sprite.W, y0 + at / enemy.Sprite.W, enemy.Sprite.Px[at] & EnemyKit.RgbMask);
    }

    /// <summary>Every field pixel in <paramref name="colour"/> outside the rectangle (x, y, w, h), as "x,y" in reading order.</summary>
    private static List<string> Pixels(Surface field, uint colour, (int X, int Y, int W, int H) skip)
    {
        var found = new List<string>();
        for (int y = 0; y < field.H; y++)
            for (int x = 0; x < field.W; x++)
                if (field.Get(x, y) == colour && !(x >= skip.X && x < skip.X + skip.W && y >= skip.Y && y < skip.Y + skip.H)) found.Add($"{x},{y}");
        return found;
    }

    /// <summary>Points as "x,y" in reading order, the form <see cref="Pixels"/> gives.</summary>
    private static string[] PixelList(params (int X, int Y)[] points) =>
        points.OrderBy(point => point.Y).ThenBy(point => point.X).Select(point => $"{point.X},{point.Y}").ToArray();

    private static (int X, int Y, int W, int H) Body(Enemy enemy)
    {
        var (x0, y0) = SpriteCorner(enemy);
        return (x0, y0, enemy.Sprite.W, enemy.Sprite.H);
    }

    private static uint FgAt(Screen screen, int col, int row) => screen.Fg[row * screen.Cols + col];

    // ---------------------------------------------------------------- the screen around the field

    [Fact]
    public void The_rails_stand_in_the_columns_either_side_of_the_field_on_every_row()
    {
        var scr = new Screen(EnemyKit.BigCols, EnemyKit.BigRows);
        var game = Scene();
        Draw(game, scr);
        var lay = game.Layout;
        for (int row = 0; row < scr.Rows; row++)
        {
            foreach (int col in new[] { lay.Col0 - 1, lay.Col0 + lay.PCols })
            {
                Assert.Equal(RaiderView.RailGlyph, scr.CharAt(col, row));
                Assert.Equal(RaiderView.Rail, FgAt(scr, col, row));
                Assert.Equal(RaiderView.Ground, scr.BgAt(col, row));
            }
            Assert.NotEqual(RaiderView.RailGlyph, scr.CharAt(lay.Col0 - 2, row));
        }
    }

    [Fact]
    public void A_screen_too_small_for_the_field_says_so_in_the_ground_colours_and_draws_nothing_else()
    {
        var scr = new Screen(20, 4);
        new RaiderView().FitAndRender(new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true }, scr);
        const int col = RaiderView.TooSmallCol, row = RaiderView.TooSmallRow;
        Assert.Equal(RaiderView.TooSmallMessage, Read(scr, row, col, RaiderView.TooSmallMessage.Length));
        Assert.Equal(RaiderView.TooSmallText, FgAt(scr, col, row));
        Assert.Equal(RaiderView.Ground, scr.BgAt(col, row));
        Assert.Equal(' ', scr.CharAt(0, 0));
    }

    [Fact]
    public void A_shaking_screen_reads_the_field_shifted_by_the_shake_in_both_directions()
    {
        var game = Scene(7);
        game.AddShake(6);
        var (shakeX, shakeY) = game.ShakeOffset();
        Assert.True(shakeX != 0 && shakeY != 0, $"the test needs a shake that moves both ways, got {shakeX},{shakeY}");
        var scr = new Screen(EnemyKit.BigCols, EnemyKit.BigRows);
        var field = Draw(game, scr);
        // the same field read by hand: each pixel from (x - shakeX, y - shakeY), the edges repeating
        var shifted = new Surface(field.W, field.H);
        for (int y = 0; y < field.H; y++)
            for (int x = 0; x < field.W; x++)
                shifted.Set(x, y, field.Get(Math.Clamp(x - shakeX, 0, field.W - 1), Math.Clamp(y - shakeY, 0, field.H - 1)));
        var want = new Screen(EnemyKit.BigCols, EnemyKit.BigRows);
        new QuadComposer().Compose(shifted, 0, 0, want, game.Layout.Col0, 0);
        const int bottomTextRows = 6;
        for (int row = RaiderView.HudRows; row < field.H / CardField.PixelsPerCell - bottomTextRows; row++)
            for (int col = game.Layout.Col0; col < game.Layout.Col0 + game.Layout.PCols; col++)
            {
                int at = row * scr.Cols + col;
                Assert.True(want.Ch[at] == scr.Ch[at] && want.Fg[at] == scr.Fg[at] && want.Bg[at] == scr.Bg[at], $"cell {col},{row} differs from the shifted field");
            }
    }

    // ---------------------------------------------------------------- mines

    [Fact]
    public void An_unarmed_mine_lights_for_its_last_twelve_frames_on_the_lit_beat_and_is_cold_before_that()
    {
        var (dx, dy, lit, cold) = MineProbe();
        foreach (var (grace, want) in new[] { (RaiderView.MineBlinkFrames, lit), (RaiderView.MineBlinkFrames + 1, cold) })
        {
            var game = Scene();
            var enemy = EnemyKit.Place(game, EnemyKind.Mine, EnemyX, EnemyY);
            enemy.Grace = grace;
            var (x0, y0) = SpriteCorner(enemy);
            Assert.Equal(want, Draw(game).Get(x0 + dx, y0 + dy));
        }
    }

    [Fact]
    public void An_unarmed_mine_is_cold_on_the_off_beat_of_the_blink_however_near_it_is_to_arming()
    {
        var (dx, dy, _, cold) = MineProbe();
        var game = Scene(RaiderView.FastBlinkFrames);
        var enemy = EnemyKit.Place(game, EnemyKind.Mine, EnemyX, EnemyY);
        enemy.Grace = 1;
        var (x0, y0) = SpriteCorner(enemy);
        Assert.Equal(cold, Draw(game).Get(x0 + dx, y0 + dy));
    }

    [Fact]
    public void An_armed_mine_pulses_toward_white_every_other_eight_frames_and_a_hit_one_flashes_whiter()
    {
        var (dx, dy, lit, _) = MineProbe();
        foreach (var (frame, flash, white) in new[] { (0, 0, RaiderView.MineGlint), (RaiderView.MineGlintFrames, 0, 0.0), (0, 1, RaiderView.FlashWhite) })
        {
            var game = Scene(frame);
            var enemy = EnemyKit.Place(game, EnemyKind.Mine, EnemyX, EnemyY);
            enemy.Flash = flash;
            var (x0, y0) = SpriteCorner(enemy);
            uint want = white > 0 ? Surface.Mix(lit, Palette.White, white) : lit;
            Assert.Equal(want, Draw(game).Get(x0 + dx, y0 + dy));
        }
    }

    // ---------------------------------------------------------------- the diver's tell

    [Fact]
    public void A_diver_in_its_windup_blinks_toward_white_on_alternate_pairs_of_frames()
    {
        foreach (var (frame, white) in new[] { (0, RaiderView.DiverWindupWhite), (RaiderView.FastBlinkFrames, 0.0) })
        {
            var game = Scene(frame);
            var enemy = EnemyKit.Place(game, EnemyKind.Diver, EnemyX, AimerY);
            enemy.Windup = Tuning.DiverWindupFrames; enemy.AimX = EnemyX; enemy.AimY = AimBelowY;
            var (x, y, colour) = FirstSpritePixel(enemy);
            Assert.Equal(white > 0 ? Surface.Mix(colour, Palette.White, white) : colour, Draw(game).Get(x, y));
        }
    }

    [Fact]
    public void A_dashing_diver_glints_toward_white_on_alternate_triples_of_frames()
    {
        foreach (var (frame, white) in new[] { (0, RaiderView.DiverDashGlint), (RaiderView.StrobeFrames, 0.0) })
        {
            var game = Scene(frame);
            var enemy = EnemyKit.Place(game, EnemyKind.Diver, EnemyX, AimerY);
            enemy.Dashing = true;
            var (x, y, colour) = FirstSpritePixel(enemy);
            Assert.Equal(white > 0 ? Surface.Mix(colour, Palette.White, white) : colour, Draw(game).Get(x, y));
        }
    }

    [Fact]
    public void A_diver_marks_the_spot_it_locked_with_a_cross_two_pixels_out_that_blinks_between_lime_and_white()
    {
        const int aimX = SideAimX, aimY = SpotY, reach = RaiderView.DiverCrossRadius;
        foreach (var (frame, colour) in new[] { (0, Art.DiverLime), (RaiderView.StrobeFrames, Palette.White) })
        {
            var game = Scene(frame);
            var enemy = EnemyKit.Place(game, EnemyKind.Diver, EnemyX, AimerY);
            enemy.Windup = Tuning.DiverWindupFrames; enemy.AimX = aimX; enemy.AimY = aimY;        // off to the side, so the dotted line passes the cross only on its way in
            var field = Draw(game);
            for (int step = -reach; step <= reach; step++)
            {
                Assert.Equal(colour, field.Get(aimX + step, aimY + step));
                Assert.Equal(colour, field.Get(aimX + step, aimY - step));
            }
            foreach (int sideX in new[] { -1, 1 })
                foreach (int sideY in new[] { -1, 1 })
                    Assert.NotEqual(colour, field.Get(aimX + sideX * (reach + 1), aimY + sideY * (reach + 1)));
        }
    }

    // ---------------------------------------------------------------- the turret's tell

    [Fact]
    public void A_turret_whitens_as_its_windup_runs_down()
    {
        foreach (var (windup, white) in new[] { (Tuning.TurretWindupFrames, 0.0), (1, 1 - 1.0 / Tuning.TurretWindupFrames) })
        {
            var game = Scene();
            var enemy = EnemyKit.Place(game, EnemyKind.Turret, EnemyX, AimerY);
            enemy.Windup = windup; enemy.AimX = EnemyX; enemy.AimY = AimBelowY;
            var (x, y, colour) = FirstSpritePixel(enemy);
            double whiteness = white * RaiderView.TurretWindupWhiteMax;
            Assert.Equal(whiteness > 0 ? Surface.Mix(colour, Palette.White, whiteness) : colour, Draw(game).Get(x, y));
        }
    }

    [Fact]
    public void A_turrets_reticle_starts_six_pixels_out_and_closes_to_two_as_the_windup_runs_down()
    {
        const int aimX = SideAimX, aimY = SpotY;
        foreach (var (windup, reach) in new[] { (Tuning.TurretWindupFrames, RaiderView.TurretReticleMin + RaiderView.TurretReticleSpan), (7, 3), (1, RaiderView.TurretReticleMin) })
        {
            var game = Scene();
            var enemy = EnemyKit.Place(game, EnemyKind.Turret, EnemyX, AimerY);
            enemy.Windup = windup; enemy.AimX = aimX; enemy.AimY = aimY;
            var want = new List<(int, int)> { (aimX, aimY) };
            foreach (int sideX in new[] { -1, 1 })
                foreach (int sideY in new[] { -1, 1 })
                {
                    want.Add((aimX + sideX * reach, aimY + sideY * reach));
                    want.Add((aimX + sideX * (reach - 1), aimY + sideY * reach));
                    want.Add((aimX + sideX * reach, aimY + sideY * (reach - 1)));
                }
            Assert.Equal(PixelList(want.ToArray()), Pixels(Draw(game), Art.TurretGreen, Body(enemy)).ToArray());
        }
    }

    [Fact]
    public void A_turret_lights_its_barrel_pixel_under_the_sprite_only_while_winding_up()
    {
        foreach (var (windup, lit) in new[] { (Tuning.TurretWindupFrames, true), (0, false) })
        {
            var game = Scene();
            var enemy = EnemyKit.Place(game, EnemyKind.Turret, EnemyX, AimerY);
            enemy.Windup = windup; enemy.AimX = SideAimX; enemy.AimY = SpotY;
            var (x0, y0) = SpriteCorner(enemy);
            var field = Draw(game);
            Assert.Equal(lit, field.Get(x0 + enemy.Sprite.W / 2, y0 + enemy.Sprite.H) == Palette.White);
            Assert.NotEqual(Palette.White, field.Get(x0 + enemy.Sprite.W / 2, y0 + enemy.Sprite.H + 1));
        }
    }

    // ---------------------------------------------------------------- the splitter's cracks

    [Fact]
    public void A_splitter_at_half_hit_points_or_less_shows_its_cracks_and_a_seam_that_blinks_between_white_and_pink()
    {
        const int inner = RaiderView.SplitterCrackInner, outer = RaiderView.SplitterCrackOuter;
        foreach (var (frame, seam) in new[] { (0, Palette.White), (RaiderView.StrobeFrames, Art.SplitterPink) })
        {
            var game = Scene(frame);
            var enemy = EnemyKit.Place(game, EnemyKind.Splitter, EnemyX, EnemyY);
            enemy.Hp = enemy.MaxHp / 2;
            var (x0, y0) = SpriteCorner(enemy);
            int midX = x0 + enemy.Sprite.W / 2, midY = y0 + enemy.Sprite.H / 2;
            var field = Draw(game);
            foreach (var (x, y) in new[] { (midX - inner, midY - 1), (midX - outer, midY), (midX + inner, midY + 1), (midX + outer, midY) })
                Assert.Equal(Palette.Plum, field.Get(x, y));
            foreach (var (x, y) in new[] { (midX, midY - 1), (midX, midY), (midX, midY + 1) }) Assert.Equal(seam, field.Get(x, y));
            Assert.NotEqual(seam, field.Get(midX, midY + 2));
            Assert.NotEqual(Palette.Plum, field.Get(midX - inner, midY - 2));
        }
    }

    [Fact]
    public void A_splitter_above_half_hit_points_is_not_cracked()
    {
        var game = Scene();
        var enemy = EnemyKit.Place(game, EnemyKind.Splitter, EnemyX, EnemyY);
        enemy.Hp = enemy.MaxHp / 2 + 0.5;
        var (x0, y0) = SpriteCorner(enemy);
        Assert.NotEqual(Palette.Plum, Draw(game).Get(x0 + enemy.Sprite.W / 2 - RaiderView.SplitterCrackOuter, y0 + enemy.Sprite.H / 2));
    }

    // ---------------------------------------------------------------- the gunship's health bar

    [Fact]
    public void A_wounded_gunship_wears_a_one_pixel_bar_on_the_bar_row_above_it_filled_in_proportion_to_its_hit_points()
    {
        var game = Scene();
        var enemy = EnemyKit.Place(game, EnemyKind.Gunship, SpotX, GunshipY);
        enemy.Hp = enemy.MaxHp / 2;
        var field = Draw(game);
        var (x0, top) = SpriteCorner(enemy);
        int width = enemy.Sprite.W, y0 = RaiderView.BarRow(top), fill = (int)(width * (enemy.Hp / enemy.MaxHp));
        Assert.Equal(Palette.Crimson, field.Get(x0, y0));
        Assert.Equal(Palette.Crimson, field.Get(x0 + fill - 1, y0));
        Assert.Equal(Palette.Plum, field.Get(x0 + fill, y0));
        Assert.Equal(Palette.Plum, field.Get(x0 + width - 1, y0));
        Assert.NotEqual(Palette.Plum, field.Get(x0 + width, y0));
        Assert.NotEqual(Palette.Plum, field.Get(x0, y0 + 1));      // one pixel tall
        Assert.NotEqual(Palette.Crimson, field.Get(x0, y0 + 1));
    }

    [Fact]
    public void A_gunship_at_full_hit_points_has_no_bar()
    {
        var game = Scene();
        var enemy = EnemyKit.Place(game, EnemyKind.Gunship, SpotX, GunshipY);
        enemy.Hp = enemy.MaxHp;
        var (x0, top) = SpriteCorner(enemy);
        Assert.NotEqual(Palette.Plum, Draw(game).Get(x0, RaiderView.BarRow(top)));
    }

    // ---------------------------------------------------------------- bullets

    [Fact]
    public void A_turret_bolt_is_a_teal_diamond_around_a_white_centre()
    {
        var game = Scene();
        game.Bullets.Add(new Bullet { X = SpotX, Y = SpotY, Aimed = true });
        var field = Draw(game);
        foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1) }) Assert.Equal(Art.TurretGreen, field.Get(SpotX + dx, SpotY + dy));
        Assert.Equal(Palette.White, field.Get(SpotX, SpotY));
        Assert.NotEqual(Art.TurretGreen, field.Get(SpotX - 1, SpotY - 1));
        Assert.NotEqual(Art.TurretGreen, field.Get(SpotX - 2, SpotY));
    }

    [Fact]
    public void An_enemy_bolt_is_a_two_by_two_crimson_square_with_a_white_corner_at_its_position()
    {
        var game = Scene();
        game.Bullets.Add(new Bullet { X = SpotX, Y = SpotY });
        var field = Draw(game);
        foreach (var (dx, dy) in new[] { (-1, -1), (0, -1), (-1, 0) }) Assert.Equal(Palette.Crimson, field.Get(SpotX + dx, SpotY + dy));
        Assert.Equal(Palette.White, field.Get(SpotX, SpotY));
        foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1), (-1, 1), (-2, -1) }) Assert.NotEqual(Palette.Crimson, field.Get(SpotX + dx, SpotY + dy));
    }

    [Fact]
    public void The_ships_shot_is_a_four_pixel_pale_bar_under_a_white_tip()
    {
        const int shotX = SpotX, shotY = SpotY, barTop = shotY - RaiderView.ShotLength / 2;
        var game = Scene();
        game.Bullets.Add(new Bullet { X = shotX, Y = shotY, Friendly = true });
        var field = Draw(game);
        for (int y = barTop; y < barTop + RaiderView.ShotLength; y++) Assert.Equal(Palette.Ice, field.Get(shotX, y));
        Assert.Equal(Palette.White, field.Get(shotX, barTop - 1));
        Assert.NotEqual(Palette.White, field.Get(shotX, barTop - 2));
        Assert.NotEqual(Palette.Ice, field.Get(shotX, barTop + RaiderView.ShotLength));
    }

    // ---------------------------------------------------------------- rings, sparks and the flash

    /// <summary>The field with and without whatever <paramref name="add"/> puts in the game, at the same frame.</summary>
    private static (Surface Bare, Surface Drawn) Pair(int frame, Action<RaiderGame> add, Action<RaiderGame>? prep = null)
    {
        var bare = Scene(frame); prep?.Invoke(bare);
        var added = Scene(frame); prep?.Invoke(added); add(added);
        return (Draw(bare), Draw(added));
    }

    /// <summary>The picture a test expects: <paramref name="bare"/> with <paramref name="paint"/> applied through Surface's own drawing calls.</summary>
    private static void AssertPainted(Surface bare, Surface drawn, Action<Surface> paint)
    {
        var expected = new Surface(bare.W, bare.H);
        Array.Copy(bare.P, expected.P, bare.P.Length);
        paint(expected);
        Assert.True(expected.P.SequenceEqual(drawn.P), "the field is not what the drawing calls give");
        Assert.False(bare.P.SequenceEqual(drawn.P), "the thing drew nothing");
    }

    [Fact]
    public void A_burst_ring_is_at_its_eased_radius_and_fades_from_its_edge_to_a_dimmer_trail_inside()
    {
        var (bare, drawn) = Pair(0, game => game.Rings.Add(new Ring
        {
            X = SpotX,
            Y = SpotY,
            R0 = CardRig.RingStartRadius,
            R1 = CardRig.RingEndRadius,
            Life = CardRig.RingLife,
            Max = CardRig.RingMaxLife,
            Colour = Palette.Crimson,
        }));
        // halfway through its life it has eased out by 1 - 0.5^2 = 0.75 of the way from 2 to 12, and holds half its strength
        const double radius = 9.5, fade = 0.5;
        AssertPainted(bare, drawn, surface =>
        {
            surface.Ring(SpotX, SpotY, radius, Palette.Crimson, RaiderView.RingAlphaMin + RaiderView.RingAlphaFade * fade);
            surface.Ring(SpotX, SpotY, radius - 1, Palette.Crimson, RaiderView.RingInnerAlpha * fade);
        });
    }

    [Fact]
    public void A_small_ring_has_no_trail_inside_it()
    {
        const int radius = RaiderView.RingDoubledFromRadius;
        var (bare, drawn) = Pair(0, game => game.Rings.Add(new Ring { X = SpotX, Y = SpotY, R0 = radius, R1 = CardRig.RingEndRadius, Life = CardRig.RingMaxLife, Max = CardRig.RingMaxLife, Colour = Palette.Crimson }));
        AssertPainted(bare, drawn, surface => surface.Ring(SpotX, SpotY, radius, Palette.Crimson, RaiderView.RingAlphaMin + RaiderView.RingAlphaFade));
    }

    [Fact]
    public void A_spark_fades_toward_the_night_colour_in_its_last_frames_and_is_its_own_colour_before_that()
    {
        const int halfLife = SparkLife / 2;
        var (bare, drawn) = Pair(0, game =>
        {
            game.Particles.Add(new Particle { X = LeftSparkX, Y = SparkY, Life = halfLife, MaxLife = SparkLife, Colour = SparkColour });
            game.Particles.Add(new Particle { X = RightSparkX, Y = SparkY, Life = SparkLife, MaxLife = SparkLife, Colour = SparkColour });
        });
        double fade = (double)halfLife / SparkLife * RaiderView.ParticleFadeRate;
        AssertPainted(bare, drawn, surface => { surface.Set(LeftSparkX, SparkY, Surface.Mix(Palette.SkyTop, SparkColour, fade)); surface.Set(RightSparkX, SparkY, SparkColour); });
    }

    [Fact]
    public void A_spark_is_as_big_as_its_size_and_a_heart_is_the_five_pixel_heart_with_a_tail()
    {
        const int size = 2;
        var (bare, drawn) = Pair(0, game =>
        {
            game.Particles.Add(new Particle { X = LeftSparkX, Y = SparkY, Life = SparkLife, MaxLife = SparkLife, Colour = SparkColour, Size = size });
            game.Particles.Add(new Particle { X = RightSparkX, Y = SparkY, Life = SparkLife, MaxLife = SparkLife, Colour = SparkColour, Kind = ParticleKind.Heart });
        });
        AssertPainted(bare, drawn, surface =>
        {
            surface.Rect(LeftSparkX, SparkY, size, size, SparkColour);
            // the heart is three pixels wide, centred on its x with its top row at its y
            const int heartLeft = RightSparkX - 1, heartRight = RightSparkX + 1;
            surface.Set(heartLeft, SparkY, SparkColour); surface.Set(heartRight, SparkY, SparkColour);
            surface.Rect(heartLeft, SparkY + 1, heartRight - heartLeft + 1, 1, SparkColour); surface.Set(RightSparkX, SparkY + 2, SparkColour);
        });
    }

    [Fact]
    public void The_screen_flash_washes_the_whole_field_toward_white_by_up_to_half()
    {
        var (bare, drawn) = Pair(0, game => HoldRig.Force(game, nameof(RaiderGame.ScreenFlash), Tuning.ScreenFlashFrames));
        Assert.Equal(bare.P.Select(pixel => Surface.Mix(pixel, Palette.White, RaiderView.ScreenFlashMaxWhite)), drawn.P);
        var (bareHalf, drawnHalf) = Pair(0, game => HoldRig.Force(game, nameof(RaiderGame.ScreenFlash), Tuning.ScreenFlashFrames / 2));
        Assert.Equal(bareHalf.P.Select(pixel => Surface.Mix(pixel, Palette.White, RaiderView.ScreenFlashMaxWhite / 2)), drawnHalf.P);
    }

    // ---------------------------------------------------------------- pickups

    [Fact]
    public void A_pickup_is_a_capsule_in_its_colour_with_a_glow_ring_that_pulses_and_a_mark_for_its_kind()
    {
        foreach (var kind in new[] { PickupKind.Shield, PickupKind.Bomb, PickupKind.Weapon })
        {
            var (bare, drawn) = Pair(0, game => game.Pickups.Add(new Pickup { Kind = kind, X = SpotX, Y = SpotY, BaseX = SpotX }));
            uint colour = RaiderGame.PickupColour(kind);
            uint dark = Surface.Mix(colour, Palette.SkyTop, RaiderView.PickupDarken), light = Surface.Mix(colour, Palette.White, RaiderView.PickupLighten);
            const double pulse = RaiderView.PulseMid;                     // frame 0, age 0
            AssertPainted(bare, drawn, surface =>
            {
                surface.Ring(SpotX, SpotY, RaiderView.PickupGlowBase + pulse * RaiderView.PickupGlowSwing, colour, RaiderView.PickupGlowAlphaBase + RaiderView.PickupGlowAlphaSwing * pulse);
                // the capsule is a square centred on the pickup, its corners cut and a ring of dark round its body
                const int side = 6, body = side - 2, x = SpotX - side / 2, y = SpotY - side / 2;
                surface.Rect(x + 1, y, body, side, dark); surface.Rect(x, y + 1, side, body, dark); surface.Rect(x + 1, y + 1, body, body, colour);
                if (kind == PickupKind.Shield) { surface.Rect(x + 2, y + 1, 2, 4, light); surface.Rect(x + 1, y + 2, 4, 1, light); }
                else if (kind == PickupKind.Bomb) surface.Rect(x + 2, y + 2, 2, 2, light);
                else { surface.Set(x + 2, y + 1, light); surface.Set(x + 3, y + 1, light); surface.Rect(x + 1, y + 2, 4, 1, light); surface.Rect(x + 2, y + 3, 2, 2, light); }
            });
        }
    }

    // ---------------------------------------------------------------- the ship

    private static uint ShipAt(RaiderGame game, out int x, out int y)
    {
        int at = Array.FindIndex(game.Ship.Px, pixel => pixel != 0);
        x = (int)Math.Round(game.PX - game.Ship.W / 2.0) + at % game.Ship.W;
        y = (int)Math.Round(game.PY - game.Ship.H / 2.0) + at / game.Ship.W;
        return game.Ship.Px[at] & EnemyKit.RgbMask;
    }

    [Fact]
    public void The_ship_is_drawn_while_alive_and_gone_while_waiting_to_respawn()
    {
        var game = Scene();
        uint colour = ShipAt(game, out int x, out int y);
        Assert.Equal(colour, Draw(game).Get(x, y));
        var dead = Scene();
        HoldRig.Force(dead, nameof(RaiderGame.RespawnIn), Tuning.RespawnFrames);
        Assert.NotEqual(colour, Draw(dead).Get(x, y));
    }

    [Fact]
    public void A_ship_with_invulnerable_frames_blinks_three_frames_on_and_three_off_unless_a_shield_is_the_cause()
    {
        const int strobe = RaiderView.StrobeFrames;
        foreach (var (frame, shield, shown) in new[]
        {
            (0, false, false), (strobe - 1, false, false), (strobe, false, true), (2 * strobe - 1, false, true), (2 * strobe, false, false), (0, true, true),
        })
        {
            var game = Scene(frame);
            HoldRig.Force(game, nameof(RaiderGame.Invuln), Tuning.InvulnFrames); HoldRig.Force(game, nameof(RaiderGame.InvulnIsShield), shield);
            uint colour = ShipAt(game, out int x, out int y);
            Assert.Equal(shown, Draw(game).Get(x, y) == colour);
        }
    }

    [Fact]
    public void The_exhaust_flame_grows_from_one_to_three_pixels_with_the_frame_and_the_muzzle_flash_is_a_two_part_burst_over_the_nose()
    {
        for (int frame = 0; frame <= RaiderView.FlameFlicker; frame++)
        {
            var game = Scene(frame);
            var field = Draw(game);
            int x = (int)Math.Round(game.PX), y = (int)Math.Round(game.PY);
            for (int below = 0; below <= RaiderView.FlameFlicker; below++)
                Assert.Equal(below < 1 + frame % RaiderView.FlameFlicker, field.Get(x, y + game.Ship.H / 2 + below) == RaiderView.Flame);
        }
        var firing = Scene();
        HoldRig.Force(firing, nameof(RaiderGame.MuzzleFrames), Tuning.MuzzleFrames);
        var (bare, drawn) = (Draw(Scene()), Draw(firing));
        int noseX = (int)Math.Round(firing.PX), noseY = (int)Math.Round(firing.PY) - firing.Ship.H / 2 - 1;
        const int wide = RaiderView.MuzzleFlashLong, narrow = RaiderView.MuzzleFlashShort;
        AssertPainted(bare, drawn, surface =>
        {
            surface.Rect(noseX - wide / 2, noseY - narrow / 2, wide, narrow, RaiderView.ShotGlow);
            surface.Rect(noseX - narrow / 2, noseY - wide / 2, narrow, wide, Palette.White);
        });
    }

    [Fact]
    public void A_shield_draws_two_rings_round_the_ship_with_one_fixed_strength()
    {
        var (bare, drawn) = Pair(0, game => HoldRig.Force(game, nameof(RaiderGame.Shield), Tuning.ShieldHits), game => HoldRig.Force(game, nameof(RaiderGame.Shield), LikeHoldTests.NoShield));
        var ship = Scene();
        uint shield = RaiderGame.PickupColour(PickupKind.Shield);
        const double alpha = RaiderView.ShieldBaseAlpha + RaiderView.ShieldPulseAlpha * RaiderView.PulseMid;
        AssertPainted(bare, drawn, surface =>
        {
            surface.Ring(ship.PX, ship.PY, RaiderView.ShieldRadius, shield, alpha);
            surface.Ring(ship.PX, ship.PY, RaiderView.ShieldRadius - RaiderView.ShieldInnerGap, shield, alpha * RaiderView.ShieldInnerAlpha);
        });
    }

    [Fact]
    public void A_shield_that_took_a_hit_this_frame_or_the_last_five_flashes_a_white_ring_that_widens()
    {
        const int frame = 10;
        foreach (int since in new[] { 0, RaiderView.ShieldHitFlashFrames - 1 })
        {
            var (bare, drawn) = Pair(frame, game => HoldRig.Force(game, nameof(RaiderGame.ShieldHitFrame), (long)frame - since), game => HoldRig.Force(game, nameof(RaiderGame.Shield), Tuning.ShieldHits));
            var ship = Scene(frame);
            AssertPainted(bare, drawn, surface =>
                surface.Ring(ship.PX, ship.PY, RaiderView.ShieldRadius + since, Palette.White, RaiderView.ShieldHitFlashAlpha - since * RaiderView.ShieldHitFlashFade));
        }
        var (bareLate, drawnLate) = Pair(frame, game => HoldRig.Force(game, nameof(RaiderGame.ShieldHitFrame), (long)frame - RaiderView.ShieldHitFlashFrames),
            game => HoldRig.Force(game, nameof(RaiderGame.Shield), Tuning.ShieldHits));
        Assert.Equal(bareLate.P, drawnLate.P);
    }

    [Fact]
    public void A_powered_ship_wears_a_pink_ring_and_in_its_last_second_a_white_one_flickers_on_for_three_frames_in_six()
    {
        static double PowerRadius(int frame) =>
            RaiderView.PowerRingBase + (RaiderView.PulseMid + RaiderView.PulseMid * Math.Sin(frame * RaiderView.PowerPulseRate)) * RaiderView.PowerRingSwing;
        const int warnOn = RaiderView.PowerWarnOn - 1, warnOff = RaiderView.PowerWarnOn;
        const long plenty = 2L * Tuning.Fps, lastSecond = Tuning.Fps / 2;
        var ship = Scene(warnOn);
        var (bare, drawn) = Pair(warnOn, game => HoldRig.Force(game, LikeHoldTests.PowerUntilField, game.Tick + plenty));
        AssertPainted(bare, drawn, surface => surface.Ring(ship.PX, ship.PY, PowerRadius(warnOn), Palette.Pink, RaiderView.PowerRingAlpha));
        var (bareWarn, drawnWarn) = Pair(warnOn, game => HoldRig.Force(game, LikeHoldTests.PowerUntilField, game.Tick + lastSecond));
        AssertPainted(bareWarn, drawnWarn, surface =>
        {
            surface.Ring(ship.PX, ship.PY, PowerRadius(warnOn), Palette.Pink, RaiderView.PowerRingAlpha);
            surface.Ring(ship.PX, ship.PY, RaiderView.PowerWarnRadius, Palette.White, RaiderView.PowerWarnAlpha);
        });
        var (bareDark, drawnDark) = Pair(warnOff, game => HoldRig.Force(game, LikeHoldTests.PowerUntilField, game.Tick + lastSecond));
        var shipLater = Scene(warnOff);
        AssertPainted(bareDark, drawnDark, surface => surface.Ring(shipLater.PX, shipLater.PY, PowerRadius(warnOff), Palette.Pink, RaiderView.PowerRingAlpha));
    }

    // ---------------------------------------------------------------- the HUD

    private static Screen RenderScreen(RaiderGame game, out FieldLayout lay)
    {
        var scr = new Screen(EnemyKit.BigCols, EnemyKit.BigRows);
        new RaiderView().FitAndRender(game, scr);
        lay = FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows);
        return scr;
    }

    /// <summary>The text on a screen row from a column, <paramref name="len"/> cells long.</summary>
    private static string Read(Screen screen, int row, int col, int len) => new(screen.Ch.AsSpan(row * screen.Cols + col, len));

    private static void AssertColours(Screen screen, int row, int col, int len, uint fg, uint bg)
    {
        for (int offset = 0; offset < len; offset++)
        {
            Assert.Equal(fg, FgAt(screen, col + offset, row));
            Assert.Equal(bg, screen.BgAt(col + offset, row));
        }
    }

    [Fact]
    public void The_hud_shows_the_score_in_six_digits_and_the_bombs_in_ice()
    {
        const string score = "SCORE 001234";
        var game = Scene();
        HoldRig.Force(game, nameof(RaiderGame.Score), 1234);
        var scr = RenderScreen(game, out var lay);
        int col0 = lay.Col0, bombCol = col0 + RaiderView.HudBombCol;
        Assert.Equal(score, Read(scr, 0, col0 + 1, score.Length));
        AssertColours(scr, 0, col0 + 1, score.Length, Palette.White, RaiderView.HudBg);
        string bombs = "B" + new string(RaiderView.BombDot, Tuning.ShipBombs).PadRight(Tuning.BombCap);
        Assert.Equal(bombs, Read(scr, 0, bombCol, bombs.Length));
        AssertColours(scr, 0, bombCol, 1 + Tuning.ShipBombs, Palette.Ice, RaiderView.HudBg);
        AssertColours(scr, 0, col0, 1, Palette.White, RaiderView.HudBg);                        // both HUD rows are a band of their own
        AssertColours(scr, 1, col0 + lay.PCols - 1, 1, Palette.White, RaiderView.HudBg);
    }

    [Fact]
    public void The_weapon_pips_light_their_label_only_when_there_is_something_to_show()
    {
        const string pips = "W█•••";
        const int litCells = 2;
        var game = Scene();
        var scr = RenderScreen(game, out var lay);
        int weaponCol = lay.Col0 + RaiderView.HudBombCol;
        uint weapon = RaiderGame.PickupColour(PickupKind.Weapon);
        Assert.Equal(pips, Read(scr, 1, weaponCol, pips.Length));             // the weapon starts at level one of four
        AssertColours(scr, 1, weaponCol, litCells, weapon, RaiderView.HudBg);
        AssertColours(scr, 1, weaponCol + litCells, pips.Length - litCells, Palette.Dim, RaiderView.HudBg);
    }

    [Fact]
    public void The_hud_shows_no_hearts_and_one_shield_mark_that_is_lit_with_the_shield_and_dark_without()
    {
        const string up = "S█", down = "S•";
        var game = Scene();
        var scr = RenderScreen(game, out var lay);
        int col0 = lay.Col0, shieldCol = col0 + RaiderView.HudShieldCol;
        uint shield = RaiderGame.PickupColour(PickupKind.Shield);
        Assert.Equal(Tuning.ShieldHits, game.Shield);                   // a run starts with the shield up
        Assert.Equal(up, Read(scr, 0, shieldCol, up.Length));
        AssertColours(scr, 0, shieldCol, up.Length, shield, RaiderView.HudBg);
        // the weapon's pips are its label cell and a cell per level; past them the second row is blank to the field's edge, since
        // the shield's one mark is on the first row and the power label shows only while powered
        int weaponEnd = col0 + RaiderView.HudBombCol + 1 + Tuning.MaxWeaponLevel, fieldEnd = col0 + lay.PCols;
        Assert.Equal(new string(' ', fieldEnd - weaponEnd), Read(scr, 1, weaponEnd, fieldEnd - weaponEnd));
        HoldRig.Force(game, nameof(RaiderGame.Shield), LikeHoldTests.NoShield);
        scr = RenderScreen(game, out _);
        Assert.Equal(down, Read(scr, 0, shieldCol, down.Length));
        AssertColours(scr, 0, shieldCol, down.Length, Palette.Dim, RaiderView.HudBg);
        for (int row = 0; row < RaiderView.HudRows; row++)
            for (int col = col0; col < col0 + lay.PCols; col++)
                Assert.NotEqual(RaiderView.PowerHeart, scr.CharAt(col, row));
    }

    [Fact]
    public void The_wave_label_is_dim_when_no_one_is_live_and_hot_pink_when_a_live_enemy_is_on_the_field()
    {
        const string clear = "○ clear", live = "◉ LIVE spam ×1";
        var game = Scene();
        var scr = RenderScreen(game, out var lay);
        int labelCol = lay.Col0 + 1;
        Assert.Equal(clear, Read(scr, 1, labelCol, clear.Length));
        AssertColours(scr, 1, labelCol, clear.Length, Palette.Dim, RaiderView.HudBg);
        var enemy = EnemyKit.Place(game, EnemyKind.Grunt, SpotX, SpotY);
        enemy.Live = true; enemy.Source = DarknessKind.Spam;
        scr = RenderScreen(game, out _);
        Assert.Equal(live, Read(scr, 1, labelCol, live.Length));
        AssertColours(scr, 1, labelCol, live.Length, RaiderView.LiveWaveColour, RaiderView.HudBg);
    }

    [Fact]
    public void The_stage_readout_shrinks_to_fit_its_room_and_a_title_never_names_stage_zero()
    {
        const string stageOne = "STAGE 1", shortThree = "ST 3";
        Assert.Equal("STAGE 3", RaiderView.StageReadout(3, 7));
        Assert.Equal(shortThree, RaiderView.StageReadout(3, 6));
        Assert.Equal(shortThree, RaiderView.StageReadout(3, 4));
        Assert.Equal("3", RaiderView.StageReadout(3, 3));
        Assert.Equal(stageOne, RaiderView.StageTitle(0));
        Assert.Equal("STAGE " + Tuning.StageCap, RaiderView.StageTitle(99));
        var scr = RenderScreen(Scene(), out var lay);
        int stageCol = lay.Col0 + lay.PCols - stageOne.Length - 1;
        Assert.Equal(stageOne, Read(scr, 0, stageCol, stageOne.Length));
        AssertColours(scr, 0, stageCol, stageOne.Length, Palette.Ice, RaiderView.HudBg);
    }
}
