namespace FeedRaider.Engine.Raider;

/// <summary>
/// The scenery. A dark sky with a Bluesky-blue glow far back, and below it terrain that is a feed: giant faded
/// post-card slabs (avatar disc, dim text dashes, a soft edge) drifting past like city blocks, on two parallax layers.
/// The sky is baked once and each card is baked once per variant, so a frame is a handful of row copies.
/// </summary>
public sealed class Background
{
    private const int Variants = 14;
    private static readonly uint SkyTop = Surface.Pack(0x0b, 0x1c, 0x44), SkyBottom = Surface.Pack(0x05, 0x07, 0x11);
    private static readonly uint BlueskyBlue = Surface.Pack(0x00, 0x85, 0xff);

    private sealed class Layer
    {
        public required Surface?[] Cards;
        public required int[] X, Y, Height;      // per variant: left edge, offset inside the period, card height
        public required int Period;
        public required double Speed;
        public required (uint Fill, uint Edge, uint Dash, uint Bright, uint Avatar, uint Accent) Pal;
        public required int CardW;
    }

    private readonly int _w, _h;
    private readonly ulong _seed;
    private readonly Surface _sky;
    private readonly (int X, int Y, uint C)[] _stars;
    private readonly Layer _far, _near;

    public Background(int w, int h, ulong seed)
    {
        _w = w; _h = h; _seed = seed;
        _sky = BakeSky();
        _stars = BakeStars();
        _far = MakeLayer(0, (int)(w * 0.36), Tuning.ScrollFar, 13, 8, (Surface.Pack(12, 18, 36), Surface.Pack(19, 29, 54), Surface.Pack(21, 32, 58), Surface.Pack(25, 38, 68), Surface.Pack(24, 40, 72), Surface.Pack(16, 44, 84)), far: true);
        _near = MakeLayer(1, (int)(w * 0.7), Tuning.ScrollNear, 10, 12, (Surface.Pack(15, 24, 46), Surface.Pack(27, 42, 74), Surface.Pack(26, 40, 70), Surface.Pack(36, 56, 94), Surface.Pack(30, 50, 88), Surface.Pack(18, 58, 112)), far: false);
    }

    public void Draw(Surface s, double scrollFrames)
    {
        Array.Copy(_sky.P, s.P, Math.Min(_sky.P.Length, s.P.Length));
        foreach (var (x, y0, c) in _stars)
        {
            int y = (int)((y0 + scrollFrames * Tuning.ScrollStars) % _h);
            s.Set(x, y, c);
        }
        DrawLayer(s, _far, scrollFrames);
        DrawLayer(s, _near, scrollFrames);
    }

    private void DrawLayer(Surface s, Layer l, double scrollFrames)
    {
        double shift = scrollFrames * l.Speed;
        int t = (int)(shift % l.Period);
        // card k sits at Y[k] within a period; the period repeats downward as the scroll advances
        for (int n = -1; n * l.Period < _h + l.Period; n++)
            for (int k = 0; k < Variants; k++)
            {
                int y = l.Y[k] + t + n * l.Period - l.Period;
                if (y >= _h || y + l.Height[k] <= 0) continue;
                DrawCard(s, l.Cards[k] ??= BakeCard(l, k, l.Height[k], _seed), l.X[k], y);
            }
    }

    private static void DrawCard(Surface dst, Surface card, int x, int y)
    {
        int h = card.H, w = card.W;
        // rounded corners: the first and last two rows are inset
        for (int r = 0; r < h; r++)
        {
            int inset = r == 0 || r == h - 1 ? 2 : r == 1 || r == h - 2 ? 1 : 0;
            dst.CopyFrom(card, inset, r, w - 2 * inset, 1, x + inset, y + r);
        }
    }

    private Layer MakeLayer(int index, int cardW, double speed, int gapMin, int gapSpan, (uint, uint, uint, uint, uint, uint) pal, bool far)
    {
        var x = new int[Variants];
        var y = new int[Variants];
        var h = new int[Variants];
        int cursor = 0;
        for (int k = 0; k < Variants; k++)
        {
            ulong hs = Hashing.Mix(_seed + (ulong)(index * 977 + k));
            h[k] = (far ? 20 : 26) + (int)(Hashing.Unit(hs, 1) * (far ? 14 : 20));
            y[k] = cursor;
            cursor += h[k] + gapMin + (int)(Hashing.Unit(hs, 2) * gapSpan);
            if (far)
            {
                // far cards peek out beside the near column, alternating sides
                bool left = k % 2 == 0;
                x[k] = left ? -(int)(cardW * 0.38) + (int)(Hashing.Unit(hs, 3) * 5) : _w - (int)(cardW * 0.62) - (int)(Hashing.Unit(hs, 3) * 5);
            }
            else x[k] = (_w - cardW) / 2 + (int)((Hashing.Unit(hs, 3) - 0.5) * 8);
        }
        return new Layer { Cards = new Surface?[Variants], X = x, Y = y, Height = h, Period = cursor, Speed = speed, Pal = pal, CardW = cardW };
    }

    private static Surface BakeCard(Layer l, int slot, int h, ulong seed)
    {
        int w = l.CardW;
        var (fill, edge, dash, bright, avatar, accent) = l.Pal;
        ulong hs = Hashing.Mix(seed ^ (ulong)(slot * 7919 + w));
        var c = new Surface(w, h);
        c.Fill(fill);
        // soft edge: a bright ring melting into a dimmer outer ring
        uint soft = Surface.Mix(fill, edge, 0.45);
        for (int x = 0; x < w; x++) { c.Set(x, 0, soft); c.Set(x, h - 1, soft); c.Set(x, 1, edge); c.Set(x, h - 2, Surface.Mix(fill, edge, 0.7)); }
        for (int y = 0; y < h; y++) { c.Set(0, y, soft); c.Set(w - 1, y, soft); c.Set(1, y, Surface.Mix(fill, edge, 0.7)); c.Set(w - 2, y, Surface.Mix(fill, edge, 0.7)); }
        // header: avatar disc, name and handle dashes
        c.Disc(7, 7, 3, avatar);
        Dashes(c, 13, 5, 10 + (int)(Hashing.Unit(hs, 1) * 14), 1, bright);
        Dashes(c, 13, 8, 12 + (int)(Hashing.Unit(hs, 2) * 10), 1, dash, handle: true);
        int y0 = 13;
        bool image = Hashing.Unit(hs, 3) < 0.3 && h >= 34;
        int footer = h - 6;
        int line = 0;
        for (int y = y0; y + 1 < footer && !(image && y > y0 + 1); y += 3, line++)
        {
            int right = w - 6 - (int)(Hashing.Unit(hs, 10 + line) * (line == 0 ? 3 : 14));
            Words(c, 6, y, right, hs, line, dash);
        }
        if (image)
        {
            int iy = y0 + 5, ih = Math.Min(9, footer - iy - 2);
            if (ih >= 4)
            {
                for (int yy = 0; yy < ih; yy++)
                    c.Rect(6, iy + yy, w - 12, 1, Surface.Mix(Surface.Mix(fill, accent, 0.5), fill, yy / (double)ih));
                c.Rect(6, iy, w - 12, 1, edge);
                c.Rect(6, iy + ih - 1, w - 12, 1, edge);
            }
        }
        // footer: four tiny icons and a count
        for (int i = 0; i < 4; i++) c.Rect(7 + i * (w - 16) / 4, footer + 1, 2, 2, Hashing.Unit(hs, 40 + i) < 0.25 ? accent : dash);
        return c;
    }

    private static void Words(Surface c, int x, int y, int right, ulong hs, int line, uint color)
    {
        int i = 0;
        while (x < right)
        {
            int len = 2 + (int)(Hashing.Unit(hs, 100 + line * 17 + i) * 6);
            len = Math.Min(len, right - x);
            if (len < 1) break;
            c.Rect(x, y, len, 1, color);
            x += len + 1;
            i++;
        }
    }

    private static void Dashes(Surface c, int x, int y, int len, int h, uint color, bool handle = false)
    {
        c.Rect(x, y, len, h, color);
        if (handle) c.Rect(x - 1, y, 1, h, color);
    }

    private Surface BakeSky()
    {
        var s = new Surface(_w, _h);
        for (int y = 0; y < _h; y++)
        {
            double t = Math.Pow(y / (double)_h, 0.55);
            uint row = Surface.Mix(SkyTop, SkyBottom, t);
            for (int x = 0; x < _w; x++)
            {
                // the Bluesky-blue accent: a wide glow low on the far horizon (top of the screen), off-centre
                double dx = (x - _w * 0.62) / (_w * 0.75), dy = (y + 6) / (_h * 0.4);
                double g = Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy));
                s.P[y * _w + x] = g > 0 ? Surface.Mix(row, BlueskyBlue, g * g * 0.34) : row;
            }
        }
        // two thin cloud streaks catching the glow
        for (int i = 0; i < 2; i++)
        {
            int cy = (int)(_h * (0.1 + 0.11 * i)), len = (int)(_w * (0.5 - 0.12 * i)), cx = (int)(_w * (0.25 + 0.3 * i));
            for (int x = 0; x < len; x++)
            {
                double a = Math.Sin(x / (double)len * Math.PI) * 0.22;
                s.Blend(cx + x, cy, BlueskyBlue, a);
                s.Blend(cx + x + 3, cy + 1, BlueskyBlue, a * 0.5);
            }
        }
        return s;
    }

    private (int, int, uint)[] BakeStars()
    {
        var stars = new (int, int, uint)[34];
        for (int i = 0; i < stars.Length; i++)
        {
            ulong hs = Hashing.Mix(_seed + 5000 + (ulong)i);
            double b = 0.25 + Hashing.Unit(hs, 3) * 0.5;
            uint tint = Hashing.Unit(hs, 4) < 0.35 ? BlueskyBlue : 0xdfe8ff;
            stars[i] = ((int)(Hashing.Unit(hs, 1) * _w), (int)(Hashing.Unit(hs, 2) * _h), Surface.Mix(SkyTop, tint, b * 0.55));
        }
        return stars;
    }
}
