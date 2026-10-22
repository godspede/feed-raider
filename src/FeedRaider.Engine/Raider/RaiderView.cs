using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>Draws the game into a <see cref="Screen"/>: the pixel field through the quad composer, text overlays on top.</summary>
public sealed class RaiderView
{
    public static readonly uint Ground = Surface.Pack(0x05, 0x07, 0x11);
    public static readonly uint Rail = Surface.Pack(0x1c, 0x3a, 0x6e);
    public static readonly uint Blue = Surface.Pack(0x00, 0x85, 0xff);
    private const uint HudBg = 0x0a1224, PanelBg = 0x0c1830, PanelEdge = 0x1c3a6e, Dim = 0x6f86ad, Pink = 0xff5a8a, Ice = 0xbfe9ff;
    private const int PanelW = 26, PanelMinH = 8, PanelChrome = 6, PanelNarrowH = 7;

    private readonly QuadComposer _quad = new();
    private readonly QuadComposer[] _avQuad = [new(), new()];
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
        if (!lay.Ok)
        {
            scr.Text(1, 1, "terminal too small", 0xd8cba3);
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
    }

    // ---------------------------------------------------------------- pixels

    /// <summary>The newer kinds and their telegraphs. All of it is pixels, so the comm bubble's text always draws over it.</summary>
    private static void DrawNewer(RaiderGame g, Surface f, Enemy e)
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
            Cross(f, (int)Math.Round(e.AimX), (int)Math.Round(e.AimY), 2, g.Frame / 3 % 2 == 0 ? Art.DiverLime : 0xffffffu);
        }
        if (e.Kind == EnemyKind.Turret && e.Windup > 0)
        {
            double t = 1 - e.Windup / (double)Tuning.TurretWindupFrames;
            white = Math.Max(white, t * 0.6);
            int r = 2 + (int)Math.Round((1 - t) * 4);   // the reticle closes on the locked spot
            Reticle(f, (int)Math.Round(e.AimX), (int)Math.Round(e.AimY), r, Art.TurretTeal);
        }
        int x0 = (int)Math.Round(e.X - sp.W / 2.0), y0 = (int)Math.Round(e.Y - sp.H / 2.0);
        f.Draw(sp, x0, y0, white);
        if (e.Kind == EnemyKind.Turret && e.Windup > 0) f.Set(x0 + sp.W / 2, y0 + sp.H, 0xffffff);   // the barrel lights
        if (e.Kind == EnemyKind.Splitter && e.Hp <= e.MaxHp / 2)
        {
            // cracks, and a seam that glows: it is about to burst
            int cx = x0 + sp.W / 2, cy = y0 + sp.H / 2;
            f.Set(cx - 2, cy - 1, 0x2a1740); f.Set(cx - 3, cy, 0x2a1740); f.Set(cx + 2, cy + 1, 0x2a1740); f.Set(cx + 3, cy, 0x2a1740);
            uint seam = g.Frame / 3 % 2 == 0 ? 0xffffffu : Art.SplitterPink;
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
        foreach (var e in g.Enemies)
        {
            if (e.Kind >= EnemyKind.Mine) { DrawNewer(g, f, e); continue; }
            f.Draw(e.Sprite, (int)Math.Round(e.X - e.Sprite.W / 2.0), (int)Math.Round(e.Y - e.Sprite.H / 2.0), e.Flash > 0 ? 0.85 : 0);
            if (e.Kind == EnemyKind.Gunship && e.Hp < e.MaxHp)
            {
                int bw = e.Sprite.W, x0 = (int)Math.Round(e.X - bw / 2.0), y0 = (int)Math.Round(e.Y - e.Sprite.H / 2.0) - 3;
                f.Rect(x0, y0, bw, 1, 0x2a1740);
                f.Rect(x0, y0, (int)(bw * Math.Max(0, e.Hp) / e.MaxHp), 1, 0xff3d6e);
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
                f.Set(x, y, 0xffffff);
            }
            else if (!b.Friendly) { f.Rect(x - 1, y - 1, 2, 2, 0xff3d6e); f.Set(x, y, 0xffffff); }
            else if (b.Wing) { f.Rect(x, y - 1, 1, 3, 0xffe29a); }
            else { f.Rect(x, y - 2, 1, 4, 0xbfe9ff); f.Set(x, y - 3, 0xffffff); }
        }
        foreach (var r in g.Rings)
        {
            double t = 1 - r.Life / (double)r.Max, e = 1 - (1 - t) * (1 - t);
            double rad = r.R0 + (r.R1 - r.R0) * e;
            double a = 1 - t;
            f.Ring(r.X, r.Y, rad, r.Color, 0.35 + 0.65 * a);
            if (rad > 3) f.Ring(r.X, r.Y, rad - 1, r.Color, 0.25 * a);
        }
        foreach (var p in g.Particles)
        {
            int x = (int)Math.Round(p.X), y = (int)Math.Round(p.Y);
            double fade = Math.Min(1, p.Life / (double)Math.Max(1, p.MaxLife) * 1.6);
            uint c = fade < 1 ? Surface.Mix(0x0b1c44, p.Color, fade) : p.Color;
            if (p.Kind == ParticleKind.Heart) { f.Set(x - 1, y, c); f.Set(x + 1, y, c); f.Rect(x - 1, y + 1, 3, 1, c); f.Set(x, y + 2, c); }
            else f.Rect(x, y, p.Size, p.Size, c);
        }
        if (g.ScreenFlash > 0)
        {
            double a = 0.5 * g.ScreenFlash / Tuning.ScreenFlashFrames;
            var px = f.P;
            for (int i = 0; i < px.Length; i++) px[i] = Surface.Mix(px[i], 0xFFFFFF, a);
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
        f.Ring(cx, cy, r, 0x4a2038, 0.5);
        int n = (int)Math.Round(progress * 48);
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + i / 48.0 * Math.PI * 2;
            f.Set((int)Math.Round(cx + Math.Cos(a) * r), (int)Math.Round(cy + Math.Sin(a) * r), Pink);
        }
    }

    private static void FlameAndMuzzle(Surface f, double cx, double cy, Sprite plane, bool muzzle, long frame)
    {
        int x = (int)Math.Round(cx), y = (int)Math.Round(cy);
        int fl = 1 + (int)(frame % 3);
        f.Rect(x, y + plane.H / 2, 1, fl, 0xffb347);
        if (muzzle)
        {
            int ny = y - plane.H / 2 - 1;
            f.Rect(x - 2, ny - 1, 5, 3, 0xffe29a);
            f.Rect(x - 1, ny - 2, 3, 5, 0xffffff);
        }
    }

    /// <summary>A 6x6 capsule in the pickup's colour with a pulsing glow ring; it blinks for its last two seconds.</summary>
    private static void DrawPickup(RaiderGame g, Surface f, Pickup p)
    {
        if (p.Blinking && g.Frame / 3 % 2 == 0) return;
        uint c = RaiderGame.PickupColor(p.Kind), dark = Surface.Mix(c, 0x0b1c44, 0.55), light = Surface.Mix(c, 0xffffff, 0.6);
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
        f.Ring(g.PX, g.PY, 8.5, 0x4fe8ff, a);
        f.Ring(g.PX, g.PY, 7.5, 0x4fe8ff, a * 0.4);
        long since = g.Frame - g.ShieldHitFrame;
        if (since < 6) f.Ring(g.PX, g.PY, 8.5 + since, 0xffffff, 0.9 - since * 0.13);
    }

    private static void DrawPlayer(RaiderGame g, Surface f)
    {
        if (g.Powered)
        {
            double pulse = 0.5 + 0.5 * Math.Sin(g.Frame * 0.6);
            f.Ring(g.PX, g.PY, 9 + pulse * 2, Pink, 0.5);
            if (g.PowerLeft < Tuning.Fps && g.Frame % 6 < 3) f.Ring(g.PX, g.PY, 12, 0xffffff, 0.4);
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

    private static string Hearts(int n) => new('\u2665', Math.Max(0, n));   // \u2665 heart suit

    private void DrawText(RaiderGame g, Screen scr, FieldLayout lay, string? debug)
    {
        int c0 = lay.Col0, cols = lay.PCols;
        int Cx(double x) => c0 + (int)(x / 2);
        int Cy(double y) => (int)(y / 2);

        // HUD
        scr.Blank(c0, 0, cols, 0xffffff, HudBg);
        scr.Blank(c0, 1, cols, 0xffffff, HudBg);
        scr.Text(c0 + 1, 0, $"SCORE {g.Score:D6}", 0xffffff, HudBg);
        scr.Text(c0 + 15, 0, Hearts(g.Hp), Pink, HudBg);
        scr.Text(c0 + 20, 0, "B" + new string('•', g.Bombs), Ice, HudBg);
        Pips(scr, c0 + 20, 1, 'W', g.WeaponLevel, Tuning.MaxWeaponLevel, RaiderGame.PickupColor(PickupKind.Power));
        Pips(scr, c0 + 26, 1, 'S', g.Shield, Tuning.ShieldHits, RaiderGame.PickupColor(PickupKind.Shield));
        string wave = g.WaveLabel();
        scr.Text(c0 + 1, 1, wave, wave[0] == '◉' ? 0xff6b9a : Dim, HudBg);
        if (g.Powered)
        {
            int n = Math.Max(1, g.PowerLeft * 8 / Tuning.PowerUpFrames);
            scr.Text(c0 + cols - 15, 1, "\u2665 POWER " + new string('█', n), Pink, HudBg);   // \u2665 heart suit
        }
        string stage = StageReadout(g.Stage, cols - 28);
        int stageCol = c0 + cols - stage.Length - 1;
        scr.Text(stageCol, 0, stage, g.Stage >= Tuning.StageCap ? Pink : Ice, HudBg);
        if (debug is not null) scr.Text(stageCol - debug.Length - 1, 0, debug, Dim, HudBg);

        // text carried by the darkness
        foreach (var e in g.Enemies)
        {
            if (e.Text is not { Length: > 0 } t || e.Y < 4) continue;
            if (e.Kind == EnemyKind.Carrier)
            {
                int col = Math.Clamp(Cx(e.X) - t.Length / 2, c0, c0 + cols - t.Length);
                _comms.Avoid(new CellRect(col, Math.Max(2, Cy(e.Y - e.Sprite.H / 2.0) - 1), t.Length, 1));
                scr.Text(col, Math.Max(2, Cy(e.Y - e.Sprite.H / 2.0) - 1), t, 0xe3c8ff, 0x1a1030);
            }
            else if (e.Kind == EnemyKind.Gunship)
            {
                var lines = BubbleText.Format(t, Math.Min(20, cols - 6), 3);
                int w = lines.Max(l => l.Length) + 2;
                int col = Math.Clamp(Cx(e.X) - w / 2, c0 + 1, c0 + cols - w - 1);
                int row = Cy(e.Y + e.Sprite.H / 2.0) + 1;
                _comms.Avoid(new CellRect(col, row, w, lines.Length));
                for (int i = 0; i < lines.Length; i++)
                {
                    scr.Text(col, row + i, " " + lines[i].PadRight(w - 2) + " ", 0xffd0e0, 0x2a1740);
                    scr.Put(col, row + i, i % 2 == 0 ? '▌' : '▐', 0xff3d6e, 0x2a1740);
                    scr.Put(col + w - 1, row + i, i % 2 == 0 ? '▐' : '▌', 0xff3d6e, 0x2a1740);
                }
            }
        }

        // label badge over every darkness enemy
        foreach (var e in g.Enemies)
        {
            if (e.Badge is not { } badge || e.Source is not { } kind || e.Y + e.Sprite.H / 2.0 <= 0) continue;
            var (bc, br) = EnemyBadge.Place(e, c0, cols, badge.Length);
            scr.Text(bc, br, badge, EnemyBadge.Ink, EnemyBadge.Background(kind));
        }

        foreach (var p in g.Popups)
            scr.Text(Math.Clamp(Cx(p.X) - p.Text.Length / 2, c0, c0 + cols - p.Text.Length), Math.Max(2, Cy(p.Y)), p.Text, p.Color);

        if (g.StageBannerLeft > 0 && g.Frame / 6 % 4 != 0 || g.StageBannerLeft > Tuning.StageBannerFrames / 2)
        {
            int row = lay.PRows * 2 / 5;
            string title = StageTitle(g.StageShown);
            int tc = c0 + (cols - title.Length) / 2;
            _comms.Avoid(new CellRect(tc, row, title.Length, 1));
            scr.Text(tc, row, title, 0xffffff, 0x2a1740);
            if (Ramp.Hint(g.StageShown) is { } hint && hint.Length <= cols - 2)
            {
                int hc = c0 + (cols - hint.Length) / 2;
                _comms.Avoid(new CellRect(hc, row + 1, hint.Length, 1));
                scr.Text(hc, row + 1, hint, Art.Body(Ramp.Introduces(g.StageShown)!.Value), 0x1a1030);
            }
        }

        _comms.Draw(g, scr, lay);

        // like prompt
        if (g.Prompt is { } w2)
        {
            string s = g.LoggedIn ? "\u2665 E / hold" : RaiderGame.SignInHint;   // \u2665 heart suit
            uint col = g.Frame / 5 % 2 == 0 ? Pink : 0xffffff;
            int pc = Math.Clamp(Cx(w2.X) - s.Length / 2, c0, c0 + cols - s.Length);
            scr.Text(pc, Math.Max(2, Cy(w2.Y - w2.Plane.H / 2.0) - 2), s, col, 0x2a1030);
        }

        DrawPanels(g, scr, lay);

        if (g.Banner is { } banner)
        {
            var lines = BubbleText.Format(banner, cols - 6, 3);
            int w = lines.Max(l => l.Length) + 4;
            int row = Math.Max(3, lay.Rows / 4);
            uint bg = g.Frame / 4 % 2 == 0 ? 0x4a1040u : 0x2a0a30u;
            for (int i = 0; i < lines.Length; i++)
                scr.Text(c0 + (cols - w) / 2, row + i, ("  " + lines[i]).PadRight(w), 0xffffff, bg);
        }
        if (!g.Alive) CenterText(scr, c0, cols, lay.Rows / 2, " SHIP DOWN ", 0xffffff, 0x5a1030);
        else if (g.Tick < 5 * Tuning.Fps && g.Input.PointerLast) CenterText(scr, c0, cols, lay.Rows - 2, " drag to move - hold a pilot to like ", Dim, HudBg);
        else if (g.Tick < 10 * Tuning.Fps && !g.Input.PointerLast) CenterText(scr, c0, cols, lay.Rows - 2, cols >= 50 ? " WASD move  SPACE fire  B bomb  E like  F auto " : " WASD SPACE B E ", Dim, HudBg);
        if (g.Toast is { } toast) CenterText(scr, c0, cols, lay.Rows - 4, " " + toast + " ", 0xffffff, 0x3a1030);
        if (g.Input.PointerLast)
        {
            var (bc, br, bw, bh) = g.BombButton;
            uint bb = g.Bombs > 0 ? 0x1a3a6au : 0x16202eu;
            for (int i = 0; i < bh; i++) scr.Blank(bc, br + i, bw, Ice, bb);
            scr.Text(bc + 1, br, "  BOMB  ", g.Bombs > 0 ? Ice : Dim, bb);
            scr.Text(bc + 1, br + 1, ("  " + new string('•', g.Bombs)).PadRight(8), g.Bombs > 0 ? Ice : Dim, bb);
        }
    }

    private static void CenterText(Screen scr, int c0, int cols, int row, string s, uint fg, uint bg) =>
        scr.Text(c0 + Math.Max(0, (cols - s.Length) / 2), row, s, fg, bg);

    private void DrawPanels(RaiderGame g, Screen scr, FieldLayout lay)
    {
        bool wide = lay.Margin >= PanelW + 3;
        if (wide)
        {
            // each panel is as tall as its whole post (or open thread) needs; when two do not fit, the shorter keeps what it needs
            int avail = lay.Rows - 4, tw = PanelW - 2;
            int n0 = 0, n1 = 0;
            Wingman? w0 = null, w1 = null;
            foreach (var w in g.Wings) { if (w.Slot == 0) w0 = w; else w1 = w; }
            if (w0 is not null) n0 = Math.Max(PanelMinH, PanelChrome + ThreadRows(w0, tw));
            if (w1 is not null) n1 = Math.Max(PanelMinH, PanelChrome + ThreadRows(w1, tw));
            int room = avail - (w0 is not null && w1 is not null ? 1 : 0), h0 = n0, h1 = n1;
            if (n0 + n1 > room)
            {
                int half = room / 2;
                if (n0 <= half) h1 = room - n0;
                else if (n1 <= half) h0 = room - n1;
                else { h0 = half; h1 = room - half; }
            }
            int col = Math.Max(1, lay.Col0 - PanelW - 2);
            if (w0 is not null) DrawPanel(g, scr, w0, col, 2, PanelW, h0, slideFromLeft: true);
            if (w1 is not null) DrawPanel(g, scr, w1, col, 2 + (w0 is not null ? h0 + 1 : 0), PanelW, h1, slideFromLeft: true);
        }
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

    private void DrawPanel(RaiderGame g, Screen scr, Wingman w, int col, int row, int width, int height, bool slideFromLeft)
    {
        // slide in over the first 10 frames, out over the last 10 (a wingman that has left takes its panel with it)
        int total = w.TotalFrames;
        double inT = Math.Min(1, w.Age / 10.0), outT = Math.Min(1, (total - w.Age) / 10.0);
        double slide = 1 - Math.Min(inT, outT);
        slide = slide * slide;
        if (slideFromLeft) col -= (int)Math.Round(slide * (width + 4));
        else row -= (int)Math.Round(slide * (height + 4));
        for (int r = 0; r < height; r++)
            scr.Blank(col, row + r, width, 0xffffff, PanelBg);
        for (int c = 0; c < width; c++)
        {
            scr.Put(col + c, row, '─', PanelEdge, PanelBg);
            scr.Put(col + c, row + height - 1, '─', PanelEdge, PanelBg);
        }
        scr.Put(col, row, '▌', Blue, PanelBg);
        if (w == g.Comms.Speaker) for (int r = 0; r < height; r++) scr.Put(col, row + r, '▌', w.Accent, PanelBg);
        // avatar: 8x8 pixels is 4x4 cells
        FillAvatar(w);
        if (col >= 0 && row >= 0 && col + 5 < scr.Cols && row + 5 < scr.Rows) _avQuad[w.Slot & 1].Compose(_avSurface, 0, 0, scr, col + 1, row + 1);
        int tx = col + 6, tw = width - 7;
        scr.Text(tx, row + 1, Trim("@" + w.Handle, tw), Ice, PanelBg);
        string likes = w.Liked ? "\u2665 liked" : w.LikesTag;   // \u2665 heart suit
        // wide panels run the text under the avatar; narrow ones put it beside the avatar
        bool beside = width > PanelW + 4;
        int xr = beside ? tx : col + 1, wr = beside ? tw : width - 2;
        int yr = beside ? row + 2 : row + 5, nr = beside ? height - 3 : height - 6;
        if (beside) scr.Text(col + width - likes.Length - 1, row + 1, likes, w.Liked ? Pink : Dim, PanelBg);
        else scr.Text(tx, row + 2, likes, w.Liked ? Pink : Dim, PanelBg);
        int shown = w.Typed();
        var lines = ThreadLines(w, wr, Math.Max(1, nr));
        for (int i = 0; i < lines.Count; i++)
        {
            var (line, kind) = lines[i];
            if (kind == PanelRule) { for (int k = 0; k < wr; k++) scr.Put(xr + k, yr + i, '┄', PanelEdge, PanelBg); continue; }
            if (kind != PanelNewest) { scr.Text(xr, yr + i, line, kind == PanelStub ? Dim : Surface.Mix(0xe6eefc, PanelBg, 0.45), PanelBg); continue; }
            if (shown <= 0) break;
            int n = Math.Min(line.Length, shown);
            shown -= n;
            scr.Text(xr, yr + i, line[..n], 0xe6eefc, PanelBg);
            if (n < line.Length) { scr.Put(xr + n, yr + i, '▌', Blue, PanelBg); break; }
        }
    }

    private const int PanelOlder = 0, PanelStub = 1, PanelRule = 2, PanelNewest = 3;

    /// <summary>Rows the whole open thread takes wrapped to <paramref name="width"/>: every post in full, a rule between them.</summary>
    private static int ThreadRows(Wingman w, int width)
    {
        int n = BubbleText.Format(w.Text, width, 200).Length;
        foreach (var t in w.Earlier) n += BubbleText.Format(t, width, 200).Length + 1;
        return n;
    }

    /// <summary>The thread for a panel of <paramref name="rows"/> text rows: every post in full when it fits. If not, the oldest
    /// posts shrink into one "+N earlier" line until it does, and only a newest post longer than the panel itself is cut.</summary>
    private static List<(string Text, int Kind)> ThreadLines(Wingman w, int width, int rows)
    {
        var older = new string[w.Earlier.Count][];
        for (int i = 0; i < older.Length; i++) older[i] = BubbleText.Format(w.Earlier[i], width, 200);
        var newest = BubbleText.Format(w.Text, width, 200);
        int from = 0, total = newest.Length;
        foreach (var o in older) total += o.Length + 1;
        while (total > rows && from < older.Length)
        {
            total -= older[from].Length + 1;
            if (from == 0) total++;      // the "+N earlier" line
            from++;
        }
        var lines = new List<(string, int)>();
        if (from > 0) lines.Add(($"+{from} earlier", PanelStub));
        for (int i = from; i < older.Length; i++)
        {
            foreach (var l in older[i]) lines.Add((l, PanelOlder));
            lines.Add(("", PanelRule));
        }
        int left = Math.Max(1, rows - lines.Count);
        if (newest.Length > left) newest = BubbleText.Format(w.Text, width, left);
        foreach (var l in newest) lines.Add((l, PanelNewest));
        return lines;
    }

    private static void DrawPanelLine(Screen scr, Wingman w, int col, int row, int width, bool speaking)
    {
        scr.Blank(col, row, width, 0xffffff, PanelBg);
        scr.Put(col, row, '▌', speaking ? w.Accent : Blue, PanelBg);
        string likes = w.Liked ? "\u2665 liked" : w.LikesTag;   // \u2665 heart suit
        scr.Text(col + 2, row, w.HandleTag.Length <= width - likes.Length - 4 ? w.HandleTag : w.ChipTag, Ice, PanelBg);
        scr.Text(col + width - likes.Length - 1, row, likes, w.Liked ? Pink : Dim, PanelBg);
    }

    private static string Trim(string s, int w) => s.Length <= w ? s : s[..Math.Max(1, w - 1)] + "…";

    private void FillAvatar(Wingman w) => FillAvatar(w, _avSurface);

    internal static void FillAvatar(Wingman w, Surface s)
    {
        if (w.Avatar is { } av) { Array.Copy(av, s.P, 64); return; }
        // no picture (the host could not fetch one): a tinted disc from the author's DID
        ulong h = Hashing.Fnv64(w.Post.AuthorDid);
        uint bg = Surface.Mix(0x0c1830, Surface.Pack((int)(Hashing.Unit(h, 1) * 160), (int)(Hashing.Unit(h, 2) * 160), (int)(Hashing.Unit(h, 3) * 220)), 0.45);
        uint fg = Surface.Mix(bg, 0xffffff, 0.45);
        s.Fill(bg);
        s.Disc(4, 4, 3, fg);
    }
}
