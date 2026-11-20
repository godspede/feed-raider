namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Chorus (stage 8, a mini-boss): a row of small mouths that all fire at once. Neighbours are twelve pixels apart, so the
    /// bolts of two voices leave a gap too narrow for the ship; the player makes a gap by shooting a voice, or goes round the end.
    /// It holds near the top, drifts toward the ship's column, and every 120 frames charges for 36 (mouths open, a dotted column below
    /// each, white for the last twelve) and then drops three slow bolts from every living voice on one frame. It leaves after three.
    /// </summary>
    private sealed class ChorusBehaviour : EnemyBehaviour
    {
        private const int Gap = 12, Period = 120, ChargeFrames = 36, WhiteFrames = 12, Volleys = 3, Lane = 4, VoiceW = 7, VoiceH = 6;
        /// <summary>How the field is divided into voices: one voice for every 16 pixels of field, never fewer than three nor more than six.</summary>
        private const int VoiceSpacing = 16, MinVoices = 3, MaxVoices = 6;
        private const double EnterSpeed = 0.6, LeaveSpeed = 0.8, DriftSpeed = 0.2, BoltSpeed = 0.8, HoldShare = 0.12, MinHoldRow = 8;

        private const uint Sea = 0x4fbf8f, Deep = 0x2a6a4f;

        // 7 by 6: gold between two crimson eyes over a closed mouth, and the open one it sings with while it charges
        private static readonly string[] ClosedRows =
        [
            ".#####.",
            "#HhhhH#",
            "#hryrh#",
            "#HhhhH#",
            "#Hh#hH#",
            ".#####.",
        ];

        private static readonly string[] OpenRows =
        [
            ".#####.",
            "#HhhhH#",
            "#hryrh#",
            "#H###H#",
            "#H#y#H#",
            ".#####.",
        ];

        private static readonly Dictionary<char, uint> Pal = Art.With(('h', Sea), ('H', Deep));
        private static readonly Sprite ClosedArt = Sprite.FromArt(ClosedRows, Pal);
        private static readonly Sprite OpenArt = Sprite.FromArt(OpenRows, Pal);

        /// <summary>The frames since it took its row, and the volleys it has sung.</summary>
        private sealed class Song
        {
            public int Clock, Sung;
        }

        public override EnemyKind Kind => EnemyKind.Chorus;
        public override string Name => "CHORUS";
        // three voices wide, drawn as nothing: the voices are parts; Spawn gives the owner the silhouette of the row it really has
        public override Sprite Body => new(Width(MinVoices), VoiceH);
        public override uint Colour => Sea;
        public override double Hp => 3;
        public override int Score => 500;
        public override bool Speaks => true;
        public override string? Line(Enemy e, VoiceCue cue) => cue == VoiceCue.Arrive ? "All together now." : null;

        private static int Count(RaiderGame g) => Math.Clamp(g.PW / VoiceSpacing, MinVoices, MaxVoices);
        private static int Width(int voices) => (voices - 1) * Gap + VoiceW;

        public override void Spawn(RaiderGame g, Enemy e)
        {
            int n = Count(g);
            e.Sprite = new Sprite(Width(n), VoiceH);
            var voices = new Part[n];
            for (int i = 0; i < n; i++)
                voices[i] = new Part
                {
                    Name = $"voice-{i}",
                    OX = (i - (n - 1) / 2.0) * Gap,
                    OY = 0,
                    HW = VoiceW / 2,
                    HH = VoiceH / 2,
                    Closed = ClosedArt,
                    State = PartState.Open,
                    Vital = true,
                    Hp = e.Hp,
                    MaxHp = e.Hp,
                    Score = 150,   // the kill scores the chorus's own 500 on top
                };
            g.SetParts(e, voices);
            e.Kit = new Song();
        }

        public override void Update(RaiderGame g, Enemy e)
        {
            var song = (Song)(e.Kit ??= new Song());
            var parts = e.Parts!;
            double span = (parts.Count - 1) * Gap / 2.0 + VoiceW / 2.0, lo = span, hi = g.PW - span;
            if (lo > hi) lo = hi = g.PW / 2.0;
            e.X = Math.Clamp(e.X, lo, hi);
            if (e.Leaving)
            {
                e.Y -= LeaveSpeed;
                return;
            }
            double hold = Math.Round(Math.Max(HoldShare * g.PH, MinHoldRow));
            if (e.Y < hold)
            {
                e.Y = Math.Min(hold, e.Y + EnterSpeed);
                return;
            }
            e.Y = hold;
            if (g.Alive) Drift(g, e, lo, hi);
            e.BaseX = e.X;

            song.Clock++;
            int t = song.Clock % Period;
            bool charging = t >= Period - ChargeFrames;
            foreach (var p in parts) p.OpenArt = charging && p.State != PartState.Dead ? OpenArt : null;
            if (t == Period - ChargeFrames) g.Sound(SfxNames.Charge, e.X);
            if (t != 0) return;
            foreach (var p in parts)
            {
                if (p.State == PartState.Dead) continue;
                double x = e.X + p.OX;
                for (int lane = -1; lane <= 1; lane++)
                    g.Bullets.Add(new Bullet { X = x + lane * Lane, Y = e.Y + VoiceH / 2.0 + 1, VX = 0, VY = BoltSpeed });
            }
            if (++song.Sung >= Volleys) e.Leaving = true;
        }

        /// <summary>The whole row slides at a fifth of a pixel a frame so that its nearest living voice is over the ship.</summary>
        private static void Drift(RaiderGame g, Enemy e, double lo, double hi)
        {
            double best = double.MaxValue, target = e.X;
            foreach (var p in e.Parts!)
            {
                if (p.State == PartState.Dead) continue;
                double d = Math.Abs(e.X + p.OX - g.PX);
                if (d >= best) continue;
                best = d;
                target = g.PX - p.OX;
            }
            e.X = Math.Clamp(e.X + Math.Clamp(target - e.X, -DriftSpeed, DriftSpeed), lo, hi);
        }

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            p.OpenArt = null;
            g.Sound(SfxNames.Part, e.X + p.OX);
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Kit is not Song song) return;
            int t = song.Clock % Period;
            if (e.Leaving || song.Clock == 0 || t < Period - ChargeFrames) return;
            uint colour = t >= Period - WhiteFrames ? Palette.Tell : Colour;
            int top = (int)Math.Round(e.Y + VoiceH / 2.0) + 2;
            foreach (var p in e.Parts!)
            {
                if (p.State == PartState.Dead) continue;
                for (int lane = -1; lane <= 1; lane++)
                {
                    int x = (int)Math.Round(e.X + p.OX + lane * Lane);
                    for (int y = top; y < c.Height; y += 2) c.Set(x, y, colour);
                }
            }
        }
    }
}
