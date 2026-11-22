using System.Reflection;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;
using static FeedRaider.Tests.DarknessSourcesTests;

namespace FeedRaider.Tests;

/// <summary>
/// The opening of a real run, burst included: the page reads as moving, a burst comes at once, and a speaker says a rude post well
/// before the first boss. Each run is one seed and a bot that cannot die, so the clock is the run's own and not the bot's luck.
/// </summary>
public class OpeningPaceTests
{
    private const int Fps = Tuning.Fps;
    /// <summary>A desktop field and the phone's, as the web host lays them out at 1280 and 412 wide.</summary>
    public static readonly TheoryData<int, int> Fields = new() { { EnemyKit.BigCols, EnemyKit.BigRows }, { AtlasKeyTests.PhoneCols, AtlasKeyTests.PhoneRows } };

    private const int SpeakerBySeconds = 30;
    private const int BurstTakenBySeconds = 5;
    /// <summary>Frames of invulnerability the bot grants itself each step; the bot ignores enemies this near above its ship or lower,
    /// and stops steering this near its target column.</summary>
    private const int KeepAliveInvuln = 6;
    private const double BotBlindRows = 4, BotDeadband = 1;

    private static readonly PropertyInfo InvulnProp = typeof(RaiderGame).GetProperty(nameof(RaiderGame.Invuln))!;

    private static RaiderGame RealRun(int cols, int rows)
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { OpeningBurst = true, BossGates = true };
        game.SetLayout(FieldLayout.Compute(cols, rows));
        game.OfferDarkness(new DarknessPost(DarknessKind.Rude, 1, "a rude post held for a speaker", DarknessSource.Label, Rude));
        return game;
    }

    /// <summary>Fires all the time and, once the opening burst has been taken, steers under the lowest enemy ahead. Until then it
    /// holds still, as a new player does, and the opening pickup comes down to it.</summary>
    private static void Bot(RaiderGame game, bool burstTaken)
    {
        InvulnProp.SetValue(game, KeepAliveInvuln);
        game.Input.Key(GameKey.Fire, true);
        double target = game.PX, lowest = double.NegativeInfinity;
        if (burstTaken)
            foreach (var e in game.Enemies)
                if (!e.Leaving && e.Y <= game.PY - BotBlindRows && e.Y > lowest) { lowest = e.Y; target = e.X; }
        game.Input.Key(GameKey.Left, game.PX > target + BotDeadband);
        game.Input.Key(GameKey.Right, game.PX < target - BotDeadband);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void A_real_run_shows_a_burst_pickup_by_two_seconds_runs_the_burst_by_five_and_a_speaker_says_the_held_rude_post_by_thirty(int cols, int rows)
    {
        var game = RealRun(cols, rows);
        long pickupAt = -1, burstAt = -1, speakerAt = -1;
        while (game.Tick < SpeakerBySeconds * Fps && (speakerAt < 0 || burstAt < 0))
        {
            Bot(game, burstAt >= 0);
            game.Step();
            if (pickupAt < 0 && game.Pickups.Exists(p => p.Kind == PickupKind.Burst)) pickupAt = game.Tick;
            if (burstAt < 0 && game.Bursting) burstAt = game.Tick;
            if (speakerAt < 0 && game.Enemies.Exists(e => e.SpeechLines is not null)) speakerAt = game.Tick;
        }
        Assert.InRange(pickupAt, 0, Tuning.OpeningBurstFrame);
        Assert.InRange(burstAt, 0, BurstTakenBySeconds * Fps);
        Assert.True(speakerAt >= 0, $"no enemy spoke in the first {SpeakerBySeconds} s");
        Assert.Equal(GateState.None, game.Gate);
    }

    [Fact]
    public void Each_star_layer_falls_faster_than_the_one_behind_it_and_the_near_one_at_least_three_times_as_fast_as_the_cards_at_the_default_speed()
    {
        // short enough that the first card is still on the field at the end
        const int MeasureFrames = 200, Times = 3;
        // three times 0.2 in doubles is a hair over 0.6
        const double RoundingSlack = 1e-9;
        var queue = new PostQueue();
        for (int i = 0; i < 4; i++)
            queue.Offer(new FeedPost($"at://did:plc:o{i}/app.bsky.feed.post/{i}", "c", $"did:plc:o{i}", "o.test", "an ordinary post", 0, 0, 0, null, null));
        var field = new CardField(queue, new PostBook(), EnemyKit.BigCols, EnemyKit.BigRows, FeedSpeed.Default);
        field.Step();
        var card = field.Cards[0];
        int top = card.Top;
        for (int i = 0; i < MeasureFrames; i++) field.Step();
        double cardPace = (card.Top - top) * CardField.PixelsPerCell / (double)MeasureFrames;
        Assert.True(cardPace > 0, "the card did not move");
        Assert.True(Tuning.ScrollStars < Tuning.ScrollStarsFar && Tuning.ScrollStarsFar < Tuning.ScrollStarsNear, "the layers are out of order");
        Assert.True(Tuning.ScrollStarsNear >= Times * cardPace - RoundingSlack, $"near stars {Tuning.ScrollStarsNear} px a frame, cards {cardPace:0.000}");
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void The_sky_holds_more_stars_than_the_dust_alone_and_they_move_between_frames(int cols, int rows)
    {
        const int DustStars = 34, FarApartFrames = 60;
        var layout = FieldLayout.Compute(cols, rows);
        var sky = new Background(layout.PW, layout.PH, RaiderView.DefaultBackgroundSeed);
        Assert.True(sky.StarTotal > DustStars, $"{sky.StarTotal} stars");
        var (early, late) = (new Surface(layout.PW, layout.PH), new Surface(layout.PW, layout.PH));
        sky.Draw(early, 0);
        sky.Draw(late, FarApartFrames);
        int moved = early.P.Zip(late.P).Count(pair => pair.First != pair.Second);
        // the dust moves under 4 px in that time and the near layer some 36, so far more than the dust's pixels change
        Assert.True(moved > 2 * DustStars, $"{moved} pixels changed");
    }

    [Fact]
    public void A_level_one_ship_under_the_stage_one_lesser_gunship_kills_it_before_it_leaves()
    {
        var game = new RaiderGame(EnemyKit.PlainSeed);
        game.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        EnemyKit.Clear(game);
        var lesser = game.SpawnLesser();
        int frames = 0;
        while (game.Enemies.Contains(lesser) && lesser.Hp > 0 && !lesser.Leaving && frames++ < Tuning.LesserStayFrames)
        {
            InvulnProp.SetValue(game, KeepAliveInvuln);
            game.Input.Key(GameKey.Fire, true);
            game.Input.Key(GameKey.Left, game.PX > lesser.X + BotDeadband);
            game.Input.Key(GameKey.Right, game.PX < lesser.X - BotDeadband);
            game.Step();
            game.Bullets.RemoveAll(b => !b.Friendly);
            game.Enemies.RemoveAll(e => e != lesser);
        }
        Assert.Equal(1, game.WeaponLevel);
        Assert.True(lesser.Hp <= 0, $"the lesser gunship had {lesser.Hp} hit points left after {frames} frames");
    }
}
