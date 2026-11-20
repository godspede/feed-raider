namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Lobber (stage 5): a small mortar that hangs at the top and throws three shells, one at a time. A shell is thrown at where the
    /// ship stands that frame and comes down on that spot 50 frames later, whatever the ship does; the spot is marked by a rust ring that
    /// fills and turns white for its last 16 frames. The blast is a disc of radius 9 and is lethal for exactly four frames. After the
    /// third it leaves. Two lobbers (from stage 7) keep their throws 55 frames apart, so there is never more than one shell in the air.
    /// </summary>
    private sealed class LobberBehaviour : EnemyBehaviour
    {
        /// <summary>Frames a shell is in the air, frames the blast kills, the shells one lobber throws, the gaps between throws, and the frames its ring shows white before it lands.</summary>
        private const int Flight = 50, Window = 4, Throws = 3, FirstThrow = 40, Cadence = 110, PairGap = 55, WhiteFrames = 16, WhiteFrom = Flight - WhiteFrames;
        private const double Radius = 9, EnterSpeed = 0.6, LeaveSpeed = 0.8, HoldRow = 0.12, HoldMin = 8, ArcHeight = 22;
        private const uint Rust = 0xd9773c, Blast = 0xffe3b0, Shade = 0x7a3f1c;
        private const int ShellSize = 2;
        private const double LobVolume = 0.5, BlastVolume = 0.7;
        private const double RingFrom = 2, RingGrow = 3, ParticleSpeed = 1.4, ParticleJitter = 0.4, ShadeAlpha = 0.3;
        private const int RingFrames = 10, ParticleCount = 8, ParticleLife = 12;

        private static readonly string[] Rows =
        [
            "#.......#",
            "##.###.##",
            "#U#uuu#U#",
            "#UuuyuuU#",
            "#UuuuuuU#",
            "#UUuuuUU#",
            ".#UUUUU#.",
            "..#####..",
        ];

        private static readonly Sprite Whole = Sprite.FromArt(Rows, Art.With(('u', Rust), ('U', Shade)));

        /// <summary>What one lobber remembers: when it throws next, how many it has thrown, and the shell in the air (where it lands and its age).</summary>
        private sealed class Mortar
        {
            public long Next = -1;
            public int Thrown, ShellAge;
            public bool Shell;
            public double TX, TY;
        }

        public override EnemyKind Kind => EnemyKind.Lobber;
        public override string Name => "LOBBER";
        public override Sprite Body => Whole;
        public override uint Colour => Rust;
        public override double Hp => 4;
        public override int Score => 500;

        public override void Spawn(RaiderGame g, Enemy e) => e.Kit = new Mortar();

        public override void Update(RaiderGame g, Enemy e)
        {
            var m = (Mortar)e.Kit!;
            double half = e.Sprite.W / 2.0;
            e.X = Math.Clamp(e.BaseX, half, g.PW - half);
            if (e.Leaving) { e.Y -= LeaveSpeed; return; }

            double hold = Math.Max(HoldRow * g.PH, HoldMin);   // never so high that the top clips the HUD
            if (e.Y < hold)
            {
                e.Y = Math.Min(hold, e.Y + EnterSpeed);
                if (e.Y < hold) return;
            }
            if (m.Next < 0) m.Next = FirstThrowFor(g, e);

            if (m.Shell)
            {
                m.ShellAge++;
                if (m.ShellAge == Flight) Land(g, m);
                if (m.ShellAge >= Flight + Window)
                {
                    m.Shell = false;
                    if (m.Thrown >= Throws) e.Leaving = true;
                }
            }
            else if (m.Thrown < Throws && g.Frame >= m.Next && !g.Enemies.Any(o => !ReferenceEquals(o, e) && o.Kit is Mortar { Shell: true }))
            {
                g.LockAim(e);
                m.TX = e.AimX; m.TY = e.AimY;
                m.ShellAge = 0; m.Shell = true; m.Thrown++;
                m.Next = g.Frame + Cadence;
                g.Sound(SfxNames.Lob, e.X, LobVolume);
            }
        }

        /// <summary>The frame of a lobber's first throw: 40 frames after it settles, or 55 after another's if that would crowd it.</summary>
        private static long FirstThrowFor(RaiderGame g, Enemy e)
        {
            long at = g.Frame + FirstThrow;
            foreach (var o in g.Enemies)
                if (!ReferenceEquals(o, e) && o.Kit is Mortar { Next: >= 0 } other && Math.Abs(at - other.Next) < PairGap) at = other.Next + PairGap;
            return at;
        }

        private static void Land(RaiderGame g, Mortar m)
        {
            g.Sound(SfxNames.Blast, m.TX, BlastVolume);
            g.AddRing(m.TX, m.TY, RingFrom, Radius + RingGrow, Blast, RingFrames);
            for (int i = 0; i < ParticleCount; i++)
            {
                double a = i * Math.Tau / ParticleCount + g.R() * ParticleJitter;
                g.AddParticle(new Particle { X = m.TX, Y = m.TY, VX = Math.Cos(a) * ParticleSpeed, VY = Math.Sin(a) * ParticleSpeed, Life = ParticleLife, Colour = Rust });
            }
        }

        /// <summary>The blast is lethal for its four frames, inside its radius, and at no other time.</summary>
        public override bool Touches(RaiderGame g, Enemy e)
        {
            if (e.Leaving) return false;
            var m = (Mortar)e.Kit!;
            return m.Shell && m.ShellAge >= Flight && m.ShellAge < Flight + Window
                && Math.Sqrt((g.PX - m.TX) * (g.PX - m.TX) + (g.PY - m.TY) * (g.PY - m.TY)) <= Radius;
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            var m = (Mortar)e.Kit!;
            if (!m.Shell) return;
            var s = c.Surface;
            int cx = (int)Math.Round(m.TX), cy = (int)Math.Round(m.TY);
            if (m.ShellAge >= Flight)
            {
                s.Disc(cx, cy, (int)Radius, Blast);
                return;
            }
            double r = Radius * m.ShellAge / Flight;
            for (int y = -(int)Radius; y <= Radius; y++)
                for (int x = -(int)Radius; x <= Radius; x++)
                    if (x * x + y * y <= r * r) s.Blend(cx + x, cy + y, Rust, ShadeAlpha);
            s.Ring(cx, cy, Radius, m.ShellAge >= WhiteFrom ? Palette.Tell : Rust, 1.0);

            double t = (double)m.ShellAge / Flight;
            int sx = (int)Math.Round(e.X + (m.TX - e.X) * t);
            int sy = (int)Math.Round(e.Y + (m.TY - e.Y) * t - Math.Sin(t * Math.PI) * ArcHeight);
            s.Rect(sx, sy, ShellSize, ShellSize, Palette.Tell);
        }
    }
}
