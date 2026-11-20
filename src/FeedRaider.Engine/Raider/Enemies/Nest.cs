namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Nest (a stage-6 mini-boss): a plum husk hung near the top that shoots nothing. Every <see cref="NestBehaviour.ReleaseGap"/>
    /// frames its hatch pulses for <see cref="NestBehaviour.PulseFrames"/>, then it lets <see cref="NestBehaviour.GnatsPerRelease"/> gnats
    /// out below it (never more than <see cref="NestBehaviour.GnatCap"/> of its gnats alive), and shot to pieces it bursts into
    /// <see cref="NestBehaviour.BurstGnats"/> more. It stays until it is killed, so the gnats are the whole fight.
    /// </summary>
    private sealed class NestBehaviour : EnemyBehaviour
    {
        private const int ReleaseGap = 100, PulseFrames = 24, FirstClock = 60, GnatsPerRelease = 2, GnatCap = 8, BurstGnats = 3, KillScore = 800;
        private const double NestHp = 17, EnterSpeed = 0.6, SwayRate = 0.03, SwayShare = 0.1, HoldShare = 0.15;
        private const double TopClearance = 8, ReleaseSpread = 1.5, ReleaseDrop = 6, BurstSpacing = 3;
        /// <summary>The hatch under the belly: its top left corner in the drawing, and its size.</summary>
        private const int HatchX = 5, HatchY = 6, HatchW = 3, HatchH = 2;
        /// <summary>The hatch blinks in steps of <see cref="HatchBlinkFrames"/>, and is solid white for its last <see cref="HatchWhiteFrames"/>, 1/<see cref="HatchWhiteShare"/> of the pulse.</summary>
        private const int HatchBlinkFrames = 3, HatchWhiteShare = 3, HatchWhiteFrames = PulseFrames / HatchWhiteShare;

        private static readonly Sprite Whole = Sprite.FromArt(
            [
                "...#######...",
                ".##ogooogo##.",
                "#oggoogooggo#",
                "#ogoogogoogo#",
                "#oogoogoogoo#",
                "#ogoo###oogo#",
                ".#oo#ooo#oo#.",
                "..###ooo###..",
                "....#####....",
            ],
            GnatBehaviour.ClearDotPalette());

        /// <summary>What one nest remembers: frames since the last release. It starts part-way round, so the first gnats are not far off.</summary>
        private sealed class Cycle
        {
            public int Clock = FirstClock;
        }

        public override EnemyKind Kind => EnemyKind.Nest;
        public override string Name => "NEST";
        public override Sprite Body => Whole;
        public override uint Colour => GnatBehaviour.Bile;
        public override double Hp => NestHp;
        public override int Score => KillScore;
        public override bool Speaks => true;
        public override string? Line(Enemy e, VoiceCue cue) => cue == VoiceCue.Arrive ? "Tell your friends." : null;

        public override void Spawn(RaiderGame g, Enemy e) => e.Kit = new Cycle();

        public override void Update(RaiderGame g, Enemy e)
        {
            var k = (Cycle)e.Kit!;
            double hold = Math.Max(HoldShare * g.PH, TopClearance);   // never so high that the top clips the HUD
            if (e.Y < hold) e.Y = Math.Min(hold, e.Y + EnterSpeed);
            double half = Whole.W / 2.0;
            e.X = Math.Clamp(e.BaseX + Math.Sin(e.Age * SwayRate + e.Phase) * g.PW * SwayShare, half, g.PW - half);
            if (e.Y < hold) return;

            k.Clock++;
            if (k.Clock == ReleaseGap - PulseFrames) g.Sound(SfxNames.Hatch, e.X);
            if (k.Clock < ReleaseGap) return;
            k.Clock = 0;
            int alive = g.Enemies.Count(x => x.Kind == EnemyKind.Gnat && ReferenceEquals(x.Parent, e))
                + g._born.Count(x => x.Kind == EnemyKind.Gnat && ReferenceEquals(x.Parent, e));
            for (int i = 0; i < GnatsPerRelease && alive + i < GnatCap; i++)
                g.SpawnAdd(EnemyKind.Gnat, e.X + (i == 0 ? -ReleaseSpread : ReleaseSpread), e.Y + ReleaseDrop, e);
            g.Sound(SfxNames.Sting, e.X);
        }

        public override void Down(RaiderGame g, Enemy e)
        {
            for (int i = 0; i < BurstGnats; i++) g.SpawnAdd(EnemyKind.Gnat, e.X + (i - (BurstGnats - 1) / 2.0) * BurstSpacing, e.Y, e);   // the burst ignores the cap
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Kit is not Cycle k || k.Clock < ReleaseGap - PulseFrames) return;
            int left = ReleaseGap - k.Clock;
            bool white = left <= HatchWhiteFrames;
            if (!white && left / HatchBlinkFrames % 2 != 0) return;
            int x0 = (int)Math.Round(e.X - Whole.W / 2.0), y0 = (int)Math.Round(e.Y - Whole.H / 2.0);
            c.Rect(x0 + HatchX, y0 + HatchY, HatchW, HatchH, white ? Palette.Tell : GnatBehaviour.Bile);
        }
    }
}
