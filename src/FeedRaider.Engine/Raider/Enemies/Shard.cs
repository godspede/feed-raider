namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// A shard: one of the two a splitter bursts into. It flies straight on the line it was thrown, cannot touch the ship for its
    /// first frames, falls to one hit at every stage, and never drops a pickup (its splitter already had its chance). It never flies
    /// in a wave of its own.
    /// </summary>
    private sealed class ShardBehaviour : EnemyBehaviour
    {
        public override EnemyKind Kind => EnemyKind.Shard;
        public override string Name => "SHARD";
        public override Sprite Body => Art.Shard;
        public override uint Colour => Art.SplitterPink;
        public override double Hp => Tuning.ShardHp;
        public override int Score => Tuning.ScoreShard;
        public override double DropChance => 0;
        public override bool RampsHp => false;
        public override int Grace => Tuning.ShardGraceFrames;

        public override void Update(RaiderGame g, Enemy e)
        {
            e.BaseX += e.VX; e.Y += e.VY;
            e.X = e.BaseX;
        }
    }
}
