namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The diver (stage 2): it weaves down to its hold row, sidestepping a lined-up shot, then locks on where the ship is, shivers
    /// through its wind-up and dashes straight at that spot. While it dashes its plates turn most of a shot's damage.
    /// </summary>
    private sealed class DiverBehaviour : EnemyBehaviour
    {
        // how many cells each way it shivers through its wind-up
        private const double Shiver = 0.6;

        public override EnemyKind Kind => EnemyKind.Diver;
        public override string Name => "DIVER";
        public override Sprite Body => Art.Diver;
        public override uint Colour => Art.DiverLime;
        public override double Hp => Tuning.DiverHp;
        public override int Score => Tuning.ScoreDiver;

        public override void Update(RaiderGame g, Enemy e)
        {
            if (e.Windup < 0 && !e.Dashing) g.Evade(e);
            e.X = e.BaseX + (e.Dashing || e.Windup >= 0 ? 0 : Math.Sin(e.Age * Tuning.DiverWeaveRate + e.Phase) * Tuning.DiverWeaveAmp);
            if (e.Dashing) { e.BaseX += e.VX; e.Y += e.VY; e.X = e.BaseX; return; }
            if (e.Windup < 0)
            {
                if (e.Y < g.PH * Tuning.DiverHoldRow) { e.Y += Tuning.DiverFall; return; }
                g.LockAim(e);
                e.Windup = Tuning.DiverWindupFrames;
                return;
            }
            e.X = e.BaseX + (e.Age % ShiverPeriod < ShiverPeriod / 2 ? -Shiver : Shiver);   // the shiver of the wind-up
            if (--e.Windup > 0) return;
            double dx = e.AimX - e.BaseX, dy = e.AimY - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            e.VX = dx / d * Tuning.DiverDashSpeed; e.VY = dy / d * Tuning.DiverDashSpeed;
            e.Dashing = true;
        }
    }
}
