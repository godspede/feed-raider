using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

/// <summary>
/// The speed burst: a pickup that fires when it is flown over, scrolls the page up fast, and carries the ship through bolts and
/// small enemies for a few seconds. Every run is deterministic: one seed, and a field the test keeps empty of what it does not place.
/// </summary>
public class BurstTests
{
    private const int Cols = 130, Rows = 46;
    private const int Fps = Tuning.Fps;
    private const int OneHit = 1;

    private static RaiderGame NewGame(bool opening = false, bool gates = false)
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = gates, OpeningBurst = opening };
        game.SetLayout(FieldLayout.Compute(Cols, Rows));
        EnemyKit.Clear(game);
        return game;
    }

    /// <summary>One step with no enemy and no bolt left on the field but the ones in <paramref name="keep"/>.</summary>
    private static void StepQuiet(RaiderGame game, params Enemy[] keep)
    {
        game.Step();
        game.Bullets.RemoveAll(b => !b.Friendly);
        game.Enemies.RemoveAll(e => !keep.Contains(e) && e != game.Boss);
    }

    private static void RunQuiet(RaiderGame game, int frames) { for (int i = 0; i < frames; i++) StepQuiet(game); }

    /// <summary>Puts a burst pickup on the ship and steps once, so it is taken.</summary>
    private static void TakeBurst(RaiderGame game)
    {
        game.SpawnPickup(PickupKind.Burst, game.PX, game.PY);
        StepQuiet(game);
    }

    /// <summary>An enemy held over the ship, still and armed, so it touches the ship every frame it is kept there.</summary>
    private static Enemy OnTheShip(RaiderGame game, EnemyKind kind, bool lesser = false)
    {
        var e = EnemyKit.Place(game, kind, game.PX, game.PY);
        e.VY = 0; e.Amp = 0; e.Lesser = lesser;
        return e;
    }

    private static void Hold(RaiderGame game, Enemy e) { e.X = e.BaseX = game.PX; e.Y = game.PY; }

    private static int Health(RaiderGame game) => game.Hp + game.Shield;

    private static FeedPost Post(int id) =>
        new($"at://did:plc:b{id}/app.bsky.feed.post/{id}", $"cid{id}", $"did:plc:b{id}", $"b{id}.test", "hi", 0, 0, 0, null, null, DisplayName: "B");

    // ---------------------------------------------------------------- the opening pickup

    [Fact]
    public void A_real_run_has_a_burst_pickup_on_the_field_by_two_seconds_that_a_still_ship_takes_by_three_and_a_half()
    {
        const int dueBy = 2 * Fps, takenBy = 7 * Fps / 2;
        var game = NewGame(opening: true);
        while (game.Tick < dueBy) StepQuiet(game);
        Assert.Contains(game.Pickups, p => p.Kind == PickupKind.Burst);
        while (game.Tick < takenBy && !game.Bursting) StepQuiet(game);
        Assert.True(game.Bursting, $"the burst had not been taken by tick {game.Tick}");
    }

    [Fact]
    public void A_game_made_without_the_opening_burst_drops_no_burst_pickup_at_the_start()
    {
        const int pastDue = 4 * Fps;
        var game = NewGame(opening: false);
        for (int i = 0; i < pastDue; i++)
        {
            StepQuiet(game);
            Assert.DoesNotContain(game.Pickups, p => p.Kind == PickupKind.Burst);
        }
    }

    [Fact]
    public void A_ship_that_died_before_two_seconds_gets_no_opening_burst()
    {
        const int pastDue = 4 * Fps;
        var game = NewGame(opening: true);
        for (int i = 0; i < Tuning.OpeningBurstFrame && game.Deaths == 0; i++)
        {
            if (game.Alive && game.Invuln == 0) game.Bullets.Add(new Bullet { X = game.PX, Y = game.PY });
            game.Step();
        }
        Assert.Equal(1, game.Deaths);
        while (game.Tick < pastDue)
        {
            StepQuiet(game);
            Assert.DoesNotContain(game.Pickups, p => p.Kind == PickupKind.Burst);
        }
    }

    // ---------------------------------------------------------------- taking it

    [Fact]
    public void Taking_a_burst_starts_it_on_that_step_plays_its_whoosh_and_stores_nothing()
    {
        var game = NewGame();
        var played = new List<string>();
        game.Sfx = new SfxBus((sound, _, _, _) => played.Add(sound));
        int bombs = game.Bombs, shield = game.Shield, level = game.WeaponLevel;
        TakeBurst(game);
        Assert.True(game.Bursting);
        Assert.Empty(game.Pickups);
        Assert.Contains(SfxNames.Burst, played);
        Assert.Equal((bombs, shield, level), (game.Bombs, game.Shield, game.WeaponLevel));
    }

    [Fact]
    public void A_burst_runs_for_its_frames_after_the_step_it_is_taken_on_then_ends_with_its_falling_tone()
    {
        var game = NewGame();
        var played = new List<string>();
        game.Sfx = new SfxBus((sound, _, _, _) => played.Add(sound));
        TakeBurst(game);
        int frames = 0;
        while (game.Bursting) { StepQuiet(game); frames++; }
        Assert.Equal(RaiderGame.BurstTotalFrames, frames);
        Assert.Contains(SfxNames.BurstEnd, played);
    }

    // ---------------------------------------------------------------- what it carries the ship through

    [Fact]
    public void A_bolt_on_the_ship_during_a_burst_does_no_harm()
    {
        var game = NewGame();
        TakeBurst(game);
        int health = Health(game);
        for (int i = 0; i < Tuning.BurstFrames; i++)
        {
            game.Bullets.Add(new Bullet { X = game.PX, Y = game.PY });
            game.Step();
        }
        Assert.True(game.Bursting);
        Assert.Equal(health, Health(game));
    }

    [Theory]
    [InlineData(EnemyKind.Grunt)]
    [InlineData(EnemyKind.Mine)]
    [InlineData(EnemyKind.Diver)]
    public void A_small_enemy_held_on_the_ship_during_a_burst_does_no_harm_and_is_not_spent_on_it(EnemyKind kind)
    {
        var game = NewGame();
        TakeBurst(game);
        int health = Health(game);
        var e = OnTheShip(game, kind);
        for (int i = 0; i < Tuning.BurstFrames; i++) { Hold(game, e); StepQuiet(game, e); }
        Assert.True(game.Bursting);
        Assert.Equal(health, Health(game));
        Assert.Contains(e, game.Enemies);
    }

    [Theory]
    [InlineData(EnemyKind.Warden, false)]
    [InlineData(EnemyKind.Gunship, true)]
    public void A_mini_boss_or_a_lesser_gunship_on_the_ship_still_hurts_it_during_a_burst(EnemyKind kind, bool lesser)
    {
        const int hitWithin = 2 * Fps;
        var game = NewGame();
        TakeBurst(game);
        int health = Health(game);
        var e = OnTheShip(game, kind, lesser);
        for (int i = 0; i < hitWithin && Health(game) == health; i++) { Hold(game, e); StepQuiet(game, e); }
        Assert.True(game.Bursting);
        Assert.Equal(health - OneHit, Health(game));
    }

    [Fact]
    public void A_burst_that_ends_inside_a_grunt_leaves_the_ship_unhurt_while_its_grace_lasts_and_the_grunt_hurts_it_after()
    {
        var game = NewGame();
        TakeBurst(game);
        var e = OnTheShip(game, EnemyKind.Grunt);
        while (game.Bursting) { Hold(game, e); StepQuiet(game, e); }
        int health = Health(game);
        Assert.True(game.Invuln > 0, "the end of the burst gives grace");
        while (game.Invuln > 1) { Hold(game, e); StepQuiet(game, e); }
        Assert.Equal(health, Health(game));
        Hold(game, e); StepQuiet(game, e);
        Assert.Equal(health - OneHit, Health(game));
    }

    // ---------------------------------------------------------------- the page and the clock

    [Fact]
    public void A_burst_eases_the_page_up_to_faster_than_the_players_speed_and_back_and_leaves_their_speed_setting_alone()
    {
        var game = NewGame();
        game.FeedSpeed = FeedSpeed.Default;
        int own = game.Cards.FramesPerRow;
        TakeBurst(game);
        var seen = new List<int> { game.Cards.FramesPerRow };
        while (game.Bursting) { StepQuiet(game); seen.Add(game.Cards.FramesPerRow); }
        int fastest = seen.Min(), at = seen.IndexOf(fastest);
        Assert.True(fastest < own, $"the page ran at {fastest} frames a row against the player's {own}");
        for (int i = 1; i <= at; i++) Assert.True(seen[i] <= seen[i - 1], $"the page slowed on the way in at frame {i}");
        int last = seen.Count - 2;   // the final entry is the frame the burst ended on, back at the player's speed
        for (int i = at + 1; i <= last; i++) Assert.True(seen[i] >= seen[i - 1], $"the page sped up on the way out at frame {i}");
        Assert.True(seen[last] > fastest, "the page slows before the burst ends");
        Assert.Equal(own, game.Cards.FramesPerRow);
        Assert.Equal(FeedSpeed.Default, game.FeedSpeed);
        Assert.Equal(FeedSpeed.Default, game.Cards.Speed);
    }

    [Fact]
    public void The_stage_clock_runs_faster_during_a_burst_than_without_one()
    {
        const int frames = Fps;
        var plain = NewGame();
        long before = plain.Progress;
        RunQuiet(plain, frames);
        long plainRun = plain.Progress - before;

        var burst = NewGame();
        TakeBurst(burst);
        before = burst.Progress;
        RunQuiet(burst, frames);
        Assert.True(burst.Bursting);
        Assert.True(burst.Progress - before > plainRun, $"{burst.Progress - before} frames of progress in a burst, {plainRun} without");
    }

    [Fact]
    public void A_burst_takes_no_more_posts_than_a_full_queue_holds()
    {
        var game = NewGame();
        for (int id = 0; id < Tuning.PostQueueMax; id++) game.Posts.Offer(Post(id));
        RunQuiet(game, 1);
        int entered = 0;
        game.Cards.Entered += _ => entered++;
        TakeBurst(game);
        int next = Tuning.PostQueueMax;
        while (game.Bursting)
        {
            while (game.Posts.Count < Tuning.PostQueueMax) game.Posts.Offer(Post(next++));
            StepQuiet(game);
        }
        Assert.InRange(entered, 1, Tuning.PostQueueMax);
    }

    // ---------------------------------------------------------------- the gate

    [Fact]
    public void A_burst_running_as_the_gate_opens_ends_before_the_warning()
    {
        const int maxFrames = 3000;
        var game = NewGame(gates: true);
        game.SetProgress(Tuning.StageFrames - Fps);
        TakeBurst(game);
        for (int i = 0; i < maxFrames && game.Gate == GateState.None; i++) StepQuiet(game);
        Assert.Equal(GateState.Clearing, game.Gate);
        Assert.True(game.BurstLeft <= Tuning.BurstOutFrames, "the burst eases out as the gate opens");
        for (int i = 0; i < maxFrames && game.Gate == GateState.Clearing; i++) StepQuiet(game);
        Assert.Equal(GateState.Warning, game.Gate);
        Assert.False(game.Bursting);
    }

    [Fact]
    public void While_the_gate_is_up_no_burst_can_drop_and_one_flown_over_is_not_taken()
    {
        const int maxFrames = 3000;
        var game = NewGame(gates: true);
        game.SetProgress(Tuning.StageFrames - 2);
        for (int i = 0; i < maxFrames && game.Gate == GateState.None; i++) StepQuiet(game);
        foreach (var state in new[] { GateState.Clearing, GateState.Warning, GateState.Fighting })
        {
            for (int i = 0; i < maxFrames && game.Gate != state; i++) StepQuiet(game);
            Assert.Equal(state, game.Gate);
            Assert.Equal(0, game.DropWeights().Burst);
            TakeBurst(game);
            Assert.False(game.Bursting, $"a burst started during {state}");
            game.Pickups.Clear();
        }
    }

    // ---------------------------------------------------------------- how often it comes

    [Fact]
    public void No_burst_can_drop_while_one_is_on_the_field_or_running_or_for_twenty_seconds_after_one_is_taken()
    {
        const int repeatAfter = 20 * Fps;
        var game = NewGame();
        Assert.True(game.DropWeights().Burst > 0);
        var lying = game.SpawnPickup(PickupKind.Burst, game.PX, game.PH * Tuning.OpeningBurstRow);
        Assert.Equal(0, game.DropWeights().Burst);
        game.Pickups.Remove(lying);
        TakeBurst(game);
        long taken = game.Tick;
        Assert.Equal(0, game.DropWeights().Burst);
        while (game.Tick < taken + repeatAfter - 1)
        {
            StepQuiet(game);
            Assert.Equal(0, game.DropWeights().Burst);
        }
        StepQuiet(game);
        Assert.True(game.DropWeights().Burst > 0);
    }
}
