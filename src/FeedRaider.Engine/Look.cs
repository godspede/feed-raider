using TUImation;

namespace FeedRaider.Engine;

public enum Accessory { Antenna, CatEars, BearEars, BunnyEars, Cap, TopHat, Beanie, Horns }

/// <summary>What a guy looks like, hashed from DID and handle so it is the same on every client.</summary>
public sealed record Look(Rgb Body, Rgb Trim, Accessory Accessory, bool Belly, int EyeStyle)
{
    private static readonly Rgb[] Bodies =
    [
        new(0xd9, 0x77, 0x57), new(0xa3, 0xb3, 0x5f), new(0x95, 0xa3, 0xcf), new(0xd8, 0xcb, 0xa3),
        new(0xe5, 0x8f, 0xa8), new(0x5f, 0xb3, 0xa3), new(0xe8, 0xc5, 0x47), new(0x9b, 0x7f, 0xd1),
        new(0x6f, 0xb1, 0xe0), new(0x8f, 0xd6, 0xa0), new(0xef, 0x7f, 0x6a), new(0xa8, 0x32, 0x4d),
    ];

    private static readonly Rgb[] Trims =
    [
        new(0x4f, 0x5f, 0x8c), new(0x5d, 0x6a, 0x35), new(0xa8, 0x32, 0x4d), new(0xfb, 0xf6, 0xe6),
        new(0x2b, 0x2f, 0x36), new(0xbf, 0xb2, 0x8a),
    ];

    public static Look From(string did, string? handle)
    {
        ulong h = Hashing.Fnv64(did + "|" + (handle ?? ""));
        int bodyIx = (int)(Hashing.Mix(h) % (ulong)Bodies.Length);
        int trimIx = (int)(Hashing.Mix(h ^ 0xA5A5) % (ulong)Trims.Length);
        var acc = (Accessory)(int)(Hashing.Mix(h ^ 0x1234) % 8);
        bool belly = (Hashing.Mix(h ^ 0x77) & 1) == 1;
        int eyes = (int)(Hashing.Mix(h ^ 0x99) % 3);
        return new Look(Bodies[bodyIx], Trims[trimIx], acc, belly, eyes);
    }
}
