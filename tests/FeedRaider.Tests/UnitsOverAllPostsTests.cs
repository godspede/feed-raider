using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Everything that touches the game draws above every kind of post box (a wingman's, a darkness enemy's): one test per object class,
// each run over both boxes, plus the enemy that must not sit under its own post. The rendered frames of a busy scene are saved when
// FR_SCREENS names a directory.
public class UnitsOverAllPostsTests
{
    private const int Cols = 60, Rows = 60;
    private const string Post = "this is a perfectly ordinary post with enough words in it to run well past three short lines of comm text";

    private sealed class Scene
    {
        public RaiderGame G = null!;
        public Wingman W = null!;
        public Enemy E = null!;
        public CellRect WingBox, EnemyBox;
        public Screen Control = null!;
    }

    private static Scene Make()
    {
        var g = new RaiderGame(5) { LoggedIn = true, MaxWings = 2 };
        g.SetLayout(FieldLayout.Compute(Cols, Rows));
        g.Queue.Offer(new FeedPost("at://did:plc:b1/app.bsky.feed.post/1", "cid1", "did:plc:b1", "b1.test", Post, 10, 0, 0, null, null));
        for (int i = 0; i < 3000 && !(g.Wings.Count == 1 && g.Comms.Speaker is not null); i++) g.Step();
        var w = g.Wings.Single();
        w.X = g.Layout.PW * 0.72; w.Y = 20;
        SetPlayer(g, 8, g.Layout.PH - 8);
        g.Enemies.Clear(); g.Bullets.Clear(); g.Pickups.Clear(); g.Particles.Clear(); g.Rings.Clear();
        var e = new Enemy { Kind = EnemyKind.Gunship, Sprite = Art.Gunship, X = g.Layout.PW * 0.25, Y = g.Layout.PH * 0.55, Hp = 8, MaxHp = 8, Header = "RUDE", Live = true, Text = "you are all idiots and nobody likes your takes at all" };
        g.Enemies.Add(e);
        EnemyComm.Update(e, g.Layout.PCols, g.Layout.PRows);
        e.CommAge = 5;
        var s = new Scene { G = g, W = w, E = e, WingBox = CommPainter.Box(w, g.Layout), EnemyBox = EnemyComm.Box(e, g.Layout) };
        Assert.True(EnemyComm.IsOpen(e));
        Assert.False(s.WingBox.Overlaps(s.EnemyBox));
        s.Control = Render(g);
        return s;
    }

    private static void SetPlayer(RaiderGame g, double x, double y)
    {
        typeof(RaiderGame).GetProperty("PX")!.GetSetMethod(true)!.Invoke(g, [x]);
        typeof(RaiderGame).GetProperty("PY")!.GetSetMethod(true)!.Invoke(g, [y]);
    }

    private static Screen Render(RaiderGame g)
    {
        var scr = new Screen(Cols, Rows);
        new RaiderView().Render(g, scr);
        return scr;
    }

    /// <summary>A cell of the box that carries a letter of its text.</summary>
    private static (int c, int r) LetterCell(Screen scr, CellRect box, int minCol)
    {
        for (int r = box.R + 2; r < box.Bottom - 1; r++)
            for (int c = box.C + minCol; c < box.Right - 2; c++)
                if (char.IsLetter(scr.CharAt(c, r))) return (c, r);
        throw new Xunit.Sdk.XunitException("no letter in the box");
    }

    private static bool Hidden(Screen scr, Screen control, int c, int r) =>
        !char.IsLetter(scr.CharAt(c, r)) && (scr.Fg[r * Cols + c] != control.Fg[r * Cols + c] || scr.Bg[r * Cols + c] != control.Bg[r * Cols + c]);

    public static IEnumerable<object[]> Boxes() => [["wingman"], ["enemy"]];

    private static (CellRect box, int c, int r, double px, double py) Target(Scene s, string which)
    {
        var box = which == "wingman" ? s.WingBox : s.EnemyBox;
        var (c, r) = LetterCell(s.Control, box, which == "wingman" ? 8 : 2);
        return (box, c, r, (c - s.G.Layout.Col0) * 2 + 1, r * 2 + 1);
    }

    private static void Shows(Scene s, string which, Action<RaiderGame, double, double> place, string what)
    {
        var (_, c, r, px, py) = Target(s, which);
        place(s.G, px, py);
        var scr = Render(s.G);
        Assert.True(Hidden(scr, s.Control, c, r), $"{what} is drawn over the {which} box");
    }

    [Theory, MemberData(nameof(Boxes))]
    public void A_friendly_shot_shows(string which) => Shows(Make(), which, (g, x, y) => g.Bullets.Add(new Bullet { X = x, Y = y + 1, Friendly = true }), "a shot");

    [Theory, MemberData(nameof(Boxes))]
    public void A_wingmans_shot_shows(string which) => Shows(Make(), which, (g, x, y) => g.Bullets.Add(new Bullet { X = x, Y = y, Friendly = true, Wing = true }), "a wingman's shot");

    [Theory, MemberData(nameof(Boxes))]
    public void An_enemy_bullet_shows(string which) => Shows(Make(), which, (g, x, y) => g.Bullets.Add(new Bullet { X = x, Y = y, Friendly = false }), "an enemy bullet");

    [Theory, MemberData(nameof(Boxes))]
    public void An_aimed_bullet_shows(string which) => Shows(Make(), which, (g, x, y) => g.Bullets.Add(new Bullet { X = x, Y = y, Friendly = false, Aimed = true }), "an aimed bullet");

    [Theory, MemberData(nameof(Boxes))]
    public void The_players_ship_shows(string which) => Shows(Make(), which, SetPlayer, "the ship");

    [Theory, MemberData(nameof(Boxes))]
    public void A_pickup_shows(string which)
    {
        foreach (var kind in Enum.GetValues<PickupKind>())
            Shows(Make(), which, (g, x, y) => g.Pickups.Add(new Pickup { Kind = kind, X = x, Y = y, BaseX = x }), "a " + kind + " pickup");
    }

    [Theory, MemberData(nameof(Boxes))]
    public void Another_enemy_shows(string which)
    {
        foreach (var kind in new[] { EnemyKind.Grunt, EnemyKind.Mine, EnemyKind.Diver, EnemyKind.Splitter, EnemyKind.Turret })
            Shows(Make(), which, (g, x, y) =>
            {
                var sp = kind switch { EnemyKind.Diver => Art.Diver, EnemyKind.Turret => Art.Turret, _ => Art.Grunt };
                g.Enemies.Add(new Enemy { Kind = kind, Sprite = sp, X = x, BaseX = x, Y = y, Hp = 4, MaxHp = 4, Windup = -1, Grace = 0 });
            }, "a " + kind);
    }

    [Theory, MemberData(nameof(Boxes))]
    public void A_blast_ring_shows(string which) =>
        Shows(Make(), which, (g, x, y) => g.Rings.Add(new Ring { X = x - 6, Y = y, R0 = 6, R1 = 6, Life = 10, Max = 20, Color = 0xffffff }), "a ring");

    [Theory, MemberData(nameof(Boxes))]
    public void A_turret_reticle_shows(string which) =>
        Shows(Make(), which, (g, x, y) => g.Enemies.Add(new Enemy { Kind = EnemyKind.Turret, Sprite = Art.Turret, X = 4, Y = g.Layout.PH - 20, Hp = 4, MaxHp = 4, Windup = 10, AimX = x, AimY = y }), "a turret reticle");

    [Theory, MemberData(nameof(Boxes))]
    public void A_diver_aim_line_and_crosshair_show(string which)
    {
        var s = Make();
        var (box, c, r, px, py) = Target(s, which);
        s.G.Enemies.Add(new Enemy { Kind = EnemyKind.Diver, Sprite = Art.Diver, X = px - 10, Y = box.R * 2 - 20, BaseX = px, Hp = 4, MaxHp = 4, Windup = 10, AimX = px, AimY = py });
        var scr = Render(s.G);
        int changed = 0;
        for (int y = box.R; y < box.Bottom; y++)
            for (int x = box.C; x < box.Right; x++) if (Hidden(scr, s.Control, x, y)) changed++;
        Assert.True(Hidden(scr, s.Control, c, r), "the crosshair cell");
        Assert.True(changed >= 2, "the aim line crosses the box too");
    }

    [Fact]
    public void An_enemys_own_shot_shows_over_its_own_box()
    {
        var s = Make();
        var (box, c, r, px, py) = Target(s, "enemy");
        s.G.Bullets.Add(new Bullet { X = px, Y = py, Friendly = false });
        Assert.True(Hidden(Render(s.G), s.Control, c, r));
    }

    [Fact]
    public void An_enemy_near_the_top_gets_its_box_below_so_neither_hides_the_other()
    {
        var s = Make();
        var e = s.E;
        foreach (double y in new[] { 7.0, 10, 14, 20 })
        {
            e.Y = y;
            var box = EnemyComm.Box(e, s.G.Layout);
            int top = (int)Math.Floor((e.Y - e.Sprite.H / 2.0) / 2), bottom = (int)Math.Ceiling((e.Y + e.Sprite.H / 2.0) / 2);
            Assert.True(box.Bottom <= top || box.R >= bottom, $"box rows {box.R}-{box.Bottom} clear of the sprite rows {top}-{bottom} at y={y}");
            var scr = Render(s.G);
            int cc = s.G.Layout.Col0 + (int)(e.X / 2), cr = (int)(e.Y / 2);
            Assert.False(box.C <= cc && cc < box.Right && box.R <= cr && cr < box.Bottom, "the sprite's centre is outside the box");
            if (EnemyComm.IsBelow(e, box)) Assert.Equal('▲', scr.CharAt(box.C + box.W / 2, box.R));
        }
    }

    [Fact]
    public void Nothing_in_the_box_changes_when_no_unit_is_near()
    {
        var s = Make();
        s.G.Bullets.Add(new Bullet { X = 2, Y = s.G.Layout.PH - 30, Friendly = true });
        var scr = Render(s.G);
        foreach (var box in new[] { s.WingBox, s.EnemyBox })
            for (int y = box.R; y < box.Bottom; y++)
                for (int x = box.C; x < box.Right; x++) Assert.Equal(s.Control.CharAt(x, y), scr.CharAt(x, y));
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
            foreach (var name in new[] { "busy-scene-both-boxes.png", "control-boxes-only.png", "enemy-near-top-box-below.png" })
                Assert.True(new FileInfo(Path.Combine(dir, name)).Length > 0, $"{name} was written");
        }
        finally
        {
            if (string.IsNullOrEmpty(kept)) Directory.Delete(dir, true);
        }
    }

    private static void SaveBusyScenes(string dir)
    {
        var s = Make();
        var g = s.G;
        foreach (var which in new[] { "wingman", "enemy" })
        {
            var (box, _, _, _, _) = Target(s, which);
            double x0 = (box.C - g.Layout.Col0) * 2 + 4, y0 = box.R * 2 + 4;
            g.Bullets.Add(new Bullet { X = x0, Y = y0 + 2, Friendly = true });
            g.Bullets.Add(new Bullet { X = x0 + 6, Y = y0 + 4, Friendly = false });
            g.Bullets.Add(new Bullet { X = x0 + 12, Y = y0 + 4, Friendly = false, Aimed = true });
            g.Bullets.Add(new Bullet { X = x0 + 18, Y = y0 + 3, Friendly = true, Wing = true });
            g.Pickups.Add(new Pickup { Kind = PickupKind.Shield, X = x0 + 26, Y = y0 + 6, BaseX = x0 + 26 });
            g.Enemies.Add(new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = x0 + 36, Y = y0 + 8, Hp = 2, MaxHp = 2, Windup = -1 });
            g.Rings.Add(new Ring { X = x0 + 20, Y = y0 + 8, R0 = 9, R1 = 9, Life = 10, Max = 20, Color = 0xff8fb1 });
        }
        var (eb, _, _, ex, ey) = Target(s, "enemy");
        g.Enemies.Add(new Enemy { Kind = EnemyKind.Diver, Sprite = Art.Diver, X = ex + 20, Y = eb.R * 2 - 14, BaseX = ex, Hp = 4, MaxHp = 4, Windup = 10, AimX = ex, AimY = ey });
        var (_, _, _, wx, wy) = Target(s, "wingman");
        SetPlayer(g, wx, wy + 3);
        FrameImage.Write(Render(g), Path.Combine(dir, "busy-scene-both-boxes.png"));
        FrameImage.Write(s.Control, Path.Combine(dir, "control-boxes-only.png"));
        s = Make();
        s.E.Y = 9;
        FrameImage.Write(Render(s.G), Path.Combine(dir, "enemy-near-top-box-below.png"));
    }
}
