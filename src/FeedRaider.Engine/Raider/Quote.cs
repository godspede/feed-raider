namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>Makes the mines of each dead quote (deleted, blocked, removed by its author) whose box has just come out from under the
    /// HUD: <see cref="Tuning.DeadQuoteMines"/> on its notice row, at the thirds of its width, which ride the card down. A post makes them
    /// once. None are made during a boss fight, and none past <see cref="Tuning.MaxMinesAlive"/>; a box that comes out then makes none later.</summary>
    private void MakeDeadQuoteMines()
    {
        foreach (var card in Cards.Cards)
        {
            if (card.Shape.Quote is not { Dead: true } quote || card.Marks.MinesMade) continue;
            if ((card.Top + quote.Box.Y) * CardField.PixelsPerCell < CardPainter.HudPixelRows) continue;
            card.Marks.MinesMade = true;
            if (BossGates && Gate != GateState.None) continue;
            int room = Tuning.MaxMinesAlive - Enemies.Count(e => e.Kind == EnemyKind.Mine && e.Hp > 0);
            for (int third = 1; third <= Math.Min(Tuning.DeadQuoteMines, room); third++)
            {
                var (x, y) = RidePoint(card, quote, third);
                var mine = SpawnAdd(EnemyKind.Mine, x, y);
                mine.RideCard = card;
                mine.RideThird = third;
            }
        }
    }

    /// <summary>Where a riding mine sits on its card's quote box: the centre of the box's middle row, at a third of its width.</summary>
    private (double X, double Y) RidePoint(Card card, QuoteBox quote, int third)
    {
        const int px = CardField.PixelsPerCell;
        double x = (Cards.CardLeft + quote.Box.X) * px + quote.Box.W * px * third / (double)(Tuning.DeadQuoteMines + 1);
        double y = (card.Top + quote.Box.Y + quote.Box.H / 2) * px + px / 2.0;
        return (x, y);
    }

    /// <summary>Keeps a riding mine on its card: measured from the card's shape as it is now, so a resize moves the mine with the box.
    /// False when the card has no quote box left to ride.</summary>
    private bool Ride(Enemy e)
    {
        if (e.RideCard is not { Shape.Quote: { } quote } card) return false;
        (e.X, e.Y) = RidePoint(card, quote, e.RideThird);
        e.BaseX = e.X;
        return true;
    }

    /// <summary>A card gone off the bottom lets its mines go: they drift on as any mine does.</summary>
    private void DropRiders(Card card)
    {
        foreach (var e in Enemies)
            if (e.RideCard == card) e.RideCard = null;
    }
}
