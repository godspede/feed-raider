namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>The field row from which a gunship fires, and below which its muzzle shows the tell.</summary>
    private const double GunshipFireRow = 16;

    /// <summary>True in the frames just before a gunship's volley, while its muzzle shows the tell.</summary>
    public static bool GunshipVolleyDue(Enemy e) =>
        e.Kind == EnemyKind.Gunship && !e.Leaving && e.Y >= GunshipFireRow && e.FireCd <= Tuning.GunshipTellFrames;

    /// <summary>
    /// The gunship, in two forms the enemy's <see cref="Enemy.Lesser"/> flag tells apart. The boss gunship ends stage 1 (and any
    /// lap whose boss is the gunship): it descends to its hold row, sways, fires its volleys and stays until it is killed. Its manner
    /// of speech is the plain one: the oldest post first, each read out before the next, at the common pace. A lesser gunship flies the
    /// same way with a flat hit-point pool and a short stay; it says one rude post a visit (a waiting one on arriving, or a heard one
    /// again) and skips every other cue, and scores and drops as a lesser one.
    /// </summary>
    private sealed class GunshipBehaviour : EnemyBehaviour
    {
        // the field row where it stops descending, and the radians per frame of its sideways sway
        private const double HoldRow = 22, SwayRate = 0.035;
        private const double LeaveSpeed = 0.8, BossBoltMul = 1.1, Fall = 0.5;
        private const double SpreadStep = 0.28, FanStep = 0.22, FanGap = 4;

        public override EnemyKind Kind => EnemyKind.Gunship;
        public override string Name => "GUNSHIP";
        public override Sprite Body => Art.Gunship;
        public override uint Colour => Art.DarkViolet;
        public override double Hp => Tuning.GunshipHp;
        public override int Score => Tuning.ScoreGunship;
        public override double DropChance => 1;
        public override bool Boss => true;
        public override double? EntryFall => Fall;
        public override int ScoreFor(Enemy e) => e.Lesser ? Tuning.ScoreLesser : Score;
        public override double DropChanceFor(Enemy e) => e.Lesser ? Tuning.LesserDropChance : DropChance;

        public override void Update(RaiderGame g, Enemy e)
        {
            if (!e.Leaving && !ReferenceEquals(e, g.Boss) && e.Age > (e.Lesser ? Tuning.LesserStayFrames : Tuning.GunshipStayFrames)) e.Leaving = true;
            if (e.Leaving) e.Y -= LeaveSpeed;
            else if (e.Y < HoldRow) e.Y += e.VY;
            e.X = e.BaseX + Math.Sin(e.Age * SwayRate + e.Phase) * e.Amp * g.PW;
            e.X = Math.Clamp(e.X, e.Sprite.W / 2.0, g.PW - e.Sprite.W / 2.0);
            if (!e.Leaving && e.Y >= GunshipFireRow && --e.FireCd <= 0) Volley(g, e);
        }

        /// <summary>Every other volley is three bolts aimed at the ship; the rest are a fan of five straight down.</summary>
        private static void Volley(RaiderGame g, Enemy e)
        {
            e.FireCd = Tuning.GunshipVolleyFrames;
            double y = e.Y + e.Sprite.H / 2.0;
            if ((e.Age / Tuning.GunshipVolleyFrames) % 2 == 0)
                for (int i = -1; i <= 1; i++) g.EnemyBolt(e.X, y, g.PX, g.PY, i * SpreadStep, Tuning.EnemyBoltSpeed * BossBoltMul * Ramp.BoltSpeedMul(g.Stage));
            else
                for (int i = -2; i <= 2; i++) g.Bullets.Add(new Bullet { X = e.X + i * FanGap, Y = y, VX = i * FanStep, VY = Tuning.EnemyBoltSpeed * Ramp.BoltSpeedMul(g.Stage) });
        }

        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue) =>
            !e.Lesser ? base.TurnFor(g, e, cue) : cue == VoiceCue.Arrive ? Turn.Wait : Turn.Skip;

        public override string? Line(Enemy e, VoiceCue cue) => e.Lesser ? null : cue switch
        {
            VoiceCue.Arrive => "Nobody asked. I answered.",
            VoiceCue.Leaving => "I have other feeds to ruin.",
            VoiceCue.Dying => "Ratioed.",
            _ => null,
        };
    }
}
