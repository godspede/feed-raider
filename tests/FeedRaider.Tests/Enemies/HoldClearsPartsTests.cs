using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests.Enemies;

/// <summary>A finished like or repost takes a quarter off the parts of the enemies in the fight, once for each post.</summary>
public class HoldClearsPartsTests
{
    private const double QuarterOfPod = 5, QuarterOfShell = 7.5;
    private const double OneHitPointLeft = 1, NearlyDeadHp = 2;
    private const int Places = 6;
    // a card this far down has its repost button clear of the scripted enemy: a ship parked on the enemy is lost before the hold ends
    private const int CardTopUnderTheEnemy = 30;

    private static void HoldKeepingEnemies(RaiderGame g, Card card, PostAction action)
    {
        int top = card.Top;
        long until = g.Tick + Tuning.HoldFrames;
        while (g.Tick < until)
        {
            g.Bullets.Clear();
            HoldRig.Park(g, card, action);
            g.Step();
            card.Top = top;
        }
    }

    [Fact]
    public void A_finished_hold_takes_a_quarter_off_each_living_part_and_kills_none()
    {
        var (g, card, _) = HoldRig.Scene();
        var e = EnemyKit.ScriptedThreeParts(g);
        PartsTests.PodR(e).Hp = NearlyDeadHp;
        HoldKeepingEnemies(g, card, PostAction.Like);
        Assert.True(card.Marks.Paid);
        Assert.Equal(EnemyKit.PodHp - QuarterOfPod, PartsTests.PodL(e).Hp, Places);
        Assert.Equal(EnemyKit.ShellHp - QuarterOfShell, PartsTests.Shell(e).Hp, Places);
        Assert.Equal(OneHitPointLeft, PartsTests.PodR(e).Hp, Places);
        Assert.All(e.Parts!, p => Assert.NotEqual(PartState.Dead, p.State));
        Assert.Contains(g.Popups, p => p.Text == RaiderGame.PartsPopup);
    }

    [Fact]
    public void A_second_hold_on_the_same_post_takes_nothing()
    {
        var (g, card, _) = HoldRig.Scene(top: CardTopUnderTheEnemy);
        var e = EnemyKit.ScriptedThreeParts(g);
        HoldKeepingEnemies(g, card, PostAction.Like);
        double after = PartsTests.PodL(e).Hp;
        HoldKeepingEnemies(g, card, PostAction.Repost);
        Assert.NotEqual(ButtonState.Off, card.Marks.Repost);
        Assert.Equal(after, PartsTests.PodL(e).Hp, Places);
    }
}
