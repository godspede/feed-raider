using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class EnemyBadgeTests
{
    [Theory]
    [InlineData(DarknessKind.Spam, null, " SPAM ")]
    [InlineData(DarknessKind.Rude, null, " RUDE ")]
    [InlineData(DarknessKind.Rude, "rude", " RUDE ")]
    [InlineData(DarknessKind.Spam, "engagement-farming", " ENGAGEM… ")]
    [InlineData(DarknessKind.Spam, "scam", " SCAM ")]
    public void Text_is_upper_case_and_short(DarknessKind kind, string? label, string expected) =>
        Assert.Equal(expected, EnemyBadge.Text(label, kind));

    private static Enemy Make(EnemyKind k, double x, double y, string? text = null) =>
        new() { Kind = k, Sprite = k == EnemyKind.Gunship ? Art.Gunship : Art.Grunt, X = x, Y = y, Text = text };

    [Theory]
    [InlineData(DarknessKind.Spam, "spam")]
    [InlineData(DarknessKind.Rude, "rude")]
    [InlineData(DarknessKind.Spam, "engagement-farming")]
    public void Badge_is_above_the_sprite_and_centred(DarknessKind kind, string label)
    {
        var e = Make(kind == DarknessKind.Rude ? EnemyKind.Gunship : EnemyKind.Grunt, 40, 60);
        string b = EnemyBadge.Text(label, kind);
        var (col, row) = EnemyBadge.Place(e, 10, 48, b.Length);
        Assert.True(row < (e.Y - e.Sprite.H / 2.0) / 2);
        Assert.InRange(Math.Abs(col + b.Length / 2 - (10 + 20)), 0, 1);
    }

    [Fact]
    public void Badge_is_clamped_to_the_screen_edges_and_below_the_hud()
    {
        string b = EnemyBadge.Text(null, DarknessKind.Rude);
        Assert.Equal(10, EnemyBadge.Place(Make(EnemyKind.Gunship, 0, 60), 10, 48, b.Length).Col);
        Assert.Equal(10 + 48 - b.Length, EnemyBadge.Place(Make(EnemyKind.Gunship, 500, 60), 10, 48, b.Length).Col);
        Assert.Equal(2, EnemyBadge.Place(Make(EnemyKind.Gunship, 50, 4), 10, 48, b.Length).Row);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Badge_shows_on_the_first_play_row_while_the_sprite_hangs_over_the_top(int y)
    {
        var g = new RaiderGame(3);
        g.SetLayout(FieldLayout.Compute(48, 60));
        var e = new Enemy { Kind = EnemyKind.Gunship, Sprite = Art.Gunship, X = 40, Y = y, Text = "you are all idiots", Badge = EnemyBadge.Text("rude", DarknessKind.Rude), Source = DarknessKind.Rude, Live = true };
        g.Enemies.Add(e);
        var scr = new Screen(48, 60);
        new RaiderView().Render(g, scr);
        var lay = g.Layout;
        var (col, row) = EnemyBadge.Place(e, lay.Col0, lay.PCols, e.Badge.Length);
        Assert.Equal(2, row);
        Assert.InRange(col, lay.Col0, lay.Col0 + lay.PCols - e.Badge.Length);
        Assert.Equal(e.Badge, new string(Enumerable.Range(0, e.Badge.Length).Select(i => scr.CharAt(col + i, row)).ToArray()));
        Assert.Equal(EnemyBadge.RudeBg, scr.BgAt(col, row));
    }

    [Fact]
    public void Carrier_text_and_badge_do_not_share_a_row()
    {
        var plain = Make(EnemyKind.Carrier, 100, 60);
        var talking = Make(EnemyKind.Carrier, 100, 60, "free crypto");
        Assert.Equal(EnemyBadge.Place(plain, 0, 48, 6).Row - 1, EnemyBadge.Place(talking, 0, 48, 6).Row);
    }

    [Fact]
    public void Badge_never_overlaps_the_speech_bubble()
    {
        var e = Make(EnemyKind.Gunship, 100, 8);
        var (_, row) = EnemyBadge.Place(e, 0, 48, 6);
        int bubbleRow = (int)((e.Y + e.Sprite.H / 2.0) / 2) + 1;
        Assert.True(row < bubbleRow);
    }

    [Theory]
    [InlineData(DarknessKind.Spam, "spam", " SPAM ")]
    [InlineData(DarknessKind.Rude, "rude", " RUDE ")]
    [InlineData(DarknessKind.Spam, "scam", " SCAM ")]
    public void Rendered_badge_shows_on_screen_with_the_kind_colour(DarknessKind kind, string label, string expected)
    {
        var g = new RaiderGame(3);
        g.SetLayout(FieldLayout.Compute(48, 60));
        g.OfferDark(new DarkPost(kind, 5, "some darkness text here", DarkSource.Label, label));
        Enemy? e = null;
        for (int i = 0; i < 400 && (e = g.Enemies.FirstOrDefault(x => x.Y > 10 && x.Badge != null)) is null; i++) g.Step();
        Assert.NotNull(e);
        var scr = new Screen(48, 60);
        new RaiderView().Render(g, scr);
        var lay = g.Layout;
        var (col, row) = EnemyBadge.Place(e!, lay.Col0, lay.PCols, expected.Length);
        string seen = new string(Enumerable.Range(0, expected.Length).Select(i => scr.CharAt(col + i, row)).ToArray());
        Assert.Equal(expected, seen);
        Assert.Equal(EnemyBadge.Background(kind), scr.BgAt(col, row));
    }
}
