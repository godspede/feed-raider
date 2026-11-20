using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The fixed colours of a card: every text colour reads on the slab, nothing idle is bright enough to be taken for
// something to dodge, and the encoder holds them exactly (they are within the snap tolerance of one another and would fold).
public class CardColourTests
{
    private static readonly uint[] Texts = [CardColours.NameText, CardColours.PostText, CardColours.DimText];

    [Fact]
    public void Every_card_text_colour_has_a_contrast_of_at_least_four_and_a_half_to_one_on_the_slab()
    {
        foreach (uint t in Texts)
            Assert.True(Surface.Contrast(t, CardColours.Slab) >= Surface.BodyTextContrast, $"{t:x6} on the slab: {Surface.Contrast(t, CardColours.Slab):F2}");
        // and a count on the ground of a lit button, which is lighter than the slab
        Assert.True(Surface.Contrast(CardColours.LikeCountLit, CardColours.LikeButtonLit) >= Surface.BodyTextContrast);
        Assert.True(Surface.Contrast(CardColours.RepostCountLit, CardColours.RepostButtonLit) >= Surface.BodyTextContrast);
    }

    [Fact]
    public void Luminance_counts_green_most_and_blue_least_by_the_srgb_weights()
    {
        const double RedWeight = 0.2126, GreenWeight = 0.7152, BlueWeight = 0.0722;
        Assert.Equal(RedWeight, Surface.Luminance(0xFF0000), 1e-6);
        Assert.Equal(GreenWeight, Surface.Luminance(0x00FF00), 1e-6);
        Assert.Equal(BlueWeight, Surface.Luminance(0x0000FF), 1e-6);
    }

    [Fact]
    public void No_idle_card_colour_has_a_relative_luminance_above_the_cap()
    {
        Assert.Equal(0.40, CardColours.MaxIdleLuminance);
        Assert.NotEmpty(CardColours.Idle);
        foreach (uint c in CardColours.Idle)
            Assert.True(Surface.Luminance(c) <= CardColours.MaxIdleLuminance, $"{c:x6} has luminance {Surface.Luminance(c):F3}");
        Assert.All(Texts, t => Assert.Contains(t, CardColours.Idle));
        Assert.Contains(CardColours.Slab, CardColours.Idle);
        Assert.Contains(CardColours.LikeIdle, CardColours.Idle);
        Assert.Contains(CardColours.RepostIdle, CardColours.Idle);
    }

    [Fact]
    public void The_text_colours_are_the_designs_before_they_are_made_legible_and_all_three_differ()
    {
        Assert.Equal(3, Texts.Distinct().Count());
        // the three text colours: each is within the snap tolerance of the next, which is why they are primed
        Assert.Equal(0x9aa5bau, CardColours.NameText);
        Assert.Equal(0x8b95a9u, CardColours.PostText);
        Assert.Equal(0x7a859bu, CardColours.DimText);
        Assert.True(Distance(CardColours.NameText, CardColours.PostText) <= ColourSnap.DefaultTolerance);
        Assert.True(Distance(CardColours.PostText, CardColours.DimText) <= ColourSnap.DefaultTolerance);
    }

    [Fact]
    public void The_fixed_colours_list_holds_every_text_slab_edge_and_button_colour_once()
    {
        var all = CardColours.All;
        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.InRange(all.Length, 8, 40);
        foreach (uint c in Texts.Concat(CardColours.Idle).Concat([CardColours.LikeLit, CardColours.RepostLit, CardColours.LikeButtonLit, CardColours.RepostButtonLit, CardColours.LikeCountLit, CardColours.RepostCountLit]))
            Assert.Contains(c, all);
    }

    [Fact]
    public void After_Prime_every_fixed_colour_comes_through_the_snap_unchanged_even_after_the_palette_has_filled_with_others()
    {
        var snap = new ColourSnap();
        // colours already close to the fixed ones were seen first: they must not take their slots
        _ = snap[CardColours.PostText + 0x050505];
        snap.Prime(CardColours.All);
        foreach (uint c in CardColours.All) Assert.Equal(c, snap[c]);
        var rnd = new Random(11);
        for (int i = 0; i < 5000; i++) _ = snap[(uint)rnd.Next(0x1000000)];
        Assert.Equal(snap.Capacity, snap.Count);
        foreach (uint c in CardColours.All) Assert.Equal(c, snap[c]);
    }

    [Fact]
    public void Prime_leaves_the_capacity_alone_ignores_a_colour_it_already_holds_and_stops_when_the_palette_is_full()
    {
        var snap = new ColourSnap(capacity: 3);
        snap.Prime([0x111111, 0x111111, 0x222222]);
        Assert.Equal(2, snap.Count);
        snap.Prime([0x333333, 0x444444]);
        Assert.Equal(3, snap.Count);
        Assert.Equal(0x111111u, snap[0x111111]);
        Assert.Equal(0x333333u, snap[0x333333]);
    }

    [Fact]
    public void The_encoder_the_game_sends_with_holds_the_fixed_colours_exactly()
    {
        var enc = DiffEncoder.Snapped();
        Assert.NotNull(enc.Snap);
        foreach (uint c in CardColours.All) Assert.Equal(c, enc.Snap![c]);
    }

    private static int Distance(uint a, uint b) =>
        Math.Max(Math.Abs((int)(a >> 16 & 255) - (int)(b >> 16 & 255)),
            Math.Max(Math.Abs((int)(a >> 8 & 255) - (int)(b >> 8 & 255)), Math.Abs((int)(a & 255) - (int)(b & 255))));
}
