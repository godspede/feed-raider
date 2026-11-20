namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The carrier (stage 1): the grunt that carries a live wave's text, larger and with a bright core. It flies and fires as a grunt
    /// does, takes more hits, scores more and drops more often.
    /// </summary>
    private sealed class CarrierBehaviour : GruntBehaviour
    {
        public override EnemyKind Kind => EnemyKind.Carrier;
        public override string Name => "CARRIER";
        public override Sprite Body => Art.Carrier;
        public override double Hp => Tuning.CarrierHp;
        public override int Score => Tuning.ScoreCarrier;
        public override double DropChance => Tuning.DropCarrier;
    }
}
