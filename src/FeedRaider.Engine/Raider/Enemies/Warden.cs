namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Warden (stage 2): shoots nothing, and while it lives the three enemies nearest it shrug off shots. It hangs a hull of armour
    /// over a five-pixel emitter, so from below the emitter is the first thing a shot meets; killing it lifts the shield the same frame.
    /// It stays 600 frames, then leaves. Nothing it does is lethal, and a shielded enemy is shown by a blue thread and a pale ring.
    /// </summary>
    private sealed class WardenBehaviour : EnemyBehaviour
    {
        /// <summary>How many enemies one warden shields, how far from it they may be, and how long it stays.</summary>
        private const int Shields = 3, StayFrames = 600;
        private const double Reach = 30, EnterSpeed = 0.6, LeaveSpeed = 0.8, HoldRow = 0.14, SwayRate = 0.03, SwayShare = 0.1;
        /// <summary>The emitter's centre lies this far below the warden's, and the thread to a shielded enemy is dotted every second pixel.</summary>
        private const int EmitterOffset = 4, DotStep = 2;
        private const double MinHold = 10;
        /// <summary>The hull is the top 13 by 8 of the drawing, 2 pixels above the warden's middle; the emitter is the 5 by 4 cut below it, from column 4.</summary>
        private const int HullW = 13, HullH = 8, EmitterX = 4, EmitterW = 5, EmitterH = 4, HullBoxW = 6, HullBoxH = 4, EmitterBox = 2;
        private const double HullOffset = -2, ArmourHp = 1;

        // 13 by 12: twelve rows, so the height is even and the body and the parts it is cut into round alike
        private static readonly string[] Rows =
        [
            "..#########..",
            ".#ooooooooo#.",
            "#oo+++++++oo#",
            "#o+ooooooo+o#",
            "#o+ooooooo+o#",
            "#oo+++++++oo#",
            ".#ooooooooo#.",
            "..###ooo###..",
            "....#bbb#....",
            "....#byb#....",
            "....#bbb#....",
            ".....###.....",
        ];

        private static readonly Sprite Whole = Sprite.FromArt(Rows, Art.With(('b', Palette.Shield)));
        private static readonly Sprite HullArt = Whole.Cut(0, 0, HullW, HullH);
        private static readonly Sprite EmitterArt = Whole.Cut(EmitterX, HullH, EmitterW, EmitterH);

        /// <summary>What one warden remembers: the enemies it shielded this frame (the game clears the shield itself before the view draws).</summary>
        private sealed class Charge
        {
            public readonly List<Enemy> Shielded = [];
        }

        public override EnemyKind Kind => EnemyKind.Warden;
        public override string Name => "WARDEN";
        public override Sprite Body => Whole;
        public override uint Colour => Palette.Shield;
        public override double Hp => 7;
        public override int Score => 700;
        public override bool Speaks => true;
        public override string? Line(Enemy e, VoiceCue cue) => cue == VoiceCue.Arrive ? "They are with me." : null;

        public override void Spawn(RaiderGame g, Enemy e)
        {
            var hull = new Part
            {
                Name = "hull",
                OX = 0,
                OY = HullOffset,
                HW = HullBoxW,
                HH = HullBoxH,
                Closed = HullArt,
                Wreck = Art.Wrecked(HullArt),
                State = PartState.Armoured,
                Vital = false,
                Hp = ArmourHp,
                MaxHp = ArmourHp,
            };
            var emitter = new Part
            {
                Name = "emitter",
                OX = 0,
                OY = EmitterOffset,
                HW = EmitterBox,
                HH = EmitterBox,
                Closed = EmitterArt,
                Wreck = Art.Wrecked(EmitterArt),
                State = PartState.Open,
                Vital = true,
                Hp = e.Hp,
                MaxHp = e.Hp,
                Score = 0,   // the kill scores the warden's own 700
            };
            g.SetParts(e, hull, emitter);
            e.Kit = new Charge();
        }

        public override void Update(RaiderGame g, Enemy e)
        {
            var charge = (Charge)e.Kit!;
            charge.Shielded.Clear();
            if (!e.Leaving && e.Age > StayFrames) e.Leaving = true;

            double hold = Math.Max(HoldRow * g.PH, MinHold);   // never so high that the top clips the HUD, nor so low on a short field
            if (e.Leaving) e.Y -= LeaveSpeed;
            else if (e.Y < hold) e.Y = Math.Min(hold, e.Y + EnterSpeed);
            double half = e.Sprite.W / 2.0;
            e.X = Math.Clamp(e.BaseX + Math.Sin(e.Age * SwayRate + e.Phase) * g.PW * SwayShare, half, g.PW - half);
            if (e.Leaving || e.Y < 0) return;

            // the three nearest that can be hurt and are not of its own kind: the shield is for the little ones, never a gunship or a boss
            var near = new Enemy[Shields];
            var dist = new double[Shields];
            int n = 0;
            foreach (var t in g.Enemies)
            {
                if (!Shieldable(g, e, t)) continue;
                double dx = t.X - e.X, dy = t.Y - e.Y, d = dx * dx + dy * dy;
                if (d > Reach * Reach) continue;
                int at = n;
                while (at > 0 && d < dist[at - 1]) at--;   // a tie stays behind the older enemy
                if (at >= Shields) continue;
                for (int i = Math.Min(n, Shields - 1); i > at; i--) { near[i] = near[i - 1]; dist[i] = dist[i - 1]; }
                near[at] = t; dist[at] = d;
                if (n < Shields) n++;
            }
            for (int i = 0; i < n; i++)
            {
                near[i].ShieldedBy = e;
                charge.Shielded.Add(near[i]);
            }
        }

        private static bool Shieldable(RaiderGame g, Enemy warden, Enemy t) =>
            !ReferenceEquals(t, warden) && t.Hp > 0 && !t.Untouchable && t.Y >= 0
            && t.Kind is not (EnemyKind.Warden or EnemyKind.Gunship or EnemyKind.Mine)   // a mine already shrugs off every shot
            && !g.BehaviourFor(t.Kind).Boss;

        public override void Down(RaiderGame g, Enemy e)
        {
            // hurt again at once: shots later in the very frame it dies land
            foreach (var t in g.Enemies) if (ReferenceEquals(t.ShieldedBy, e)) t.ShieldedBy = null;
            (e.Kit as Charge)?.Shielded.Clear();
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Kit is not Charge charge) return;
            foreach (var t in charge.Shielded)
            {
                if (t.Hp <= 0 || !c.Game.Enemies.Contains(t)) continue;
                DrawThread(c, e, t);
                c.Draw(Art.Shield, (int)Math.Round(t.X - Art.Shield.W / 2.0), (int)Math.Round(t.Y - Art.Shield.H / 2.0));
            }
        }

        /// <summary>A dotted blue line from the emitter to the shielded enemy, left off the two sprites it joins.</summary>
        private static void DrawThread(RaiderView.Canvas c, Enemy w, Enemy t)
        {
            double ax = w.X, ay = w.Y + EmitterOffset, dx = t.X - ax, dy = t.Y - ay;
            int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
            for (int i = 0; i <= steps; i += DotStep)
            {
                double x = steps == 0 ? ax : ax + dx * i / steps, y = steps == 0 ? ay : ay + dy * i / steps;
                if (Inside(w, x, y) || Inside(t, x, y)) continue;
                c.Set((int)Math.Round(x), (int)Math.Round(y), Palette.Shield);
            }
        }

        private static bool Inside(Enemy e, double x, double y) =>
            Math.Abs(x - e.X) <= e.Sprite.W / 2.0 && Math.Abs(y - e.Y) <= e.Sprite.H / 2.0;
    }
}
