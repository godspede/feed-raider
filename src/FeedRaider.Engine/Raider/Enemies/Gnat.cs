namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Gnat: what the Brood and the Nest let out. Its hit points do not ramp with the stage, and it cannot hurt for its first
    /// <see cref="GnatBehaviour.GraceFrames"/> frames (its wing tips are dim until then). It flies at the ship at
    /// <see cref="GnatBehaviour.Speed"/> pixels a frame, turning no more than <see cref="GnatBehaviour.TurnPerFrame"/> a frame, so a ship that
    /// keeps moving outruns it, and it burns out after <see cref="GnatBehaviour.BurnFrames"/> frames: no score, no kill, the field is only
    /// the lighter for it.
    /// </summary>
    private sealed class GnatBehaviour : EnemyBehaviour
    {
        private const int GraceFrames = 20, BurnFrames = 420, KillScore = 30;
        private const double Speed = 0.75, GnatHp = 1;
        private static readonly double TurnPerFrame = double.DegreesToRadians(4);
        /// <summary>The gnat's bile green, which the Brood and the Nest that let it out wear too.</summary>
        public const uint Bile = 0xc8e03c;
        private const double EdgeMargin = 1.5;
        private const int BurnPuffs = 3, PuffLife = 8, PuffLifeSpread = 6;
        private const double PuffSpeed = 0.2, PuffSpeedSpread = 0.5;

        /// <summary>The dark palette with its '.' left clear: the corners and the empty rows show the field, not black.</summary>
        public static Dictionary<char, uint> ClearDotPalette()
        {
            var pal = Art.With(('g', Bile));
            pal.Remove('.');
            return pal;
        }

        private static readonly Sprite Whole = Sprite.FromArt(
            ["g.g", ".y.", "g.g"],
            ClearDotPalette());

        /// <summary>What one gnat remembers: the way it is flying, in radians (0 is right, a quarter turn is down).</summary>
        private sealed class Flight
        {
            public double Heading = Math.PI / 2;
        }

        public override EnemyKind Kind => EnemyKind.Gnat;
        public override string Name => "GNAT";
        public override Sprite Body => Whole;
        public override uint Colour => Bile;
        public override double Hp => GnatHp;
        public override int Score => KillScore;
        public override double DropChance => 0;
        /// <summary>The stage's hit-point ramp is for the big ones: a gnat always falls to one shot.</summary>
        public override bool RampsHp => false;
        public override int Grace => GraceFrames;

        public override void Spawn(RaiderGame g, Enemy e) => e.Kit = new Flight();

        public override void Update(RaiderGame g, Enemy e)
        {
            if (e.Leaving) return;
            if (e.Age >= BurnFrames)
            {
                for (int i = 0; i < BurnPuffs; i++)
                {
                    double a = g.R() * Math.Tau, sp = PuffSpeed + g.R() * PuffSpeedSpread;
                    g.AddParticle(new Particle { X = e.X, Y = e.Y, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp, Life = PuffLife + (int)(g.R() * PuffLifeSpread), Colour = Bile, Kind = ParticleKind.Debris });
                }
                e.Leaving = true;
                e.Y = -e.Sprite.H - 1;   // out of the field: the game removes it without a kill or a score
                return;
            }
            var f = (Flight)e.Kit!;
            if (g.Alive)
            {
                double want = Math.Atan2(g.PY - e.Y, g.PX - e.X);
                double d = want - f.Heading;
                d -= Math.Tau * Math.Floor((d + Math.PI) / Math.Tau);   // into -pi..pi
                f.Heading += Math.Clamp(d, -TurnPerFrame, TurnPerFrame);
            }
            e.BaseX = e.X = Math.Clamp(e.X + Math.Cos(f.Heading) * Speed, EdgeMargin, g.PW - EdgeMargin);
            e.Y = Math.Min(e.Y + Math.Sin(f.Heading) * Speed, g.PH - EdgeMargin);
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Armed) return;
            int x0 = (int)Math.Round(e.X - Whole.W / 2.0), y0 = (int)Math.Round(e.Y - Whole.H / 2.0);
            foreach (var (dx, dy) in new[] { (0, 0), (Whole.W - 1, 0), (0, Whole.H - 1), (Whole.W - 1, Whole.H - 1) }) c.Set(x0 + dx, y0 + dy, Art.Husk);
        }
    }
}
