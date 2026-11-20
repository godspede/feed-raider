namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Bulwark (stage 2's boss): a core between two shield pods on struts. While a pod lives the core is armour and a shot at it
    /// only pings; the second pod's death bares it. So it is taken apart in order, three ways to be shot at:
    /// <list type="bullet">
    /// <item><description><b>Guarded</b> (both pods): the pods take turns firing one aimed bolt every 40 frames.</description></item>
    /// <item><description><b>Half-guarded</b> (one pod): the live pod fires an aimed spread of three every 50 frames, and every 150 the core charges for 30 and throws a ring of 8.</description></item>
    /// <item><description><b>Bare</b> (no pod): the core opens, sways wider, fires a spread of three every 45 frames and a ring of 10 every 150.</description></item>
    /// </list>
    /// Every volley is warned: a pod's gold centre blinks white for 20 frames before it fires, and the core's centre whitens over the
    /// 30 frames before a ring (a dot for each bolt shows where it will go). While it charges the core holds its spread, so the
    /// ring is the only thing coming. It speaks from behind its shield: a hidden reply first, a new post each time a pod is lost.
    /// </summary>
    private sealed class BulwarkBehaviour : EnemyBehaviour
    {
        private const int Entering = 0, Guarded = 1, Half = 2, Bare = 3;

        /// <summary>The pods take turns every 40 frames; the live pod's spread comes every 50, the bared core's every 45; a ring every 150 after a 30-frame charge.</summary>
        private const int PodGap = 40, HalfGap = 50, BareGap = 45, RingGap = 150, Charge = 30, Blink = 20, HalfRingAt = 85, BareRingAt = 110;

        private const double PodHp = 20, CoreHp = 40;
        private const double BoltSpeed = 1.15, RingSpeed = 0.9, Fan = 0.28, MuzzleDrop = 5, DotRadius = 10;
        private const double EnterSpeed = 0.6, LeaveSpeed = 0.8, SwayRate = 0.03, GuardedShare = 0.05, BareShare = 0.125;
        private const int Widen = 90;

        /// <summary>The columns of the two pods' centres and of the core's centre in the drawing, and the row all three sit on.</summary>
        private const int PodLeftCol = 4, PodRightCol = 34, CoreCol = 19, CentreRow = 5;

        private const string PodLeftName = "pod-l", PodRightName = "pod-r", CoreName = "core";

        /// <summary>The widths of a pod and of the core in the drawing, and their hit boxes' half sizes.</summary>
        private const int PodW = 9, CoreW = 15, PodHalfW = PodW / 2, PodHalfH = 4, CoreHalfW = CoreW / 2, CoreHalfH = 5;
        private const int PodScore = 500, KillScore = 4000;
        /// <summary>How many pixels a pod's arc sags at its middle.</summary>
        private const double ArcSag = 2;
        /// <summary>A pod's gold centre flashes on and off every this many frames of its warning.</summary>
        private const int FlashFrames = 2;
        /// <summary>A pod's warning is white from this frame on, its last third.</summary>
        private const int BlinkWhiteFrom = Blink * 2 / 3;
        /// <summary>The core's charge whitens in three steps of this many frames.</summary>
        private const int ChargeThirdFrames = Charge / 3;

        private const int HalfRingCount = 8, BareRingCount = 10;
        private const uint BodyColour = 0x3f6fd0;

        /// <summary>The size a part's sound is given: a pod going down and a charge or a throw all sound at half the largest.</summary>
        private const double SoundSize = 0.5;

        private static readonly string[] Rows =
        [
            "................#######................",
            "..#####.......##ooooooo##.......#####..",
            ".#BBBBB#.....#oo+++++++oo#.....#BBBBB#.",
            "#BBbbbBB#...#oo++ooooo++oo#...#BBbbbBB#",
            "#BbbybbB#####o++oobbboo++o#####BbbybbB#",
            "#BbyyybB#ooo#o+oobbrbboo+o#ooo#BbyyybB#",
            "#BbbybbB#####o++oobbboo++o#####BbbybbB#",
            "#BBbbbBB#...#oo++ooooo++oo#...#BBbbbBB#",
            ".#BBBBB#.....#oo+++o+++oo#.....#BBBBB#.",
            "..#####.......##oo#o#oo##.......#####..",
            "................##...##................",
        ];

        // the pods and the core are cut from the whole drawing, each its full height, so the body and every part round alike
        private static readonly Sprite Whole = Sprite.FromArt(Rows, Art.With(('b', Palette.Shield), ('B', Palette.ShieldDeep)));
        private static readonly Sprite PodArt = Whole.Cut(0, 0, PodW, Whole.H);
        private static readonly Sprite PodArtR = Whole.Cut(Whole.W - PodW, 0, PodW, Whole.H);
        private static readonly Sprite CoreArt = Whole.Cut(CoreCol - CoreHalfW, 0, CoreW, Whole.H);
        private static readonly Sprite CoreOpenArt =
            Sprite.FromArt(Rows, Art.With(('b', Palette.WeakBody), ('B', Palette.ShieldDeep), ('r', Palette.WeakMark))).Cut(CoreCol - CoreHalfW, 0, CoreW, Whole.H);

        /// <summary>What one bulwark remembers: the rings it has thrown (they take turns leaving a gap straight down).</summary>
        private sealed class Kit
        {
            public int Rings;
        }

        public override EnemyKind Kind => EnemyKind.Bulwark;
        public override string Name => "BULWARK";
        public override Sprite Body => Whole;
        public override uint Colour => BodyColour;
        public override double Hp => PodHp * 2 + CoreHp;
        public override int Score => KillScore;
        public override bool Boss => true;

        // ---------------------------------------------------------------- the voice

        /// <summary>A hidden reply first (it speaks from behind something), then the oldest.</summary>
        public override int Taste(Enemy e, IReadOnlyList<RudePost> waiting)
        {
            for (int i = 0; i < waiting.Count; i++)
                if (waiting[i].Source == DarkSource.Hidden) return i;
            return 0;
        }

        // behind its shield it is in no hurry; bared it talks fast
        private const int ShieldedPace = 10 * Tuning.Fps, BaredPace = 4 * Tuning.Fps;

        private static bool Shielded(Enemy e) => Live(e.Parts?.Find(p => p.Name == PodLeftName)) || Live(e.Parts?.Find(p => p.Name == PodRightName));

        public override int IdleFrames(Enemy e) => Shielded(e) ? ShieldedPace : BaredPace;

        /// <summary>A post on arriving, and a new one cutting the old each time a pod is lost and when the core is bared. Behind its shield
        /// it says one thing and holds it, and the loss that bares it is said once, by the phase.</summary>
        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue)
        {
            bool shielded = Shielded(e);
            return cue switch
            {
                VoiceCue.PartLost => shielded ? Turn.Cut : Turn.Skip,
                VoiceCue.Phase => Turn.Cut,
                _ => base.TurnFor(g, e, cue),
            };
        }

        public override string? Line(Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive => "Replies are limited.",
            VoiceCue.PartLost => "That was load-bearing.",
            VoiceCue.Phase => "Fine. Say it to my face.",
            VoiceCue.Dying => "Unblocked.",
            _ => null,
        };

        // ---------------------------------------------------------------- the parts

        private static bool Live(Part? p) => p is { State: not PartState.Dead };

        public override void Spawn(RaiderGame g, Enemy e)
        {
            var core = new Part
            {
                Name = CoreName,
                OX = 0,
                OY = 0,
                HW = CoreHalfW,
                HH = CoreHalfH,
                Closed = CoreArt,
                OpenArt = CoreOpenArt,
                Wreck = Art.Wrecked(CoreArt),
                State = PartState.Armoured,
                Vital = true,
                Hp = CoreHp,
                MaxHp = CoreHp,
                Score = 0,   // the kill scores the bulwark's own KillScore
            };
            var left = new Part
            {
                Name = PodLeftName,
                OX = PodLeftCol - CoreCol,
                OY = 0,
                HW = PodHalfW,
                HH = PodHalfH,
                Closed = PodArt,
                Wreck = Art.Wrecked(PodArt),
                State = PartState.Open,
                Vital = true,
                Hp = PodHp,
                MaxHp = PodHp,
                Score = PodScore,
            };
            var right = new Part
            {
                Name = PodRightName,
                OX = PodRightCol - CoreCol,
                OY = 0,
                HW = PodHalfW,
                HH = PodHalfH,
                Closed = PodArtR,
                Wreck = Art.Wrecked(PodArtR),
                State = PartState.Open,
                Vital = true,
                Hp = PodHp,
                MaxHp = PodHp,
                Score = PodScore,
            };
            g.SetParts(e, core, left, right);
            e.Kit = new Kit();
        }

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            if (p.Name is not (PodLeftName or PodRightName)) return;
            g.Sound(SfxNames.Part, e.X + p.OX, SoundSize);
            bool left = Live(e.Parts?.Find(p => p.Name == PodLeftName)), right = Live(e.Parts?.Find(p => p.Name == PodRightName));
            if (left || right)
            {
                e.Mode = Half; e.ModeAge = 0;
                return;
            }
            e.Mode = Bare; e.ModeAge = 0;
            if (e.Parts?.Find(p => p.Name == CoreName) is { State: PartState.Armoured } core) core.State = PartState.Open;
            g.Cue(e, VoiceCue.Phase);
        }

        // ---------------------------------------------------------------- the machine

        public override void Update(RaiderGame g, Enemy e)
        {
            var kit = (Kit)e.Kit!;
            if (e.Leaving) e.Y -= LeaveSpeed;
            else if (e.Mode == Entering)
            {
                double hold = BossHoldRow(g, Whole.H);
                e.Y = Math.Min(hold, e.Y + EnterSpeed);
                if (e.Y >= hold) { e.Mode = Guarded; e.ModeAge = 0; }
            }

            // the sway is a twentieth of the field wide; bared, it eases out to an eighth
            double share = e.Mode == Bare ? GuardedShare + (BareShare - GuardedShare) * Math.Min(1, e.ModeAge / (double)Widen) : GuardedShare;
            double half = e.Sprite.W / 2.0;
            e.X = Math.Clamp(e.BaseX + Math.Sin(e.Age * SwayRate + e.Phase) * g.PW * share, half, g.PW - half);
            if (e.Leaving || e.Mode == Entering) return;

            int a = e.ModeAge;
            var l = e.Parts?.Find(p => p.Name == PodLeftName); var r = e.Parts?.Find(p => p.Name == PodRightName);
            switch (e.Mode)
            {
                case Guarded:
                    if (a > 0 && a % PodGap == 0)
                    {
                        var turn = (a / PodGap - 1) % 2 == 0 ? l : r;
                        if (Live(turn)) FireBolts(g, e, turn!.OX, fanned: false);
                    }
                    break;
                case Half:
                    if (a > 0 && a % HalfGap == 0 && (Live(l) ? l : Live(r) ? r : null) is { } pod) FireBolts(g, e, pod.OX, fanned: true);
                    Ring(g, e, kit, a, HalfRingAt, HalfRingCount);
                    break;
                case Bare:
                    if (a > 0 && a % BareGap == 0 && !Charging(a, BareRingAt)) FireBolts(g, e, 0, fanned: true);
                    Ring(g, e, kit, a, BareRingAt, BareRingCount);
                    break;
            }
        }

        /// <summary>True for the 30 frames before a ring (the mode clock modulo the ring gap).</summary>
        private static bool Charging(int age, int ringAt)
        {
            int c = age % RingGap;
            return c >= ringAt - Charge && c < ringAt;
        }

        /// <summary>The charge's sound at its first frame, and the ring on the frame after its last.</summary>
        private void Ring(RaiderGame g, Enemy e, Kit kit, int age, int ringAt, int count)
        {
            int c = age % RingGap;
            if (c == ringAt - Charge) g.Sound(SfxNames.Charge, e.X, SoundSize);
            if (c != ringAt) return;
            double speed = RingSpeed * Ramp.BoltSpeedMul(g.Stage), step = Math.Tau / count, start = RingStart(kit.Rings, count);
            for (int i = 0; i < count; i++)
            {
                double ang = start + i * step;
                g.Bullets.Add(new Bullet { X = e.X, Y = e.Y, VX = Math.Cos(ang) * speed, VY = Math.Sin(ang) * speed });
            }
            kit.Rings++;
        }

        /// <summary>The angle of a ring's first bolt: rings take turns leaving a gap straight down, then a bolt straight down.</summary>
        private static double RingStart(int rings, int count) =>
            Math.PI / 2 + (rings % 2 == 0 ? 0.5 : 0) * Math.Tau / count;

        /// <summary>An aimed bolt, or <paramref name="fanned"/> a spread of three, from a muzzle under the pod at <paramref name="ox"/>.</summary>
        private static void FireBolts(RaiderGame g, Enemy e, double ox, bool fanned)
        {
            g.LockAim(e);
            double speed = BoltSpeed * Ramp.BoltSpeedMul(g.Stage), x = e.X + ox, y = e.Y + MuzzleDrop;
            if (!fanned) { g.EnemyBolt(x, y, e.AimX, e.AimY, 0, speed); return; }
            for (int i = -1; i <= 1; i++) g.EnemyBolt(x, y, e.AimX, e.AimY, i * Fan, speed);
        }

        // ---------------------------------------------------------------- the warnings

        /// <summary>A pod's gold centre blinks for 20 frames, white for the last third: <paramref name="w"/> counts from 0 to 19.</summary>
        private static bool Lit(int w) => w >= BlinkWhiteFrom || w / FlashFrames % 2 == 1;

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Parts is null) return;
            int x0 = (int)Math.Round(e.X - Whole.W / 2.0), y0 = (int)Math.Round(e.Y - Whole.H / 2.0);
            var l = e.Parts.Find(p => p.Name == PodLeftName); var r = e.Parts.Find(p => p.Name == PodRightName);
            if (Live(l)) Arc(c, x0, y0, PodLeftCol);
            if (Live(r)) Arc(c, x0, y0, PodRightCol);

            int a = e.ModeAge;
            switch (e.Mode)
            {
                case Guarded:
                    {
                        int w = a % PodGap - (PodGap - Blink);
                        if (w >= 0 && Lit(w)) c.Set(x0 + ((a / PodGap) % 2 == 0 ? PodLeftCol : PodRightCol), y0 + CentreRow, Palette.Tell);
                        break;
                    }
                case Half:
                    {
                        int w = a % HalfGap - (HalfGap - Blink);
                        var pod = Live(l) ? l : Live(r) ? r : null;
                        if (pod is not null && w >= 0 && Lit(w)) c.Set(x0 + (pod == l ? PodLeftCol : PodRightCol), y0 + CentreRow, Palette.Tell);
                        DrawCharge(c, e, x0, y0, a, HalfRingAt, HalfRingCount);
                        break;
                    }
                case Bare:
                    {
                        int w = a % BareGap - (BareGap - Blink);
                        if (w >= 0 && Lit(w) && !Charging(a, BareRingAt)) foreach (int dx in CoreEnds) c.Set(x0 + CoreCol + dx, y0 + CentreRow, Palette.Tell);
                        DrawCharge(c, e, x0, y0, a, BareRingAt, BareRingCount);
                        break;
                    }
            }
        }

        /// <summary>The blue arc a live pod hangs on, sagging under the body between its centre column and the core's.</summary>
        private static void Arc(RaiderView.Canvas c, int x0, int y0, int from)
        {
            int to = CoreCol, lo = Math.Min(from, to), hi = Math.Max(from, to);
            for (int col = lo; col <= hi; col++)
            {
                double t = (double)(col - lo) / (hi - lo);
                c.Set(x0 + col, y0 + Whole.H + (int)Math.Round(ArcSag * Math.Sin(Math.PI * t)), Palette.Shield);
            }
        }

        /// <summary>The core whitens inward in thirds over the charge (its two ends, its rim, then all but the gold and the crimson beside it, which
        /// stay to say where to shoot), and a dot on a circle shows where each bolt will go.</summary>
        private static void DrawCharge(RaiderView.Canvas c, Enemy e, int x0, int y0, int age, int ringAt, int count)
        {
            if (!Charging(age, ringAt)) return;
            int third = (age % RingGap - (ringAt - Charge)) / ChargeThirdFrames;
            foreach (var (dx, dy) in Cluster)
            {
                bool mark = dy == 0 && Math.Abs(dx) <= 1, end = Math.Abs(dx) == CoreEnd, arm = dx == 0;
                if (!mark && (end || third >= 1 && !arm || third >= 2)) c.Set(x0 + CoreCol + dx, y0 + CentreRow + dy, Palette.Tell);
            }
            int rings = ((Kit)e.Kit!).Rings;
            double start = RingStart(rings, count), step = Math.Tau / count;
            for (int i = 0; i < count; i++)
            {
                double ang = start + i * step;
                c.Set((int)Math.Round(e.X + Math.Cos(ang) * DotRadius), (int)Math.Round(e.Y + Math.Sin(ang) * DotRadius), third >= 2 ? Palette.Tell : Palette.Crimson);
            }
        }

        /// <summary>How far either end of the core's middle row is from its gold, and the two ends: the aimed bolt's blink shows there.</summary>
        private const int CoreEnd = 2;
        private static readonly int[] CoreEnds = [-CoreEnd, CoreEnd];

        /// <summary>The core's centre as the drawing has it: three rows of three, five and three.</summary>
        private static readonly (int, int)[] Cluster =
        [
            (0, 0), (-1, 0), (1, 0), (0, -1), (0, 1), (-2, 0), (2, 0), (-1, -1), (1, -1), (-1, 1), (1, 1),
        ];
    }
}
