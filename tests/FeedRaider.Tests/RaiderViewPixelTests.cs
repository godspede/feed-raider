using System.Reflection;
using FeedRaider.Engine.Raider;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

// What RaiderView puts on the field, pixel for pixel. The screen only shows 2x2 cells reduced to two colours, so these
// render through the public Render and then read the field the view drew into, before the cells were made of it.
public class RaiderViewPixelTests
{
    private const uint White = 0xffffff, Crack = 0x2a1740;

    /// <summary>A game stepped to <paramref name="frame"/> with the field emptied, so a test places exactly what it draws.</summary>
    private static RaiderGame Scene(int frame = 0)
    {
        var g = new RaiderGame(5) { LoggedIn = true };
        g.SetLayout(FieldLayout.Compute(120, 46));
        for (int i = 0; i < frame; i++) g.Step();
        g.Enemies.Clear(); g.Bullets.Clear(); g.Particles.Clear(); g.Rings.Clear(); g.Pickups.Clear();
        Assert.Equal(frame, g.Frame);
        return g;
    }

    private static Enemy Place(RaiderGame g, EnemyKind kind, double x, double y)
    {
        var e = g.SpawnKind(kind, x / g.PW);
        e.X = x; e.Y = y; e.Grace = 0; e.Age = 0; e.Windup = 0; e.Flash = 0;
        return e;
    }

    /// <summary>Renders and returns the field surface the view drew, before the screen shake and the cell reduction.</summary>
    private static Surface Draw(RaiderGame g, Screen? scr = null)
    {
        var v = new RaiderView();
        v.Render(g, scr ?? new Screen(120, 46));
        return (Surface)typeof(RaiderView).GetField("_field", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(v)!;
    }

    private static (int X, int Y) Top(Enemy e) => ((int)Math.Round(e.X - e.Sprite.W / 2.0), (int)Math.Round(e.Y - e.Sprite.H / 2.0));

    /// <summary>The first sprite pixel where the lit and the cold mine differ, as a field position and the two colours.</summary>
    private static (int Dx, int Dy, uint Lit, uint Cold) MineProbe()
    {
        for (int i = 0; i < Art.Mine.Px.Length; i++)
            if (Art.Mine.Px[i] != 0 && Art.MineCold.Px[i] != 0 && Art.Mine.Px[i] != Art.MineCold.Px[i])
                return (i % Art.Mine.W, i / Art.Mine.W, Art.Mine.Px[i] & 0xffffff, Art.MineCold.Px[i] & 0xffffff);
        throw new InvalidOperationException("the lit and cold mine are the same picture");
    }

    /// <summary>Every field pixel in <paramref name="c"/> outside the rectangle (x, y, w, h), as "x,y" in reading order.</summary>
    private static List<string> Pixels(Surface f, uint c, (int X, int Y, int W, int H) skip)
    {
        var o = new List<string>();
        for (int y = 0; y < f.H; y++)
            for (int x = 0; x < f.W; x++)
                if (f.Get(x, y) == c && !(x >= skip.X && x < skip.X + skip.W && y >= skip.Y && y < skip.Y + skip.H)) o.Add($"{x},{y}");
        return o;
    }

    private static string[] Pts(params (int X, int Y)[] p) => p.OrderBy(q => q.Y).ThenBy(q => q.X).Select(q => $"{q.X},{q.Y}").ToArray();

    private static (int X, int Y, int W, int H) Body(Enemy e) => (Top(e).X, Top(e).Y, e.Sprite.W, e.Sprite.H);

    // ---------------------------------------------------------------- the screen around the field

    [Fact]
    public void The_rails_stand_in_the_columns_either_side_of_the_field_on_every_row()
    {
        var scr = new Screen(120, 46);
        var g = Scene();
        Draw(g, scr);
        var lay = g.Layout;
        for (int r = 0; r < scr.Rows; r++)
        {
            Assert.Equal('│', scr.CharAt(lay.Col0 - 1, r));
            Assert.Equal(RaiderView.Rail, scr.Fg[r * scr.Cols + lay.Col0 - 1]);
            Assert.Equal('│', scr.CharAt(lay.Col0 + lay.PCols, r));
            Assert.Equal(RaiderView.Ground, scr.BgAt(lay.Col0 + lay.PCols, r));
            Assert.NotEqual('│', scr.CharAt(lay.Col0 - 2, r));
        }
    }

    [Fact]
    public void A_screen_too_small_for_the_field_says_so_in_the_ground_colours_and_draws_nothing_else()
    {
        var scr = new Screen(20, 4);
        new RaiderView().Render(new RaiderGame(5) { LoggedIn = true }, scr);
        Assert.Equal("terminal too small", new string(Enumerable.Range(1, 18).Select(c => scr.CharAt(c, 1)).ToArray()));
        Assert.Equal(0xd8cba3u, scr.Fg[1 * scr.Cols + 1]);
        Assert.Equal(RaiderView.Ground, scr.BgAt(1, 1));
        Assert.Equal(' ', scr.CharAt(0, 0));
    }

    [Fact]
    public void A_shaking_screen_reads_the_field_shifted_by_the_shake_in_both_directions()
    {
        var g = Scene(7);
        g.AddShake(6);
        var (sx, sy) = g.ShakeOffset();
        Assert.True(sx != 0 && sy != 0, $"the test needs a shake that moves both ways, got {sx},{sy}");
        var scr = new Screen(120, 46);
        var f = Draw(g, scr);
        // the same field read by hand: each pixel from (x - sx, y - sy), the edges repeating
        var shifted = new Surface(f.W, f.H);
        for (int y = 0; y < f.H; y++)
            for (int x = 0; x < f.W; x++)
                shifted.Set(x, y, f.Get(Math.Clamp(x - sx, 0, f.W - 1), Math.Clamp(y - sy, 0, f.H - 1)));
        var want = new Screen(120, 46);
        new QuadComposer().Compose(shifted, 0, 0, want, g.Layout.Col0, 0);
        for (int r = 4; r < f.H / 2 - 6; r++)   // between the HUD above and the text along the bottom
            for (int c = g.Layout.Col0; c < g.Layout.Col0 + g.Layout.PCols; c++)
            {
                int i = r * scr.Cols + c;
                Assert.True(want.Ch[i] == scr.Ch[i] && want.Fg[i] == scr.Fg[i] && want.Bg[i] == scr.Bg[i], $"cell {c},{r} differs from the shifted field");
            }
    }

    // ---------------------------------------------------------------- mines

    [Fact]
    public void An_unarmed_mine_lights_for_its_last_twelve_frames_on_the_lit_beat_and_is_cold_before_that()
    {
        var (dx, dy, lit, cold) = MineProbe();
        foreach (var (grace, want) in new[] { (12, lit), (13, cold) })
        {
            var g = Scene();
            var e = Place(g, EnemyKind.Mine, 40, 30);
            e.Grace = grace;
            var (x0, y0) = Top(e);
            Assert.Equal(want, Draw(g).Get(x0 + dx, y0 + dy));
        }
    }

    [Fact]
    public void An_unarmed_mine_is_cold_on_the_off_beat_of_the_blink_however_near_it_is_to_arming()
    {
        var (dx, dy, lit, cold) = MineProbe();
        var g = Scene(2);
        var e = Place(g, EnemyKind.Mine, 40, 30);
        e.Grace = 5;
        var (x0, y0) = Top(e);
        Assert.Equal(cold, Draw(g).Get(x0 + dx, y0 + dy));
    }

    [Fact]
    public void An_armed_mine_pulses_toward_white_every_other_eight_frames_and_a_hit_one_flashes_whiter()
    {
        var (dx, dy, lit, _) = MineProbe();
        foreach (var (frame, flash, white) in new[] { (0, 0, 0.3), (8, 0, 0.0), (0, 1, 0.85) })
        {
            var g = Scene(frame);
            var e = Place(g, EnemyKind.Mine, 40, 30);
            e.Flash = flash;
            var (x0, y0) = Top(e);
            uint want = white > 0 ? Surface.Mix(lit, White, white) : lit;
            Assert.Equal(want, Draw(g).Get(x0 + dx, y0 + dy));
        }
    }

    // ---------------------------------------------------------------- the diver's telegraph

    [Fact]
    public void A_diver_in_its_windup_blinks_toward_white_on_alternate_pairs_of_frames()
    {
        foreach (var (frame, white) in new[] { (0, 0.7), (2, 0.0) })
        {
            var g = Scene(frame);
            var e = Place(g, EnemyKind.Diver, 40, 20);
            e.Windup = 10; e.AimX = 40; e.AimY = 80;
            var (x0, y0) = Top(e);
            int i = Array.FindIndex(e.Sprite.Px, p => p != 0);
            uint c = e.Sprite.Px[i] & 0xffffff;
            Assert.Equal(white > 0 ? Surface.Mix(c, White, white) : c, Draw(g).Get(x0 + i % e.Sprite.W, y0 + i / e.Sprite.W));
        }
    }

    [Fact]
    public void A_dashing_diver_glints_toward_white_on_alternate_triples_of_frames()
    {
        foreach (var (frame, white) in new[] { (0, 0.4), (3, 0.0) })
        {
            var g = Scene(frame);
            var e = Place(g, EnemyKind.Diver, 40, 20);
            e.Dashing = true; e.Windup = 0;
            var (x0, y0) = Top(e);
            int i = Array.FindIndex(e.Sprite.Px, p => p != 0);
            uint c = e.Sprite.Px[i] & 0xffffff;
            Assert.Equal(white > 0 ? Surface.Mix(c, White, white) : c, Draw(g).Get(x0 + i % e.Sprite.W, y0 + i / e.Sprite.W));
        }
    }

    [Fact]
    public void A_diver_marks_the_spot_it_locked_with_a_cross_two_pixels_out_that_blinks_between_lime_and_white()
    {
        foreach (var (frame, colour) in new[] { (0, Art.DiverLime), (3, White) })
        {
            var g = Scene(frame);
            var e = Place(g, EnemyKind.Diver, 40, 20);
            e.Windup = 10; e.AimX = 90; e.AimY = 60;        // off to the side, so the dotted line passes the cross only on its way in
            var f = Draw(g);
            foreach (var (x, y) in new[] { (88, 58), (89, 59), (90, 60), (91, 61), (92, 62), (88, 62), (89, 61), (91, 59), (92, 58) })
                Assert.Equal(colour, f.Get(x, y));
            foreach (var (x, y) in new[] { (87, 57), (93, 63), (87, 63), (93, 57) })
                Assert.NotEqual(colour, f.Get(x, y));
        }
    }

    // ---------------------------------------------------------------- the turret's telegraph

    [Fact]
    public void A_turret_whitens_as_its_windup_runs_down()
    {
        foreach (var (windup, white) in new[] { (Tuning.TurretWindupFrames, 0.0), (1, 1 - 1.0 / Tuning.TurretWindupFrames) })
        {
            var g = Scene();
            var e = Place(g, EnemyKind.Turret, 40, 20);
            e.Windup = windup; e.AimX = 40; e.AimY = 80;
            var (x0, y0) = Top(e);
            int i = Array.FindIndex(e.Sprite.Px, p => p != 0);
            uint c = e.Sprite.Px[i] & 0xffffff;
            double w = white * 0.6;
            Assert.Equal(w > 0 ? Surface.Mix(c, White, w) : c, Draw(g).Get(x0 + i % e.Sprite.W, y0 + i / e.Sprite.W));
        }
    }

    [Fact]
    public void A_turrets_reticle_starts_six_pixels_out_and_closes_to_two_as_the_windup_runs_down()
    {
        foreach (var (windup, r) in new[] { (Tuning.TurretWindupFrames, 6), (7, 3), (1, 2) })
        {
            var g = Scene();
            var e = Place(g, EnemyKind.Turret, 40, 20);
            e.Windup = windup; e.AimX = 90; e.AimY = 60;
            var want = new List<(int, int)> { (90, 60) };
            foreach (int sx in new[] { -1, 1 })
                foreach (int sy in new[] { -1, 1 })
                {
                    want.Add((90 + sx * r, 60 + sy * r));
                    want.Add((90 + sx * (r - 1), 60 + sy * r));
                    want.Add((90 + sx * r, 60 + sy * (r - 1)));
                }
            Assert.Equal(Pts(want.ToArray()), Pixels(Draw(g), Art.TurretTeal, Body(e)).ToArray());
        }
    }

    [Fact]
    public void A_turret_lights_its_barrel_pixel_under_the_sprite_only_while_winding_up()
    {
        foreach (var (windup, lit) in new[] { (5, true), (0, false) })
        {
            var g = Scene();
            var e = Place(g, EnemyKind.Turret, 40, 20);
            e.Windup = windup; e.AimX = 90; e.AimY = 60;
            var (x0, y0) = Top(e);
            var f = Draw(g);
            Assert.Equal(lit, f.Get(x0 + e.Sprite.W / 2, y0 + e.Sprite.H) == White);
            Assert.NotEqual(White, f.Get(x0 + e.Sprite.W / 2, y0 + e.Sprite.H + 1));
        }
    }

    // ---------------------------------------------------------------- the splitter's cracks

    [Fact]
    public void A_splitter_at_half_health_or_less_shows_its_cracks_and_a_seam_that_blinks_between_white_and_pink()
    {
        foreach (var (frame, seam) in new[] { (0, White), (3, Art.SplitterPink) })
        {
            var g = Scene(frame);
            var e = Place(g, EnemyKind.Splitter, 40, 30);
            e.Hp = e.MaxHp / 2;
            var (x0, y0) = Top(e);
            int cx = x0 + e.Sprite.W / 2, cy = y0 + e.Sprite.H / 2;
            var f = Draw(g);
            foreach (var (x, y) in new[] { (cx - 2, cy - 1), (cx - 3, cy), (cx + 2, cy + 1), (cx + 3, cy) }) Assert.Equal(Crack, f.Get(x, y));
            foreach (var (x, y) in new[] { (cx, cy - 1), (cx, cy), (cx, cy + 1) }) Assert.Equal(seam, f.Get(x, y));
            Assert.NotEqual(seam, f.Get(cx, cy + 2));
            Assert.NotEqual(Crack, f.Get(cx - 2, cy - 2));
        }
    }

    [Fact]
    public void A_splitter_above_half_health_is_not_cracked()
    {
        var g = Scene();
        var e = Place(g, EnemyKind.Splitter, 40, 30);
        e.Hp = e.MaxHp / 2 + 0.5;
        var (x0, y0) = Top(e);
        Assert.NotEqual(Crack, Draw(g).Get(x0 + e.Sprite.W / 2 - 3, y0 + e.Sprite.H / 2));
    }

    // ---------------------------------------------------------------- the gunship's health bar

    [Fact]
    public void A_wounded_gunship_wears_a_one_pixel_bar_three_above_it_filled_in_proportion_to_its_health()
    {
        var g = Scene();
        var e = Place(g, EnemyKind.Gunship, 60, 50);
        e.Hp = e.MaxHp / 2;
        var f = Draw(g);
        int w = e.Sprite.W, x0 = Top(e).X, y0 = Top(e).Y - 3, fill = (int)(w * 0.5);
        Assert.Equal(0xff3d6eu, f.Get(x0, y0));
        Assert.Equal(0xff3d6eu, f.Get(x0 + fill - 1, y0));
        Assert.Equal(Crack, f.Get(x0 + fill, y0));
        Assert.Equal(Crack, f.Get(x0 + w - 1, y0));
        Assert.NotEqual(Crack, f.Get(x0 + w, y0));
        Assert.NotEqual(Crack, f.Get(x0, y0 + 1));      // one pixel tall
        Assert.NotEqual(0xff3d6eu, f.Get(x0, y0 + 1));
    }

    [Fact]
    public void A_gunship_at_full_health_has_no_bar()
    {
        var g = Scene();
        var e = Place(g, EnemyKind.Gunship, 60, 50);
        e.Hp = e.MaxHp;
        Assert.NotEqual(Crack, Draw(g).Get(Top(e).X, Top(e).Y - 3));
    }

    // ---------------------------------------------------------------- bullets

    [Fact]
    public void A_turret_shot_is_a_teal_diamond_around_a_white_centre()
    {
        var g = Scene();
        g.Bullets.Add(new Bullet { X = 60, Y = 60, Aimed = true });
        var f = Draw(g);
        foreach (var (x, y) in new[] { (60, 59), (59, 60), (61, 60), (60, 61) }) Assert.Equal(Art.TurretTeal, f.Get(x, y));
        Assert.Equal(White, f.Get(60, 60));
        Assert.NotEqual(Art.TurretTeal, f.Get(59, 59));
        Assert.NotEqual(Art.TurretTeal, f.Get(58, 60));
    }

    [Fact]
    public void An_enemy_shot_is_a_two_by_two_pink_square_with_a_white_corner_at_its_position()
    {
        var g = Scene();
        g.Bullets.Add(new Bullet { X = 60, Y = 60 });
        var f = Draw(g);
        foreach (var (x, y) in new[] { (59, 59), (60, 59), (59, 60) }) Assert.Equal(0xff3d6eu, f.Get(x, y));
        Assert.Equal(White, f.Get(60, 60));
        foreach (var (x, y) in new[] { (61, 60), (60, 61), (61, 61), (61, 59), (59, 61), (58, 59) }) Assert.NotEqual(0xff3d6eu, f.Get(x, y));
    }

    [Fact]
    public void A_wingmans_shot_is_a_three_pixel_gold_bar_and_the_players_a_four_pixel_pale_bar_under_a_white_tip()
    {
        var g = Scene();
        g.Bullets.Add(new Bullet { X = 40, Y = 60, Friendly = true, Wing = true });
        g.Bullets.Add(new Bullet { X = 80, Y = 60, Friendly = true });
        var f = Draw(g);
        foreach (int y in new[] { 59, 60, 61 }) Assert.Equal(0xffe29au, f.Get(40, y));
        Assert.NotEqual(0xffe29au, f.Get(40, 58));
        Assert.NotEqual(0xffe29au, f.Get(40, 62));
        foreach (int y in new[] { 58, 59, 60, 61 }) Assert.Equal(Ice, f.Get(80, y));
        Assert.Equal(White, f.Get(80, 57));
        Assert.NotEqual(White, f.Get(80, 56));
        Assert.NotEqual(Ice, f.Get(80, 62));
    }

    // ---------------------------------------------------------------- rings, sparks and the flash

    /// <summary>The field with and without whatever <paramref name="add"/> puts in the game, at the same frame.</summary>
    private static (Surface Base, Surface With) Pair(int frame, Action<RaiderGame> add, Action<RaiderGame>? prep = null)
    {
        var a = Scene(frame); prep?.Invoke(a);
        var b = Scene(frame); prep?.Invoke(b); add(b);
        return (Draw(a), Draw(b));
    }

    /// <summary>The picture a test expects: <paramref name="b"/> with <paramref name="paint"/> applied through Surface's own drawing calls.</summary>
    private static void AssertPainted(Surface b, Surface w, Action<Surface> paint)
    {
        var e = new Surface(b.W, b.H);
        Array.Copy(b.P, e.P, b.P.Length);
        paint(e);
        Assert.True(e.P.SequenceEqual(w.P), "the field is not what the drawing calls give");
        Assert.False(b.P.SequenceEqual(w.P), "the thing drew nothing");
    }

    /// <summary>Sets a game's state that only the simulation may change.</summary>
    private static void Force(object o, string member, object value)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var p = o.GetType().GetProperty(member, all);
        if (p is not null) p.GetSetMethod(true)!.Invoke(o, [value]);
        else o.GetType().GetField(member, all)!.SetValue(o, value);
    }

    [Fact]
    public void A_burst_ring_is_at_its_eased_radius_and_fades_from_its_edge_to_a_dimmer_trail_inside()
    {
        var (b, w) = Pair(0, g => g.Rings.Add(new Ring { X = 60, Y = 60, R0 = 2, R1 = 12, Life = 3, Max = 6, Color = 0xff3d6e }));
        // halfway through its life it has eased out by 1 - 0.5^2 = 0.75, to 9.5, at 0.35 + 0.65 * 0.5; the trail is 8.5 at 0.25 * 0.5
        AssertPainted(b, w, s => { s.Ring(60, 60, 9.5, 0xff3d6e, 0.675); s.Ring(60, 60, 8.5, 0xff3d6e, 0.125); });
    }

    [Fact]
    public void A_small_ring_has_no_trail_inside_it()
    {
        var (b, w) = Pair(0, g => g.Rings.Add(new Ring { X = 60, Y = 60, R0 = 3, R1 = 5, Life = 6, Max = 6, Color = 0xff3d6e }));
        AssertPainted(b, w, s => s.Ring(60, 60, 3, 0xff3d6e, 1.0));        // a radius of 3 is not over 3
    }

    [Fact]
    public void A_spark_fades_toward_the_night_colour_in_its_last_frames_and_is_its_own_colour_before_that()
    {
        var (b, w) = Pair(0, g =>
        {
            g.Particles.Add(new Particle { X = 40, Y = 40, Life = 5, MaxLife = 10, Color = 0xff00ff });
            g.Particles.Add(new Particle { X = 80, Y = 40, Life = 10, MaxLife = 10, Color = 0xff00ff });
        });
        AssertPainted(b, w, s => { s.Set(40, 40, Surface.Mix(0x0b1c44, 0xff00ff, 0.8)); s.Set(80, 40, 0xff00ff); });
    }

    [Fact]
    public void A_spark_is_as_big_as_its_size_and_a_heart_is_the_five_pixel_heart_with_a_tail()
    {
        var (b, w) = Pair(0, g =>
        {
            g.Particles.Add(new Particle { X = 40, Y = 40, Life = 10, MaxLife = 10, Color = 0xff00ff, Size = 2 });
            g.Particles.Add(new Particle { X = 80, Y = 40, Life = 10, MaxLife = 10, Color = 0xff00ff, Kind = ParticleKind.Heart });
        });
        AssertPainted(b, w, s =>
        {
            s.Rect(40, 40, 2, 2, 0xff00ff);
            s.Set(79, 40, 0xff00ff); s.Set(81, 40, 0xff00ff); s.Rect(79, 41, 3, 1, 0xff00ff); s.Set(80, 42, 0xff00ff);
        });
    }

    [Fact]
    public void The_screen_flash_washes_the_whole_field_toward_white_by_up_to_half()
    {
        var (b, w) = Pair(0, g => Force(g, "ScreenFlash", Tuning.ScreenFlashFrames));
        Assert.Equal(b.P.Select(p => Surface.Mix(p, White, 0.5)), w.P);
        var (b2, w2) = Pair(0, g => Force(g, "ScreenFlash", Tuning.ScreenFlashFrames / 2));
        Assert.Equal(b2.P.Select(p => Surface.Mix(p, White, 0.25)), w2.P);
    }

    // ---------------------------------------------------------------- pickups

    [Fact]
    public void A_pickup_is_a_capsule_in_its_colour_with_a_glow_ring_that_pulses_and_a_mark_for_its_kind()
    {
        foreach (var kind in new[] { PickupKind.Shield, PickupKind.Bomb, PickupKind.Power })
        {
            var (b, w) = Pair(0, g => g.Pickups.Add(new Pickup { Kind = kind, X = 60, Y = 60, BaseX = 60 }));
            uint c = RaiderGame.PickupColor(kind), dark = Surface.Mix(c, 0x0b1c44, 0.55), light = Surface.Mix(c, 0xffffff, 0.6);
            double pulse = 0.5;                     // frame 0, age 0
            AssertPainted(b, w, s =>
            {
                s.Ring(60, 60, 5 + pulse * 2, c, 0.3 + 0.35 * pulse);
                int x = 57, y = 57;
                s.Rect(x + 1, y, 4, 6, dark); s.Rect(x, y + 1, 6, 4, dark); s.Rect(x + 1, y + 1, 4, 4, c);
                if (kind == PickupKind.Shield) { s.Rect(x + 2, y + 1, 2, 4, light); s.Rect(x + 1, y + 2, 4, 1, light); }
                else if (kind == PickupKind.Bomb) s.Rect(x + 2, y + 2, 2, 2, light);
                else { s.Set(x + 2, y + 1, light); s.Set(x + 3, y + 1, light); s.Rect(x + 1, y + 2, 4, 1, light); s.Rect(x + 2, y + 3, 2, 2, light); }
            });
        }
    }

    // ---------------------------------------------------------------- the player and the wingmen

    private static uint PlaneAt(RaiderGame g, out int x, out int y)
    {
        int i = Array.FindIndex(g.Plane.Px, p => p != 0);
        x = (int)Math.Round(g.PX - g.Plane.W / 2.0) + i % g.Plane.W;
        y = (int)Math.Round(g.PY - g.Plane.H / 2.0) + i / g.Plane.W;
        return g.Plane.Px[i] & 0xffffff;
    }

    [Fact]
    public void The_ship_is_drawn_while_alive_and_gone_while_waiting_to_respawn()
    {
        var g = Scene();
        uint c = PlaneAt(g, out int x, out int y);
        Assert.Equal(c, Draw(g).Get(x, y));
        var dead = Scene();
        Force(dead, "RespawnIn", 5);
        Assert.NotEqual(c, Draw(dead).Get(x, y));
    }

    [Fact]
    public void A_ship_with_grace_frames_blinks_three_frames_on_and_three_off_unless_a_shield_is_the_cause()
    {
        foreach (var (frame, shield, shown) in new[] { (0, false, false), (2, false, false), (3, false, true), (5, false, true), (6, false, false), (0, true, true) })
        {
            var g = Scene(frame);
            Force(g, "Invuln", 20); Force(g, "InvulnIsShield", shield);
            uint c = PlaneAt(g, out int x, out int y);
            Assert.Equal(shown, Draw(g).Get(x, y) == c);
        }
    }

    [Fact]
    public void The_exhaust_flame_grows_from_one_to_three_pixels_with_the_frame_and_the_muzzle_flash_is_a_two_part_burst_over_the_nose()
    {
        foreach (int frame in new[] { 0, 1, 2, 3 })
        {
            var (b, w) = Pair(frame, _ => { });
            var g = Scene(frame);
            int x = (int)Math.Round(g.PX), y = (int)Math.Round(g.PY);
            for (int k = 0; k < 4; k++) Assert.Equal(k < 1 + frame % 3, b.Get(x, y + g.Plane.H / 2 + k) == 0xffb347u);
        }
        var m = Scene();
        Force(m, "MuzzleFrames", 2);
        var (b0, w0) = (Draw(Scene()), Draw(m));
        int nx = (int)Math.Round(m.PX), ny = (int)Math.Round(m.PY) - m.Plane.H / 2 - 1;
        AssertPainted(b0, w0, s => { s.Rect(nx - 2, ny - 1, 5, 3, 0xffe29a); s.Rect(nx - 1, ny - 2, 3, 5, 0xffffff); });
    }

    [Fact]
    public void A_shield_draws_two_rings_round_the_ship_whose_strength_grows_with_the_hits_it_has_left()
    {
        var (b, w) = Pair(0, g => Force(g, "Shield", 3));
        var g0 = Scene();
        uint shield = RaiderGame.PickupColor(PickupKind.Shield);
        double a = 0.25 + 0.12 * 3 + 0.1 * 0.5;        // frame 0: the pulse is half
        AssertPainted(b, w, s => { s.Ring(g0.PX, g0.PY, 8.5, shield, a); s.Ring(g0.PX, g0.PY, 7.5, shield, a * 0.4); });
    }

    [Fact]
    public void A_shield_that_took_a_hit_this_frame_or_the_last_five_flashes_a_white_ring_that_widens()
    {
        foreach (int since in new[] { 0, 5 })
        {
            var (b, w) = Pair(10, g => Force(g, "ShieldHitFrame", 10L - since), g => Force(g, "Shield", 1));
            var g0 = Scene(10);
            AssertPainted(b, w, s => s.Ring(g0.PX, g0.PY, 8.5 + since, White, 0.9 - since * 0.13));
        }
        // six frames on there is no white ring any more
        var (b6, w6) = Pair(10, g => Force(g, "ShieldHitFrame", 10L - 6), g => Force(g, "Shield", 1));
        Assert.Equal(b6.P, w6.P);
    }

    [Fact]
    public void A_powered_ship_wears_a_pink_ring_and_in_its_last_second_a_white_one_flickers_on_for_three_frames_in_six()
    {
        var g0 = Scene(2);
        double pulse = 0.5 + 0.5 * Math.Sin(2 * 0.6);
        var (b, w) = Pair(2, g => Force(g, "_powerUntil", g.Tick + 100L));
        AssertPainted(b, w, s => s.Ring(g0.PX, g0.PY, 9 + pulse * 2, Pink, 0.5));
        var (b2, w2) = Pair(2, g => Force(g, "_powerUntil", g.Tick + 20L));
        AssertPainted(b2, w2, s => { s.Ring(g0.PX, g0.PY, 9 + pulse * 2, Pink, 0.5); s.Ring(g0.PX, g0.PY, 12, White, 0.4); });
        var (b3, w3) = Pair(3, g => Force(g, "_powerUntil", g.Tick + 20L));
        var g3 = Scene(3);
        double p3 = 0.5 + 0.5 * Math.Sin(3 * 0.6);
        AssertPainted(b3, w3, s => { s.Ring(g3.PX, g3.PY, 9 + p3 * 2, Pink, 0.5); });
    }

    private static Wingman Wing(RaiderGame g, double x, double y)
    {
        var w = new Wingman
        {
            Post = new FeedPost("at://did:plc:w/app.bsky.feed.post/1", "c", "did:plc:w", "w.test", "hello", 1, 0, 0, null, null),
            Plane = g.Plane,
            HomeX = x,
            HomeY = y,
            Text = "hello",
            Handle = "w.test",
            Firepower = 1,
            X = x,
            Y = y,
        };
        g.Wings.Add(w);
        return w;
    }

    [Fact]
    public void A_wingman_who_can_be_liked_pulses_a_pink_ring_at_the_like_radius()
    {
        var a = Scene(3); var wa = Wing(a, 60.3, 60);
        var b = Scene(3); var wb = Wing(b, 60.3, 60);
        Force(b, "Prompt", wb);
        double pulse = 0.5 + 0.5 * Math.Sin(3 * 0.5);
        AssertPainted(Draw(a), Draw(b), s => s.Ring(60.3, 60, Tuning.LikeRadius * 0.45, Pink, 0.35 + 0.4 * pulse));
    }

    [Fact]
    public void A_wingman_being_liked_gets_a_dim_ring_and_pink_dots_clockwise_from_the_top_in_step_with_the_hold()
    {
        foreach (double frac in new[] { 0.25, 0.5 })
        {
            var a = Scene(); Wing(a, 60.3, 60);
            var b = Scene(); var wb = Wing(b, 60.3, 60);
            Force(b, "HoldTarget", wb); Force(b, "_holdFrames", (int)(Tuning.LikeHoldFrames * frac));
            int n = (int)Math.Round(b.HoldProgress * 48);
            AssertPainted(Draw(a), Draw(b), s =>
            {
                s.Ring(60.3, 60, 9, 0x4a2038, 0.5);
                for (int i = 0; i < n; i++)
                {
                    double ang = -Math.PI / 2 + i / 48.0 * Math.PI * 2;
                    s.Set((int)Math.Round(60.3 + Math.Cos(ang) * 9), (int)Math.Round(60 + Math.Sin(ang) * 9), Pink);
                }
            });
        }
    }

    [Fact]
    public void A_wingman_has_its_own_flame_and_muzzle_flash()
    {
        var a = Scene(); Wing(a, 60.3, 60);
        var b = Scene(); var wb = Wing(b, 60.3, 60); wb.MuzzleFrames = 2;
        int ny = 60 - a.Plane.H / 2 - 1;
        AssertPainted(Draw(a), Draw(b), s => { s.Rect(60 - 2, ny - 1, 5, 3, 0xffe29a); s.Rect(60 - 1, ny - 2, 3, 5, White); });
    }

    // ---------------------------------------------------------------- the HUD and the rails

    private const uint HudBg = 0x0a1224, Dim = 0x6f86ad, Pink = 0xff5a8a, Ice = 0xbfe9ff;

    private static Screen Shot(RaiderGame g, out FieldLayout lay)
    {
        var scr = new Screen(120, 46);
        new RaiderView().Render(g, scr);
        lay = FieldLayout.Compute(120, 46);
        return scr;
    }

    /// <summary>The text on a screen row from a column, <paramref name="len"/> cells long.</summary>
    private static string Read(Screen s, int row, int col, int len) => new(s.Ch.AsSpan(row * s.Cols + col, len));

    private static void Colours(Screen s, int row, int col, int len, uint fg, uint bg)
    {
        for (int i = 0; i < len; i++)
        {
            Assert.Equal(fg, s.Fg[row * s.Cols + col + i]);
            Assert.Equal(bg, s.Bg[row * s.Cols + col + i]);
        }
    }

    [Fact]
    public void The_hud_shows_the_score_in_six_digits_the_hearts_in_pink_and_the_bombs_in_ice()
    {
        var g = Scene();
        Force(g, "Score", 1234);
        var scr = Shot(g, out var lay);
        int c0 = lay.Col0;
        Assert.Equal("SCORE 001234", Read(scr, 0, c0 + 1, 12));
        Colours(scr, 0, c0 + 1, 12, White, HudBg);
        Assert.Equal(new string('\u2665', Tuning.PlayerHp).PadRight(3), Read(scr, 0, c0 + 15, 3));
        Colours(scr, 0, c0 + 15, Tuning.PlayerHp, Pink, HudBg);
        Assert.Equal("B" + new string('•', Tuning.PlayerBombs).PadRight(3), Read(scr, 0, c0 + 20, 4));
        Colours(scr, 0, c0 + 20, 1 + Tuning.PlayerBombs, Ice, HudBg);
        Colours(scr, 0, c0, 1, White, HudBg);                        // both HUD rows are a band of their own
        Colours(scr, 1, c0 + lay.PCols - 1, 1, White, HudBg);
    }

    [Fact]
    public void The_weapon_and_shield_pips_light_their_label_only_when_there_is_something_to_show()
    {
        var g = Scene();
        var scr = Shot(g, out var lay);
        int c0 = lay.Col0;
        uint power = RaiderGame.PickupColor(PickupKind.Power), shield = RaiderGame.PickupColor(PickupKind.Shield);
        Assert.Equal("W█•••", Read(scr, 1, c0 + 20, 5));             // the weapon starts at level one of four
        Colours(scr, 1, c0 + 20, 2, power, HudBg);
        Colours(scr, 1, c0 + 22, 3, Dim, HudBg);
        Assert.Equal("S" + new string('•', Tuning.ShieldHits), Read(scr, 1, c0 + 26, 1 + Tuning.ShieldHits));
        Colours(scr, 1, c0 + 26, 1 + Tuning.ShieldHits, Dim, HudBg);
        Force(g, "Shield", 1);
        scr = Shot(g, out _);
        Assert.Equal("S█" + new string('•', Tuning.ShieldHits - 1), Read(scr, 1, c0 + 26, 1 + Tuning.ShieldHits));
        Colours(scr, 1, c0 + 26, 2, shield, HudBg);
        Colours(scr, 1, c0 + 28, Tuning.ShieldHits - 1, Dim, HudBg);
    }

    [Fact]
    public void The_wave_label_is_dim_when_no_one_is_live_and_hot_pink_when_a_live_enemy_is_on_the_field()
    {
        var g = Scene();
        var scr = Shot(g, out var lay);
        Assert.Equal("○ clear", Read(scr, 1, lay.Col0 + 1, 7));
        Colours(scr, 1, lay.Col0 + 1, 7, Dim, HudBg);
        var e = Place(g, EnemyKind.Grunt, 60, 60);
        e.Live = true; e.Source = FeedRaider.Engine.DarknessKind.Spam;
        scr = Shot(g, out _);
        Assert.Equal("◉ LIVE spam ×1", Read(scr, 1, lay.Col0 + 1, 14));
        Colours(scr, 1, lay.Col0 + 1, 14, 0xff6b9a, HudBg);
    }

    [Fact]
    public void The_stage_readout_shrinks_to_fit_its_room_and_a_title_never_names_stage_zero()
    {
        Assert.Equal("STAGE 3", RaiderView.StageReadout(3, 7));
        Assert.Equal("ST 3", RaiderView.StageReadout(3, 6));
        Assert.Equal("ST 3", RaiderView.StageReadout(3, 4));
        Assert.Equal("3", RaiderView.StageReadout(3, 3));
        Assert.Equal("STAGE 1", RaiderView.StageTitle(0));
        Assert.Equal("STAGE " + Tuning.StageCap, RaiderView.StageTitle(99));
        var scr = Shot(Scene(), out var lay);
        Assert.Equal("STAGE 1", Read(scr, 0, lay.Col0 + lay.PCols - 8, 7));
        Colours(scr, 0, lay.Col0 + lay.PCols - 8, 7, Ice, HudBg);
    }

    [Fact]
    public void A_rail_runs_down_each_side_of_the_field_and_a_terminal_too_small_says_so()
    {
        var scr = Shot(Scene(), out var lay);
        for (int r = 0; r < scr.Rows; r++)
            foreach (int c in new[] { lay.Col0 - 1, lay.Col0 + lay.PCols })
            {
                Assert.Equal('│', scr.Ch[r * scr.Cols + c]);
                Assert.Equal(RaiderView.Rail, scr.Fg[r * scr.Cols + c]);
                Assert.Equal(RaiderView.Ground, scr.Bg[r * scr.Cols + c]);
            }
        var tiny = new Screen(20, 4);
        new RaiderView().Render(Scene(), tiny);
        Assert.Equal("terminal too small", Read(tiny, 1, 1, 18));
        Assert.Equal(0xd8cba3u, tiny.Fg[tiny.Cols + 1]);
    }
}
