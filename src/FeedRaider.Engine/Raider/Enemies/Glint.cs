namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Glint (stage 9): a small pane that is a mirror half the time. Lit, a shot that strikes it comes straight back down the
    /// column it went up; dull, it is silver round a gold mark on crimson and can be hurt. It fires nothing. Before it lights it
    /// shimmers: a band of the glass's own sheen runs down it, and then it glows round its mark. A pair keep to cycles half a
    /// cycle apart, so one of the two is nearly always dull.
    /// </summary>
    private sealed class GlintBehaviour : EnemyBehaviour
    {
        private static readonly string[] Rows =
        [
            "....#....",
            "..##I##..",
            ".#IrrrI#.",
            "#IIryrII#",
            ".#IrrrI#.",
            "..##I##..",
            "....#....",
        ];

        private static readonly Sprite Dull = Sprite.FromArt(Rows, Glass.DullPalette);
        private static readonly Sprite Lit = Sprite.FromArt(Rows, Glass.LitPalette);

        private const int Cycle = 150, LitFrames = 60, Cycles = 4;
        private const double HoldRow = 0.16, EnterSpeed = 0.8, LeaveSpeed = 0.8, SlideSpeed = 0.3;
        private const int EdgeMargin = 5, BandFrames = 8, BandsBeforeGlow = 2;
        private const double GlowAlpha = 0.55;
        /// <summary>The shimmer's band runs down a pane that can still be broken, so it is the glass's own sheen and never the shield's blue.</summary>
        private const uint BandColour = Glass.Sheen;
        /// <summary>The mark sits at this column and row of the pane.</summary>
        private const int MarkX = 3, MarkY = 2;
        private const int KillScore = 700;

        /// <summary>Where a Glint is in its cycle (it is lit for the last <see cref="LitFrames"/> frames) and how long since it returned a shot.</summary>
        private sealed class Pane
        {
            public int Clock, Cooldown;
        }

        public override EnemyKind Kind => EnemyKind.Glint;
        public override string Name => "GLINT";
        public override Sprite Body => Dull;
        public override uint Colour => Palette.Ice;
        public override double Hp => 5;
        public override int Score => KillScore;

        public override void Spawn(RaiderGame g, Enemy e)
        {
            var pane = new Pane();
            // a second one on the field keeps half a cycle from the first
            foreach (var other in g.Enemies)
                if (other.Kind == EnemyKind.Glint && !other.Leaving && other.Hp > 0 && other.Kit is Pane first) { pane.Clock = (first.Clock + Cycle / 2) % Cycle; break; }
            e.Kit = pane;
            g.SetParts(e, new Part
            {
                Name = "pane",
                OX = 0,
                OY = 0,
                HW = Dull.W / 2,
                HH = Dull.H / 2,
                Closed = Lit,
                OpenArt = Dull,
                Wreck = null,
                State = IsLit(pane) ? PartState.Armoured : PartState.Open,
                Vital = true,
                Hp = e.Hp,
                MaxHp = e.Hp,
                Score = 0,   // the kill scores the Glint's own KillScore
            });
        }

        private static bool IsLit(Pane p) => p.Clock >= Cycle - LitFrames;

        public override void Update(RaiderGame g, Enemy e)
        {
            var pane = (Pane)e.Kit!;
            if (pane.Cooldown > 0) pane.Cooldown--;
            var part = e.Parts![0];
            bool was = part.State == PartState.Armoured, lit = IsLit(pane);
            if (!e.Leaving && e.Age > Cycles * Cycle) e.Leaving = true;
            part.State = lit ? PartState.Armoured : PartState.Open;
            if (lit != was && e.Y >= 0) g.Sound(lit ? SfxNames.Open : SfxNames.Shut, e.X);
            pane.Clock = (pane.Clock + 1) % Cycle;

            double hold = HoldRow * g.PH;
            if (e.Leaving) { e.Y -= LeaveSpeed; return; }
            if (e.Y < hold) e.Y = Math.Min(hold, e.Y + EnterSpeed);
            else if (g.Alive)
            {
                double dx = g.PX - e.X;
                e.X = Math.Clamp(e.X + Math.Clamp(dx, -SlideSpeed, SlideSpeed), EdgeMargin, g.PW - EdgeMargin);
                e.BaseX = e.X;
            }
        }

        public override void PartHit(RaiderGame g, Enemy e, Part p, Bullet b, bool stopped)
        {
            if (!stopped) return;
            var pane = (Pane)e.Kit!;
            bool allowed = pane.Cooldown <= 0;
            if (allowed) pane.Cooldown = Glass.ReturnGap;
            Glass.ReturnShot(g, b, allowed);
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Leaving) return;
            var pane = (Pane)e.Kit!;
            int s = pane.Clock - 1 - (Cycle - LitFrames - Glass.ShimmerFrames);   // frames into the warning, or negative
            if (s < 0 || s >= Glass.ShimmerFrames || e.Parts![0].State == PartState.Armoured) return;
            int x = (int)Math.Round(e.X - Dull.W / 2.0), y = (int)Math.Round(e.Y - Dull.H / 2.0);
            if (s >= BandsBeforeGlow * BandFrames)
            {
                c.Draw(Dull, x, y, GlowAlpha);
                c.Draw(Glass.Mark, x + MarkX, y + MarkY);   // the pane glows round its mark: it can still be broken
                return;
            }
            int row = s % BandFrames * Dull.H / BandFrames;
            bool markRow = row >= MarkY && row < MarkY + Glass.MarkSize;
            for (int px = 0; px < Dull.W; px++)
                if (Dull.Px[row * Dull.W + px] != 0 && !(markRow && px >= MarkX && px < MarkX + Glass.MarkSize)) c.Set(x + px, y + row, BandColour);
        }
    }
}
