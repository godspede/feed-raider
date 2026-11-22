namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Fuse (stage 10): a lit bomb that sinks to a row, plants, counts three beats of 40 frames (a pip goes out and the game ticks on
    /// each, higher each time) and goes off, a disc of radius 26 that is lethal to the ship for 6 frames and hits every other enemy in it
    /// for 30 (an open part of one, never its armour). The disc is drawn from the moment it plants, crimson and then white for the last
    /// beat, so the ship has the whole count to walk out. Shot dead before the count ends, it does nothing at all.
    /// </summary>
    private sealed class FuseBehaviour : EnemyBehaviour
    {
        private const int Planting = 0, Counting = 1, Blasting = 2;
        private const int Beat = 40, Beats = 3, BlastFrames = 6;
        private const double Reach = 26, SinkSpeed = 0.6, RowShare = 0.3, BlastDmg = 30;
        /// <summary>The beat lights: the first one's left column and the row above the centre, and the gap between lights.</summary>
        private const int LightsLeft = 5, LightGap = 4, LightsAbove = 9, SinkBelow = 11;

        private static readonly string[] Rows =
        [
            "....y....",
            "...###...",
            ".##rrr##.",
            "#rryyyrr#",
            "#ryyyyyr#",
            "#rryyyrr#",
            "#rrrrrrr#",
            ".##rrr##.",
            "...###...",
        ];

        private static readonly Sprite Whole = Sprite.FromArt(Rows, Art.DarkPal);

        public override EnemyKind Kind => EnemyKind.Fuse;
        public override string Name => "FUSE";
        public override Sprite Body => Whole;
        public override uint Colour => Palette.Crimson;
        public override double Hp => 7;
        public override int Score => 900;

        public override void Update(RaiderGame g, Enemy e)
        {
            double half = e.Sprite.W / 2.0;
            e.X = Math.Clamp(e.BaseX, half, g.PW - half);
            double pan = g.PW <= 0 ? 0 : e.X / g.PW * 2 - 1;

            if (e.Mode == Planting)
            {
                double row = RowShare * g.PH;
                if (e.Y < row) e.Y = Math.Min(row, e.Y + SinkSpeed);
                if (e.Y >= row) { e.Mode = Counting; e.ModeAge = 0; }
                return;
            }
            if (e.Mode == Counting)
            {
                if (e.ModeAge > 0 && e.ModeAge < Beat * Beats && e.ModeAge % Beat == 0)
                    g.Sfx?.Play(SfxNames.Tick, pan, g.Frame, 0, e.ModeAge / Beat - 1);
                if (e.ModeAge >= Beat * Beats)
                {
                    e.Mode = Blasting;
                    g.Sound(SfxNames.Blast, e.X, 1);
                    g.Sfx?.Play(SfxNames.Tick, pan, g.Frame, 0, Beats - 1);
                    Blast(g, e);
                }
                return;
            }
            if (e.ModeAge >= Beat * Beats + BlastFrames)
            {
                e.Untouchable = true;   // spent: no score, no kill; the walk sweeps it off the bottom
                e.Y = g.PH + SinkBelow;
            }
        }

        /// <summary>
        /// A friendly shot of 30 timed to land on the middle of every other enemy, or of every open part of one, inside the disc: the
        /// blast is the game's own damage, so score, shields, drops and a boss's phases all answer to it as they do to a shot.
        /// </summary>
        private static void Blast(RaiderGame g, Enemy e)
        {
            foreach (var t in g.Enemies)
            {
                if (ReferenceEquals(t, e) || t.Hp <= 0 || t.Untouchable || t.Y < 0) continue;
                if (t.Parts is null) { Strike(g, e, t.X, t.Y); continue; }
                foreach (var p in t.Parts)
                    if (p.State == PartState.Open) Strike(g, e, t.X + p.OX, t.Y + p.OY);
            }
        }

        private static void Strike(RaiderGame g, Enemy fuse, double x, double y)
        {
            double dx = x - fuse.X, dy = y - fuse.Y;
            if (dx * dx + dy * dy > Reach * Reach) return;
            double v = Tuning.ShipShotSpeed;
            g.Bullets.Add(new Bullet { X = x, Y = y + v, VX = 0, VY = -v, Dmg = BlastDmg, Friendly = true });
        }

        public override bool Touches(RaiderGame g, Enemy e)
        {
            if (e.Mode != Blasting || e.ModeAge >= Beat * Beats + BlastFrames) return false;
            double dx = g.PX - e.X, dy = g.PY - e.Y;
            return dx * dx + dy * dy <= Reach * Reach;
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Mode == Planting) return;
            int cx = (int)Math.Round(e.X), cy = (int)Math.Round(e.Y);
            if (e.Mode == Counting)
            {
                uint ring = e.ModeAge >= Beat * (Beats - 1) ? Palette.Tell : Palette.Crimson;
                for (int a = 0; a < 360; a++)
                {
                    double r = double.DegreesToRadians(a);
                    c.Set((int)Math.Round(e.X + Reach * Math.Cos(r)), (int)Math.Round(e.Y + Reach * Math.Sin(r)), ring);
                }
                int lit = Beats - Math.Min(Beats, e.ModeAge / Beat);
                for (int i = 0; i < lit; i++)
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                            c.Set(cx - LightsLeft + LightGap * i + dx, cy - LightsAbove + dy, Palette.Gold);
                return;
            }
            // the blast: a white checker over the whole disc, so the ground it covers is plain to see
            int reach = (int)Reach;
            for (int y = -reach; y <= reach; y++)
                for (int x = -reach; x <= reach; x++)
                    if (x * x + y * y <= Reach * Reach && ((x + y) & 1) == 0) c.Set(cx + x, cy + y, Palette.Tell);
        }
    }
}
