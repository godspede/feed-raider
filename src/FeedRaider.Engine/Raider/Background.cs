namespace FeedRaider.Engine.Raider;

/// <summary>
/// The scenery. A dark sky with a Bluesky-blue glow far back and three layers of drifting stars: a faint dust, a far layer and a
/// sparse, bright near layer, each faster than the one behind, so the field reads as flying up the page. The posts themselves are
/// not here: the real cards are drawn over this by <see cref="CardPainter"/>. The sky is baked once, so a frame is a copy and a few dots.
/// </summary>
public sealed class Background
{
    private const int DustCount = 34;
    private const ulong DustSeedOffset = 5000, FarSeedOffset = 6000, NearSeedOffset = 7000;

    /// <summary>The sky's fade from top to bottom: the row's depth raised to this, so the dark comes on early.</summary>
    private const double SkyFadeCurve = 0.55;
    /// <summary>The glow's centre across the width, its half width and half height as fractions of the sky, its top offset in rows, and its strongest mix.</summary>
    private const double GlowCentreX = 0.62, GlowHalfWidth = 0.75, GlowHalfHeight = 0.4, GlowMaxMix = 0.34;
    private const int GlowTopOffset = 6;
    /// <summary>The faint cloud streaks: how many, the first one's row, centre column and length as fractions of the sky, and the step of each further one.</summary>
    private const int StreakCount = 2;
    private const double StreakRow = 0.1, StreakRowStep = 0.11, StreakLength = 0.5, StreakLengthStep = -0.12, StreakCol = 0.25, StreakColStep = 0.3;
    /// <summary>The streak's strongest blend, and the strength and offset of the shadow row beneath it.</summary>
    private const double StreakAlpha = 0.22, StreakShadowAlpha = 0.5;
    private const int StreakShadowShiftX = 3, StreakShadowShiftY = 1;
    /// <summary>A dust star's brightness range as a fraction, the chance a star takes the Bluesky tint, the pale tint, and how far dust is mixed from the sky.</summary>
    private const double DustBrightnessMin = 0.25, DustBrightnessSpread = 0.5, StarBlueChance = 0.35, DustMix = 0.55;
    private const uint StarPaleTint = 0xdfe8ff;
    /// <summary>The far and near layers' brightness, as the share mixed from the sky to the star's tint. One shade a layer and tint: a
    /// moving star pairs its colour with every colour of the fade it crosses, so each shade more is hundreds more keys in a phone's
    /// glyph atlas. Near stars are the brightest.</summary>
    private const double FarMix = 0.4, NearMix = 0.75;
    /// <summary>Hash streams a star draws its brightness, tint, column and row from.</summary>
    private const int StarColumnStream = 1, StarRowStream = 2, StarBrightnessStream = 3, StarTintStream = 4;

    /// <summary>The colour the sky fades to at the foot of the field.</summary>
    internal static readonly uint SkyBottom = Surface.Pack(0x05, 0x07, 0x11);

    /// <summary>The most two neighbouring <see cref="SkySteps"/> differ on any channel: the snap's tolerance. Every colour of the
    /// fade then lies within half the tolerance of a step, so a sky colour the snap would fold onto a card's colour is sent as a
    /// step that lies nearer it. Wider steps leave more of the fade nearer the card than any step, and each step is one more colour
    /// a phone's glyph atlas may be sent.</summary>
    private const int SkyStepSpacing = ColourSnap.DefaultTolerance;

    /// <summary>Colours evenly down the fade from <see cref="Palette.SkyTop"/> to <see cref="SkyBottom"/>, the fewest that keep neighbours
    /// within <see cref="SkyStepSpacing"/> of each other: what a snapped encoder sends the sky as where it would fold onto a card
    /// (see <see cref="ColourSnap.Divert"/>).</summary>
    public static IReadOnlyList<uint> SkySteps { get; } = FadeSteps();

    internal static readonly uint BlueskyBlue = Surface.Pack(0x00, 0x85, 0xff);

    private readonly int _width, _height;
    private readonly ulong _seed;
    private readonly Surface _sky;
    private readonly Layer[] _layers;

    /// <summary>One layer of stars: each star's start and colour, the layer's pace in pixels a frame, and its streak during a burst at
    /// full strength, from the shortest to the longest, in pixels behind each star.</summary>
    private sealed record Layer((int X, int Y, uint Colour)[] Stars, double Pace, int StreakMin, int StreakMax);

    /// <summary>The streaks of the dust, the far layer and the near layer: a faster layer draws a longer line, as it rushes past nearer.</summary>
    private const int DustStreakMin = 3, DustStreakMax = 6, FarStreakMin = 5, FarStreakMax = 9, NearStreakMin = 8, NearStreakMax = 14;

    public Background(int width, int height, ulong seed)
    {
        _width = width; _height = height; _seed = seed;
        _sky = BakeSky();
        _layers =
        [
            new(BakeDust(), Tuning.ScrollStars, DustStreakMin, DustStreakMax),
            new(BakeLayer(width * height / Tuning.StarsFarPixels, FarSeedOffset, FarMix), Tuning.ScrollStarsFar, FarStreakMin, FarStreakMax),
            new(BakeLayer(width * height / Tuning.StarsNearPixels, NearSeedOffset, NearMix), Tuning.ScrollStarsNear, NearStreakMin, NearStreakMax),
        ];
    }

    /// <summary>The stars of every layer.</summary>
    public int StarTotal => _layers.Sum(layer => layer.Stars.Length);

    /// <summary>Draws the sky and its stars, each layer at its own pace. <paramref name="streak"/>, 0 to 1, draws each star as a fading
    /// streak behind it, as long as the speed burst is strong; 0 draws dots.</summary>
    public void Draw(Surface target, double scrollFrames, double streak = 0)
    {
        Array.Copy(_sky.P, target.P, Math.Min(_sky.P.Length, target.P.Length));
        foreach (var layer in _layers)
            for (int star = 0; star < layer.Stars.Length; star++)
            {
                var (x, startY, colour) = layer.Stars[star];
                int y = (int)((startY + scrollFrames * layer.Pace) % _height);
                target.Set(x, y, colour);
                int length = (int)Math.Round((layer.StreakMin + star % (layer.StreakMax - layer.StreakMin + 1)) * streak);
                for (int i = 1; i <= length; i++)
                    target.Blend(x, y - i, colour, 1 - i / (double)(length + 1));
            }
    }

    private Surface BakeSky()
    {
        var sky = new Surface(_width, _height);
        for (int y = 0; y < _height; y++)
        {
            double fade = Math.Pow(y / (double)_height, SkyFadeCurve);
            uint row = Surface.Mix(Palette.SkyTop, SkyBottom, fade);
            for (int x = 0; x < _width; x++)
            {
                // the Bluesky-blue accent: a wide glow low on the far horizon (top of the screen), off-centre
                double glowX = (x - _width * GlowCentreX) / (_width * GlowHalfWidth), glowY = (y + GlowTopOffset) / (_height * GlowHalfHeight);
                double glow = Math.Max(0, 1 - double.Hypot(glowX, glowY));
                sky.P[y * _width + x] = glow > 0 ? Surface.Mix(row, BlueskyBlue, glow * glow * GlowMaxMix) : row;
            }
        }
        for (int streak = 0; streak < StreakCount; streak++)
        {
            int streakY = (int)(_height * (StreakRow + StreakRowStep * streak));
            int length = (int)(_width * (StreakLength + StreakLengthStep * streak));
            int streakX = (int)(_width * (StreakCol + StreakColStep * streak));
            for (int x = 0; x < length; x++)
            {
                double alpha = Math.Sin(x / (double)length * Math.PI) * StreakAlpha;
                sky.Blend(streakX + x, streakY, BlueskyBlue, alpha);
                sky.Blend(streakX + x + StreakShadowShiftX, streakY + StreakShadowShiftY, BlueskyBlue, alpha * StreakShadowAlpha);
            }
        }
        return sky;
    }

    private static uint[] FadeSteps()
    {
        var (r, g, b) = Surface.Unpack(SkyBottom);
        int span = ColourSnap.Distance(Palette.SkyTop, r, g, b);
        int gaps = (int)Math.Ceiling(span / (double)SkyStepSpacing);
        return [.. Enumerable.Range(0, gaps + 1).Select(step => Surface.Mix(Palette.SkyTop, SkyBottom, step / (double)gaps))];
    }

    private (int X, int Y, uint Colour)[] BakeDust()
    {
        var stars = new (int X, int Y, uint Colour)[DustCount];
        for (int star = 0; star < stars.Length; star++)
        {
            ulong hash = Hashing.Mix(_seed + DustSeedOffset + (ulong)star);
            double brightness = DustBrightnessMin + Hashing.Unit(hash, StarBrightnessStream) * DustBrightnessSpread;
            uint tint = Hashing.Unit(hash, StarTintStream) < StarBlueChance ? BlueskyBlue : StarPaleTint;
            stars[star] = ((int)(Hashing.Unit(hash, StarColumnStream) * _width), (int)(Hashing.Unit(hash, StarRowStream) * _height), Surface.Mix(Palette.SkyTop, tint, brightness * DustMix));
        }
        return stars;
    }

    /// <summary><paramref name="count"/> stars, each <paramref name="mix"/> of the way from the sky to its tint.</summary>
    private (int X, int Y, uint Colour)[] BakeLayer(int count, ulong seedOffset, double mix)
    {
        var stars = new (int X, int Y, uint Colour)[Math.Max(1, count)];
        for (int star = 0; star < stars.Length; star++)
        {
            ulong hash = Hashing.Mix(_seed + seedOffset + (ulong)star);
            uint tint = Hashing.Unit(hash, StarTintStream) < StarBlueChance ? BlueskyBlue : StarPaleTint;
            stars[star] = ((int)(Hashing.Unit(hash, StarColumnStream) * _width), (int)(Hashing.Unit(hash, StarRowStream) * _height), ClearOfCards(tint, mix));
        }
        return stars;
    }

    /// <summary>How much brighter a layer star's shade is made each time it is still within the snap's reach of a card colour.</summary>
    private const double ShadeNudge = 0.03;

    /// <summary>The star colour <paramref name="mix"/> of the way from the sky to <paramref name="tint"/>, brightened while it lies within
    /// <see cref="ColourSnap.DefaultTolerance"/> of a card colour: the game's encoder would send such a star as that card colour,
    /// a speck of card in the sky.</summary>
    private static uint ClearOfCards(uint tint, double mix)
    {
        uint colour = Surface.Mix(Palette.SkyTop, tint, mix);
        while (mix < 1 && CardColours.All.Any(card => NearCard(card, colour)))
        {
            mix = Math.Min(1, mix + ShadeNudge);
            colour = Surface.Mix(Palette.SkyTop, tint, mix);
        }
        return colour;
    }

    private static bool NearCard(uint card, uint colour)
    {
        var (r, g, b) = Surface.Unpack(colour);
        return ColourSnap.Distance(card, r, g, b) <= ColourSnap.DefaultTolerance;
    }
}
