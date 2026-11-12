namespace FeedRaider.Engine.Raider;

/// <summary>The colours that more than one painter or sprite uses, so a change lands everywhere at once.</summary>
public static class Palette
{
    public const uint White = 0xffffff, Pink = 0xff5a8a, Crimson = 0xff3d6e, Gold = 0xffd166, Ice = 0xbfe9ff;
    /// <summary>Text and rules that recede against the dark panels.</summary>
    public const uint Dim = 0x6f86ad;
    /// <summary>The top of the sky, which fading particles blend toward.</summary>
    public const uint SkyTop = 0x0b1c44;
    /// <summary>The dark plum of the speech boxes and sprite outlines.</summary>
    public const uint Plum = 0x2a1740;
}
