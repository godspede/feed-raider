namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The turret (stage 4): it descends to its hold row and sways a little there, settles for a beat, then winds up on where the ship
    /// is and fires an aimed burst at that spot. After its last volley it climbs away.
    /// </summary>
    private sealed class TurretBehaviour : EnemyBehaviour
    {
        // radians per frame of its sway, and how many cells it sways each way
        private const double SwayRate = 0.03, SwayCells = 4;
        private const double LeaveSpeed = 0.7, DescendSpeed = 0.6;
        private const int SettleFrames = 12;   // the beat after it settles, before its first wind-up

        public override EnemyKind Kind => EnemyKind.Turret;
        public override string Name => "TURRET";
        public override Sprite Body => Art.Turret;
        public override uint Colour => Art.TurretGreen;
        public override double Hp => Tuning.TurretHp;
        public override int Score => Tuning.ScoreTurret;
        public override double DropChance => Tuning.DropCarrier;

        public override void Update(RaiderGame g, Enemy e)
        {
            e.X = Math.Clamp(e.BaseX + Math.Sin(e.Age * SwayRate + e.Phase) * SwayCells, e.Sprite.W / 2.0, g.PW - e.Sprite.W / 2.0);
            if (e.Leaving) { e.Y -= LeaveSpeed; return; }
            if (e.Y < g.PH * Tuning.TurretHoldRow) { e.Y += DescendSpeed; return; }
            if (e.Burst > 0)
            {
                if (--e.FireCd > 0) return;
                Fire(g, e);
                e.FireCd = Tuning.TurretBurstGap;
                if (--e.Burst == 0) { e.FireCd = Tuning.TurretRestFrames; if (++e.Volleys >= Tuning.TurretVolleys) e.Leaving = true; }
                return;
            }
            if (e.Windup > 0)
            {
                if (--e.Windup == 0) { e.Burst = Tuning.TurretBurst; e.FireCd = 1; }
                return;
            }
            if (e.Windup < 0) { e.Windup = 0; e.FireCd = SettleFrames; }   // just settled: a short beat, then the first wind-up
            if (--e.FireCd <= 0) { g.LockAim(e); e.Windup = Tuning.TurretWindupFrames; }
        }

        private static void Fire(RaiderGame g, Enemy e)
        {
            double y = e.Y + e.Sprite.H / 2.0;
            double a = Math.Atan2(e.AimY - y, e.AimX - e.X), v = Tuning.TurretBoltSpeed * Ramp.BoltSpeedMul(g.Stage);
            g.Bullets.Add(new Bullet { X = e.X, Y = y, VX = Math.Cos(a) * v, VY = Math.Sin(a) * v, Aimed = true });
        }
    }
}
