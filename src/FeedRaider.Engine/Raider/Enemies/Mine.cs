namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The mine (from halfway through stage 1): it drifts down and bobs from side to side, and cannot touch the ship until it has
    /// armed. A shot pings off it, so only a bomb or the ship itself ends it; its one hit point is the same at every stage. A dead
    /// quote's mine rides its card instead, still and level with the notice, until the card has gone (<see cref="Ride"/>).
    /// </summary>
    private sealed class MineBehaviour : EnemyBehaviour
    {
        // radians per frame of its bob, and how many cells it bobs each way
        private const double BobRate = 0.04, BobCells = 3;

        public override EnemyKind Kind => EnemyKind.Mine;
        public override string Name => "MINE";
        public override Sprite Body => Art.Mine;
        public override uint Colour => Art.MineLit;
        public override double Hp => 1;
        public override int Score => Tuning.ScoreMine;
        public override bool RampsHp => false;
        public override int Grace => Tuning.MineArmFrames;

        public override void Update(RaiderGame g, Enemy e)
        {
            if (g.Ride(e)) return;
            e.Y += Tuning.MineDrift;
            e.X = e.BaseX + Math.Sin(e.Age * BobRate + e.Phase) * BobCells;
        }
    }
}
