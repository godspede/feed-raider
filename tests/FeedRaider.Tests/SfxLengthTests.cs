using FeedRaider.Console.Audio;

namespace FeedRaider.Tests;

// How long a rendered sound is: its latest-ending source plus the tail, whatever order the sources are added in.
public class SfxLengthTests
{
    private const int Rate = SfxSynth.SampleRate;

    [Fact]
    public void A_short_source_added_last_does_not_shorten_the_sound()
    {
        // the shot's longest source (0.1 s) comes before its shortest (0.012 s)
        Assert.Equal((int)((0.1 + 0.02) * Rate), SfxSynth.Render("shot", 0, 0, new Random(1)).Length);
    }

    [Fact]
    public void A_source_runs_a_fixed_tail_past_its_envelope()
    {
        // the hit's longest source is 0.07 s
        Assert.Equal((int)((0.07 + 0.02) * Rate), SfxSynth.Render("hit", 0, 0, new Random(1)).Length);
    }

    [Fact]
    public void A_sound_several_times_the_first_buffer_is_rendered_whole()
    {
        var s = SfxSynth.Render("boom", 0, 1, new Random(1));
        Assert.Equal((int)((0.3 + 2 + 0.02) * Rate), s.Length);
        Assert.Contains(s.Skip(s.Length - 2000).Take(1000), x => x != 0f);
    }
}
