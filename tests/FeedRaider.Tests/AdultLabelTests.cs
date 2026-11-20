using System.Text.Json;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

/// <summary>A post that carries one of <see cref="FeedMapper.AdultLabels"/>, on itself, on its author or as its record's
/// self-label, gives no words to a speech box, signed in or out.</summary>
public class AdultLabelTests
{
    private const string Did = "did:plc:labelledlabelledlabelled";
    private const string Words = "the words of the post";
    private const string OlderWords = "an older post";

    public enum Where { Post, Author, Self }

    public static TheoryData<string, Where, bool> Cases()
    {
        var data = new TheoryData<string, Where, bool>();
        foreach (var label in FeedMapper.AdultLabels)
            foreach (var where in Enum.GetValues<Where>())
                foreach (var signedOut in new[] { true, false })
                    data.Add(label, where, signedOut);
        return data;
    }

    private static object[] LabelsWith(string? label) => label is null ? [] : [new { src = Did, val = label }];

    private static object PostView(string text, string? label = null, Where where = Where.Post) => new
    {
        uri = $"at://{Did}/app.bsky.feed.post/3kabc",
        labels = LabelsWith(where == Where.Post ? label : null),
        author = new { did = Did, labels = LabelsWith(where == Where.Author ? label : null) },
        record = new { text, labels = new { values = where == Where.Self && label is not null ? new[] { new { val = label } } : [] } },
    };

    private static string GetPosts(object post) => JsonSerializer.Serialize(new { posts = new[] { post } });

    private static string AuthorFeed(params object[] posts) => JsonSerializer.Serialize(new { feed = posts.Select(post => new { post }) });

    [Theory]
    [MemberData(nameof(Cases))]
    public void GetPosts_gives_an_adult_post_no_words(string label, Where where, bool signedOut) =>
        Assert.Equal("", PostTextFetcher.ParseGetPosts(GetPosts(PostView(Words, label, where)), signedOut));

    [Theory]
    [MemberData(nameof(Cases))]
    public void AuthorFeed_gives_an_adult_newest_post_no_words_and_reads_no_older_one(string label, Where where, bool signedOut) =>
        Assert.Equal("", PostTextFetcher.ParseAuthorFeed(AuthorFeed(PostView(Words, label, where), PostView(OlderWords)), signedOut));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_post_without_an_adult_label_keeps_its_words(bool signedOut)
    {
        Assert.Equal(Words, PostTextFetcher.ParseGetPosts(GetPosts(PostView(Words)), signedOut));
        Assert.Equal(Words, PostTextFetcher.ParseAuthorFeed(AuthorFeed(PostView(Words), PostView(OlderWords)), signedOut));
    }
}
