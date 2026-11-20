namespace FeedRaider.Engine.Raider;

/// <summary>The colours that more than one painter or sprite uses, so a change lands everywhere at once.</summary>
public static class Palette
{
    public const uint White = 0xffffff, Pink = 0xff5a8a, Crimson = 0xff3d6e, Gold = 0xffd166, Ice = 0xbfe9ff;
    /// <summary>
    /// The three meanings every enemy shares, so a new one reads in its first ten seconds. Blue is a shield and what makes it:
    /// <see cref="Shield"/>, <see cref="ShieldDeep"/> and <see cref="ShieldGlow"/> sit only on a part while a shield keeps shots off it,
    /// on the shield itself and on the thing that casts it. White is about to hurt: <see cref="Tell"/> on an enemy means an attack leaves
    /// that place within about a second. Gold on crimson is where to shoot: a part shows <see cref="WeakMark"/> touching
    /// <see cref="WeakBody"/> exactly while a shot hurts it. The ship, its shots, the pickups, the cards and the page stand outside the rule.
    /// </summary>
    public const uint Shield = 0x5a9cff, ShieldDeep = 0x2a4f9a, ShieldGlow = 0x9fe8ff;
    /// <summary>The ground of a spot that takes damage; see <see cref="Shield"/> for the rule.</summary>
    public const uint WeakBody = Crimson;
    /// <summary>The mark on <see cref="WeakBody"/> that says where to shoot.</summary>
    public const uint WeakMark = Gold;
    /// <summary>The white of an attack about to leave an enemy.</summary>
    public const uint Tell = White;
    /// <summary>Text and rules that recede against the dark HUD and side panel.</summary>
    public const uint Dim = 0x6f86ad;
    /// <summary>The top of the sky, which fading particles blend toward.</summary>
    public const uint SkyTop = 0x0b1c44;
    /// <summary>The dark plum of the speech boxes and sprite outlines.</summary>
    public const uint Plum = 0x2a1740;
    /// <summary>The backing of the stage banner and its hints: the plum, which is not a post card's colour, so the block's edge shows against any card.</summary>
    public const uint BannerBack = Plum;
    /// <summary>The backing of the boss warning and the boss's name: a deep wine, so white and gold both read on it.</summary>
    public const uint WarningBack = 0x6a1030;

    /// <summary>The lowest contrast ratio (WCAG) a hint's own colour may have against the block it is drawn on.</summary>
    public const double HintMinContrast = 3.0;
}
