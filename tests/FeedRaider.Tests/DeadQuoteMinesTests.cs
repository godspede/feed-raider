using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// A dead quote's notice turning into mines: when they come, where they sit on the card, that a post makes them once, when none come,
// and what a shot and a bomb do to them.
public class DeadQuoteMinesTests
{
    private const int Px = CardField.PixelsPerCell;
    /// <summary>How long the mines are watched riding the card: long enough for the card to move down several rows.</summary>
    private const int RideFrames = 60;
    /// <summary>Frames of fire at a mine: shots enough to have reached it many times over.</summary>
    private const int FireFrames = 120;
    /// <summary>A field narrower than the rig's, for a resize.</summary>
    private const int NarrowCols = 60;
    /// <summary>Decimal places a mine's position must match the box thirds to.</summary>
    private const int PositionDecimals = 6;

    private static FeedPost Quoting(QuoteState state, bool optOut = false) => PressRig.Post(1) with
    {
        HasEmbed = true,
        Attachment = AttachmentKind.Quote,
        Quote = new PostQuote(state, "Quoted", "q.test", state == QuoteState.Shown ? "the quoted words" : "", false, state == QuoteState.Other ? "a list" : null, optOut),
    };

    /// <summary>A game with the card on the field, its quote box's top row on <paramref name="boxRow"/>, nothing else on it and the ship parked.</summary>
    private static (RaiderGame G, Card C) Scene(FeedPost post, int boxRow, bool gates = false)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = false, BossGates = gates };
        g.SetLayout(FieldLayout.Compute(CardRig.ScrCols, CardRig.ScrRows));
        g.Posts.Offer(post);
        g.Step();
        var card = g.Cards.Cards[0];
        PutBoxOn(card, boxRow);
        g.Enemies.Clear();
        PressRig.Away(g);
        return (g, card);
    }

    private static void PutBoxOn(Card card, int boxRow) => card.Top = boxRow - card.Shape.Quote!.Box.Y;

    /// <summary>The first field row whose top pixel is clear of the HUD.</summary>
    private static int FirstClearRow => (CardPainter.HudPixelRows + Px - 1) / Px;
    /// <summary>A row two above it, under the HUD: a row step of the card in the next frame still leaves the box under.</summary>
    private static int UnderHudRow => FirstClearRow - 2;

    /// <summary>One step with the ship parked, keeping only the mines and the player's shots.</summary>
    private static void StepKeepingMines(RaiderGame g)
    {
        PressRig.Away(g);
        g.Step();
        g.Enemies.RemoveAll(e => e.Kind != EnemyKind.Mine);
        g.Bullets.RemoveAll(b => !b.Friendly);
    }

    private static List<Enemy> Riders(RaiderGame g, Card c) => [.. g.Enemies.Where(e => e.RideCard == c)];

    /// <summary>A mine's place measured from the quote box's top left pixel.</summary>
    private static (double X, double Y) OnBox(RaiderGame g, Card c, Enemy e)
    {
        var b = c.Shape.Quote!.Box;
        return (e.X - (g.Cards.CardLeft + b.X) * Px, e.Y - (c.Top + b.Y) * Px);
    }

    [Theory]
    [InlineData(QuoteState.Deleted)]
    [InlineData(QuoteState.Blocked)]
    [InlineData(QuoteState.Detached)]
    public void A_dead_quote_makes_two_unarmed_mines_at_the_thirds_of_its_box_on_its_middle_row_once_the_box_clears_the_HUD_and_none_before(QuoteState state)
    {
        var (g, c) = Scene(Quoting(state), UnderHudRow);
        StepKeepingMines(g);
        Assert.Empty(Riders(g, c));

        PutBoxOn(c, FirstClearRow);
        StepKeepingMines(g);
        var mines = Riders(g, c).OrderBy(e => e.X).ToList();
        Assert.Equal(Tuning.DeadQuoteMines, mines.Count);
        var b = c.Shape.Quote!.Box;
        int middleRow = b.H / 2;
        for (int i = 0; i < mines.Count; i++)
        {
            var (x, y) = OnBox(g, c, mines[i]);
            Assert.Equal(b.W * Px * (i + 1) / (double)(mines.Count + 1), x, PositionDecimals);
            Assert.InRange(y, middleRow * Px, (middleRow + 1) * Px);
            Assert.False(mines[i].Armed);
        }
    }

    [Fact]
    public void Dead_quote_mines_keep_their_place_on_the_box_for_60_frames_while_the_card_moves_down()
    {
        var (g, c) = Scene(Quoting(QuoteState.Deleted), FirstClearRow);
        StepKeepingMines(g);
        var mines = Riders(g, c);
        var places = mines.Select(e => OnBox(g, c, e)).ToList();
        int top = c.Top;
        for (int f = 0; f < RideFrames; f++)
        {
            StepKeepingMines(g);
            for (int i = 0; i < mines.Count; i++)
            {
                Assert.Contains(mines[i], g.Enemies);
                Assert.Equal(places[i], OnBox(g, c, mines[i]));
            }
        }
        Assert.True(c.Top > top, "the card moved down");
    }

    [Fact]
    public void A_post_makes_its_dead_quote_mines_once_even_across_a_resize_that_moves_its_box_back_under_the_HUD()
    {
        var (g, c) = Scene(Quoting(QuoteState.Blocked), FirstClearRow);
        StepKeepingMines(g);
        Assert.Equal(Tuning.DeadQuoteMines, Riders(g, c).Count);

        g.SetLayout(FieldLayout.Compute(NarrowCols, CardRig.ScrRows));
        foreach (var e in Riders(g, c))
            Assert.InRange(OnBox(g, c, e).X, 0, c.Shape.Quote!.Box.W * Px);
        g.Enemies.Clear();
        PutBoxOn(c, UnderHudRow);
        StepKeepingMines(g);
        PutBoxOn(c, FirstClearRow);
        for (int f = 0; f < RideFrames; f++) StepKeepingMines(g);
        Assert.Empty(Riders(g, c));
        Assert.True(c.Marks.MinesMade);
    }

    [Fact]
    public void A_dead_quote_makes_no_mines_in_a_boss_gate()
    {
        var (g, c) = Scene(Quoting(QuoteState.Deleted), UnderHudRow, gates: true);
        g.StartAtBoss(1);
        Assert.NotEqual(GateState.None, g.Gate);
        PutBoxOn(c, FirstClearRow);
        for (int f = 0; f < RideFrames; f++)
        {
            PressRig.Away(g);
            g.Step();
            Assert.NotEqual(GateState.None, g.Gate);
        }
        Assert.Empty(Riders(g, c));
        Assert.DoesNotContain(g.Enemies, e => e.Kind == EnemyKind.Mine);
    }

    [Fact]
    public void A_dead_quote_adds_only_the_mines_that_keep_the_field_within_the_most_alive()
    {
        var (g, c) = Scene(Quoting(QuoteState.Deleted), FirstClearRow);
        int already = Tuning.MaxMinesAlive - 1;
        for (int i = 0; i < already; i++) g.SpawnKind(EnemyKind.Mine, (i + 1) / (double)(already + 1));
        StepKeepingMines(g);
        Assert.Equal(Tuning.MaxMinesAlive, g.Enemies.Count(e => e.Kind == EnemyKind.Mine));
        Assert.Single(Riders(g, c));
    }

    [Theory]
    [InlineData(QuoteState.Shown, false)]
    [InlineData(QuoteState.Shown, true)]
    [InlineData(QuoteState.Other, false)]
    public void A_quote_that_is_shown_hidden_or_not_a_post_makes_no_mines(QuoteState state, bool optOut)
    {
        var (g, c) = Scene(Quoting(state, optOut), FirstClearRow);
        for (int f = 0; f < RideFrames; f++) StepKeepingMines(g);
        Assert.DoesNotContain(g.Enemies, e => e.Kind == EnemyKind.Mine);
        Assert.False(c.Shape.Quote!.Dead);
    }

    [Fact]
    public void A_shot_pings_off_a_dead_quote_mine_and_a_bomb_sets_it_off()
    {
        var (g, c) = Scene(Quoting(QuoteState.Deleted), FirstClearRow);
        StepKeepingMines(g);
        var mines = Riders(g, c);
        var target = mines[0];
        int pings = 0, kills = g.Kills;
        g.Input.Key(GameKey.Fire, true);
        for (int f = 0; f < FireFrames; f++)
        {
            PressRig.Force(g, nameof(RaiderGame.PX), target.X);
            g.Step();
            g.Enemies.RemoveAll(e => e.Kind != EnemyKind.Mine);
            if (target.Flash > 0) pings++;
        }
        g.Input.Key(GameKey.Fire, false);
        Assert.True(pings > 0, "shots reached the mine");
        Assert.Contains(target, g.Enemies);
        Assert.Equal(target.MaxHp, target.Hp);

        g.Input.Bomb();
        StepKeepingMines(g);
        Assert.Empty(Riders(g, c));
        Assert.Equal(kills + mines.Count, g.Kills);
    }
}
