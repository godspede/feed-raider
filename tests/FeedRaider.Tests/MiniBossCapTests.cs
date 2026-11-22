using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

/// <summary>
/// Mini-bosses do not pile up: a filler wave that would add a Warden (or a Nest, Yoke or Chorus) to a field that already holds
/// its share gets another kind in that place, and a stage that brings two new kinds shows both hints on its banner.
/// </summary>
public sealed class MiniBossCapTests
{
    private const ulong Seed = 5;
    private const int DefaultCols = 120, DefaultRows = 46;

    private static RaiderGame NewGame(int stage, int cols = DefaultCols, int rows = DefaultRows)
    {
        var g = new RaiderGame(Seed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtStage(stage);
        return g;
    }

    private static int Alive(RaiderGame g, EnemyKind kind) => g.Enemies.Count(e => e.Kind == kind && !e.Leaving);

    /// <summary>Steps with the ship idle, sweeping away everything but the mini-bosses a test holds, and says what it saw arrive.</summary>
    private static List<EnemyKind> Run(RaiderGame g, int frames, Action<RaiderGame>? each = null, params EnemyKind[] keep)
    {
        var arrived = new List<EnemyKind>();
        var seen = new HashSet<Enemy>(ReferenceEqualityComparer.Instance);
        foreach (var e in g.Enemies) seen.Add(e);
        for (int i = 0; i < frames; i++)
        {
            g.Step();
            foreach (var e in g.Enemies) if (seen.Add(e)) arrived.Add(e.Kind);
            each?.Invoke(g);
            g.Enemies.RemoveAll(e => Array.IndexOf(keep, e.Kind) < 0);
            g.Bullets.RemoveAll(b => !b.Friendly);
        }
        return arrived;
    }

    /// <summary>The enemies the filler waves launched so far would put on the field with no cap, less those still queued.</summary>
    private static int Planned(RaiderGame g, int stage, out int wardens)
    {
        int total = 0; wardens = 0;
        for (int n = 1; n <= g.WavesSpawned; n++)
        {
            var w = WaveSpawner.Filler(Hashing.Mix(Seed + (ulong)n), stage, n, 0);
            total += w.Enemies.Count;
            wardens += w.Enemies.Count(e => e.Kind == EnemyKind.Warden);
        }
        return total - g.PendingSpawns;
    }

    [Fact]
    public void With_a_warden_alive_at_stage_two_no_second_one_comes_and_every_wave_keeps_its_count()
    {
        var g = NewGame(2);
        var warden = g.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
        var arrived = Run(g, 400, gg => { warden.Age = 0; Assert.True(Alive(gg, EnemyKind.Warden) <= 1); }, EnemyKind.Warden);
        Assert.Contains(warden, g.Enemies);
        Assert.DoesNotContain(EnemyKind.Warden, arrived);
        int planned = Planned(g, 2, out int wardens);
        Assert.True(wardens >= 2, "the seed must pick a warden more than once, or this proves nothing");
        Assert.Equal(planned, arrived.Count);   // each warden became something else, none went missing
    }

    [Fact]
    public void The_replacement_is_the_stages_other_unlock_and_a_mine_only_when_it_is_the_only_one_unlocked()
    {
        Assert.Equal(EnemyKind.Diver, WaveSpawner.Substitute(EnemyKind.Warden, 2, 1, shootable: false));
        Assert.Equal(EnemyKind.Diver, WaveSpawner.Substitute(EnemyKind.Warden, 2, 1, shootable: true));
        // a stage with no other unlock of its own takes the newest kind that is no mini-boss
        var got = WaveSpawner.Substitute(EnemyKind.Warden, 6, 1, shootable: false, available: _ => true);
        Assert.Equal(EnemyKind.Lobber, got);
        Assert.False(Ramp.IsMiniBoss(got));
        // only a mine is unlocked: a wave that must be clearable gets a grunt, any other gets the mine
        static bool OnlyMine(EnemyKind k) => k is EnemyKind.Mine or EnemyKind.Warden;
        Assert.Equal(EnemyKind.Mine, WaveSpawner.Substitute(EnemyKind.Warden, 2, 1, shootable: false, available: OnlyMine));
        Assert.Equal(EnemyKind.Grunt, WaveSpawner.Substitute(EnemyKind.Warden, 2, 1, shootable: true, available: OnlyMine));
        for (int stage = 2; stage <= 12; stage++)
            foreach (var k in new[] { EnemyKind.Warden, EnemyKind.Nest, EnemyKind.Yoke, EnemyKind.Chorus })
            {
                var r = WaveSpawner.Substitute(k, stage, 1, shootable: false, available: _ => true);
                Assert.NotEqual(k, r);
                Assert.False(Ramp.IsMiniBoss(r), $"{k} at stage {stage} became {r}");
                Assert.NotEqual(EnemyKind.Mine, r);
            }
    }

    [Fact]
    public void When_the_warden_dies_or_starts_to_leave_the_next_wave_that_picks_one_spawns_it()
    {
        var g = NewGame(2);
        var first = g.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
        Assert.DoesNotContain(EnemyKind.Warden, Run(g, 200, gg => first.Age = 0, EnemyKind.Warden));
        g.Enemies.Remove(first);   // killed
        Assert.Contains(EnemyKind.Warden, Run(g, 400, null, EnemyKind.Warden));

        var h = NewGame(2);
        var leaving = h.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
        Assert.DoesNotContain(EnemyKind.Warden, Run(h, 200, hh => leaving.Age = 0, EnemyKind.Warden));
        bool both = false;
        Run(h, 400, hh => { leaving.Age = 0; leaving.Leaving = true; leaving.Y = 20; both |= hh.Enemies.Count(e => e.Kind == EnemyKind.Warden) == 2; }, EnemyKind.Warden);
        Assert.True(both, "a leaving warden does not count, so a second one may come while it goes");
    }

    [Fact]
    public void From_stage_six_two_may_live_and_a_third_is_replaced()
    {
        Assert.Equal(1, Tuning.MiniBossCap);
        Assert.Equal(2, Tuning.MiniBossCapLate);
        Assert.Equal(6, Tuning.MiniBossCapLateFromStage);
        var g = NewGame(6);
        var a = g.SpawnKind(EnemyKind.Warden, 0.3);
        var b = g.SpawnKind(EnemyKind.Warden, 0.7);
        var arrived = Run(g, 600, gg => { a.Age = 0; b.Age = 0; }, EnemyKind.Warden);
        Assert.DoesNotContain(EnemyKind.Warden, arrived);
        Planned(g, 6, out int wardens);
        Assert.True(wardens >= 1, "the seed must pick a warden, or this proves nothing");

        var h = NewGame(6);
        var one = h.SpawnKind(EnemyKind.Warden, 0.3);
        int most = 0;
        Run(h, 1200, hh => { one.Age = 0; most = Math.Max(most, Alive(hh, EnemyKind.Warden)); Assert.True(Alive(hh, EnemyKind.Warden) <= 2); }, EnemyKind.Warden);
        Assert.Equal(2, most);
    }

    [Fact]
    public void The_cap_is_one_at_stages_one_to_five_and_two_from_six()
    {
        for (int s = 1; s <= 5; s++) Assert.Equal(1, Ramp.MiniBossCapAt(s));
        for (int s = 6; s <= Tuning.StageCap; s++) Assert.Equal(2, Ramp.MiniBossCapAt(s));
    }

    [Fact]
    public void The_same_seed_gives_the_same_replacements()
    {
        static List<EnemyKind> ReplacementsOnStage2()
        {
            var g = NewGame(2);
            var w = g.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
            return Run(g, 500, gg => w.Age = 0, EnemyKind.Warden);
        }
        var a = ReplacementsOnStage2();
        Assert.NotEmpty(a);
        Assert.Equal(a, ReplacementsOnStage2());
    }

    [Fact]
    public void The_lesser_gunship_keeps_its_own_limits()
    {
        var g = NewGame(2);
        var l1 = g.SpawnLesser();
        var l2 = g.SpawnLesser();
        Assert.True(l1.Lesser && l2.Lesser);
        Assert.Equal(2, g.Enemies.Count(e => e.Kind == EnemyKind.Gunship && e.Lesser));
        Assert.Equal(new Ramp.LesserPlan(2, 0, 0), Ramp.Lessers(4));
        Assert.Equal(new Ramp.LesserPlan(0, 600, 2), Ramp.Lessers(9));
        Assert.Equal(new Ramp.LesserPlan(0, 420, 2), Ramp.Lessers(10));
        Assert.False(Ramp.IsMiniBoss(EnemyKind.Gunship));
    }

    // ---------------------------------------------------------------- the hints

    private static string ScreenText(RaiderGame g, int cols, int rows)
    {
        var scr = new Screen(cols, rows);
        new RaiderView().FitAndRender(g, scr);
        return string.Join("\n", Enumerable.Range(0, scr.Rows).Select(r => new string(Enumerable.Range(0, scr.Cols).Select(c => scr.CharAt(c, r)).ToArray())));
    }

    [Fact]
    public void Stage_two_shows_the_divers_hint_and_the_wardens_once_each()
    {
        Assert.Equal(new[] { EnemyKind.Diver, EnemyKind.Warden }, Ramp.Introduced(2));
        Assert.Equal(EnemyKind.Warden, Ramp.Introduced(2)[^1]);
        var g = NewGame(2);
        string all = ScreenText(g, DefaultCols, DefaultRows);
        Assert.Equal(1, all.AsSpan().Count("divers charge where they aim"));
        Assert.Equal(1, all.AsSpan().Count("wardens shield others: kill one first"));
    }

    [Fact]
    public void On_the_smallest_field_both_lines_still_read_by_wrapping_the_long_one()
    {
        var g = NewGame(2, 30, 16);
        string all = ScreenText(g, 30, 16);
        Assert.Contains("divers charge where they aim", all);
        Assert.Contains("wardens shield others: kill", all);
        Assert.Contains("one first", all);
    }

    [Fact]
    public void A_stage_with_one_new_kind_shows_what_it_showed_before_and_a_later_start_shows_neither_stage_two_hint()
    {
        for (int s = 1; s <= Tuning.StageCap; s++)
        {
            var unlocks = Ramp.Unlocks(s, 0);
            Assert.Equal(unlocks.Where(k => Ramp.HintFor(k) is not null), Ramp.Introduced(s));
            if (unlocks.Length == 1) Assert.Equal(new[] { Ramp.HintFor(unlocks[0]) }, Ramp.Introduced(s).Select(Ramp.HintFor));
        }
        Assert.Empty(Ramp.Introduced(1));   // the mines have their own hint, part way through
        var later = NewGame(3);
        string all = ScreenText(later, DefaultCols, DefaultRows);
        Assert.DoesNotContain("divers charge", all);
        Assert.DoesNotContain("wardens shield", all);
    }
}
