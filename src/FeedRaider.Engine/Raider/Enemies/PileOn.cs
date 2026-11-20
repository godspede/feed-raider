namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// Boss 8, the Pile-On (stage 8): a wall of bricks that never moves, with an instigator pacing on top of it. The core is the only
    /// vital part, and the shots only reach it down a column the player has worn through. Every 50 frames the wall throws its lowest brick
    /// down on the ship; where a column has no brick left the core stops over it and drops three bolts down the hole. A short column
    /// slowly regrows. Everything is drawn on whole pixels and moves in whole cells: the wall stands, the core steps two pixels at a time,
    /// and the whole thing enters and leaves two pixels every four frames.
    /// </summary>
    private sealed class PileOnBehaviour : EnemyBehaviour
    {
        private const int ThrowEvery = 50, ThrowFast = 35, ShakeFrames = 30, SlitWait = 24, SlitGap = 5, SlitRest = 60, SlitBolts = 3;
        private const int RegrowEvery = 150, OutlineFrames = 45, OpenForcedAfter = 300, ForcedGap = 10, EntryStepFrames = 4, EntryStep = 2;
        private const int BrickW = 7, BrickH = 5, CellW = 8, MaxHostileBolts = 48, ClearAway = 20;
        /// <summary>The core moves two pixels a step, and sits on a lattice of that pitch.</summary>
        private const int CoreStep = 2;
        private const int InstigatorBigW = 21, InstigatorBigH = 13, InstigatorSmallW = 13, InstigatorSmallH = 9;
        private const uint BrickEdge = 0xd9d2e6, GrowOutline = 0xcdbfe8;
        private const int WarnCrimsonFrames = 16;
        private const double CorePace = 0.6, SlitBoltSpeed = 1.5, ColumnHp = 14, UpperBrickHp = 5, ArrivedWithin = 0.01, MuzzleLead = 0.5;
        /// <summary>The smallest field that takes the big instigator, and the height from which the wall is three bricks deep.</summary>
        private const int BigFieldW = 96, BigFieldH = 44, DeepFieldH = 80, DeepRows = 3;
        /// <summary>The wall starts <see cref="TopRowsCompact"/> pixels from the top on a field shorter than <see cref="CompactFieldH"/>, else <see cref="TopRows"/>.</summary>
        private const int CompactFieldH = 48, TopRowsCompact = 5, TopRows = 6;
        private const int BrickScore = 20, OpensEvery = 3, ShakeJoltFrames = 2, EchoGap = 2, GrowBlinkFrames = 4, MuzzleBlinkFrames = 3;
        /// <summary>A falling wall throws debris at a speed of <see cref="DebrisSpeedMin"/> plus up to <see cref="DebrisSpeedSpread"/>, lifted by <see cref="DebrisLift"/>,
        /// living <see cref="DebrisLifeMin"/> to twice that, and a <see cref="BigChunkShare"/> of it two pixels wide.</summary>
        private const double DebrisSpeedMin = 0.4, DebrisSpeedSpread = 1.4, DebrisLift = 0.3, BigChunkShare = 0.3;
        private const int DebrisLifeMin = 16, BigChunkSize = 2;
        /// <summary>The clear pixel row between the instigator and the top row of bricks.</summary>
        private const int WallSeam = 1;
        /// <summary>The muzzle that blinks over the hole is a bar this many pixels wide, centred on the core.</summary>
        private const int MuzzleW = 3;
        private const double BossHp = 120;
        private const int KillScore = 8000;

        // the instigator, 21x13 on a big field and 13x9 on a small one
        private static readonly Sprite Big = Sprite.FromArt(
        [
            ".......#######.......",
            ".....##ooooooo##.....",
            "...##ooo+++++ooo##...",
            "..#ooo+++++++++ooo#..",
            ".#oo++rrr+++rrr++oo#.",
            ".#oo++ryr+++ryr++oo#.",
            "#ooo++rrr+++rrr++ooo#",
            "#oooo+++++y+++++oooo#",
            ".#ooo++++yyy++++ooo#.",
            ".#oooo+++++++++oooo#.",
            "..#ooo#o#o#o#o#ooo#..",
            "...##ooooooooooo##...",
            ".....###########.....",
        ], Art.With());

        private static readonly Sprite Small = Sprite.FromArt(
        [
            "...#######...",
            ".##ooooooo##.",
            "#oo+++++++oo#",
            "#o+rrooorr+o#",
            "#o+ryoooyr+o#",
            "#oo+++y+++oo#",
            ".#oooyyyooo#.",
            "..##ooooo##..",
            "....#####....",
        ], Art.With());

        // the brick that is about to fall: the same drawing, whitened
        private static readonly Sprite BrickWhite = Sprite.FromArt(
        [
            "#######",
            "#MmmmM#",
            "#mryrm#",
            "#MmmmM#",
            "#######",
        ], Art.With(('m', Palette.Tell), ('M', BrickEdge)));

        /// <summary>Everything the wall remembers: its lay-out, the clocks of its throws and bolts, and what is shaking or growing.</summary>
        private sealed class Wall
        {
            public int Cols, Deep, InstW, InstH, H, Top, Left;
            public double SettleY, CoreX, MinX, MaxX, Acc, ColumnOffset;
            public int Clock, Age, Dir = 1, Slit = -1, SlitCol, LastCol = -1, ThrowIn = ThrowEvery, RegrowIn = RegrowEvery, LastOpenAge, Thrown, GrowTimer;
            public bool Raised, Holed, Phased;
            public Part Core = null!;
            public Part[,] Bricks = null!;
            public int[] ShortSince = null!;
            public readonly List<Part> Shaking = [];
            public Part? Growing;
            public readonly Dictionary<Part, double> BaseOX = [];

            /// <summary>The x of the middle of a column's bricks.</summary>
            public double Centre(int col) => Left + CellW * col + BrickW / 2.0;
        }

        public override EnemyKind Kind => EnemyKind.PileOn;
        public override string Name => "PILE-ON";
        public override Sprite Body => Small;
        public override uint Colour => BrickBehaviour.Light;
        public override double Hp => BossHp;
        public override int Score => KillScore;
        public override bool Boss => true;
        public override double DropChance => 0;

        public override string? Line(Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive => "We are all saying it.",
            VoiceCue.PartLost => "So true. So true. So true.",
            VoiceCue.Phase => "Screenshot this.",
            VoiceCue.Dying => "It was only a joke.",
            _ => null,
        };

        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive or VoiceCue.Opens or VoiceCue.Phase => Turn.Stack,
            VoiceCue.PartLost => e.Kit is Wall { Raised: true } ? Turn.Stack : Turn.Skip,   // a brick shot off is not a turn; the first hole is
            _ => base.TurnFor(g, e, cue),
        };

        public override void Spawn(RaiderGame g, Enemy e)
        {
            var w = new Wall();
            bool big = g.PW >= BigFieldW && g.PH >= BigFieldH;
            w.Cols = g.PW / CellW;
            w.Deep = g.PH >= DeepFieldH ? DeepRows : 1;
            w.InstW = big ? InstigatorBigW : InstigatorSmallW;
            w.InstH = big ? InstigatorBigH : InstigatorSmallH;
            w.H = w.InstH + WallSeam + BrickH * w.Deep;
            w.Top = g.PH < CompactFieldH ? TopRowsCompact : TopRows;
            w.Left = (g.PW - CellW * w.Cols) / 2;
            w.SettleY = w.Top + w.H / 2.0;
            w.ColumnOffset = w.Left + BrickW / 2.0 - g.PW / 2.0;
            // the owner is a clear silhouette of the whole wall; its parts are what is drawn and hit
            e.Sprite = new Sprite(CellW * w.Cols, w.H);
            e.X = e.BaseX = g.PW / 2.0;
            e.Y = w.SettleY - EntryStep * Math.Ceiling((w.H + w.Top) / (double)EntryStep);

            // the core sits on the lattice of the column centres, two pixels apart, wherever all of it is on the field
            double hw = w.InstW / 2.0;
            w.MinX = double.MaxValue; w.MaxX = double.MinValue;
            for (double x = w.Centre(0); x <= g.PW; x += CoreStep)
            {
                if (x - hw < 0 || x + hw > g.PW) continue;
                w.MinX = Math.Min(w.MinX, x); w.MaxX = Math.Max(w.MaxX, x);
            }
            w.CoreX = Math.Clamp(w.MinX + Math.Round((g.PW / 2.0 - w.MinX) / CoreStep) * CoreStep, w.MinX, w.MaxX);
            w.Core = new Part
            {
                Name = "core",
                OX = w.CoreX - e.X,
                OY = w.InstH / 2.0 - w.H / 2.0,
                HW = w.InstW / 2,
                HH = w.InstH / 2,
                Closed = big ? Big : Small,
                State = PartState.Open,
                Vital = true,
                Hp = e.Hp,
                MaxHp = e.Hp,
            };
            var parts = new List<Part> { w.Core };

            w.Bricks = new Part[w.Deep, w.Cols];
            w.ShortSince = new int[w.Cols];
            Array.Fill(w.ShortSince, -1);
            for (int d = 0; d < w.Deep; d++)
                for (int c = 0; c < w.Cols; c++)
                {
                    double hp = w.Deep == 1 ? ColumnHp : d == w.Deep - 1 ? ColumnHp - UpperBrickHp * (w.Deep - 1) : UpperBrickHp;   // 5, 5 and 4 when three deep
                    var p = new Part
                    {
                        Name = $"brick-{d}-{c}",
                        OX = c * CellW + w.ColumnOffset,
                        OY = w.InstH + WallSeam + BrickH * d + BrickH / 2.0 - w.H / 2.0,
                        HW = CellW / 2,
                        HH = BrickH / 2,
                        Closed = BrickBehaviour.Art7x5,
                        State = PartState.Open,
                        Vital = false,
                        Hp = hp,
                        MaxHp = hp,
                        Score = BrickScore,
                    };
                    w.Bricks[d, c] = p;
                    w.BaseOX[p] = p.OX;
                    parts.Add(p);
                }
            e.Kit = w;
            g.SetParts(e, [.. parts]);
        }

        // ---------------------------------------------------------------- the frame

        public override void Update(RaiderGame g, Enemy e)
        {
            var w = (Wall)e.Kit!;
            w.Clock++;
            if (e.Leaving)
            {
                if (w.Clock % EntryStepFrames == 0) e.Y -= EntryStep;
                return;
            }
            if (e.Y < w.SettleY)
            {
                if (w.Clock % EntryStepFrames == 0) e.Y = Math.Min(w.SettleY, e.Y + EntryStep);
                return;
            }
            e.Y = w.SettleY;
            w.Age++;
            PlaceEchoes(g, e);
            TrackHoles(g, e, w);
            CheckPhase(g, e, w);
            ShakeAndRelease(g, e, w);
            Grow(g, e, w);
            QueueThrow(g, w);
            MoveAndFireCore(g, e, w);
        }

        /// <summary>True when every brick of the column is gone and none is on its way back.</summary>
        private static bool Open(Wall w, int col)
        {
            for (int d = 0; d < w.Deep; d++)
                if (w.Bricks[d, col].State != PartState.Dead) return false;
            return w.Growing is null || !w.Growing.Name.EndsWith($"-{col}");
        }

        /// <summary>Which columns are short of a brick, and since when; the first hole is a turn of the voice.</summary>
        private static void TrackHoles(RaiderGame g, Enemy e, Wall w)
        {
            bool anyOpen = false;
            for (int c = 0; c < w.Cols; c++)
            {
                bool shortOne = false;
                for (int d = 0; d < w.Deep; d++) shortOne |= w.Bricks[d, c].State == PartState.Dead;
                if (!shortOne) w.ShortSince[c] = -1;
                else if (w.ShortSince[c] < 0) w.ShortSince[c] = w.Age;
                anyOpen |= Open(w, c);
            }
            if (anyOpen)
            {
                w.LastOpenAge = w.Age;
                if (!w.Holed)
                {
                    w.Holed = true;
                    w.Raised = true;
                    g.Cue(e, VoiceCue.PartLost);
                    w.Raised = false;
                }
            }
        }

        /// <summary>The halfway mark of the core's hit points is a turn of the voice.</summary>
        private static void CheckPhase(RaiderGame g, Enemy e, Wall w)
        {
            if (w.Phased || w.Core.Hp >= w.Core.MaxHp / 2) return;
            w.Phased = true;
            g.Cue(e, VoiceCue.Phase);
        }

        /// <summary>The boxes this boss has left on show are set side by side, one more step out each: the voice leaves them all in one place.</summary>
        private static void PlaceEchoes(RaiderGame g, Enemy e)
        {
            int n = 0;
            foreach (var echo in g.Echoes)
            {
                if (echo.Kind != EnemyKind.PileOn) continue;
                n++;
                int w = EnemySpeechBox.Box(echo, g.Layout).W, step = (n + 1) / 2 * (w * 2 + EchoGap);
                echo.X = Math.Clamp(e.X + (n % 2 == 1 ? -step : step), 0, g.PW);
            }
        }

        // ---------------------------------------------------------------- the bricks

        /// <summary>Jolts each shaking brick, and lets go of the ones whose shake has run out.</summary>
        private static void ShakeAndRelease(RaiderGame g, Enemy e, Wall w)
        {
            for (int i = w.Shaking.Count - 1; i >= 0; i--)
            {
                var p = w.Shaking[i];
                p.Timer--;
                if (p.Timer > 0)
                {
                    // a pixel either way, but never out past the wall's own width: the edge columns shake inward only
                    double jolt = w.BaseOX[p] + (p.Timer / ShakeJoltFrames % 2 == 0 ? -1 : 1), half = e.Sprite.W / 2.0 - BrickW / 2.0;
                    p.OX = Math.Clamp(jolt, -half, half);
                    continue;
                }
                w.Shaking.RemoveAt(i);
                p.OX = w.BaseOX[p];
                p.OpenArt = null;
                p.Timer = 0;
                p.State = PartState.Dead;
                p.Hp = 0;
                e.PartBox.Frame = -1;
                g.SpawnAdd(EnemyKind.Brick, e.X + p.OX, e.Y + p.OY, e);
                if (++w.Thrown % OpensEvery == 0) g.Cue(e, VoiceCue.Opens);
            }
        }

        /// <summary>Every so often picks the column nearest the ship and sets its lowest brick shaking, to fall when the shake ends.</summary>
        private static void QueueThrow(RaiderGame g, Wall w)
        {
            if (--w.ThrowIn > 0) return;
            w.ThrowIn = w.Core.Hp < w.Core.MaxHp / 2 ? ThrowFast : ThrowEvery;
            int best = -1;
            double near = double.MaxValue;
            for (int c = 0; c < w.Cols; c++)
            {
                if (!Standing(w, c).Any()) continue;
                double d = Math.Abs(w.Centre(c) - g.PX);
                if (d >= near) continue;
                near = d; best = c;
            }
            if (best < 0) return;
            var standing = Standing(w, best).ToList();
            g.Sound(SfxNames.Lob, w.Centre(best));
            if (w.Age - w.LastOpenAge >= OpenForcedAfter)
            {
                for (int k = 0; k < standing.Count; k++) StartShake(w, standing[k], ShakeFrames + ForcedGap * k);
                w.LastOpenAge = w.Age;
                return;
            }
            StartShake(w, standing[0], ShakeFrames);
        }

        /// <summary>The bricks of a column still on the wall and not yet shaking, lowest first.</summary>
        private static IEnumerable<Part> Standing(Wall w, int col)
        {
            for (int d = w.Deep - 1; d >= 0; d--)
            {
                var p = w.Bricks[d, col];
                if (p.State == PartState.Open && p.Timer <= 0) yield return p;
            }
        }

        private static void StartShake(Wall w, Part p, int frames)
        {
            p.Timer = frames;
            p.OpenArt = BrickWhite;
            w.Shaking.Add(p);
        }

        private static void Grow(RaiderGame g, Enemy e, Wall w)
        {
            if (w.Growing is { } p)
            {
                if (--w.GrowTimer > 0) return;
                w.Growing = null;
                g.Revive(e, p, p.MaxHp);
                return;
            }
            if (--w.RegrowIn > 0) return;
            w.RegrowIn = RegrowEvery;
            int best = -1;
            for (int c = 0; c < w.Cols; c++)
                if (w.ShortSince[c] >= 0 && (best < 0 || w.ShortSince[c] < w.ShortSince[best])) best = c;
            if (best < 0) return;
            for (int d = 0; d < w.Deep; d++)
            {
                var b = w.Bricks[d, best];
                if (b.State != PartState.Dead) continue;
                w.Growing = b;
                w.GrowTimer = OutlineFrames;
                return;
            }
        }

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            if (e.Kit is not Wall w || p == w.Core) return;
            // a brick shot dead while it shook is not thrown after all
            if (w.Shaking.Remove(p)) { p.OX = w.BaseOX[p]; p.OpenArt = null; p.Timer = 0; }
            g.Sound(SfxNames.Part, e.X + p.OX);
        }

        // ---------------------------------------------------------------- the core

        private static void MoveAndFireCore(RaiderGame g, Enemy e, Wall w)
        {
            if (w.Slit >= 0)
            {
                w.Slit++;
                int into = w.Slit - SlitWait;
                if (into >= 0 && into % SlitGap == 0 && into / SlitGap < SlitBolts && g.Bullets.Count(b => !b.Friendly) < MaxHostileBolts)
                    g.Bullets.Add(new Bullet { X = w.Centre(w.SlitCol), Y = e.Y - w.H / 2.0 + w.InstH + MuzzleLead, VX = 0, VY = SlitBoltSpeed });
                if (w.Slit < SlitWait + SlitGap * (SlitBolts - 1) + SlitRest) return;
                w.Slit = -1;
                w.LastCol = w.SlitCol;
                w.Acc = CoreStep;
            }
            if (w.LastCol >= 0 && Math.Abs(w.CoreX - w.Centre(w.LastCol)) > ClearAway) w.LastCol = -1;
            w.Acc += CorePace;
            if (w.Acc < CoreStep) return;
            w.Acc -= CoreStep;

            int target = -1;
            double near = double.MaxValue;
            for (int c = 0; c < w.Cols; c++)
            {
                double x = w.Centre(c);
                if (c == w.LastCol || x < w.MinX || x > w.MaxX || !Open(w, c)) continue;
                double d = Math.Abs(x - w.CoreX);
                if (d >= near) continue;
                near = d; target = c;
            }
            int dir;
            if (target >= 0) dir = Math.Sign(w.Centre(target) - w.CoreX);
            else
            {
                if (w.CoreX + CoreStep * w.Dir > w.MaxX || w.CoreX + CoreStep * w.Dir < w.MinX) w.Dir = -w.Dir;
                dir = w.Dir;
            }
            if (dir == 0) return;
            w.CoreX = Math.Clamp(w.CoreX + CoreStep * dir, w.MinX, w.MaxX);
            w.Core.OX = w.CoreX - e.X;
            e.PartBox.Frame = -1;
            if (target >= 0 && Math.Abs(w.CoreX - w.Centre(target)) < ArrivedWithin)
            {
                w.Slit = 0;
                w.SlitCol = target;
                g.Sound(SfxNames.Charge, w.CoreX);
            }
        }

        public override void Down(RaiderGame g, Enemy e)
        {
            if (e.Kit is not Wall w) return;
            foreach (var p in e.Parts!)
            {
                if (p == w.Core || p.State == PartState.Dead) continue;
                double a = g.R() * Math.Tau, sp = DebrisSpeedMin + g.R() * DebrisSpeedSpread;
                g.AddParticle(new Particle
                {
                    X = e.X + p.OX + (g.R() - 0.5) * BrickW,
                    Y = e.Y + p.OY + (g.R() - 0.5) * BrickH,
                    VX = Math.Cos(a) * sp,
                    VY = Math.Sin(a) * sp - DebrisLift,
                    Life = DebrisLifeMin + (int)(g.R() * DebrisLifeMin),
                    Colour = g.R() < 0.5 ? BrickBehaviour.Light : BrickBehaviour.Dark,
                    Size = g.R() < BigChunkShare ? BigChunkSize : 1,
                    Kind = ParticleKind.Debris,
                });
            }
        }

        // ---------------------------------------------------------------- the drawing

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Kit is not Wall w || e.Y < w.SettleY || e.Leaving) return;
            if (w.Growing is { } p && w.GrowTimer % (2 * GrowBlinkFrames) < GrowBlinkFrames)
            {
                int x = (int)Math.Round(e.X + p.OX - BrickW / 2.0), y = (int)Math.Round(e.Y + p.OY - BrickH / 2.0);
                c.Rect(x, y, BrickW, 1, GrowOutline); c.Rect(x, y + BrickH - 1, BrickW, 1, GrowOutline);
                c.Rect(x, y, 1, BrickH, GrowOutline); c.Rect(x + BrickW - 1, y, 1, BrickH, GrowOutline);
            }
            if (w.Slit < 0 || w.Slit >= SlitWait) return;
            uint colour = w.Slit < WarnCrimsonFrames ? Palette.Crimson : Palette.Tell;
            int top = w.Top + w.InstH + WallSeam, rows = BrickH * w.Deep, left = w.Left + CellW * w.SlitCol;
            c.Rect(left - 1, top, 1, rows, colour);
            c.Rect(left + BrickW, top, 1, rows, colour);
            if (w.Slit / MuzzleBlinkFrames % 2 == 0) c.Rect((int)Math.Floor(w.CoreX) - MuzzleW / 2, top - WallSeam, MuzzleW, 1, Palette.Tell);
        }
    }
}
