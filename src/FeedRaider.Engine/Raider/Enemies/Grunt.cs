namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The grunt (stage 1): a small dark husk that falls at its own speed, sways when its wave gave it a sway and wobbles a little
    /// when not, sidesteps a lined-up shot, and fires one bolt at the ship while it is in the upper part of the field. The carrier
    /// flies the same way.
    /// </summary>
    private class GruntBehaviour : EnemyBehaviour
    {
        // radians per frame of its sway and of the small wobble of a straight one; the share of its wave's sway it uses; the wobble's cells
        private const double SwayRate = 0.06, WobbleRate = 0.05, SwayShare = 0.5, WobbleCells = 1.5;
        // it fires below this row and above this share of the field's height; its bolt leaves this far below its centre
        private const double FireTopRow = 6, FireBottomShare = 0.6, BoltOffset = 2;

        public override EnemyKind Kind => EnemyKind.Grunt;
        public override string Name => "GRUNT";
        public override Sprite Body => Art.Grunt;
        public override uint Colour => Art.DarkViolet;
        public override double Hp => Tuning.GruntHp;
        public override int Score => Tuning.ScoreGrunt;

        public override void Update(RaiderGame g, Enemy e)
        {
            g.Evade(e);
            e.Y += e.VY;
            e.X = e.BaseX + (e.Amp > 0 ? Math.Sin(e.Age * SwayRate + e.Phase) * e.Amp * g.PW * SwayShare : Math.Sin(e.Age * WobbleRate + e.Phase) * WobbleCells);
            if (e.Y > FireTopRow && e.Y < g.PH * FireBottomShare && --e.FireCd <= 0)
            {
                double gap = Ramp.FireGapMul(g.Stage);
                int baseGap = e.Live ? Tuning.EnemyFireGapLive : Tuning.EnemyFireGapIdle;
                e.FireCd = (int)((baseGap + g.R() * Tuning.EnemyFireGapJitter) * gap);
                if (g.Alive) g.EnemyBolt(e.X, e.Y + BoltOffset, g.PX, g.PY, 0, Tuning.EnemyBoltSpeed * Ramp.BoltSpeedMul(g.Stage));
            }
        }
    }
}
