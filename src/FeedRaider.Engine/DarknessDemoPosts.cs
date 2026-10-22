namespace FeedRaider.Engine;

/// <summary>Invented stand-ins (not real posts) for when the labeler has nothing to say; used only in demo mode.</summary>
public static class DarknessDemoPosts
{
    private static readonly (DarknessKind Kind, string Text)[] Samples =
    [
        (DarknessKind.Spam, "BUY FOLLOWERS NOW cheap fast guaranteed"),
        (DarknessKind.Rude, "nobody asked for your opinion, go away"),
        (DarknessKind.Spam, "click my bio for free crypto giveaway"),
        (DarknessKind.Rude, "this take is garbage and so are you"),
        (DarknessKind.Spam, "make $5000 a week from home, no experience"),
        (DarknessKind.Rude, "ugh, of course you would post that"),
    ];

    public static DarkPost Next(int i)
    {
        var (kind, text) = Samples[i % Samples.Length];
        return new DarkPost(kind, Hashing.Fnv64("demo" + i), DarknessText.Clean(text), DarkSource.Demo);
    }
}
