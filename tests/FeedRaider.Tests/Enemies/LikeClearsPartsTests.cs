using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests.Enemies;

/// <summary>A like or repost takes a quarter off the parts of the enemies in the fight, once for each post.</summary>
public class LikeClearsPartsTests
{
    private const double QuarterOfPod = 5, QuarterOfShell = 7.5;
    private const double OneHitPointLeft = 1, NearlyDeadHp = 2;
    private const int Places = 6;
    // a card this far down has its repost button clear of the scripted enemy: a ship parked on the enemy is lost before its key is read
    private const int CardTopUnderTheEnemy = 30;

    /// <summary>The ship parked on the button and its key pressed, stepped until one tick has run, with the enemies left in the
    /// field and their bolts cleared.</summary>
    internal static void PressKeepingEnemies(RaiderGame g, Card card, PostAction action)
    {
        int top = card.Top;
        g.Bullets.Clear();
        PressRig.Park(g, card, action);
        PressRig.PressKey(g, action);
        long tick = g.Tick;
        while (g.Tick == tick) g.Step();
        card.Top = top;
    }

    [Fact]
    public void A_like_key_takes_a_quarter_off_each_living_part_and_kills_none()
    {
        var (g, card, _) = PressRig.Scene();
        var e = EnemyKit.ScriptedThreeParts(g);
        PartsTests.PodR(e).Hp = NearlyDeadHp;
        PressKeepingEnemies(g, card, PostAction.Like);
        Assert.True(card.Marks.Paid);
        Assert.Equal(EnemyKit.PodHp - QuarterOfPod, PartsTests.PodL(e).Hp, Places);
        Assert.Equal(EnemyKit.ShellHp - QuarterOfShell, PartsTests.Shell(e).Hp, Places);
        Assert.Equal(OneHitPointLeft, PartsTests.PodR(e).Hp, Places);
        Assert.All(e.Parts!, p => Assert.NotEqual(PartState.Dead, p.State));
        Assert.Contains(g.Popups, p => p.Text == RaiderGame.PartsPopup);
    }

    [Fact]
    public void A_repost_key_on_a_post_already_liked_takes_nothing_more_off_the_parts()
    {
        var (g, card, _) = PressRig.Scene(top: CardTopUnderTheEnemy);
        var e = EnemyKit.ScriptedThreeParts(g);
        PressKeepingEnemies(g, card, PostAction.Like);
        double after = PartsTests.PodL(e).Hp;
        PressKeepingEnemies(g, card, PostAction.Repost);
        Assert.NotEqual(ButtonState.Off, card.Marks.Repost);
        Assert.Equal(after, PartsTests.PodL(e).Hp, Places);
    }
}
