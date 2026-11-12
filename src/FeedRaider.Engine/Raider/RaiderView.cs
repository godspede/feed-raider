using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>Draws the game into a <see cref="Screen"/>: the pixel field through the quad composer, text overlays on top.</summary>
public sealed class RaiderView
{
    public static readonly uint Ground = Surface.Pack(0x05, 0x07, 0x11);
    public static readonly uint Rail = Surface.Pack(0x1c, 0x3a, 0x6e);
    public static readonly uint Blue = Surface.Pack(0x00, 0x85, 0xff);
    private const uint HudBg = 0x0a1224, PanelBg = CommPainter.PanelBg, PanelEdge = 0x1c3a6e, Dim = Palette.Dim, Pink = Palette.Pink, Ice = Palette.Ice;
    private const uint TooSmallText = 0xd8cba3, ChargeTrack = 0x4a2038, BulletGlow = 0xffe29a, Flame = 0xffb347, WaveCarrierMark = 0xff6b9a;
    private const uint PostText = 0xe3c8ff, PostBg = 0x1a1030, LikeHintBg = 0x2a1030, EnemyCommText = 0xffd0e0;
    private const uint ShipDownBg = 0x5a1030, ToastBg = 0x3a1030, BombReady = 0x1a3a6a, BombSpent = 0x16202e;
    private const uint BannerFlashA = 0x4a1040, BannerFlashB = 0x2a0a30;
    private const int ChargeRingSteps = 48;   // points round a full like-charge ring
    private int PanelW = Tuning.SidePanelMinCols;

    private readonly QuadComposer _quad = new();
    private readonly QuadComposer _avQuad = new();
    private readonly Surface _avSurface = new(8, 8);
    private readonly CommPainter _comms = new();
    private Surface? _field;
    private Background? _bg;
    private FieldLayout _lay;
    private readonly ulong _seed;

    public RaiderView(ulong seed = 7) { _seed = seed; }

    public FieldLayout Layout => _lay;
    public int ReducedCells => _quad.Reduced;

    public void Render(RaiderGame g, Screen scr, string? debug = null)
    {
        scr.Clear(Ground);
        var lay = FieldLayout.Compute(scr.Cols, scr.Rows);
        if (!lay.FitsMinimum)
        {
            scr.Text(1, 1, "terminal too small", TooSmallText);
            return;
        }
        if (g.Layout != lay) g.SetLayout(lay);
        if (_field is null || _lay != lay)
        {
            _lay = lay;
            _field = new Surface(lay.PW, lay.PH);
            _bg = new Background(lay.PW, lay.PH, _seed);
        }
        var f = _field;
        _bg!.Draw(f, g.ScrollFrames);
        DrawWorld(g, f);
        var (sx, sy) = g.ShakeOffset();
        _quad.Compose(f, sx, sy, scr, lay.Col0, 0);
        for (int r = 0; r < scr.Rows; r++)
        {
            scr.Put(lay.Col0 - 1, r, '│', Rail, Ground);
            scr.Put(lay.Col0 + lay.PCols, r, '│', Rail, Ground);
        }
        DrawText(g, scr, lay, debug);
        LayUnitsOverPosts(g, scr, lay, sx, sy);
    }

    // ---------------------------------------------------------------- pixels

    private readonly List<CellRect> _posts = [];
    private Surface? _units;
    private const uint NoUnit = Surface.None;

    /// <summary>Puts the units and shots back over the post boxes. The boxes are text cells drawn after the pixel field, so where a ship or
    /// a shot passes behind one, the cell is rebuilt from the unit pixels (quadrants, as the field is) and the box colour fills the rest
    /// of that cell. Only the cells of boxes are looked at, and nothing is drawn when no box is up.</summary>
    private void LayUnitsOverPosts(RaiderGame g, Screen scr, FieldLayout lay, int sx, int sy)
    {
        _posts.Clear();
        _posts.AddRange(_comms.Boxes);
        _posts.AddRange(_enemyPosts);
        if (_posts.Count == 0) return;
        if (_units is null || _units.W != lay.PW || _units.H != lay.PH) _units = new Surface(lay.PW, lay.PH) { SkipBlends = true };
        var u = _units;
        u.Fill(NoUnit);
        DrawUnits(g, u);
        DrawRings(g, u);   // blasts and the shield hurt or guard, so they show over a box too
        int cols = u.W / 2, rows = u.H / 2;
        var px = new uint[4];
        int top = 2, bottom = Math.Min(lay.PRows, scr.Rows);
        foreach (var b in _posts)
            for (int r = Math.Max(b.R, top); r < Math.Min(b.Bottom, bottom); r++)
                for (int c = Math.Max(b.C, lay.Col0); c < Math.Min(b.Right, lay.Col0 + lay.PCols); c++)
                {
                    int cx = c - lay.Col0, cy = r;
                    int x0 = Math.Clamp(cx * 2 - sx, 0, u.W - 1), x1 = Math.Clamp(cx * 2 + 1 - sx, 0, u.W - 1);
                    int y0 = Math.Clamp(cy * 2 - sy, 0, u.H - 1), y1 = Math.Clamp(cy * 2 + 1 - sy, 0, u.H - 1);
                    uint a = u.P[y0 * u.W + x0], bb = u.P[y0 * u.W + x1], cc = u.P[y1 * u.W + x0], d = u.P[y1 * u.W + x1];
                    if (a == NoUnit && bb == NoUnit && cc == NoUnit && d == NoUnit) continue;
                    int i = r * scr.Cols + c;
                    uint back = scr.Bg[i];
                    px[0] = a == NoUnit ? back : a; px[1] = bb == NoUnit ? back : bb; px[2] = cc == NoUnit ? back : cc; px[3] = d == NoUnit ? back : d;
                    int mask = QuadComposer.SplitMask(px, out uint fg, out uint bgc);
                    if (mask == 15) bgc = fg;   // one colour all through: the split leaves the other group empty
                    scr.Ch[i] = QuadRenderer.GlyphFor(mask); scr.Fg[i] = fg; scr.Bg[i] = bgc;
                }
    }

    private readonly List<CellRect> _enemyPosts = [];

    /// <summary>The newer kinds and their telegraphs. All of it is pixels, so the comm bubble's text always draws over it.</summary>
    private static void DrawUnlockedKinds(RaiderGame g, Surface f, Enemy e)
    {
        var sp = e.Sprite;
        double white = e.Flash > 0 ? 0.85 : 0;
        if (e.Kind == EnemyKind.Mine && !e.Armed)
            sp = e.Grace - e.Age <= 12 && g.Frame / 2 % 2 == 0 ? Art.Mine : Art.MineCold;   // blinks lit just before it arms
        else if (e.Kind == EnemyKind.Mine && g.Frame / 8 % 2 == 0 && white == 0) white = 0.3;
        if (e.Kind == EnemyKind.Diver && e.Windup > 0 && !e.Dashing)
        {
            white = Math.Max(white, g.Frame / 2 % 2 == 0 ? 0.7 : 0);
            Aim(f, e.X, e.Y + sp.H / 2.0, e.AimX, e.AimY, Art.DiverLime, 3);
            Cross(f, (int)Math.Round(e.AimX), (int)Math.Round(e.AimY), 2, g.Frame / 3 % 2 == 0 ? Art.DiverLime : Palette.White);
        }
        else if (e.Kind == EnemyKind.Diver && e.Dashing && white == 0 && g.Frame / 3 % 2 == 0) white = 0.4;   // the dash plates glint: shots bounce
        if (e.Kind == EnemyKind.Turret && e.Windup > 0)
        {
            double t = 1 - e.Windup / (double)Tuning.TurretWindupFrames;
            white = Math.Max(white, t * 0.6);
            int r = 2 + (int)Math.Round((1 - t) * 4);   // the reticle closes on the locked spot
            Reticle(f, (int)Math.Round(e.AimX), (int)Math.Round(e.AimY), r, Art.TurretTeal);
        }
        int x0 = (int)Math.Round(e.X - sp.W / 2.0), y0 = (int)Math.Round(e.Y - sp.H / 2.0);
        f.Draw(sp, x0, y0, white);
        if (e.Kind == EnemyKind.Turret && e.Windup > 0) f.Set(x0 + sp.W / 2, y0 + sp.H, Palette.White);   // the barrel lights
        if (e.Kind == EnemyKind.Splitter && e.Hp <= e.MaxHp / 2)
        {
            // cracks, and a seam that glows: it is about to burst
            int cx = x0 + sp.W / 2, cy = y0 + sp.H / 2;
            f.Set(cx - 2, cy - 1, Palette.Plum); f.Set(cx - 3, cy, Palette.Plum); f.Set(cx + 2, cy + 1, Palette.Plum); f.Set(cx + 3, cy, Palette.Plum);
            uint seam = g.Frame / 3 % 2 == 0 ? Palette.White : Art.SplitterPink;
            f.Set(cx, cy - 1, seam); f.Set(cx, cy, seam); f.Set(cx, cy + 1, seam);
        }
    }

    /// <summary>A dotted line from (x0, y0) to (x1, y1), one pixel in every <paramref name="every"/>.</summary>
    private static void Aim(Surface f, double x0, double y0, double x1, double y1, uint c, int every)
    {
        double dx = x1 - x0, dy = y1 - y0;
        int n = (int)Math.Max(Math.Abs(dx), Math.Abs(dy));
        for (int i = every; i < n; i += every)
            f.Set((int)Math.Round(x0 + dx * i / n), (int)Math.Round(y0 + dy * i / n), c);
    }

    private static void Cross(Surface f, int x, int y, int r, uint c)
    {
        for (int i = -r; i <= r; i++) { f.Set(x + i, y + i, c); f.Set(x + i, y - i, c); }
    }

    /// <summary>Four corner brackets around (x, y).</summary>
    private static void Reticle(Surface f, int x, int y, int r, uint c)
    {
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                f.Set(x + sx * r, y + sy * r, c);
                f.Set(x + sx * (r - 1), y + sy * r, c);
                f.Set(x + sx * r, y + sy * (r - 1), c);
            }
        f.Set(x, y, c);
    }

    private void DrawWorld(RaiderGame g, Surface f)
    {
        DrawUnits(g, f);
        DrawEffects(g, f);
    }

    /// <summary>The things that must stay readable over a post box: enemies, pickups, wingmen, the player and every shot.</summary>
    private void DrawUnits(RaiderGame g, Surface f)
    {
        foreach (var e in g.Enemies)
        {
            if (e.Kind >= EnemyKind.Mine) { DrawUnlockedKinds(g, f, e); continue; }
            f.Draw(e.Sprite, (int)Math.Round(e.X - e.Sprite.W / 2.0), (int)Math.Round(e.Y - e.Sprite.H / 2.0), e.Flash > 0 ? 0.85 : 0);
            if (e.Kind == EnemyKind.Gunship && e.Hp < e.MaxHp)
            {
                int bw = e.Sprite.W, x0 = (int)Math.Round(e.X - bw / 2.0), y0 = (int)Math.Round(e.Y - e.Sprite.H / 2.0) - 3;
                f.Rect(x0, y0, bw, 1, Palette.Plum);
                f.Rect(x0, y0, (int)(bw * Math.Max(0, e.Hp) / e.MaxHp), 1, Palette.Crimson);
            }
        }
        foreach (var pk in g.Pickups) DrawPickup(g, f, pk);
        foreach (var w in g.Wings) DrawWing(g, f, w);
        if (g.Alive) DrawPlayer(g, f);
        foreach (var b in g.Bullets)
        {
            int x = (int)Math.Round(b.X), y = (int)Math.Round(b.Y);
            if (b.Aimed)
            {
                f.Set(x, y - 1, Art.TurretTeal); f.Set(x - 1, y, Art.TurretTeal); f.Set(x + 1, y, Art.TurretTeal); f.Set(x, y + 1, Art.TurretTeal);
                f.Set(x, y, Palette.White);
            }
            else if (!b.Friendly) { f.Rect(x - 1, y - 1, 2, 2, Palette.Crimson); f.Set(x, y, Palette.White); }
            else if (b.Wing) { f.Rect(x, y - 1, 1, 3, BulletGlow); }
            else { f.Rect(x, y - 2, 1, 4, Palette.Ice); f.Set(x, y - 3, Palette.White); }
        }
    }

    private static void DrawEffects(RaiderGame g, Surface f)
    {
        DrawRings(g, f);
        foreach (var p in g.Particles)
        {
            int x = (int)Math.Round(p.X), y = (int)Math.Round(p.Y);
            double fade = Math.Min(1, p.Life / (double)Math.Max(1, p.MaxLife) * 1.6);
            uint c = fade < 1 ? Surface.Mix(Palette.SkyTop, p.Color, fade) : p.Color;
            if (p.Kind == ParticleKind.Heart) { f.Set(x - 1, y, c); f.Set(x + 1, y, c); f.Rect(x - 1, y + 1, 3, 1, c); f.Set(x, y + 2, c); }
            else f.Rect(x, y, p.Size, p.Size, c);
        }
        if (g.ScreenFlash > 0)
        {
            double a = 0.5 * g.ScreenFlash / Tuning.ScreenFlashFrames;
            var px = f.P;
            for (int i = 0; i < px.Length; i++) px[i] = Surface.Mix(px[i], Palette.White, a);
        }
    }

    private static void DrawRings(RaiderGame g, Surface f)
    {
        foreach (var r in g.Rings)
        {
            double t = 1 - r.Life / (double)r.Max, e = 1 - (1 - t) * (1 - t);
            double rad = r.R0 + (r.R1 - r.R0) * e;
            double a = 1 - t;
            f.Ring(r.X, r.Y, rad, r.Color, 0.35 + 0.65 * a);
            if (rad > 3) f.Ring(r.X, r.Y, rad - 1, r.Color, 0.25 * a);
        }
    }

    private static void DrawWing(RaiderGame g, Surface f, Wingman w)
    {
        int x = (int)Math.Round(w.X - w.Plane.W / 2.0), y = (int)Math.Round(w.Y - w.Plane.H / 2.0);
        bool ready = g.Prompt == w;
        if (ready)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(g.Frame * 0.5);
            f.Ring(w.X, w.Y, Tuning.LikeRadius * 0.45, Pink, 0.35 + 0.4 * pulse);
        }
        f.Draw(w.Plane, x, y);
        FlameAndMuzzle(f, w.X, w.Y, w.Plane, w.MuzzleFrames > 0, g.Frame);
        if (g.HoldTarget == w) ChargeRing(f, w.X, w.Y, g.HoldProgress);
    }

    /// <summary>The like charge: a ring around the wingman that fills clockwise from the top as the hold goes on.</summary>
    private static void ChargeRing(Surface f, double cx, double cy, double progress)
    {
        const double r = 9;
        f.Ring(cx, cy, r, ChargeTrack, 0.5);
        int n = (int)Math.Round(progress * ChargeRingSteps);
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + i / (double)ChargeRingSteps * Math.PI * 2;
            f.Set((int)Math.Round(cx + Math.Cos(a) * r), (int)Math.Round(cy + Math.Sin(a) * r), Pink);
        }
    }

    private static void FlameAndMuzzle(Surface f, double cx, double cy, Sprite plane, bool muzzle, long frame)
    {
        int x = (int)Math.Round(cx), y = (int)Math.Round(cy);
        int fl = 1 + (int)(frame % 3);
        f.Rect(x, y + plane.H / 2, 1, fl, Flame);
        if (muzzle)
        {
            int ny = y - plane.H / 2 - 1;
            f.Rect(x - 2, ny - 1, 5, 3, BulletGlow);
            f.Rect(x - 1, ny - 2, 3, 5, Palette.White);
        }
    }

    /// <summary>A 6x6 capsule in the pickup's colour with a pulsing glow ring; it blinks for its last two seconds.</summary>
    private static void DrawPickup(RaiderGame g, Surface f, Pickup p)
    {
        if (p.Blinking && g.Frame / 3 % 2 == 0) return;
        uint c = RaiderGame.PickupColor(p.Kind), dark = Surface.Mix(c, Palette.SkyTop, 0.55), light = Surface.Mix(c, Palette.White, 0.6);
        double pulse = 0.5 + 0.5 * Math.Sin(g.Frame * 0.3 + p.Age * 0.1);
        f.Ring(p.X, p.Y, 5 + pulse * 2, c, 0.3 + 0.35 * pulse);
        int x = (int)Math.Round(p.X) - 3, y = (int)Math.Round(p.Y) - 3;
        f.Rect(x + 1, y, 4, 6, dark); f.Rect(x, y + 1, 6, 4, dark);          // rounded body
        f.Rect(x + 1, y + 1, 4, 4, c);
        switch (p.Kind)
        {
            case PickupKind.Shield: f.Rect(x + 2, y + 1, 2, 4, light); f.Rect(x + 1, y + 2, 4, 1, light); break;   // a cross
            case PickupKind.Bomb: f.Rect(x + 2, y + 2, 2, 2, light); break;                                          // a round charge
            default: f.Set(x + 2, y + 1, light); f.Set(x + 3, y + 1, light); f.Rect(x + 1, y + 2, 4, 1, light); f.Rect(x + 2, y + 3, 2, 2, light); break;   // an arrow up
        }
    }

    private static void DrawShield(RaiderGame g, Surface f)
    {
        double pulse = 0.5 + 0.5 * Math.Sin(g.Frame * 0.2);
        double a = 0.25 + 0.12 * g.Shield + 0.1 * pulse;
        uint shield = RaiderGame.PickupColor(PickupKind.Shield);
        f.Ring(g.PX, g.PY, 8.5, shield, a);
        f.Ring(g.PX, g.PY, 7.5, shield, a * 0.4);
        long since = g.Frame - g.ShieldHitFrame;
        if (since < 6) f.Ring(g.PX, g.PY, 8.5 + since, Palette.White, 0.9 - since * 0.13);
    }

    private static void DrawPlayer(RaiderGame g, Surface f)
    {
        if (g.Powered)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(g.Frame * 0.6);
            f.Ring(g.PX, g.PY, 9 + pulse * 2, Pink, 0.5);
            if (g.PowerLeft < Tuning.Fps && g.Frame % 6 < 3) f.Ring(g.PX, g.PY, 12, Palette.White, 0.4);
        }
        if (g.Shield > 0) DrawShield(g, f);
        if (g.Invuln > 0 && !g.InvulnIsShield && g.Frame / 3 % 2 == 0) return;
        f.Draw(g.Plane, (int)Math.Round(g.PX - g.Plane.W / 2.0), (int)Math.Round(g.PY - g.Plane.H / 2.0));
        FlameAndMuzzle(f, g.PX, g.PY, g.Plane, g.MuzzleFrames > 0, g.Frame);
    }

    // ---------------------------------------------------------------- text

    /// <summary>A label and a row of pips: <paramref name="have"/> filled in <paramref name="color"/>, the rest dim dots.</summary>
    private static void Pips(Screen scr, int col, int row, char label, int have, int max, uint color)
    {
        scr.Text(col, row, label.ToString(), have > 0 ? color : Dim, HudBg);
        scr.Text(col + 1, row, new string('█', have), color, HudBg);
        scr.Text(col + 1 + have, row, new string('•', max - have), Dim, HudBg);
    }

    /// <summary>The HUD's stage readout, as long as <paramref name="room"/> allows: "STAGE 3", "ST 3" on a narrow field, "3" at the narrowest.</summary>
    public static string StageReadout(int stage, int room)
    {
        int i = Math.Clamp(stage, 1, Tuning.StageCap);
        return room >= StageText[i].Length ? StageText[i] : room >= ShortStageText[i].Length ? ShortStageText[i] : NumberText[i];
    }

    public static string StageTitle(int stage) => StageText[Math.Clamp(stage, 1, Tuning.StageCap)];

    // made once, so the HUD does not format a string every frame
    private static readonly string[] StageText = Enumerable.Range(0, Tuning.StageCap + 1).Select(i => "STAGE " + i).ToArray();
    private static readonly string[] ShortStageText = Enumerable.Range(0, Tuning.StageCap + 1).Select(i => "ST " + i).ToArray();
    private static readonly string[] NumberText = Enumerable.Range(0, Tuning.StageCap + 1).Select(i => i.ToString()).ToArray();

    private static string Hearts(int n) => new('\u2665', Math.Max(0, n));

    /// <summary>Draws the HUD and every text overlay, and records the boxes of the darkness text in <c>_enemyPosts</c> for
    /// <see cref="LayUnitsOverPosts"/>, which must run after it.</summary>
    private void DrawText(RaiderGame g, Screen scr, FieldLayout lay, string? debug)
    {
        int c0 = lay.Col0, cols = lay.PCols;
        int Cx(double x) => c0 + (int)(x / 2);
        int Cy(double y) => (int)(y / 2);

        // HUD
        scr.Blank(c0, 0, cols, Palette.White, HudBg);
        scr.Blank(c0, 1, cols, Palette.White, HudBg);
        scr.Text(c0 + 1, 0, $"SCORE {g.Score:D6}", Palette.White, HudBg);
        scr.Text(c0 + 15, 0, Hearts(g.Hp), Pink, HudBg);
        scr.Text(c0 + 20, 0, "B" + new string('•', g.Bombs), Ice, HudBg);
        Pips(scr, c0 + 20, 1, 'W', g.WeaponLevel, Tuning.MaxWeaponLevel, RaiderGame.PickupColor(PickupKind.Power));
        Pips(scr, c0 + 26, 1, 'S', g.Shield, Tuning.ShieldHits, RaiderGame.PickupColor(PickupKind.Shield));
        string wave = g.WaveLabel();
        scr.Text(c0 + 1, 1, wave, wave[0] == '◉' ? WaveCarrierMark : Dim, HudBg);
        if (g.Powered)
        {
            int n = Math.Max(1, g.PowerLeft * 8 / Tuning.PowerUpFrames);
            scr.Text(c0 + cols - 15, 1, "\u2665 POWER " + new string('█', n), Pink, HudBg);
        }
        string stage = StageReadout(g.Stage, cols - 28);
        int stageCol = c0 + cols - stage.Length - 1;
        scr.Text(stageCol, 0, stage, g.Stage >= Tuning.StageCap ? Pink : Ice, HudBg);
        if (debug is not null) scr.Text(stageCol - debug.Length - 1, 0, debug, Dim, HudBg);

        // text carried by the darkness
        _enemyPosts.Clear();
        foreach (var e in g.Enemies)
        {
            if (e.Text is not { Length: > 0 } t || e.Y < 4 || e.Header is not null) continue;   // a labelled enemy speaks in a comm box
            if (e.Kind == EnemyKind.Carrier)
            {
                int col = Math.Clamp(Cx(e.X) - t.Length / 2, c0, c0 + cols - t.Length);
                int crow = Math.Max(2, Cy(e.Y - e.Sprite.H / 2.0) - 1);
                scr.Text(col, crow, t, PostText, PostBg);
                _enemyPosts.Add(new CellRect(col, crow, t.Length, 1));
            }
            else if (e.Kind == EnemyKind.Gunship)
            {
                var lines = BubbleText.Format(t, Math.Min(20, cols - 6), 3);
                int w = lines.Max(l => l.Length) + 2;
                int col = Math.Clamp(Cx(e.X) - w / 2, c0 + 1, c0 + cols - w - 1);
                int row = Cy(e.Y + e.Sprite.H / 2.0) + 1;
                for (int i = 0; i < lines.Length; i++)
                {
                    scr.Text(col, row + i, " " + lines[i].PadRight(w - 2) + " ", EnemyCommText, Palette.Plum);
                    scr.Put(col, row + i, i % 2 == 0 ? '▌' : '▐', Palette.Crimson, Palette.Plum);
                    scr.Put(col + w - 1, row + i, i % 2 == 0 ? '▐' : '▌', Palette.Crimson, Palette.Plum);
                }
                _enemyPosts.Add(new CellRect(col, row, w, lines.Length));
            }
        }

        foreach (var p in g.Popups)
            scr.Text(Math.Clamp(Cx(p.X) - p.Text.Length / 2, c0, c0 + cols - p.Text.Length), Math.Max(2, Cy(p.Y)), p.Text, p.Color);

        if (g.StageBannerLeft > 0 && g.Frame / 6 % 4 != 0 || g.StageBannerLeft > Tuning.StageBannerFrames / 2)
        {
            int row = lay.PRows * 2 / 5;
            string title = StageTitle(g.StageShown);
            int tc = c0 + (cols - title.Length) / 2;
            scr.Text(tc, row, title, Palette.White, Palette.Plum);
            if (Ramp.Hint(g.StageShown) is { } hint && hint.Length <= cols - 2)
            {
                int hc = c0 + (cols - hint.Length) / 2;
                scr.Text(hc, row + 1, hint, Art.Body(Ramp.Introduces(g.StageShown)!.Value), PostBg);
            }
        }

        _comms.Draw(g, scr, lay);

        // like prompt
        if (g.Prompt is { } w2)
        {
            string s = g.LoggedIn ? "\u2665 E / hold" : RaiderGame.SignInHint;
            uint col = g.Frame / 5 % 2 == 0 ? Pink : Palette.White;
            int pc = Math.Clamp(Cx(w2.X) - s.Length / 2, c0, c0 + cols - s.Length);
            scr.Text(pc, Math.Max(2, Cy(w2.Y - w2.Plane.H / 2.0) - 2), s, col, LikeHintBg);
        }

        DrawPanels(g, scr, lay);

        if (g.Banner is { } banner)
        {
            var lines = BubbleText.Format(banner, cols - 6, 3);
            int w = lines.Max(l => l.Length) + 4;
            int row = Math.Max(3, lay.Rows / 4);
            uint bg = g.Frame / 4 % 2 == 0 ? BannerFlashA : BannerFlashB;
            for (int i = 0; i < lines.Length; i++)
                scr.Text(c0 + (cols - w) / 2, row + i, ("  " + lines[i]).PadRight(w), Palette.White, bg);
        }
        if (!g.Alive) CenterText(scr, c0, cols, lay.Rows / 2, " SHIP DOWN ", Palette.White, ShipDownBg);
        else if (g.Tick < 5 * Tuning.Fps && g.Input.PointerLast) CenterText(scr, c0, cols, lay.Rows - 2, " drag to move - hold a pilot to like ", Dim, HudBg);
        else if (g.Tick < 10 * Tuning.Fps && !g.Input.PointerLast) CenterText(scr, c0, cols, lay.Rows - 2, cols >= 50 ? " WASD move  SPACE fire  B bomb  E like  F auto " : " WASD SPACE B E ", Dim, HudBg);
        if (g.Toast is { } toast) CenterText(scr, c0, cols, lay.Rows - 4, " " + toast + " ", Palette.White, ToastBg);
        if (g.Input.PointerLast)
        {
            var (bc, br, bw, bh) = g.BombButton;
            uint bb = g.Bombs > 0 ? BombReady : BombSpent;
            for (int i = 0; i < bh; i++) scr.Blank(bc, br + i, bw, Ice, bb);
            scr.Text(bc + 1, br, "  BOMB  ", g.Bombs > 0 ? Ice : Dim, bb);
            scr.Text(bc + 1, br + 1, ("  " + new string('•', g.Bombs)).PadRight(8), g.Bombs > 0 ? Ice : Dim, bb);
        }
    }

    private static void CenterText(Screen scr, int c0, int cols, int row, string s, uint fg, uint bg) =>
        scr.Text(c0 + Math.Max(0, (cols - s.Length) / 2), row, s, fg, bg);

    /// <summary>Draws the side log or the phone panel. It also sets <c>PanelW</c>, which the log's wrapping and drawing read, so it
    /// must run before them.</summary>
    private void DrawPanels(RaiderGame g, Screen scr, FieldLayout lay)
    {
        PanelW = lay.PanelCols;
        bool wide = PanelW > 0;
        if (wide) DrawLog(g, scr, lay.Col0 - Tuning.SideGapCols - PanelW, 2, lay.Rows - 2);
        else
        {
            // the speaker, else the one still fighting with the lowest age (else the youngest leaver)
            Wingman? w = g.Comms.Speaker;
            if (w is null) foreach (var x in g.Wings)
            {
                bool a = x.Age < Tuning.WingEnterFrames + Tuning.WingFightFrames;
                if (w is null) { w = x; continue; }
                bool wa = w.Age < Tuning.WingEnterFrames + Tuning.WingFightFrames;
                if (a && !wa || a == wa && x.Age < w.Age) w = x;
            }
            // the comm under the ship carries the portrait and the words, so the phone panel shrinks to one line
            if (w is not null) DrawPanelLine(scr, w, lay.Col0 + 1, 2, lay.PCols - 2, w == g.Comms.Speaker);
        }
    }

    /// <summary>The wide screen's left column as a feed: every wingman's post since the run began, the newest ending at
    /// <paramref name="bottom"/> and the older ones above it, cut off by <paramref name="top"/>. It moves only when the log
    /// grows (a wingman arrives, or a thread opens under its post), as a quick scroll of <see cref="LogScrollFrames"/>.</summary>
    private void DrawLog(RaiderGame g, Screen scr, int col, int top, int bottom)
    {
        var entries = g.Log.Entries;
        int total = 0;
        foreach (var w in entries) total += LogLines(w).Length + LogHeader;
        if (_logRows >= 0 && total > _logRows)
        {
            // the scroll runs from when the post opened (game time), not from when this frame first saw it
            int youngest = int.MaxValue;
            foreach (var w in g.Wings) youngest = Math.Min(youngest, w.PostAge);
            _scrollRows = ScrollOffset(g.Tick) + total - _logRows;
            _scrollAt = g.Tick - (youngest == int.MaxValue ? 0 : youngest);
        }
        else if (total < _logRows) _scrollRows = 0;
        _logRows = total;

        int y = bottom + 1 + ScrollOffset(g.Tick);
        for (int i = entries.Count - 1; i >= 0 && y > top; i--)
        {
            var w = entries[i];
            var lines = LogLines(w);
            y -= LogHeader + lines.Length;
            DrawLogEntry(g, scr, w, lines, col, y, top, bottom);
        }
    }

    /// <summary>Rows the scroll still has to run at <paramref name="frame"/>: it starts fast and eases in.</summary>
    private int ScrollOffset(long frame)
    {
        double t = (frame - _scrollAt) / (double)LogScrollFrames;
        return t >= 1 || t < 0 ? 0 : (int)Math.Round(_scrollRows * (1 - t) * (1 - t));
    }

    private const int LogHeader = 5, LogScrollFrames = 6;
    private const int LogOlder = 0, LogRule = 1, LogNewest = 2;
    private int _logRows = -1, _scrollRows;
    private long _scrollAt;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Wingman, LogText> _logText = new();
    private sealed record LogText(string Text, int Earlier, int Width, (string Line, int Kind)[] Lines);

    /// <summary>The wingman's whole thread wrapped to the column: each earlier post, a rule, then the newest; wrapped once per post.</summary>
    private (string Line, int Kind)[] LogLines(Wingman w)
    {
        if (_logText.TryGetValue(w, out var t) && ReferenceEquals(t.Text, w.Text) && t.Earlier == w.Earlier.Count && t.Width == PanelW - 2) return t.Lines;
        int width = PanelW - 2;
        var lines = new List<(string, int)>();
        foreach (var e in w.Earlier)
        {
            foreach (var l in BubbleText.Format(e, width, 200)) lines.Add((l, LogOlder));
            lines.Add(("", LogRule));
        }
        foreach (var l in BubbleText.Format(w.Text, width, 200)) lines.Add((l, LogNewest));
        var arr = lines.ToArray();
        _logText.AddOrUpdate(w, new LogText(w.Text, w.Earlier.Count, width, arr));
        return arr;
    }

    /// <summary>One entry: a rule, the avatar with the handle and likes beside it, then the post. The speaker is brightest with
    /// its accent bar; a wingman still on screen is plain; one that has flown off (or gone) is dimmed, avatar and all.</summary>
    private void DrawLogEntry(RaiderGame g, Screen scr, Wingman w, (string Line, int Kind)[] lines, int col, int row, int top, int bottom)
    {
        bool speaking = w == g.Comms.Speaker, gone = !FeedLog.OnScreen(w, g.Wings);
        double fade = gone ? 0.6 : speaking ? 0 : 0.2;
        // dimmed, but never below the contrast body text needs: a gone entry sits at that floor, not under it
        uint text = Surface.Legible(Surface.Mix(CommPainter.TextFg, PanelBg, fade), PanelBg);
        uint older = Surface.Legible(Surface.Mix(CommPainter.TextFg, PanelBg, 0.45 + fade / 2), PanelBg);
        uint handle = Surface.Legible(gone ? Surface.Mix(Ice, PanelBg, 0.55) : Ice, PanelBg), bar = speaking ? w.Accent : gone ? PanelEdge : Blue;
        uint likesFg = Surface.Legible(Surface.Mix(w.Liked ? Pink : Dim, PanelBg, gone ? 0.4 : 0), PanelBg);
        int height = LogHeader + lines.Length;
        for (int r = Math.Max(row, top); r < Math.Min(row + height, bottom + 1); r++)
        {
            scr.Blank(col, r, PanelW, Palette.White, PanelBg);
            scr.Put(col, r, '▌', bar, PanelBg);
        }
        bool In(int r) => r >= top && r <= bottom;
        if (In(row)) for (int c = 1; c < PanelW; c++) scr.Put(col + c, row, '─', PanelEdge, PanelBg);
        // avatar: 8x8 pixels is 4x4 cells, drawn only when all of it is inside the column
        if (In(row + 1) && In(row + 4))
        {
            FillAvatar(w, _avSurface);
            if (gone) for (int i = 0; i < _avSurface.P.Length; i++) _avSurface.P[i] = Surface.Mix(_avSurface.P[i], PanelBg, 0.6);
            _avQuad.Compose(_avSurface, 0, 0, scr, col + 1, row + 1);
        }
        int tx = col + 6, tw = PanelW - 7;
        if (In(row + 1)) scr.Text(tx, row + 1, WingComms.Trim("@" + w.Handle, tw), handle, PanelBg);
        string likes = w.Liked ? "\u2665 liked" : w.LikesTag;
        if (In(row + 2)) scr.Text(tx, row + 2, likes, likesFg, PanelBg);
        for (int i = 0; i < lines.Length; i++)
        {
            int r = row + LogHeader + i;
            if (!In(r)) continue;
            var (line, kind) = lines[i];
            if (kind == LogRule) for (int k = 0; k < PanelW - 2; k++) scr.Put(col + 1 + k, r, '\u2504', PanelEdge, PanelBg);
            else scr.Text(col + 1, r, line, kind == LogNewest ? text : older, PanelBg);
        }
    }

    private static void DrawPanelLine(Screen scr, Wingman w, int col, int row, int width, bool speaking)
    {
        scr.Blank(col, row, width, Palette.White, PanelBg);
        scr.Put(col, row, '▌', speaking ? w.Accent : Blue, PanelBg);
        string likes = w.Liked ? "\u2665 liked" : w.LikesTag;
        scr.Text(col + 2, row, w.HandleTag.Length <= width - likes.Length - 4 ? w.HandleTag : w.ChipTag, Ice, PanelBg);
        scr.Text(col + width - likes.Length - 1, row, likes, w.Liked ? Pink : Dim, PanelBg);
    }

    internal static void FillAvatar(Wingman w, Surface s)
    {
        if (w.Avatar is { } av) { Array.Copy(av, s.P, 64); return; }
        // no picture (the host could not fetch one): a tinted disc from the author's DID
        ulong h = Hashing.Fnv64(w.Post.AuthorDid);
        uint bg = Surface.Mix(PanelBg, Surface.Pack((int)(Hashing.Unit(h, 1) * 160), (int)(Hashing.Unit(h, 2) * 160), (int)(Hashing.Unit(h, 3) * 220)), 0.45);
        uint fg = Surface.Mix(bg, Palette.White, 0.45);
        s.Fill(bg);
        s.Disc(4, 4, 3, fg);
    }
}
