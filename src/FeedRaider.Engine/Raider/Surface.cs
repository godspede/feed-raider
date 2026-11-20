using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>A flat opaque pixel buffer (0xRRGGBB). The renderer's working canvas: cheap to copy and compare, which
/// matters under the browser's interpreter.</summary>
public sealed class Surface
{
    public int W { get; }
    public int H { get; }
    /// <summary>The pixels, row by row from the top left: (x, y) is at y * <see cref="W"/> + x.</summary>
    public readonly uint[] P;

    public Surface(int w, int h) { W = w; H = h; P = new uint[w * h]; ClipRight = w; ClipBottom = h; }

    /// <summary>The rectangle writes land in (left and top inclusive, right and bottom exclusive); the whole surface until <see cref="SetClip"/>.</summary>
    public int ClipLeft { get; private set; }
    public int ClipTop { get; private set; }
    public int ClipRight { get; private set; }
    public int ClipBottom { get; private set; }

    /// <summary>Limits <see cref="Set"/>, <see cref="Blend"/>, <see cref="Rect"/> and sprite drawing to a rectangle of the surface.</summary>
    public void SetClip(int left, int top, int right, int bottom)
    {
        ClipLeft = Math.Clamp(left, 0, W); ClipTop = Math.Clamp(top, 0, H);
        ClipRight = Math.Clamp(right, ClipLeft, W); ClipBottom = Math.Clamp(bottom, ClipTop, H);
    }

    public void ClearClip() { ClipLeft = 0; ClipTop = 0; ClipRight = W; ClipBottom = H; }

    /// <summary>Where the red and the green channel sit in a 0xRRGGBB pixel, in bits; blue is the low byte.</summary>
    private const int RedShift = 16, GreenShift = 8;
    /// <summary>The highest value of one channel.</summary>
    private const int ChannelMax = 255;

    public static uint Pack(Rgb c) => (uint)(c.R << RedShift | c.G << GreenShift | c.B);
    public static uint Pack(int r, int g, int b) => (uint)(Math.Clamp(r, 0, ChannelMax) << RedShift | Math.Clamp(g, 0, ChannelMax) << GreenShift | Math.Clamp(b, 0, ChannelMax));
    public static Rgb Unpack(uint c) => new((byte)(c >> RedShift), (byte)(c >> GreenShift), (byte)c);

    public static uint Mix(uint a, uint b, double t)
    {
        var (ar, ag, ab) = Unpack(a);
        var (br, bg, bb) = Unpack(b);
        return Pack((int)(ar + (br - ar) * t), (int)(ag + (bg - ag) * t), (int)(ab + (bb - ab) * t));
    }

    /// <summary>The WCAG contrast ratio for body text.</summary>
    public const double BodyTextContrast = 4.5;

    /// <summary><paramref name="fg"/>, moved toward white (or black, on a light <paramref name="bg"/>) only as far as it takes
    /// to read at <see cref="BodyTextContrast"/>:1 against <paramref name="bg"/>.</summary>
    public static uint Legible(uint fg, uint bg)
    {
        // asked every frame for a handful of pairs, so remembered: the interpreter in the browser is slow at Math.Pow
        ulong key = (ulong)fg << ColourBits | bg;
        if (LegibleSeen.TryGetValue(key, out uint seen)) return seen;
        uint pole = Luminance(bg) > DarkTextBgLuminance ? 0u : Palette.White;
        uint legible = fg;
        for (int step = 1; step <= LegibleSteps && Contrast(legible, bg) < BodyTextContrast; step++) legible = Mix(fg, pole, step / (double)LegibleSteps);
        if (LegibleSeen.Count > LegibleSeenMax) LegibleSeen.Clear();
        LegibleSeen[key] = legible;
        return legible;
    }

    /// <summary>The width of one pixel value in bits: a pair's key holds the foreground above the background.</summary>
    private const int ColourBits = 32;
    /// <summary>How many pairs are remembered before the memory is emptied: far more than a frame asks for, so it empties
    /// only when the colours keep changing.</summary>
    private const int LegibleSeenMax = 4096;
    /// <summary>A background brighter than this takes black text; a darker one takes white.</summary>
    private const double DarkTextBgLuminance = 0.18;
    /// <summary>How many even steps the search for a legible colour takes from the colour toward black or white.</summary>
    private const int LegibleSteps = 20;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, uint> LegibleSeen = new();

    /// <summary>The flare WCAG adds to both luminances, so a ratio against black stays finite.</summary>
    private const double ContrastFlare = 0.05;

    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + ContrastFlare) / (Math.Min(la, lb) + ContrastFlare);
    }

    /// <summary>The sRGB curve as WCAG gives it: at or under the threshold a channel is linear and divided by the divisor; above it
    /// the offset is added, the sum divided by the scale and raised to the exponent.</summary>
    private const double SrgbLinearThreshold = 0.03928, SrgbLinearDivisor = 12.92, SrgbOffset = 0.055, SrgbScale = 1.055, SrgbExponent = 2.4;
    /// <summary>How much each linear channel gives to the luminance: the eye's sensitivity to red, green and blue.</summary>
    private const double RedWeight = 0.2126, GreenWeight = 0.7152, BlueWeight = 0.0722;

    internal static double Luminance(uint c)
    {
        static double Lin(uint channel) { double level = channel / (double)ChannelMax; return level <= SrgbLinearThreshold ? level / SrgbLinearDivisor : Math.Pow((level + SrgbOffset) / SrgbScale, SrgbExponent); }
        var (r, g, b) = Unpack(c);
        return RedWeight * Lin(r) + GreenWeight * Lin(g) + BlueWeight * Lin(b);
    }

    /// <summary>The pixel value of "nothing drawn here" in a mask pass (no real colour has the top byte set).</summary>
    public const uint None = 0xFF000000u;

    /// <summary>A mask pass: <see cref="Blend"/> (and so <see cref="Ring"/>) cannot mix with a background it does not have, so a pixel
    /// still <see cref="None"/> takes the colour mixed over a dark stand-in and rings stay visible over the speech boxes.</summary>
    public bool SkipBlends;

    private const uint MaskStandIn = 0x0c1020;

    /// <summary>Fills the clip rectangle only.</summary>
    public void FillClip(uint c)
    {
        for (int y = ClipTop; y < ClipBottom; y++) Array.Fill(P, c, y * W + ClipLeft, ClipRight - ClipLeft);
    }

    public void Set(int x, int y, uint c)
    {
        if (x >= ClipLeft && x < ClipRight && y >= ClipTop && y < ClipBottom) P[y * W + x] = c;
    }

    /// <summary>The pixel at (x, y), or 0 outside the surface. Like <see cref="Set"/> and <see cref="Blend"/>, which drop a write
    /// outside it, the read does not report the miss, so 0 is also black: callers read inside the bounds.</summary>
    public uint Get(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H ? P[y * W + x] : 0;

    public void Blend(int x, int y, uint c, double a)
    {
        if (x < ClipLeft || x >= ClipRight || y < ClipTop || y >= ClipBottom) return;
        uint back = P[y * W + x];
        if (SkipBlends && back == None) back = MaskStandIn;
        P[y * W + x] = Mix(back, c, a);
    }

    public void Rect(int x, int y, int w, int h, uint c)
    {
        int left = Math.Max(ClipLeft, x), right = Math.Min(ClipRight, x + w);
        if (right <= left) return;
        for (int row = Math.Max(ClipTop, y); row < Math.Min(ClipBottom, y + h); row++)
            Array.Fill(P, c, row * W + left, right - left);
    }

    public void Disc(int cx, int cy, int r, uint c)
    {
        // the half-radius of slack rounds the edge, so a disc has no lone pixel standing out at its top, bottom and sides
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r + r / 2) Set(cx + x, cy + y, c);
    }

    // A little over 2π, so a ring takes a bit more than one step per pixel of its circumference and has no gaps. (Math.Tau
    // would change the step count for some radii, and so the pixels drawn.)
    private const double RingStepsPerRadius = 6.3;
    /// <summary>The fewest steps a ring takes: one for each of the eight pixels around its centre, so a tiny ring is still closed.</summary>
    private const int MinRingSteps = 8;

    /// <summary>One-pixel circle outline; <paramref name="a"/> blends it over what is there.</summary>
    public void Ring(double cx, double cy, double r, uint c, double a)
    {
        int steps = Math.Max(MinRingSteps, (int)(r * RingStepsPerRadius));
        for (int i = 0; i < steps; i++)
        {
            double angle = i * Math.Tau / steps;
            Blend((int)Math.Round(cx + Math.Cos(angle) * r), (int)Math.Round(cy + Math.Sin(angle) * r), c, a);
        }
    }
}
