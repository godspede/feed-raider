using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The text colours drawn on the side panel and in a speech box read at body text contrast on their grounds.
public class SideAndSpeechBoxContrastTests
{
    [Fact]
    public void Side_panel_and_speech_box_text_colours_read_at_body_text_contrast()
    {
        // the side panel's heading takes the dim colour as it is; its handles, likes and post text start from these colours
        foreach (uint fg in new[] { Palette.Dim, Palette.Ice, RaiderView.PanelText })
            Assert.True(Surface.Contrast(fg, RaiderView.PanelBg) >= Surface.BodyTextContrast, $"{fg:x6} on the side panel");
        // a speech box draws its fixed colours as they are, with no lift toward white
        foreach (uint fg in new[] { EnemySpeechBox.HeaderFg, EnemySpeechBox.TextFg, EnemySpeechBox.UnreadFg })
            Assert.True(Surface.Contrast(fg, EnemySpeechBox.BoxBg) >= Surface.BodyTextContrast, $"{fg:x6} in a speech box");
        Assert.Equal(RaiderView.PanelText, Surface.Legible(RaiderView.PanelText, RaiderView.PanelBg));   // already legible: left alone
        const uint darkText = 0x203040, lightGround = 0xf0f0f0;
        Assert.True(Surface.Contrast(Surface.Legible(darkText, lightGround), lightGround) >= Surface.BodyTextContrast);   // on a light ground it darkens
    }
}
