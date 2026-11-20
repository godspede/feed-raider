namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Lurk (stage 7): it hangs above the field as four dim dots at the corners of its box, and nothing touches it there, a shot
    /// or a bomb or the ship. <see cref="LurkBehaviour.HiddenFrames"/> frames on it fills in over <see cref="LurkBehaviour.SurfaceFrames"/>
    /// (an outline, then a dark body, then the full art with white eyes, a step every <see cref="LurkBehaviour.StepFrames"/>), and it can be
    /// killed from the first of them; at the end it fans bolts at where the ship stood when it began, then shows solid for
    /// <see cref="LurkBehaviour.SolidFrames"/> frames and hides again. After its last strike (<see cref="LurkBehaviour.Strikes"/>) it goes up
    /// and away. It comes as a pair, the second <see cref="LurkBehaviour.PartnerLag"/> frames behind the first, so the field is never quiet
    /// while one hides.
    /// </summary>
    private sealed class LurkBehaviour : EnemyBehaviour
    {
        private const int HiddenFrames = 90, SurfaceFrames = 36, StepFrames = 12, SolidFrames = 30, Strikes = 3, PartnerLag = 60;
        private const double EnterSpeed = 0.6, DriftSpeed = 0.6, LeaveSpeed = 0.8, HoldRow = 0.2, Near = 20, BoltSpeed = 1.25, Spread = 0.25, PairApart = 18;
        private const double LurkHp = 4;
        private const int KillScore = 650;
        /// <summary>The fan is one bolt at the aim and this many more on either side of it, <see cref="Spread"/> radians apart.</summary>
        private const int SideBolts = 1;
        private const int EyeRow = 3, LeftEye = 2, RightEye = 6;
        private const uint Smoke = 0x8a94a8, Dark = 0x4f5360;
        /// <summary>How far below its centre the bolts leave a lurk.</summary>
        private const double MuzzleDrop = 2;

        private static readonly string[] Rows =
        [
            "..#####..",
            ".#KKKKK#.",
            "#KkKKKkK#",
            "#KrKKKrK#",
            "#KkkkkkK#",
            "#KkKkKkK#",
            "#K#K#K#K#",
            "#.#.#.#.#",
        ];

        private static readonly Dictionary<char, uint> SpritePalette = Art.With(('k', Smoke), ('K', Dark));
        private static readonly Sprite Whole = Sprite.FromArt(Rows, SpritePalette);
        private static readonly Sprite Dots = Corners();
        private static readonly Sprite Outline = Filled("#");
        private static readonly Sprite Dressed = Filled("#K");
        private static readonly Sprite Seen = WithEyes(Whole, Palette.Tell);

        private enum Phase { Entering, Hidden, Surfacing, Solid }

        /// <summary>What one lurk remembers: its strikes so far and the idle its pair-mate waits out before its first surfacing.</summary>
        private sealed class Hunt
        {
            public int Strikes, Lag;
        }

        public override EnemyKind Kind => EnemyKind.Lurk;
        public override string Name => "LURK";
        public override Sprite Body => Whole;
        public override uint Colour => Dark;
        public override double Hp => LurkHp;
        public override int Score => KillScore;

        private static Sprite Corners()
        {
            var s = new Sprite(Whole.W, Whole.H);
            foreach (var (x, y) in new[] { (0, 0), (Whole.W - 1, 0), (0, Whole.H - 1), (Whole.W - 1, Whole.H - 1) }) s.Px[y * Whole.W + x] = Dark | Sprite.Opaque;
            return s;
        }

        private static Sprite Filled(string keep)
        {
            var s = new Sprite(Whole.W, Whole.H);
            for (int i = 0; i < s.Px.Length; i++) if (keep.Contains(Rows[i / Whole.W][i % Whole.W])) s.Px[i] = Whole.Px[i];
            return s;
        }

        private static Sprite WithEyes(Sprite from, uint colour)
        {
            var s = new Sprite(from.W, from.H);
            Array.Copy(from.Px, s.Px, s.Px.Length);
            s.Px[EyeRow * from.W + LeftEye] = colour | Sprite.Opaque;
            s.Px[EyeRow * from.W + RightEye] = colour | Sprite.Opaque;
            return s;
        }

        public override void Spawn(RaiderGame g, Enemy e)
        {
            bool second = e.Parent is not null;
            e.Kit = new Hunt { Lag = second ? PartnerLag : 0 };
            e.Mode = (int)Phase.Entering;
            e.Untouchable = true;
            if (second) return;
            double half = Whole.W / 2.0, x = e.X, m = g.PW - x;
            if (Math.Abs(m - x) < PairApart) m = x < g.PW / 2.0 ? x + PairApart : x - PairApart;
            g.SpawnAdd(EnemyKind.Lurk, Math.Clamp(m, half, g.PW - half), e.Y, e);
        }

        public override void Update(RaiderGame g, Enemy e)
        {
            var h = (Hunt)e.Kit!;
            if (e.Leaving)
            {
                e.Untouchable = true;
                e.Sprite = Dots;
                e.Y -= LeaveSpeed;
                return;
            }
            double hold = HoldRow * g.PH, half = Whole.W / 2.0;
            switch ((Phase)e.Mode)
            {
                case Phase.Entering:
                    e.Y += EnterSpeed;
                    e.X = e.BaseX;
                    if (e.Y >= hold) { e.Y = hold; EnterPhase(e, Phase.Hidden); }
                    break;
                case Phase.Hidden:
                    e.Untouchable = true;
                    e.Sprite = Dots;
                    double away = g.PX - e.BaseX;
                    if (Math.Abs(away) > Near) e.BaseX += Math.Sign(away) * Math.Min(DriftSpeed, Math.Abs(away) - Near);
                    e.BaseX = Math.Clamp(e.BaseX, half, g.PW - half);
                    e.X = e.BaseX;
                    e.Y = hold;
                    if (e.ModeAge >= HiddenFrames + h.Lag)
                    {
                        h.Lag = 0;
                        EnterPhase(e, Phase.Surfacing);
                        e.Untouchable = false;
                        e.AimX = g.PX; e.AimY = g.PY;
                        g.Sound(SfxNames.Open, e.X);
                    }
                    break;
                case Phase.Surfacing:
                    e.X = e.BaseX;
                    if (e.ModeAge >= SurfaceFrames)
                    {
                        Fan(g, e);
                        h.Strikes++;
                        EnterPhase(e, Phase.Solid);
                    }
                    break;
                default:
                    e.X = e.BaseX;
                    if (e.ModeAge >= SolidFrames)
                    {
                        if (h.Strikes >= Strikes) { e.Leaving = true; e.Untouchable = true; }
                        else { EnterPhase(e, Phase.Hidden); e.Untouchable = true; }
                    }
                    break;
            }
            e.Sprite = SpriteFor(e);
        }

        private static void EnterPhase(Enemy e, Phase to)
        {
            e.Mode = (int)to;
            e.ModeAge = 0;
        }

        private static Sprite SpriteFor(Enemy e) => (Phase)e.Mode switch
        {
            Phase.Surfacing => (Math.Max(0, e.ModeAge - 1) / StepFrames) switch { 0 => Outline, 1 => Dressed, _ => Seen },
            Phase.Solid => Whole,
            _ => Dots,
        };

        private static void Fan(RaiderGame g, Enemy e)
        {
            if (!g.Alive) return;
            double y = e.Y + MuzzleDrop, a = Math.Atan2(e.AimY - y, e.AimX - e.X), v = BoltSpeed * Ramp.BoltSpeedMul(g.Stage);
            for (int i = -SideBolts; i <= SideBolts; i++)
                g.Bullets.Add(new Bullet { X = e.X, Y = y, VX = Math.Cos(a + i * Spread) * v, VY = Math.Sin(a + i * Spread) * v, Aimed = true });
        }
    }
}
