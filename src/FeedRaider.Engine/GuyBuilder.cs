using TUImation;

namespace FeedRaider.Engine;

/// <summary>Per-frame pose. Everything a sprite needs besides its Look.</summary>
public readonly record struct Pose(int Step, int Facing, bool Speaking, bool Blink);

/// <summary>A sprite builder is a pure function from (look, pose) to Canvas; draw order is depth.</summary>
public static class GuyBuilder
{
    public const int Width = 12;
    public const int Height = 16;

    private static readonly Rgb Ink = new(0x18, 0x22, 0x3a);

    public static Canvas Build(Look look, Pose pose)
    {
        var c = new Canvas(Width, Height);
        Rgb body = look.Body, shade = body.Darken(0.78), belly = body.Lerp(new Rgb(255, 255, 240), 0.55);

        // legs (back), one lifts on odd steps while walking
        int lift = pose.Step & 1;
        c.FillRect(3, 12, 2, 4 - (pose.Step > 0 && lift == 1 ? 1 : 0), shade);
        c.FillRect(7, 12, 2, 4 - (pose.Step > 0 && lift == 0 ? 1 : 0), shade);

        // body, corners trimmed so it reads as a soft block
        c.FillRect(1, 4, 10, 8, body);
        foreach (var (x, y) in new[] { (1, 4), (10, 4), (1, 11), (10, 11) })
            c[x, y] = null;
        if (look.Belly) c.FillRect(3, 9, 6, 2, belly);

        // arms: raised while speaking
        int armY = pose.Speaking ? 5 : 7;
        c.FillRect(0, armY, 1, 3, shade);
        c.FillRect(11, armY, 1, 3, shade);

        // eyes
        int ex = Math.Clamp(pose.Facing, -1, 1);
        int lx = 3 + ex, rx = 8 + ex;
        if (pose.Blink)
        {
            c.FillRect(lx, 7, 1, 1, Ink);
            c.FillRect(rx, 7, 1, 1, Ink);
        }
        else
        {
            switch (look.EyeStyle)
            {
                case 0: c.FillRect(lx, 6, 1, 2, Ink); c.FillRect(rx, 6, 1, 2, Ink); break;
                case 1: c.FillRect(lx, 6, 2, 2, Ink); c.FillRect(rx - 1, 6, 2, 2, Ink); c[lx, 6] = Rgb255(255); c[rx - 1, 6] = Rgb255(255); break;
                default: c.FillRect(lx, 7, 1, 1, Ink); c.FillRect(rx, 7, 1, 1, Ink); c[lx, 6] = Ink; c[rx, 6] = Ink; break;
            }
        }
        if (pose.Speaking) c.FillRect(5, 9, 2, 1, Ink);

        DrawAccessory(c, look);
        return c;
    }

    private static Rgb Rgb255(byte v) => new(v, v, v);

    private static void DrawAccessory(Canvas c, Look look)
    {
        Rgb t = look.Trim, b = look.Body, tl = t.Lerp(new Rgb(255, 255, 255), 0.35);
        switch (look.Accessory)
        {
            case Accessory.Antenna:
                c.FillRect(5, 2, 2, 2, b.Darken(0.78));
                c.FillRect(5, 0, 2, 2, t);
                break;
            case Accessory.CatEars:
                foreach (int s in new[] { 0, 1 })
                {
                    int x = s == 0 ? 1 : 10;
                    int dir = s == 0 ? 1 : -1;
                    c[x, 1] = b; c[x, 2] = b; c[x + dir, 2] = b;
                    c[x, 3] = b; c[x + dir, 3] = b; c[x + 2 * dir, 3] = b;
                    c[x, 2] = t.Lerp(b, 0.3);
                }
                break;
            case Accessory.BearEars:
                c.FillRect(1, 2, 3, 2, b); c.FillRect(8, 2, 3, 2, b);
                c[2, 3] = t; c[9, 3] = t;
                break;
            case Accessory.BunnyEars:
                c.FillRect(2, 0, 2, 4, b); c.FillRect(8, 0, 2, 4, b);
                c.FillRect(3, 1, 1, 3, t.Lerp(b, 0.4)); c.FillRect(8, 1, 1, 3, t.Lerp(b, 0.4));
                break;
            case Accessory.Cap:
                c.FillRect(2, 2, 8, 2, t);
                c.FillRect(9, 3, 3, 1, tl);
                break;
            case Accessory.TopHat:
                c.FillRect(3, 0, 6, 3, Ink);
                c.FillRect(3, 2, 6, 1, t);
                c.FillRect(2, 3, 8, 1, Ink);
                break;
            case Accessory.Beanie:
                c.FillRect(2, 2, 8, 2, t);
                c.FillRect(2, 3, 8, 1, tl);
                c.FillRect(5, 0, 2, 2, tl);
                break;
            case Accessory.Horns:
                c.FillRect(1, 2, 1, 2, t); c[0, 1] = t;
                c.FillRect(10, 2, 1, 2, t); c[11, 1] = t;
                break;
        }
    }
}
