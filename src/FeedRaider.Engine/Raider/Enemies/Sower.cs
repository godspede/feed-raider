namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Sower (a stage 3 unlock): a small orchid thing that crosses the top of the field at a steady pace and, as it passes each
    /// column of a lattice ten pixels apart, lets a slow seed fall. Two places in the lattice are left unsown, one gap thirty pixels
    /// wide that is never at an edge, and a dotted line down the middle of it shows where before any seed gets there. The seeds are
    /// slow and straight: reading the line and walking to it is the whole test, and a ship in the gap is never touched.
    /// </summary>
    private sealed class SowerBehaviour : EnemyBehaviour
    {
        private const double Speed = 0.8, SeedSpeed = 0.7, Pitch = 10, First = 5, Margin = 3, EdgeClearance = 20, RowShare = 0.1;
        private const uint Orchid = 0xc77dff, Shade = 0x6a3f9a;
        /// <summary>The rows from one dot of the gap's dotted line to the next.</summary>
        private const int DotStep = 2;

        private static readonly string[] Rows =
        [
            "#.........#",
            "##..###..##",
            "#P##ppp##P#",
            "#PPppyppPP#",
            ".##PpppP##.",
            "...#####...",
        ];

        private static readonly Sprite Sheet = Sprite.FromArt(Rows, Art.With(('p', Orchid), ('P', Shade)));

        /// <summary>Which way it crosses, the next lattice place to pass, the two it leaves unsown (the first of them), and the middle of the gap.</summary>
        private sealed class Furrow
        {
            public int Dir, Next, Gap;
            public double Mid;
        }

        public override EnemyKind Kind => EnemyKind.Sower;
        public override string Name => "SOWER";
        public override Sprite Body => Sheet;
        public override uint Colour => Orchid;
        public override double Hp => 8;
        public override int Score => 450;

        /// <summary>The x of lattice place k when it is crossed left to right; right to left it is mirrored.</summary>
        private static double Place(int k, int dir, double pw) => dir > 0 ? First + Pitch * k : pw - First - Pitch * k;

        private static int PlaceCount(double pw) => (int)Math.Floor((pw - Margin - First) / Pitch) + 1;

        public override void Spawn(RaiderGame g, Enemy e)
        {
            int dir = e.BaseX < g.PW / 2.0 ? 1 : -1;
            double half = e.Sprite.W / 2.0;
            e.X = dir > 0 ? -half : g.PW + half;
            e.BaseX = e.X;
            e.Y = RowShare * g.PH;
            // the gap takes places j and j + 1, and its middle (10 + 10j) keeps twenty pixels from either edge
            int count = PlaceCount(g.PW);
            var valid = new List<int>();
            for (int j = 0; j + 1 < count; j++) if (Middle(j) >= EdgeClearance && Middle(j) <= g.PW - EdgeClearance) valid.Add(j);
            int at = valid.Count > 0 ? valid[(int)(g.R() * valid.Count)] : 0;
            e.Kit = new Furrow { Dir = dir, Next = 0, Gap = at, Mid = dir > 0 ? Middle(at) : g.PW - Middle(at) };
        }

        private static double Middle(int j) => First + Pitch * j + Pitch / 2;

        public override void Update(RaiderGame g, Enemy e)
        {
            var f = (Furrow)e.Kit!;
            e.X += f.Dir * Speed;
            int count = PlaceCount(g.PW);
            while (f.Next < count && (f.Dir > 0 ? e.X >= Place(f.Next, f.Dir, g.PW) : e.X <= Place(f.Next, f.Dir, g.PW)))
            {
                if (f.Next != f.Gap && f.Next != f.Gap + 1)
                    g.Bullets.Add(new Bullet { X = Place(f.Next, f.Dir, g.PW), Y = e.Y + e.Sprite.H / 2.0, VX = 0, VY = SeedSpeed });
                f.Next++;
            }
        }

        /// <summary>The dotted line down the gap: every second pixel from under the sower to the bottom of the field.</summary>
        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            var f = (Furrow)e.Kit!;
            int x = (int)Math.Round(f.Mid);
            for (int y = (int)Math.Ceiling(e.Y + e.Sprite.H / 2.0); y < c.Game.PH; y += DotStep) c.Set(x, y, Orchid);
        }
    }
}
