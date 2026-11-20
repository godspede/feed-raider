namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Flanker (stage 4): it locks the ship's row when it appears and comes down the edge it will fly from, marking the row with a
    /// dotted lane and a chevron that goes white for the last 13 frames. Then it flies the row once, across the whole field, and is
    /// gone. The row is the one the ship stood on at the lock and it never follows the ship, so a ship that has moved 8 pixels off it
    /// is safe. Twenty frames after the first, a second comes from the other side on a row at least 10 pixels from the first's.
    /// While it marks, it cannot touch the ship (a ship standing in its column is not hit by the descent). Its speeds are its own
    /// and are not scaled by the stage. A flanker a live wave would carry is a grunt instead.
    /// </summary>
    private sealed class FlankerBehaviour : EnemyBehaviour
    {
        private const double MarkSpeed = 1.2, CrossSpeed = 2.4, MinRowGap = 10;
        /// <summary>Frames before the second one is called, before the cross may begin, and the chevron's white warning.</summary>
        private const int CallAge = 20, MinMarkFrames = 40, WarnFrames = 13;
        /// <summary>The chevron's arm length in pixels, and the dotted lane's spacing.</summary>
        private const int ChevronArm = 4, LaneDotGap = 3;
        private const uint Coral = 0xff7043, Rust = 0x9a3a1f;

        // flying right: a coral dart with a gold nose and a plum husk behind it
        private static readonly string[] Rows =
        [
            "##.......",
            "#Ff###...",
            "#Ffffffy#",
            "#Ff###...",
            "##.......",
        ];

        private static readonly Sprite Right = Sprite.FromArt(Rows, Art.With(('f', Coral), ('F', Rust)));
        private static readonly Sprite Left = Sprite.FromArt([.. Rows.Select(r => new string([.. r.Reverse()]))], Art.With(('f', Coral), ('F', Rust)));

        /// <summary>What one flanker remembers.</summary>
        private sealed class Lane
        {
            public double Row;
            /// <summary>+1 flies right (enters at the left edge), -1 flies left.</summary>
            public int Dir = 1;
            public bool Called, Crossing;
            /// <summary>The age at which the cross begins (after at least 40 frames of marking); 0 until it is on its row.</summary>
            public int CrossAt;
        }

        public override EnemyKind Kind => EnemyKind.Flanker;
        public override string Name => "FLANKER";
        public override Sprite Body => Right;
        public override uint Colour => Coral;
        public override double Hp => 3;
        public override int Score => 350;

        public override void Spawn(RaiderGame g, Enemy e)
        {
            if (e.Live)
            {
                // a flanker stays out of a live wave; the wave builder does not ask, so the kind answers for itself
                e.Kind = EnemyKind.Grunt;
                e.Sprite = Art.Grunt;
                e.Hp = e.MaxHp = TableBehaviour(EnemyKind.Grunt).Hp * Ramp.HpMul(g.Stage);
                return;
            }
            var k = new Lane();
            g.LockAim(e);
            double row = e.AimY, low = g.PH - g.Ship.H / 2.0 - 1;
            if (e.Parent is { Kind: EnemyKind.Flanker, Kit: Lane first })
            {
                k.Dir = -first.Dir;
                if (Math.Abs(row - first.Row) < MinRowGap) row = first.Row + MinRowGap <= low ? first.Row + MinRowGap : first.Row - MinRowGap;
            }
            else k.Dir = g.R() < 0.5 ? -1 : 1;
            k.Row = Math.Clamp(row, Right.H / 2.0, g.PH - 1);
            double half = Right.W / 2.0;
            e.X = e.BaseX = k.Dir > 0 ? half : g.PW - half;
            e.Y = -Right.H;
            e.Sprite = k.Dir > 0 ? Right : Left;
            e.Grace = int.MaxValue;   // unarmed until it flies
            e.Kit = k;
        }

        public override void Update(RaiderGame g, Enemy e)
        {
            if (e.Kit is not Lane k) return;
            if (!k.Called && e.Parent is null && e.Age > CallAge)
            {
                k.Called = true;
                g.SpawnAdd(EnemyKind.Flanker, e.X, -Right.H, e);
            }
            if (!k.Crossing)
            {
                if (e.Y < k.Row) e.Y = Math.Min(k.Row, e.Y + MarkSpeed);
                else if (k.CrossAt == 0) k.CrossAt = Math.Max(MinMarkFrames + 1, e.Age + WarnFrames);
                if (k.CrossAt > 0 && e.Age == k.CrossAt - WarnFrames) g.Sound(SfxNames.Charge, e.X);
                if (k.CrossAt > 0 && e.Age >= k.CrossAt)
                {
                    k.Crossing = true;
                    e.Grace = 0;
                }
            }
            if (k.Crossing)
            {
                e.X += k.Dir * CrossSpeed;
                e.BaseX = e.X;
                e.Y = k.Row;
            }
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Kit is not Lane k) return;
            int row = (int)Math.Round(k.Row);
            for (int x = 0; x < c.Width; x += LaneDotGap)
            {
                if (k.Crossing && (x - e.X) * k.Dir <= Right.W / 2.0) continue;
                if (!k.Crossing && Math.Abs(x - e.X) <= Right.W / 2.0 && Math.Abs(row - e.Y) <= Right.H / 2.0) continue;
                c.Set(x, row, Coral);
            }
            if (k.Crossing) return;
            bool white = k.CrossAt > 0 && e.Age >= k.CrossAt - WarnFrames;
            uint colour = white ? Palette.Tell : Coral;
            for (int i = 0; i < ChevronArm; i++)
                foreach (int dy in i == 0 ? new[] { 0 } : new[] { -i, i })
                {
                    int x = k.Dir > 0 ? ChevronArm - 1 - i : c.Width - ChevronArm + i;
                    c.Set(x, row + dy, colour);
                }
        }
    }
}
