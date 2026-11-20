using System.Globalization;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class PlayerLanguagesTests
{
    private const string Author = "did:plc:authorauthorauthorauthor";
    private const string Latin = "buy followers now cheap and fast", Cyrillic = "покупайте подписчиков прямо сейчас";
    private static readonly PlayerLanguages English = new(["en"]), Russian = new(["ru"]);

    private static string GetPosts(string text, string? langs = null) =>
        $$$"""
        {"posts":[{"uri":"at://{{{Author}}}/app.bsky.feed.post/3kabc","cid":"bafy",
          "author":{"did":"{{{Author}}}","handle":"a.example.social"},
          "record":{"text":"{{{text}}}"{{{(langs is null ? "" : $",\"langs\":{langs}")}}}}}]}
        """;

    [Fact]
    public void Tags_are_reduced_to_lower_case_primary_subtags()
    {
        Assert.Equal(["en", "pt", "zh"], PlayerLanguages.FromTags(["en-GB", "pt-BR", "EN", "zh-Hant-TW"]).Codes);
    }

    [Fact]
    public void An_empty_tag_list_reads_English()
    {
        Assert.Equal(["en"], PlayerLanguages.FromTags([]).Codes);
        Assert.Equal(["en"], PlayerLanguages.FromList("").Codes);
        Assert.Equal(["en"], PlayerLanguages.FromList(null).Codes);
        Assert.Equal(["en"], PlayerLanguages.FromTags(["", "  ", "12"]).Codes);
    }

    [Fact]
    public void English_is_not_added_to_a_list_that_has_languages()
    {
        Assert.Equal(["ru"], PlayerLanguages.FromList("ru-RU").Codes);
    }

    [Theory]
    [InlineData("pt-BR", "pt")]
    [InlineData("zh-Hant-TW", "zh")]
    [InlineData("en", "en")]
    public void With_no_locale_set_a_ui_culture_with_a_region_or_script_gives_its_primary_language(string cultureName, string expected)
    {
        Assert.Equal([expected], PlayerLanguages.FromEnvironment(_ => null, cultureName).Codes);
    }

    [Fact]
    public void With_no_locale_set_the_invariant_ui_culture_reads_English()
    {
        Assert.Equal(["en"], PlayerLanguages.FromEnvironment(_ => null, CultureInfo.InvariantCulture.Name).Codes);
    }

    /// <summary>An environment that holds only <paramref name="set"/>.</summary>
    private static Func<string, string?> Variables(params (string Name, string Value)[] set) =>
        name => set.Where(variable => variable.Name == name).Select(variable => variable.Value).FirstOrDefault();

    [Theory]
    [InlineData("de_DE.UTF-8", "de")]
    [InlineData("pt_BR", "pt")]
    [InlineData("sr_RS@latin", "sr")]
    [InlineData("ru", "ru")]
    public void A_posix_locale_in_LANG_gives_its_language(string locale, string expected)
    {
        Assert.Equal([expected], PlayerLanguages.FromEnvironment(Variables(("LANG", locale)), null).Codes);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("POSIX")]
    [InlineData("C.UTF-8")]
    public void The_C_and_POSIX_locales_give_no_language_so_the_ui_culture_stands_in(string locale)
    {
        Assert.Equal(["fr"], PlayerLanguages.FromEnvironment(Variables(("LANG", locale)), "fr-CA").Codes);
    }

    [Fact]
    public void LANGUAGE_gives_every_language_in_its_colon_separated_list_ahead_of_the_locale()
    {
        var env = Variables(("LANGUAGE", "pt_BR:pt:en"), ("LC_ALL", "de_DE.UTF-8"), ("LANG", "ru_RU.UTF-8"));
        Assert.Equal(["pt", "en"], PlayerLanguages.FromEnvironment(env, "fr-FR").Codes);
    }

    [Fact]
    public void LC_ALL_wins_over_LC_MESSAGES_which_wins_over_LANG()
    {
        Assert.Equal(["de"], PlayerLanguages.FromEnvironment(Variables(("LC_ALL", "de_DE"), ("LC_MESSAGES", "ru_RU"), ("LANG", "fr_FR")), null).Codes);
        Assert.Equal(["ru"], PlayerLanguages.FromEnvironment(Variables(("LC_MESSAGES", "ru_RU"), ("LANG", "fr_FR")), null).Codes);
    }

    [Fact]
    public void An_empty_LANGUAGE_and_an_empty_LC_ALL_are_passed_over()
    {
        Assert.Equal(["fr"], PlayerLanguages.FromEnvironment(Variables(("LANGUAGE", ""), ("LC_ALL", ""), ("LANG", "fr_FR.UTF-8")), null).Codes);
    }

    [Fact]
    public void With_no_locale_set_the_ui_culture_gives_the_language()
    {
        Assert.Equal(["ja"], PlayerLanguages.FromEnvironment(Variables(), "ja-JP").Codes);
    }

    [Fact]
    public void With_no_locale_set_and_no_ui_culture_the_player_reads_English()
    {
        // FromSystem passes no culture under invariant globalization, which every project here builds with
        Assert.Equal(["en"], PlayerLanguages.FromEnvironment(Variables(), null).Codes);
        Assert.Equal(["en"], PlayerLanguages.FromEnvironment(Variables(("LANG", "C")), null).Codes);
    }

    [Fact]
    public void A_post_whose_langs_name_a_player_language_is_kept()
    {
        Assert.Equal(Cyrillic, PostTextFetcher.ParseGetPosts(GetPosts(Cyrillic, "[\"fr\",\"ru\"]"), false, Russian));
    }

    [Fact]
    public void A_post_whose_langs_name_none_of_the_player_languages_is_skipped()
    {
        Assert.Equal("", PostTextFetcher.ParseGetPosts(GetPosts(Latin, "[\"de\",\"fr\"]"), false, English));
    }

    [Fact]
    public void A_langs_tag_with_a_region_matches_on_its_primary_subtag()
    {
        Assert.Equal(Latin, PostTextFetcher.ParseGetPosts(GetPosts(Latin, "[\"en-AU\"]"), false, new(["en"])));
        Assert.Equal(Latin, PostTextFetcher.ParseGetPosts(GetPosts(Latin, "[\"en\"]"), false, PlayerLanguages.FromTags(["en-GB"])));
    }

    [Fact]
    public void Langs_decide_over_the_script_of_the_text()
    {
        Assert.Equal("", PostTextFetcher.ParseGetPosts(GetPosts(Latin, "[\"ru\"]"), false, English));
    }

    [Fact]
    public void A_Latin_post_without_langs_is_kept_for_an_English_player()
    {
        Assert.Equal(Latin, PostTextFetcher.ParseGetPosts(GetPosts(Latin), false, English));
    }

    [Fact]
    public void A_Cyrillic_post_without_langs_is_skipped_for_an_English_player()
    {
        Assert.Equal("", PostTextFetcher.ParseGetPosts(GetPosts(Cyrillic), false, English));
    }

    [Fact]
    public void A_Cyrillic_post_without_langs_is_kept_for_a_Russian_player()
    {
        Assert.Equal(Cyrillic, PostTextFetcher.ParseGetPosts(GetPosts(Cyrillic), false, Russian));
    }

    [Fact]
    public void A_post_with_no_letters_and_no_langs_is_kept()
    {
        Assert.Equal("!!! 123 :)", PostTextFetcher.ParseGetPosts(GetPosts("!!! 123 :)"), false, English));
    }

    [Fact]
    public void The_dominant_script_decides_for_a_mixed_post()
    {
        Assert.Equal("ok " + Cyrillic, PostTextFetcher.ParseGetPosts(GetPosts("ok " + Cyrillic), false, Russian));
        Assert.Equal("", PostTextFetcher.ParseGetPosts(GetPosts("ok " + Cyrillic), false, English));
    }

    [Theory]
    [InlineData("ja", "これはスパムです")]
    [InlineData("ja", "这是垃圾")]
    [InlineData("zh", "这是垃圾")]
    [InlineData("ko", "스팸입니다")]
    [InlineData("ar", "هذا بريد مزعج")]
    [InlineData("fa", "این هرزنامه است")]
    [InlineData("he", "זהו ספאם")]
    [InlineData("el", "αυτό είναι σπαμ")]
    [InlineData("hi", "यह स्पैम है")]
    [InlineData("th", "นี่คือสแปม")]
    [InlineData("uk", "це спам")]
    [InlineData("de", "das ist Müll")]
    public void A_post_in_a_script_of_the_player_language_is_kept(string language, string text)
    {
        Assert.Equal(text, PostTextFetcher.ParseGetPosts(GetPosts(text), false, new([language])));
    }

    [Fact]
    public void Without_languages_nothing_is_filtered()
    {
        Assert.Equal(Cyrillic, PostTextFetcher.ParseGetPosts(GetPosts(Cyrillic, "[\"ru\"]"), false));
    }

    private static string Entry(string text) => $$$$"""{"post":{"author":{"did":"{{{{Author}}}}"},"record":{"text":"{{{{text}}}}"}}}""";

    [Fact]
    public void An_author_feed_post_in_another_language_is_skipped_and_an_older_one_is_not_read_in_its_place()
    {
        string json = "{\"feed\":[" + Entry(Cyrillic) + "," + Entry(Latin) + "]}";
        Assert.Equal("", PostTextFetcher.ParseAuthorFeed(json, false, English));
    }

    [Fact]
    public void Ordinary_cards_are_not_filtered_by_language()
    {
        const string json = """
            {"feed":[{"post":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"c","author":{"did":"did:plc:a","handle":"a.test"},
              "record":{"text":"это обычный пост","langs":["ru"],"createdAt":"2026-01-01T00:00:00Z"}}}]}
            """;
        Assert.Equal("это обычный пост", Assert.Single(FeedMapper.ParsePage(json).Posts).Text);
    }
}
