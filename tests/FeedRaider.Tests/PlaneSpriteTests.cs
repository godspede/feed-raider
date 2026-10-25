using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The generated fighter for a DID: pinned cell by cell (a golden grid and its colours) for seeds with and without a twin tail.
public class PlaneSpriteTests
{
    /// <summary>The sprite as rows of letters, one per distinct colour in order of first sight ('.' is clear), and the colour behind each letter.</summary>
    private static (string Grid, string Legend) Describe(Sprite sp)
    {
        var seen = new List<uint>();
        var rows = new List<string>();
        for (int y = 0; y < sp.H; y++)
        {
            var row = "";
            for (int x = 0; x < sp.W; x++)
            {
                uint px = sp.Px[y * sp.W + x];
                if (px == 0) { row += '.'; continue; }
                Assert.Equal(Sprite.Opaque, px & Sprite.Opaque);
                if (!seen.Contains(px & 0xffffff)) seen.Add(px & 0xffffff);
                row += (char)('a' + seen.IndexOf(px & 0xffffff));
            }
            rows.Add(row);
        }
        return (string.Join("/", rows), string.Join(" ", seen.Select((c, i) => (char)('a' + i) + "=" + c.ToString("x6"))));
    }

    [Fact]
    public void The_plane_is_eleven_by_twelve()
    {
        var sp = PlaneFactory.Make("did:plc:alice111");
        Assert.Equal((11, 12), (sp.W, sp.H));
    }

    [Fact]
    public void A_twin_tailed_plane_with_wings_from_row_four()
    {
        var (grid, legend) = Describe(PlaneFactory.Make("did:plc:alice111"));
        Assert.Equal(".....a...../.....b...../....bab..../....bcb..../..aababaa../aaddbbbddaa/ddddbbbdddd/ddddbbbdddd/.d.dbbbd.d./...dbbbd.../...dbbbd.../...eefee...", grid);
        Assert.Equal("a=ffbfc3 b=6df2e8 c=bfe9ff d=3946bf e=1c235f f=ffd166", legend);
    }

    [Fact]
    public void A_twin_tailed_plane_with_a_narrower_wing_that_starts_lower()
    {
        var (grid, legend) = Describe(PlaneFactory.Make("did:plc:dave4444"));
        Assert.Equal(".....a...../.....b...../....bab..../....bcb..../....bab..../..aabbbaa../.addbbbdda./.dddbbbddd./.dddbbbddd./.d.dbbbd.d./...dbbbd.../...eefee...", grid);
        Assert.Equal("a=ffd1bf b=6dcbf2 c=bfe9ff d=3954bf e=1c2a5f f=ffd166", legend);
    }

    [Fact]
    public void A_single_tailed_plane_keeps_its_body_colour_at_the_tail()
    {
        var (grid, legend) = Describe(PlaneFactory.Make("did:plc:carol333"));
        Assert.Equal(".....a...../.....b...../....bab..../....bcb..../....bab..../aaaabbbaaaa/ddddbbbdddd/ddddbbbdddd/.dddbbbddd./...dbbbd.../....bbb..../...bbebb...", grid);
        Assert.Equal("a=bfffcc b=f26dd7 c=bfe9ff d=bf5139 e=ffd166", legend);
    }

    [Fact]
    public void The_cockpit_glass_is_pale_blue_on_the_third_row()
    {
        var sp = PlaneFactory.Make("did:web:example.com");
        Assert.Equal(0xbfe9ffu | Sprite.Opaque, sp.Px[3 * PlaneFactory.W + 5]);
        Assert.Equal(PlaneFactory.Accent("did:web:example.com") | Sprite.Opaque, sp.Px[7 * PlaneFactory.W + 5]);
    }
}
