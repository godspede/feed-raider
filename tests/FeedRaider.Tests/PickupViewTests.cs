using System.Reflection;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// How a pickup looks on the pixel surface: its capsule, and its blink in the last two seconds of its life.
public class PickupViewTests
{
    private const int Cx = 20, Cy = 20;       // the pickup's centre; its 6x6 body covers 17..22

    private static readonly MethodInfo Draw = typeof(RaiderView).GetMethod("DrawPickup", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>A game stepped to frame <paramref name="frame"/>, and a pickup of <paramref name="kind"/> drawn on a fresh surface.</summary>
    private static Surface Drawn(PickupKind kind, long frame, int age = 0)
    {
        var g = new RaiderGame(5);
        g.SetLayout(FieldLayout.Compute(120, 46));
        while (g.Frame < frame) g.Step();
        var p = g.SpawnPickup(kind, Cx, Cy);
        p.Age = age;
        var f = new Surface(40, 40);
        Draw.Invoke(null, [g, f, p]);
        return f;
    }

    /// <summary>The 6x6 body as letters: d dark rim, c colour, l light mark, . untouched.</summary>
    private static string Body(Surface f, PickupKind kind)
    {
        uint c = RaiderGame.PickupColor(kind), dark = Surface.Mix(c, Palette.SkyTop, 0.55), light = Surface.Mix(c, 0xffffff, 0.6);
        var rows = new List<string>();
        for (int y = Cy - 3; y < Cy + 3; y++)
        {
            var row = "";
            for (int x = Cx - 3; x < Cx + 3; x++)
            {
                uint px = f.Get(x, y);
                row += px == dark ? 'd' : px == c ? 'c' : px == light ? 'l' : '.';
            }
            rows.Add(row);
        }
        return string.Join("/", rows);
    }

    [Fact]
    public void A_bomb_is_a_rounded_capsule_with_a_round_charge()
    {
        Assert.Equal(".dddd./dccccd/dcllcd/dcllcd/dccccd/.dddd.", Body(Drawn(PickupKind.Bomb, 3), PickupKind.Bomb));
    }

    [Theory]
    [InlineData(PickupKind.Shield)]
    [InlineData(PickupKind.Power)]
    public void A_shield_and_a_power_pickup_carry_their_marks(PickupKind kind)
    {
        // the shield's cross and the power arrow share a shape at this size
        Assert.Equal(".dddd./dcllcd/dlllld/dcllcd/dcllcd/.dddd.", Body(Drawn(kind, 3), kind));
    }

    [Fact]
    public void The_body_stays_inside_its_six_pixel_box()
    {
        var f = Drawn(PickupKind.Bomb, 3);
        uint c = RaiderGame.PickupColor(PickupKind.Bomb), dark = Surface.Mix(c, Palette.SkyTop, 0.55);
        for (int y = 0; y < f.H; y++)
            for (int x = 0; x < f.W; x++)
                if ((f.Get(x, y) == c && (x < Cx - 2 || x > Cx + 1 || y < Cy - 2 || y > Cy + 1)) || (f.Get(x, y) == dark && (x < Cx - 3 || x > Cx + 2 || y < Cy - 3 || y > Cy + 2))) Assert.Fail($"body pixel outside the body at {x},{y}");
    }

    [Fact]
    public void A_fresh_pickup_is_drawn_on_every_frame()
    {
        for (long frame = 0; frame < 12; frame++)
            Assert.NotEqual('.', Body(Drawn(PickupKind.Bomb, frame), PickupKind.Bomb)[1]);
    }

    [Fact]
    public void A_pickup_in_its_last_two_seconds_blinks_every_three_frames()
    {
        int age = Tuning.PickupLifeFrames - 10;
        for (long frame = 0; frame < 12; frame++)
        {
            bool hidden = frame / 3 % 2 == 0;
            string body = Body(Drawn(PickupKind.Bomb, frame, age), PickupKind.Bomb);
            if (hidden) Assert.DoesNotContain('d', body); else Assert.Contains('d', body);
        }
    }

    [Fact]
    public void A_pickup_still_in_the_clear_does_not_blink()
    {
        int age = Tuning.PickupLifeFrames - Tuning.PickupBlinkFrames - 1;
        for (long frame = 0; frame < 6; frame++) Assert.Contains('d', Body(Drawn(PickupKind.Bomb, frame, age), PickupKind.Bomb));
    }

    [Fact]
    public void The_view_draws_every_pickup_on_the_field()
    {
        var g = new RaiderGame(5);
        g.SetLayout(FieldLayout.Compute(120, 46));
        g.Enemies.Clear(); g.Bullets.Clear(); g.Particles.Clear(); g.Rings.Clear();
        var view = new RaiderView();
        var empty = new Screen(120, 46);
        view.Render(g, empty);
        g.SpawnPickup(PickupKind.Bomb, g.PW / 2.0, g.PH / 3.0);
        var withPickup = new Screen(120, 46);
        view.Render(g, withPickup);
        Assert.False(empty.Ch.SequenceEqual(withPickup.Ch) && empty.Fg.SequenceEqual(withPickup.Fg) && empty.Bg.SequenceEqual(withPickup.Bg));
    }
}
