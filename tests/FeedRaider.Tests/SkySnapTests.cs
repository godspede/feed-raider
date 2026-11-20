using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The sky's fade passes within the snap's tolerance of a card's slab. Sent through the game's encoder, every pixel of it
// keeps a colour of the sky, so the gaps between the cards stay sky and the cards stand out from it.
public class SkySnapTests
{
    private static readonly FieldLayout Phone = FieldLayout.Compute(AtlasKeyTests.PhoneCols, AtlasKeyTests.PhoneRows);
    private static readonly FieldLayout WideTerminal = FieldLayout.Compute(150, 50);
    // a field tall enough that its fade takes every level from the top of the sky to its foot
    private const int TallHeight = 400;

    private const ulong BackgroundSeed = RaiderView.DefaultBackgroundSeed;

    public static TheoryData<int, int> SkySizes => new()
    {
        { Phone.PW, Phone.PH },
        { WideTerminal.PW, WideTerminal.PH },
        { Phone.PW, TallHeight },
    };

    [Theory]
    [MemberData(nameof(SkySizes))]
    public void No_pixel_of_the_sky_is_sent_as_a_card_slab(int width, int height)
    {
        var sky = DrawnSky(width, height);
        var encoder = DiffEncoder.Snapped();
        for (int pixel = 0; pixel < sky.P.Length; pixel++)
            Assert.True(encoder.Snap![sky.P[pixel]] != CardColours.Slab, $"{SkyPixel(sky, pixel)} is sent as the slab");
    }

    // the grounds of the HUD and the feed log lie beside the fade as well, and are no colour of the sky
    [Theory]
    [MemberData(nameof(SkySizes))]
    public void Every_pixel_of_the_sky_is_sent_as_a_sky_step_or_a_colour_the_sky_draws(int width, int height)
    {
        var sky = DrawnSky(width, height);
        var skyColours = new HashSet<uint>([.. sky.P, .. Background.SkySteps]);
        var encoder = DiffEncoder.Snapped();
        for (int pixel = 0; pixel < sky.P.Length; pixel++)
        {
            uint sent = encoder.Snap![sky.P[pixel]];
            Assert.True(skyColours.Contains(sent), $"{SkyPixel(sky, pixel)} is sent as {sent:x6}, which the sky never draws");
        }
    }

    [Fact]
    public void Every_sky_step_comes_through_the_game_encoder_unchanged()
    {
        var encoder = DiffEncoder.Snapped();
        Assert.NotEmpty(Background.SkySteps);
        foreach (uint step in Background.SkySteps) Assert.Equal(step, encoder.Snap![step]);
    }

    // the HUD's and the feed log's grounds lie within the tolerance of the slab too
    [Fact]
    public void The_grounds_of_the_hud_and_the_feed_log_come_through_the_game_encoder_unchanged()
    {
        var encoder = DiffEncoder.Snapped();
        Assert.Equal(RaiderView.HudBg, encoder.Snap![RaiderView.HudBg]);
        Assert.Equal(RaiderView.PanelBg, encoder.Snap![RaiderView.PanelBg]);
    }

    private static Surface DrawnSky(int width, int height)
    {
        var sky = new Surface(width, height);
        new Background(width, height, BackgroundSeed).Draw(sky, 0);
        return sky;
    }

    private static string SkyPixel(Surface sky, int pixel) => $"{sky.W}x{sky.H}, row {pixel / sky.W}: sky colour {sky.P[pixel]:x6}";
}
