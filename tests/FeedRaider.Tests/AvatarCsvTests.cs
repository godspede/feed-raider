using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class AvatarCsvTests
{
    private static string Csv(int n) => string.Join(',', Enumerable.Range(0, n).Select(i => (i * 4000 + 1).ToString()));

    [Fact]
    public void Sixty_four_pixels_come_back_in_order()
    {
        var px = AvatarCsv.Parse(Csv(64));
        Assert.NotNull(px);
        Assert.Equal(64, px!.Length);
        Assert.Equal(1u, px[0]);
        Assert.Equal(63u * 4000 + 1, px[63]);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(128)]
    public void Any_other_count_is_refused(int n) => Assert.Null(AvatarCsv.Parse(Csv(n)));

    [Fact]
    public void A_value_that_is_not_a_number_throws()
    {
        Assert.Throws<FormatException>(() => AvatarCsv.Parse(Csv(63) + ",x"));
        Assert.Throws<OverflowException>(() => AvatarCsv.Parse(Csv(63) + ",99999999999"));
    }

    [Fact]
    public void A_game_keeps_a_pixel_set_the_page_sent_and_refuses_a_short_one()
    {
        var g = new FeedRaider.Engine.Raider.RaiderGame(1);
        g.SetAvatar("did:plc:a", AvatarCsv.Parse(Csv(64))!);
        Assert.Equal(64, g.AvatarOf("did:plc:a")!.Length);
        Assert.Throws<ArgumentException>(() => g.SetAvatar("did:plc:b", new uint[63]));
        Assert.Null(g.AvatarOf("did:plc:b"));
    }
}
