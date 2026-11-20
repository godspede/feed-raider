namespace FeedRaider.Engine.Raider;

/// <summary>Small pictures drawn from the data below, for the invented posts of the start page's tour and of the bench: no network and no
/// account. A post names one as <c>builtin:name</c> where a real post has its thumbnail's address.</summary>
public static class BuiltinPictures
{
    private static readonly Dictionary<char, uint> Paint = new()
    {
        ['.'] = 0x1b2a4a,
        ['s'] = 0x3a5f9a,
        ['S'] = 0x6f9bd0,
        ['o'] = 0xf0b040,
        ['O'] = 0xffe08a,
        ['r'] = 0xd0503a,
        ['p'] = 0xc070a0,
        ['w'] = 0xe8e8e0,
        ['g'] = 0x4f9a4a,
        ['G'] = 0x8fd06a,
        ['b'] = 0x7a4a2a,
        ['B'] = 0xb07a4a,
        ['k'] = 0x20242c,
        ['c'] = 0x9ad0e0,
        ['t'] = 0xa05a2a,
    };

    private static readonly Dictionary<string, string[]> All = new(StringComparer.Ordinal)
    {
        // a boat in a harbour at first light: wide
        ["harbour"] =
        [
            "SSSSSSSSSSSSSOOSSSSS",
            "SSSSSSSSSSSSOOOOSSSS",
            "SSSSSSwSSSSSSOOSSSSS",
            "ppppppwwpppppppppppp",
            "pppppwwwpppppppppppp",
            "ssssbbbbbbbsssssOsss",
            "sssssbbbbbsssssOssss",
            "sssssssssssssOssssss",
            "ssssssssssssssssssss",
            "ssssssssssssssssssss",
        ],
        // a cup of tea, steaming: square
        ["tea"] =
        [
            "............",
            "....c..c....",
            "...c..c.....",
            "....c..c....",
            "............",
            ".wwwwwwww...",
            ".wttttttwww.",
            ".wttttttw.w.",
            ".wwwwwwwwww.",
            "..wwwwww....",
            "BBBBBBBBBBBB",
            "bbbbbbbbbbbb",
        ],
        // a plant in a pot: tall
        ["plant"] =
        [
            "........",
            "...G....",
            "..GGg...",
            ".GGg.G..",
            "..g.GGg.",
            ".G.gGg..",
            "GGgg....",
            ".Ggg.G..",
            "...gGGg.",
            "...g.g..",
            "...g....",
            ".rrrrrr.",
            ".rrrrrr.",
            "..rrrr..",
            "..rrrr..",
            "bbbbbbbb",
        ],
        // toast on a plate: square
        ["toast"] =
        [
            "kkkkkkkkkkkk",
            "kkkBBBBBBkkk",
            "kkBOOOOOOBkk",
            "kkBOOooOOBkk",
            "kkBOooooOBkk",
            "kkBOOooOOBkk",
            "kkBOOOOOOBkk",
            "kkBBBBBBBBkk",
            "kwwwwwwwwwwk",
            "kkwwwwwwwwkk",
            "kkkkkkkkkkkk",
            "kkkkkkkkkkkk",
        ],
        // a hill under a low sun: wide
        ["hill"] =
        [
            "SSSSSSSSSSSSSSSSSSSS",
            "SSSSwwSSSSSSSSSSOOSS",
            "SSSwwwwSSSSSSSSOOOOS",
            "SSSSSSSSSSSGGSSSOOSS",
            "SSSSSSSSSGGGGGGSSSSS",
            "SSSSSSSGGGGGGGGGGSSS",
            "ggSSSGGGGGGGGGGGGGGg",
            "ggggGGGGGGGGGGGGGGgg",
            "gggggggggggggggggggg",
            "gggggggggggggggggggg",
        ],
    };

    /// <summary>Every built-in picture's name.</summary>
    public static IReadOnlyCollection<string> Names => All.Keys;

    /// <summary>A post's picture entry for the built-in picture <paramref name="name"/>, with its shape.</summary>
    public static PostPicture Picture(string name)
    {
        var art = All[name];
        return new PostPicture(RaiderGame.BuiltinPrefix + name, art[0].Length, art.Length);
    }

    /// <summary>The picture as <paramref name="w"/> by <paramref name="h"/> colours, or null for a name that is not one of them.</summary>
    public static uint[]? Render(string name, int w, int h)
    {
        if (w < 1 || h < 1 || !All.TryGetValue(name, out var art)) return null;
        int aw = art[0].Length, ah = art.Length;
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = Paint[art[y * ah / h][x * aw / w]];
        return px;
    }
}
