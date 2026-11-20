namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The splitter (stage 3): it falls slowly and sways across its wave's sway, sidesteps a lined-up shot, and cracks when it is half
    /// spent. Killed by a shot it bursts into two shards (<see cref="SpawnShards"/>); a bomb ends it whole.
    /// </summary>
    private sealed class SplitterBehaviour : EnemyBehaviour
    {
        // radians per frame of its sway
        private const double SwayRate = 0.05;

        public override EnemyKind Kind => EnemyKind.Splitter;
        public override string Name => "SPLITTER";
        public override Sprite Body => Art.Splitter;
        public override uint Colour => Art.SplitterPink;
        public override double Hp => Tuning.SplitterHp;
        public override int Score => Tuning.ScoreSplitter;

        public override void Update(RaiderGame g, Enemy e)
        {
            g.Evade(e);
            e.Y += Tuning.SplitterFall;
            e.X = e.BaseX + Math.Sin(e.Age * SwayRate + e.Phase) * e.Amp * g.PW;
        }
    }
}
