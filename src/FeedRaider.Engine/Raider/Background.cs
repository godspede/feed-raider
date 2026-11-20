namespace FeedRaider.Engine.Raider;

/// <summary>
/// The scenery. A dark sky with a Bluesky-blue glow far back and a few drifting stars. The posts themselves are not here: the
/// real cards are drawn over this by <see cref="CardPainter"/>. The sky is baked once, so a frame is a copy and a few dots.
/// </summary>
public sealed class Background
{
    private const int StarCount = 34;
    private const ulong StarSeedOffset = 5000;

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
    /// <summary>A star's brightness range as a fraction, the chance it takes the Bluesky tint, the pale tint, and how far it is mixed from the sky.</summary>
    private const double StarBrightnessMin = 0.25, StarBrightnessSpread = 0.5, StarBlueChance = 0.35, StarMix = 0.55;
    private const uint StarPaleTint = 0xdfe8ff;
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
    private readonly (int X, int Y, uint Colour)[] _stars;

    public Background(int width, int height, ulong seed)
    {
        _width = width; _height = height; _seed = seed;
        _sky = BakeSky();
        _stars = BakeStars();
    }

    public void Draw(Surface target, double scrollFrames)
    {
        Array.Copy(_sky.P, target.P, Math.Min(_sky.P.Length, target.P.Length));
        foreach (var (x, startY, colour) in _stars)
        {
            int y = (int)((startY + scrollFrames * Tuning.ScrollStars) % _height);
            target.Set(x, y, colour);
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

    private (int X, int Y, uint Colour)[] BakeStars()
    {
        var stars = new (int X, int Y, uint Colour)[StarCount];
        for (int star = 0; star < stars.Length; star++)
        {
            ulong hash = Hashing.Mix(_seed + StarSeedOffset + (ulong)star);
            double brightness = StarBrightnessMin + Hashing.Unit(hash, StarBrightnessStream) * StarBrightnessSpread;
            uint tint = Hashing.Unit(hash, StarTintStream) < StarBlueChance ? BlueskyBlue : StarPaleTint;
            stars[star] = ((int)(Hashing.Unit(hash, StarColumnStream) * _width), (int)(Hashing.Unit(hash, StarRowStream) * _height), Surface.Mix(Palette.SkyTop, tint, brightness * StarMix));
        }
        return stars;
    }
}
