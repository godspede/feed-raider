namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Shutter (stage 3's boss): a slab of armour with three eyes under it, one open at a time in a pattern the player can learn. A
    /// closed eye is armour; an open one takes damage. Two warnings go before every move: a slit of light shows in the lid of the eye
    /// about to open for twenty frames, and the top of an open eye's iris whitens through its last twenty-four. Only an eye that shuts fires,
    /// one aimed spread of three, so nothing it does is lethal while an eye is open. Each eye lost shortens the beat but never
    /// leaves the field idle: the next eye is up within about twenty frames. It holds below the HUD rows and drifts across the field.
    /// </summary>
    private sealed class ShutterBehaviour : EnemyBehaviour
    {
        private const double EnterSpeed = 0.8, LeaveSpeed = 0.8, DriftSpeed = 0.25, BoltSpeed = 1.2, Lean = 0.2;
        private const int SlitFrames = 20, IrisFrames = 24;
        /// <summary>Beat and open lengths in frames with three, two and one eye left.</summary>
        private const int BeatThree = 80, BeatTwo = 65, BeatOne = 60, OpenThree = 50, OpenTwo = 45, OpenOne = 40;
        /// <summary>The hold row: a fixed one on a short field, else a share of the height between two limits.</summary>
        private const int ShortField = 48;
        private const double ShortHold = 11, HoldShare = 0.24, HoldMin = 14, HoldMax = 30;
        /// <summary>The slab is the top five rows of the drawing, the sockets the seven below them, each nine wide.</summary>
        private const int SlabH = 5, SlabHalfH = 3, EyeW = 9, EyeH = 7;
        private const int Eyes = 3, EyeScore = 800;
        /// <summary>The columns of an eye that the slit and the iris cover, the slit's row, and the row of the iris that whitens.</summary>
        private const int PupilX0 = 3, PupilX1 = 5, SlitY = 2, IrisY = 2;
        private const uint Ivory = 0xf2e6d0;
        /// <summary>The clear pixels it keeps between the slab and each side of the field as it drifts.</summary>
        private const int EdgeGap = 1;

        /// <summary>The order the eyes open in with all three alive: left, middle, right, middle.</summary>
        private static readonly int[] Pattern = [0, 1, 2, 1];

        // 41 by 12: a slab over three sockets, each a closed eye of 9 by 7 that the parts are cut from
        private static readonly string[] Rows =
        [
            "..#####################################..",
            ".#ooooooooooooooooooooooooooooooooooooo#.",
            "#oo++oo++oo++oo++oo+o+oo++oo++oo++oo++oo#",
            "#ooooooooooooooooooooooooooooooooooooooo#",
            ".#######################################.",
            ".#ooooooo#......#ooooooo#......#ooooooo#.",
            ".#o#####o#......#o#####o#......#o#####o#.",
            ".#o#ooo#o#......#o#ooo#o#......#o#ooo#o#.",
            ".#o#####o#......#o#####o#......#o#####o#.",
            ".#ooooooo#......#ooooooo#......#ooooooo#.",
            "..#ooooo#........#ooooo#........#ooooo#..",
            "...#####..........#####..........#####...",
        ];

        // the open eye: an ivory ring round a crimson iris with a gold pupil
        private static readonly string[] OpenRows =
        [
            "#ooooooo#",
            "#oiiiiio#",
            "#oirrrio#",
            "#oiryrio#",
            "#oiiiiio#",
            ".#ooooo#.",
            "..#####..",
        ];

        private static readonly Sprite Whole = Sprite.FromArt(Rows, Art.With());
        private static readonly Sprite SlabArt = Whole.Cut(0, 0, Whole.W, SlabH);
        private static readonly Sprite OpenArt = Sprite.FromArt(OpenRows, Art.With(('i', Ivory)));
        /// <summary>The left column of each socket in the drawing.</summary>
        private static readonly int[] EyeLeft = [1, 16, 31];
        private static readonly string[] EyeNames = ["eye-l", "eye-c", "eye-r"];

        /// <summary>What one shutter remembers: where it is to the fraction of a pixel, and where it is in its beat.</summary>
        private sealed class Run
        {
            public double RawX, RawY;
            public int Dir = 1;
            public bool Arrived;
            /// <summary>Frames since the last eye opened, which index in the pattern is next, the eye that opened last, and the eye that opens next.</summary>
            public int Beat, Seq, Last = -1, Next;
        }

        public override EnemyKind Kind => EnemyKind.Shutter;
        public override string Name => "SHUTTER";
        public override Sprite Body => Whole;
        public override uint Colour => Ivory;
        public override double Hp => 45;
        public override int Score => 5000;
        public override bool Boss => true;

        public override void Spawn(RaiderGame g, Enemy e)
        {
            var slab = new Part
            {
                Name = "slab",
                OX = 0,
                OY = (SlabH - Whole.H) / 2.0,
                HW = SlabArt.W / 2,
                HH = SlabHalfH,
                Closed = SlabArt,
                State = PartState.Armoured,
                Vital = false,
                Hp = 1,
                MaxHp = 1,
            };
            var parts = new List<Part> { slab };
            for (int i = 0; i < Eyes; i++)
            {
                var closed = Whole.Cut(EyeLeft[i], SlabH, EyeW, EyeH);
                parts.Add(new Part
                {
                    Name = EyeNames[i],
                    OX = EyeLeft[i] + EyeW / 2.0 - Whole.W / 2.0,
                    OY = SlabH + EyeH / 2.0 - Whole.H / 2.0,
                    HW = EyeW / 2,
                    HH = EyeH / 2,
                    Closed = closed,
                    OpenArt = OpenArt,
                    Wreck = Art.Wrecked(closed),
                    State = PartState.Armoured,
                    Vital = true,
                    Hp = e.Hp / Eyes,
                    MaxHp = e.Hp / Eyes,
                    Score = EyeScore,
                });
            }
            g.SetParts(e, [.. parts]);
            e.Kit = new Run { RawX = e.X, RawY = e.Y };
        }

        private static Part Eye(Enemy e, int i) => e.Parts![1 + i];

        private static int Alive(Enemy e)
        {
            int n = 0;
            for (int i = 0; i < Eyes; i++) if (Eye(e, i).State != PartState.Dead) n++;
            return n;
        }

        /// <summary>The frames of one beat, and of the part of it an eye stays open, by how many eyes are left.</summary>
        private static int BeatLength(int alive) => alive >= Eyes ? BeatThree : alive == 2 ? BeatTwo : BeatOne;
        private static int OpenLength(int alive) => alive >= Eyes ? OpenThree : alive == 2 ? OpenTwo : OpenOne;

        /// <summary>The eye that opens after the last: the pattern while there are three, else the next one alive in turn.</summary>
        private static int Pick(Enemy e, Run run)
        {
            if (Alive(e) >= Eyes) return Pattern[run.Seq % Pattern.Length];
            for (int i = 1; i <= Eyes; i++)
            {
                int k = (run.Last + i + Eyes) % Eyes;
                if (Eye(e, k).State != PartState.Dead) return k;
            }
            return 0;
        }

        public override void Update(RaiderGame g, Enemy e)
        {
            var run = (Run)e.Kit!;
            double hold = g.PH < ShortField ? ShortHold : Math.Round(Math.Clamp(HoldShare * g.PH, HoldMin, HoldMax));
            double half = Whole.W / 2.0;
            if (e.Leaving) run.RawY -= LeaveSpeed;
            else if (run.RawY < hold)
            {
                run.RawY = Math.Min(hold, run.RawY + EnterSpeed);
                if (run.RawY >= hold && !run.Arrived)
                {
                    run.Arrived = true;
                    run.Beat = BeatLength(Alive(e)) - SlitFrames;   // the first warning begins at once
                    run.Next = Pick(e, run);
                }
            }
            double lo = half + EdgeGap, hi = g.PW - half - EdgeGap;
            run.RawX = Math.Clamp(run.RawX + run.Dir * DriftSpeed, lo, hi);
            if (run.RawX >= hi) run.Dir = -1;
            else if (run.RawX <= lo) run.Dir = 1;
            // whole pixels, and a half in x for the odd width: the parts then fall exactly over the body they were cut from
            e.X = Math.Floor(run.RawX) + 0.5;
            e.Y = Math.Round(run.RawY);
            if (e.Leaving || !run.Arrived) return;

            for (int i = 0; i < Eyes; i++)
            {
                var p = Eye(e, i);
                if (p.State == PartState.Open && --p.Timer <= 0) ShutAndFire(g, e, p);
            }
            if (++run.Beat >= BeatLength(Alive(e))) Open(g, e, run);
        }

        /// <summary>An eye closes and fires: three bolts from its underside, the middle one aimed at the ship.</summary>
        private static void ShutAndFire(RaiderGame g, Enemy e, Part p)
        {
            p.State = PartState.Armoured;
            p.Timer = 0;
            g.Sound(SfxNames.Shut, e.X + p.OX, 1);
            double x = e.X + p.OX, y = e.Y + p.OY + p.HH;
            for (int i = -1; i <= 1; i++) g.EnemyBolt(x, y, g.PX, g.PY, i * Lean, BoltSpeed);
        }

        private static void Open(RaiderGame g, Enemy e, Run run)
        {
            run.Beat = 0;
            int alive = Alive(e);
            var p = Eye(e, run.Next);
            if (p.State == PartState.Dead) { run.Next = Pick(e, run); p = Eye(e, run.Next); }
            p.State = PartState.Open;
            p.Timer = OpenLength(alive);
            g.Sound(SfxNames.Open, e.X + p.OX, 1);
            run.Last = run.Next;
            run.Seq++;
            run.Next = Pick(e, run);
        }

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            if (e.Kit is not Run run || !p.Name.StartsWith("eye", StringComparison.Ordinal)) return;
            int alive = Alive(e);
            if (alive == 0) return;
            // the eye that was open is gone: the next warns at once, so the field is never idle for long
            run.Beat = Math.Max(run.Beat, BeatLength(alive) - SlitFrames);
            run.Last = Array.IndexOf(EyeNames, p.Name);
            run.Next = Pick(e, run);
            if (alive == 1) g.Cue(e, VoiceCue.Phase);
        }

        // ---------------------------------------------------------------- voice

        public override int Taste(Enemy e, IReadOnlyList<RudePost> waiting)
        {
            int best = 0;
            for (int i = 1; i < waiting.Count; i++) if (waiting[i].Text.Length < waiting[best].Text.Length) best = i;
            return best;
        }

        /// <summary>It types only while an eye is open: the box freezes mid-word at each blink.</summary>
        public override bool HoldBox(RaiderGame g, Enemy e)
        {
            if (e.Parts is null) return false;
            for (int i = 0; i < Eyes; i++) if (Eye(e, i).State == PartState.Open) return false;
            return true;
        }

        // it glances: short posts, quickly one after another
        private const int GlancePace = 3 * Tuning.Fps;

        public override int IdleFrames(Enemy e) => GlancePace;

        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Phase => Turn.Cut,
            VoiceCue.PartLost => e.Parts is not null && Alive(e) == 1 ? Turn.Skip : Turn.Cut,   // the last eye's phase line speaks instead
            _ => base.TurnFor(g, e, cue),
        };

        public override string? Line(Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive => "I see every post.",
            VoiceCue.PartLost => "I was not looking anyway.",
            VoiceCue.Phase => "Do not make me blink.",
            VoiceCue.Dying => "Lights out.",
            _ => null,
        };

        // ---------------------------------------------------------------- the warnings

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Kit is not Run run || e.Parts is null || e.Parts.Count < Eyes + 1) return;
            if (run.Arrived && !e.Leaving && run.Beat >= BeatLength(Alive(e)) - SlitFrames && Eye(e, run.Next) is { State: PartState.Armoured } next)
            {
                int x0 = (int)Math.Round(e.X + next.OX - next.Closed.W / 2.0), y0 = (int)Math.Round(e.Y + next.OY - next.Closed.H / 2.0);
                for (int dx = PupilX0; dx <= PupilX1; dx++) c.Set(x0 + dx, y0 + SlitY, Ivory);
            }
            for (int i = 0; i < Eyes; i++)
            {
                var p = Eye(e, i);
                if (p.State != PartState.Open || p.Timer > IrisFrames) continue;
                uint white = Surface.Mix(Palette.WeakBody, Palette.Tell, (IrisFrames + 1 - p.Timer) / (double)IrisFrames);
                int x0 = (int)Math.Round(e.X + p.OX - OpenArt.W / 2.0), y0 = (int)Math.Round(e.Y + p.OY - OpenArt.H / 2.0);
                for (int dx = PupilX0; dx <= PupilX1; dx++) c.Set(x0 + dx, y0 + IrisY, white);
            }
        }
    }
}
