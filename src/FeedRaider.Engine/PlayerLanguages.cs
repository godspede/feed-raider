using System.Globalization;
using System.Text;

namespace FeedRaider.Engine;

/// <summary>
/// The languages the player reads, as primary subtags, and the test of whether a <see cref="DarknessPost"/> is in one of them.
/// A post's own <c>langs</c> decides when it has them. Without, the post's writing system stands in: counting the
/// letters of each Unicode script needs no language model and no network call, and a post in the wrong script is
/// the one a player cannot read at all.
/// </summary>
public sealed class PlayerLanguages
{
    /// <summary>What the player reads when no language can be read from the host.</summary>
    public const string Fallback = "en";

    private enum Script { Latin, Cyrillic, Greek, Arabic, Hebrew, Devanagari, Thai, Hangul, Kana, Han, Other }

    private static readonly Dictionary<string, Script[]> ScriptsOf = BuildTable();

    private static Dictionary<string, Script[]> BuildTable()
    {
        var table = new Dictionary<string, Script[]>(StringComparer.Ordinal);
        foreach (var code in "en de fr es pt it nl sv da no nb nn fi is ca gl eu pl cs sk sl hr ro hu tr id ms vi et lv lt sq af sw tl fil cy ga mt eo la".Split(' '))
            table[code] = [Script.Latin];
        foreach (var code in "ru uk bg sr be mk kk mn".Split(' ')) table[code] = [Script.Cyrillic];
        foreach (var code in "ar fa ur ps ku ug".Split(' ')) table[code] = [Script.Arabic];
        foreach (var code in "hi mr ne sa".Split(' ')) table[code] = [Script.Devanagari];
        table["he"] = table["iw"] = table["yi"] = [Script.Hebrew];
        table["el"] = [Script.Greek];
        table["th"] = [Script.Thai];
        table["ko"] = [Script.Hangul];
        table["ja"] = [Script.Kana, Script.Han];
        table["zh"] = [Script.Han];
        return table;
    }

    private readonly HashSet<string> _codes;

    public PlayerLanguages(IEnumerable<string> codes)
    {
        _codes = new HashSet<string>(codes, StringComparer.Ordinal);
        if (_codes.Count == 0) _codes.Add(Fallback);
    }

    /// <summary>The primary subtags, lower case, in the order given and without repeats.</summary>
    public IReadOnlyCollection<string> Codes => _codes;

    /// <summary>These languages and <see cref="Fallback"/>: what a <see cref="DarknessPost"/> may be in while the player's own languages give too few.
    /// The same instance when they already hold it.</summary>
    public PlayerLanguages WithFallback() => _codes.Contains(Fallback) ? this : new(_codes.Append(Fallback));

    /// <summary>The primary language subtag of a BCP-47 tag in lower case (<c>pt-BR</c> gives <c>pt</c>); null for a tag with none.</summary>
    public static string? Primary(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var head = tag.Trim().Split('-', '_')[0].ToLowerInvariant();
        return head.Length > 0 && head.All(char.IsAsciiLetter) ? head : null;
    }

    /// <summary>The languages from tags such as <c>navigator.languages</c>; English when none can be read.</summary>
    public static PlayerLanguages FromTags(IEnumerable<string?> tags) =>
        new(tags.Select(Primary).OfType<string>().Distinct(StringComparer.Ordinal));

    /// <summary>The languages from a comma-separated list of tags, as the page passes them.</summary>
    public static PlayerLanguages FromList(string? list) => FromTags((list ?? "").Split(','));

    /// <summary>The terminal's languages, read from this process's environment by <see cref="FromEnvironment"/>; the UI culture
    /// stands in only when globalization is not invariant, since an invariant build's culture names no language.</summary>
    public static PlayerLanguages FromSystem() =>
        FromEnvironment(Environment.GetEnvironmentVariable, IsGlobalizationInvariant() ? null : CultureInfo.CurrentUICulture.Name);

    /// <summary>The languages a POSIX locale gives, as GNU gettext reads them: each locale in <c>LANGUAGE</c>'s colon-separated
    /// list when it names any; else the locale of the first of <c>LC_ALL</c>, <c>LC_MESSAGES</c> and <c>LANG</c> that is set
    /// (<c>de_DE.UTF-8</c> gives <c>de</c>, while <c>C</c> and <c>POSIX</c> give none); else <paramref name="uiCultureName"/>'s
    /// language; else English.</summary>
    public static PlayerLanguages FromEnvironment(Func<string, string?> variable, string? uiCultureName)
    {
        var listed = (variable(LanguageList) ?? "").Split(':').Select(LocaleLanguage).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (listed.Count > 0) return new(listed);
        string? locale = LocaleVariables.Select(variable).FirstOrDefault(value => !string.IsNullOrEmpty(value));
        return LocaleLanguage(locale) is { } language ? new([language]) : FromTags([uiCultureName]);
    }

    /// <summary>The variable GNU gettext reads a list of languages from, ahead of the locale.</summary>
    private const string LanguageList = "LANGUAGE";

    /// <summary>The variables that set the locale messages are shown in, the one that wins first.</summary>
    private static readonly string[] LocaleVariables = ["LC_ALL", "LC_MESSAGES", "LANG"];

    /// <summary>The locales that name no language: the portable locale under both its names.</summary>
    private static readonly string[] NoLanguageLocales = ["C", "POSIX"];

    /// <summary>The language of a POSIX locale such as <c>pt_BR.UTF-8@euro</c>: its primary subtag, with the codeset and modifier
    /// dropped; null for <c>C</c>, <c>POSIX</c> or a locale with no language.</summary>
    private static string? LocaleLanguage(string? locale)
    {
        string name = (locale ?? "").Split('.', '@')[0].Trim();
        return NoLanguageLocales.Contains(name, StringComparer.Ordinal) ? null : Primary(name);
    }

    private static bool IsGlobalizationInvariant() =>
        AppContext.TryGetSwitch("System.Globalization.Invariant", out bool invariant) && invariant;

    /// <summary>Whether a post is in one of these languages: by its <paramref name="langs"/> when it has any, else by its text's script.</summary>
    public bool Reads(IReadOnlyCollection<string>? langs, string text) =>
        langs is { Count: > 0 } ? langs.Select(Primary).Any(p => p is not null && _codes.Contains(p)) : ReadsScriptOf(text);

    private bool ReadsScriptOf(string text) =>
        DominantScript(text) is not { } script || _codes.Any(c => ScriptsOf.TryGetValue(c, out var scripts) && scripts.Contains(script));

    // null when the text has no letters in a known script; Other (a script in no table) is never the player's
    private static Script? DominantScript(string text)
    {
        var counts = new Dictionary<Script, int>();
        foreach (var rune in text.EnumerateRunes())
            if (Rune.IsLetter(rune)) counts[ScriptOfRune(rune.Value)] = counts.GetValueOrDefault(ScriptOfRune(rune.Value)) + 1;
        return counts.Count == 0 ? null : counts.MaxBy(p => p.Value).Key;
    }

    private static Script ScriptOfRune(int c) => c switch
    {
        < 0x250 => Script.Latin,
        >= 0x1E00 and <= 0x1EFF => Script.Latin,
        >= 0x370 and <= 0x3FF or >= 0x1F00 and <= 0x1FFF => Script.Greek,
        >= 0x400 and <= 0x52F => Script.Cyrillic,
        >= 0x590 and <= 0x5FF => Script.Hebrew,
        >= 0x600 and <= 0x6FF or >= 0x750 and <= 0x77F or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF => Script.Arabic,
        >= 0x900 and <= 0x97F => Script.Devanagari,
        >= 0xE00 and <= 0xE7F => Script.Thai,
        >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F or >= 0xAC00 and <= 0xD7AF => Script.Hangul,
        >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF => Script.Kana,
        >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0x20000 and <= 0x2FA1F => Script.Han,
        _ => Script.Other,
    };
}
