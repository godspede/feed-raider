namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Yoke (stage 7 mini-boss): two nodes thirty-five pixels wide joined by a bar that kills on touch, falling down the field
    /// and drifting toward the ship. The bar shows first as a dim dotted line, goes white in the last third of sixty harmless
    /// frames, and then burns solid. Shooting either node puts the bar out for good and sends the other drifting off to the side;
    /// shooting both kills the yoke. A yoke left alone falls out of the bottom of the field without a kill.
    /// </summary>
    private sealed class YokeBehaviour : EnemyBehaviour
    {
        private const double FallSpeed = 0.45, DriftSpeed = 0.2, SideSpeed = 0.6, Reach = 14, Thick = 2;
        /// <summary>The rows of the bar: the dotted line runs along <see cref="BarMiddle"/>, the burning bar fills <see cref="BarTop"/> to <see cref="BarBottom"/>.</summary>
        private const int BarTop = 2, BarMiddle = 3, BarBottom = 4;
        private const int NodeApart = 14, WarnFrames = 60, DottedFrames = 40, BarFrom = 7, BarTo = 27, NodeScore = 300;
        private const int NodeSize = 7, RightNodeX = 28;
        /// <summary>Columns between the dots of the warning line, matching the drawing.</summary>
        private const int DotStep = 2;
        private const uint Volt = 0xffe85a, Dim = 0x8a7a1f;

        private enum Phase { Falling, Warning, Live, Broken }

        private static readonly string[] Rows =
        [
            ".#####.......................#####.",
            "#ZZZZZ#.....................#ZZZZZ#",
            "#ZrrrZ#.....................#ZrrrZ#",
            "#ZryrZ#.d.d.d.d.d.d.d.d.d.d.#ZryrZ#",
            "#ZrrrZ#.....................#ZrrrZ#",
            "#ZZZZZ#.....................#ZZZZZ#",
            ".#####.......................#####.",
        ];

        private static readonly Sprite Whole = Sprite.FromArt(Rows, Art.With(('Z', Dim), ('d', Dim)));
        private static readonly Sprite LeftArt = Whole.Cut(0, 0, NodeSize, NodeSize);
        private static readonly Sprite RightArt = Whole.Cut(RightNodeX, 0, NodeSize, NodeSize);
        private static readonly Sprite Nothing = new(Whole.W, Whole.H);

        public override EnemyKind Kind => EnemyKind.Yoke;
        public override string Name => "YOKE";
        public override Sprite Body => Whole;
        public override uint Colour => Volt;
        public override double Hp => 10;
        public override int Score => 0;   // the nodes score; the kill adds nothing to them
        public override bool Speaks => true;
        public override string? Line(Enemy e, VoiceCue cue) => cue == VoiceCue.Arrive ? "Stay in your lane." : null;

        public override void Spawn(RaiderGame g, Enemy e)
        {
            Part Node(string name, int ox, Sprite art) => new()
            {
                Name = name,
                OX = ox,
                OY = 0,
                HW = NodeSize / 2,
                HH = NodeSize / 2,
                Closed = art,
                State = PartState.Open,
                Vital = true,
                Hp = e.Hp / 2,
                MaxHp = e.Hp / 2,
                Score = NodeScore,
            };
            g.SetParts(e, Node("node-l", -NodeApart, LeftArt), Node("node-r", NodeApart, RightArt));
            e.Mode = (int)Phase.Falling;
        }

        public override void Update(RaiderGame g, Enemy e)
        {
            e.Y += FallSpeed;
            var phase = (Phase)e.Mode;
            if (phase == Phase.Broken)
            {
                // the survivor drifts off to its own side; the bar is gone
                foreach (var p in e.Parts!) if (p.State != PartState.Dead) p.OX += Math.Sign(p.OX) * SideSpeed;
                return;
            }
            double half = Whole.W / 2.0, to = g.PX - e.BaseX;
            e.BaseX = Math.Clamp(e.BaseX + Math.Clamp(to, -DriftSpeed, DriftSpeed), half, g.PW - half);
            e.X = e.BaseX;
            if (phase == Phase.Falling && e.Y - Whole.H / 2.0 >= 0)
            {
                e.Mode = (int)Phase.Warning;
                e.ModeAge = 0;
            }
            else if (phase == Phase.Warning && e.ModeAge >= WarnFrames)
            {
                e.Mode = (int)Phase.Live;
                e.ModeAge = 0;
                g.Sound(SfxNames.Beam, e.X);
            }
        }

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            g.Sound(SfxNames.Part, e.X + p.OX);
            e.Mode = (int)Phase.Broken;
            e.ModeAge = 0;
            e.Sprite = Nothing;   // the bar and the body between the nodes go; each node is drawn as a part
        }

        public override bool Touches(RaiderGame g, Enemy e) =>
            e.Mode == (int)Phase.Live && Math.Abs(g.PX - e.X) <= Reach && Math.Abs(g.PY - e.Y) <= Thick;

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            int x0 = (int)Math.Round(e.X - Whole.W / 2.0), y0 = (int)Math.Round(e.Y - Whole.H / 2.0);
            switch ((Phase)e.Mode)
            {
                case Phase.Warning when e.ModeAge > DottedFrames:
                    for (int x = BarFrom + 1; x <= BarTo - 1; x += DotStep) c.Set(x0 + x, y0 + BarMiddle, Palette.Tell);   // the last third: the dots flare
                    break;
                case Phase.Live:
                    for (int x = BarFrom; x <= BarTo; x++)
                        for (int y = BarTop; y <= BarBottom; y++) c.Set(x0 + x, y0 + y, Volt);
                    break;
            }
        }
    }
}
