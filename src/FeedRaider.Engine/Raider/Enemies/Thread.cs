namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Thread (boss 4): a head and six cars that run across the field lane by lane. Only the last car can be hurt; when it goes
    /// the one before it opens, and with no car left the head opens, speeds up and shoots. Every part stays exactly 8 pixels from the
    /// part in front of it, so the train bends round a turn without stretching. A car that passes over the ship drops one slow bolt
    /// straight down, once a lane. After the last lane the train runs off the field, waits 40 frames out of sight and comes in again
    /// (it stays in the field's list, untouchable, so a fight never goes empty). It speaks the longest rude post it finds, and its
    /// header carries the page of a box that pages, "2/3".
    /// </summary>
    private sealed class ThreadBehaviour : EnemyBehaviour
    {
        private const int Cars = 6;
        private const double Gap = 8, LaneStep = 11, TurnMargin = 6, HeadHalf = HeadSize / 2.0;
        private const double CarSpeed = 0.9, CarSpeedPerLoss = 0.08, AloneSpeed = 1.4, DropSpeed = 0.9, AloneBoltSpeed = 1.2;
        private const int ParkFrames = 40, TurnWarnFrames = 36, FireEvery = 45, BlinkFrames = 24;
        /// <summary>The next lane's dotted line is white for its last frames before the turn, and the muzzle under a car for as many before its bolt drops.</summary>
        private const int TurnTellFrames = TurnWarnFrames / 3, DropTellFrames = 12;
        /// <summary>Low, so the fight is a short one at weapon level 2: the tail is hidden behind its own cars at every turn, and the park
        /// between passes costs more time than the shooting does.</summary>
        private const double HeadHp = 12, CarHp = 3;
        private const uint Pale = 0xc9a0ff, Shell = 0x6a4fc0;
        /// <summary>The path keeps this many points, half a pixel apart.</summary>
        private const int TrailLength = 240;
        /// <summary>The row it waits on out of sight; the fields at least <see cref="ThreeLaneH"/> and <see cref="TwoLaneH"/> pixels tall hold three and two lanes.</summary>
        private const double ParkY = -40;
        private const int ThreeLaneH = 80, TwoLaneH = 48;
        /// <summary>Every field holds one lane, and one more for each of these heights it reaches.</summary>
        private static readonly int[] ExtraLaneH = [TwoLaneH, ThreeLaneH];
        /// <summary>A car drops its bolt when within <see cref="DropReach"/> pixels of the ship across, <see cref="DropBelow"/> pixels under its middle.</summary>
        private const double DropReach = 2, DropBelow = 4, WarnVolume = 0.33;
        /// <summary>The head is a <see cref="HeadSize"/> square and a car a <see cref="CarSize"/> square; their boxes are half that, rounded down.</summary>
        private const int HeadSize = 9, CarSize = 7, CarY = 1, CarX = 10, CarOpenX = 26, HeadBox = HeadSize / 2, CarBox = CarSize / 2, CarScore = 300, VisorSize = 3;
        private const double TrailSpacing = 0.5, FirstLaneShare = 0.12, FirstLaneMin = 10;
        /// <summary>A path step shorter than this is a repeated point, with no direction to lay a part along.</summary>
        private const double TinySegment = 1e-12;
        /// <summary>The dots of the next lane's line are this many pixels apart.</summary>
        private const int LaneDotStep = 2;
        /// <summary>A lost car is always a turn of the voice once half the cars are gone, and one car either side of that only when a post is waiting.</summary>
        private const int HalfwayLoss = Cars / 2;
        private const int BossScore = 5000;
        /// <summary>The head's part name: every other part is a car.</summary>
        private const string HeadName = "head";
        /// <summary>A part counts as gone once it is more than this many pixels past the field's edge.</summary>
        private const int OffFieldMargin = 1;
        /// <summary>A head off the field when its bolt comes due tries again this many frames later.</summary>
        private const int OffFieldFireRetry = 1;
        /// <summary>A fresh pass puts the head's leading edge this far onto the field.</summary>
        private const double EntryInset = 0.5;

        // a head of 9 by 9 and three cars of 7 by 7 in one picture; a car in armour is cut from the middle one, and the car
        // that is open from the last, its gold middle ringed in crimson
        private static readonly string[] Rows =
        [
            "..#####..........................",
            ".#VVVVV#...#####...#####...#####.",
            "#VVvvvVV#.#VVVVV#.#VVVVV#.#VVVVV#",
            "#VvbbbvV#.#VvvvV#.#VvvvV#.#VvvvV#",
            "#VvbrbvV###VvVvV###VvVvV###VvyvV#",
            "#VvbbbvV#.#VvvvV#.#VvvvV#.#VvvvV#",
            "#VVvvvVV#.#VVVVV#.#VVVVV#.#VVVVV#",
            ".#VVVVV#...#####...#####...#####.",
            "..#####..........................",
        ];

        private static Dictionary<char, uint> ArtColours(uint inner, uint visor, uint pupil) =>
            Art.With(('v', inner), ('V', Shell), ('b', visor), ('r', pupil), ('y', Palette.WeakMark));

        // armoured, the head's visor is shield blue; a part that a shot hurts trades it for crimson round a gold middle
        private static readonly Sprite Whole = Sprite.FromArt(Rows, ArtColours(Pale, Palette.Shield, Palette.ShieldGlow));
        private static readonly Sprite WholeHeadOpen = Sprite.FromArt(Rows, ArtColours(Pale, Palette.WeakBody, Palette.WeakMark));
        private static readonly Sprite WholeCarOpen = Sprite.FromArt(Rows, ArtColours(Palette.WeakBody, Palette.WeakBody, Palette.WeakMark));
        private static readonly Sprite HeadArt = Whole.Cut(0, 0, HeadSize, HeadSize);
        private static readonly Sprite HeadOpenArt = WholeHeadOpen.Cut(0, 0, HeadSize, HeadSize);
        private static readonly Sprite CarArt = Whole.Cut(CarX, CarY, CarSize, CarSize);
        private static readonly Sprite CarOpenArt = WholeCarOpen.Cut(CarOpenX, CarY, CarSize, CarSize);

        /// <summary>What one thread remembers: where every part is (absolute, so the chain never drifts), its lane, and its timers.</summary>
        private sealed class Run
        {
            public readonly double[] X = new double[Cars + 1], Y = new double[Cars + 1];
            /// <summary>Where the head has been, newest first. Every car sits on this path, where it is 8 pixels from the one before it.</summary>
            public readonly List<(double X, double Y)> Trail = [];
            public int Lane, Lanes, Dir = 1, Parked, FireIn = FireEvery, DropLane = -1;
            public bool Dropping;
            public double TargetY;
        }

        public override EnemyKind Kind => EnemyKind.Thread;
        public override string Name => "THREAD";
        public override Sprite Body => HeadArt;
        public override uint Colour => Pale;
        public override double Hp => HeadHp + Cars * CarHp;
        public override int Score => BossScore;
        public override bool Boss => true;

        private static double FirstLane(RaiderGame g) => Math.Max(FirstLaneMin, FirstLaneShare * g.PH);
        private static int LaneCount(RaiderGame g) => 1 + ExtraLaneH.Count(h => g.PH >= h);

        public override void Spawn(RaiderGame g, Enemy e)
        {
            double m = e.Hp / Hp;   // a gate boss on a later lap is tougher; every part carries its share
            var parts = new List<Part>
            {
                new()
                {
                    Name = HeadName, HW = HeadBox, HH = HeadBox, Closed = HeadArt, OpenArt = HeadOpenArt, Wreck = Art.Wrecked(HeadArt),
                    State = PartState.Armoured, Vital = true, Hp = HeadHp * m, MaxHp = HeadHp * m, Score = 0,   // the kill pays the thread's own score
                },
            };
            for (int n = 1; n <= Cars; n++)
                parts.Add(new Part
                {
                    Name = "car" + n,
                    HW = CarBox,
                    HH = CarBox,
                    Closed = CarArt,
                    OpenArt = CarOpenArt,
                    Wreck = null,
                    State = n == Cars ? PartState.Open : PartState.Armoured,
                    Vital = true,
                    Hp = CarHp * m,
                    MaxHp = CarHp * m,
                    Score = CarScore,
                });
            g.SetParts(e, [.. parts]);
            var k = new Run();
            e.Kit = k;
            Enter(g, e, k);
        }

        /// <summary>A fresh pass: lane 0 from the left edge, every living part laid in a line behind the head.</summary>
        private static void Enter(RaiderGame g, Enemy e, Run k)
        {
            k.Lane = 0; k.Lanes = LaneCount(g); k.Dir = 1; k.Dropping = false; k.DropLane = -1; k.Parked = 0;
            e.Untouchable = false;
            e.X = e.BaseX = -HeadHalf + EntryInset;
            e.Y = FirstLane(g);
            k.Trail.Clear();
            for (int i = 0; i < TrailLength; i++) k.Trail.Add((e.X - TrailSpacing * i, e.Y));
            Lay(e, k);
        }

        private static void Place(Enemy e, Run k)
        {
            var parts = e.Parts!;
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i].OX = k.X[i] - e.X;
                parts[i].OY = k.Y[i] - e.Y;
            }
        }

        /// <summary>How fast the train runs: faster for each car lost, and faster still for the head alone.</summary>
        private static double Speed(int carsLeft) => carsLeft > 0 ? CarSpeed + CarSpeedPerLoss * (Cars - carsLeft) : AloneSpeed;

        private static int CarsLeft(Enemy e) => e.Parts!.Count(p => p.Name != HeadName && p.State != PartState.Dead);

        /// <summary>The head's step is added to the path, and every living part is laid on it.</summary>
        private static void Follow(Enemy e, Run k)
        {
            k.Trail.Insert(0, (e.X, e.Y));
            if (k.Trail.Count > TrailLength) k.Trail.RemoveRange(TrailLength, k.Trail.Count - TrailLength);
            Lay(e, k);
        }

        /// <summary>Each living part goes to the point of the path that is exactly <see cref="Gap"/> pixels (as the crow flies) from the
        /// part in front of it, so the train bends round a turn with its spacing whole and each car runs where the head ran.</summary>
        private static void Lay(Enemy e, Run k)
        {
            var parts = e.Parts!;
            var trail = k.Trail;
            double px = trail[0].X, py = trail[0].Y, sx = px, sy = py;
            k.X[0] = px; k.Y[0] = py;
            int seg = 0;
            for (int i = 1; i < parts.Count; i++)
            {
                if (parts[i].State == PartState.Dead) continue;
                bool found = false;
                for (int j = seg; j < trail.Count - 1 && !found; j++)
                {
                    var (ax, ay) = j == seg ? (sx, sy) : trail[j];
                    var (bx, by) = trail[j + 1];
                    double dx = bx - ax, dy = by - ay, fx = ax - px, fy = ay - py;
                    double a = dx * dx + dy * dy;
                    if (a < TinySegment) continue;
                    if ((bx - px) * (bx - px) + (by - py) * (by - py) < Gap * Gap) continue;
                    double b = 2 * (fx * dx + fy * dy), c = fx * fx + fy * fy - Gap * Gap;
                    double t = Math.Clamp((-b + Math.Sqrt(Math.Max(0, b * b - 4 * a * c))) / (2 * a), 0, 1);
                    sx = ax + t * dx; sy = ay + t * dy;
                    seg = j;
                    found = true;
                }
                if (!found) { sx = trail[^1].X; sy = trail[^1].Y; }
                px = sx; py = sy;
                k.X[i] = px; k.Y[i] = py;
            }
            Place(e, k);
        }

        public override void Update(RaiderGame g, Enemy e)
        {
            var k = (Run)e.Kit!;
            var parts = e.Parts!;
            if (k.Parked > 0)
            {
                if (--k.Parked == 0) Enter(g, e, k);
                return;
            }
            int cars = CarsLeft(e);
            double speed = Speed(cars);
            bool lastLane = k.Lane == k.Lanes - 1;
            if (k.Dropping)
            {
                e.Y = Math.Min(k.TargetY, e.Y + speed);
                if (e.Y >= k.TargetY)
                {
                    k.Dropping = false;
                    k.Lane++;
                    k.Dir = -k.Dir;
                }
            }
            else
            {
                e.X += k.Dir * speed;
                if (!lastLane && (k.Dir > 0 ? e.X + HeadHalf >= g.PW - TurnMargin : e.X - HeadHalf <= TurnMargin))
                {
                    e.X = k.Dir > 0 ? g.PW - TurnMargin - HeadHalf : TurnMargin + HeadHalf;
                    k.Dropping = true;
                    k.TargetY = FirstLane(g) + LaneStep * (k.Lane + 1);
                }
            }
            e.BaseX = e.X;
            Follow(e, k);

            if (!k.Dropping && g.Alive && k.DropLane != k.Lane)
                for (int i = 1; i < parts.Count; i++)
                    if (parts[i].State != PartState.Dead && Math.Abs(k.X[i] - g.PX) <= DropReach && k.X[i] >= 0 && k.X[i] <= g.PW)
                    {
                        g.Bullets.Add(new Bullet { X = k.X[i], Y = k.Y[i] + DropBelow, VX = 0, VY = DropSpeed });
                        k.DropLane = k.Lane;
                        break;
                    }

            if (cars == 0 && parts[0].State == PartState.Open && --k.FireIn <= 0)
            {
                if (e.X >= 0 && e.X <= g.PW)
                {
                    g.LockAim(e);
                    g.EnemyBolt(e.X, e.Y + HeadHalf, e.AimX, e.AimY, 0, AloneBoltSpeed);
                    k.FireIn = FireEvery;
                }
                else k.FireIn = OffFieldFireRetry;
            }
            else if (cars == 0 && k.FireIn == BlinkFrames) g.Sound(SfxNames.Charge, e.X, WarnVolume);

            if (!k.Dropping && lastLane && Gone(g, e, k)) Park(e, k);
        }

        /// <summary>True when every living part is past the edge the train is running toward.</summary>
        private static bool Gone(RaiderGame g, Enemy e, Run k)
        {
            var parts = e.Parts!;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].State == PartState.Dead) continue;
                if (k.Dir > 0 ? k.X[i] - parts[i].HW <= g.PW + OffFieldMargin : k.X[i] + parts[i].HW >= -OffFieldMargin) return false;
            }
            return true;
        }

        private void Park(Enemy e, Run k)
        {
            k.Parked = ParkFrames;
            e.Untouchable = true;
            e.X = e.BaseX = 0;
            e.Y = ParkY;
            foreach (var p in e.Parts!) { p.OX = 0; p.OY = 0; }
        }

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            if (p.Name == HeadName) return;
            var k = (Run)e.Kit!;
            var next = e.Parts!.LastOrDefault(q => q.Name != HeadName && q.State != PartState.Dead);
            if (next is not null) { next.State = PartState.Open; return; }
            e.Parts![0].State = PartState.Open;   // the last car is gone: the head opens
            k.FireIn = FireEvery;
            g.Cue(e, VoiceCue.Phase);
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Untouchable) return;
            var k = (Run)e.Kit!;
            var parts = e.Parts!;
            int front = 0;
            for (int i = 1; i < parts.Count; i++)
            {
                if (parts[i].State == PartState.Dead) continue;
                c.Set((int)Math.Round((k.X[front] + k.X[i]) / 2), (int)Math.Round((k.Y[front] + k.Y[i]) / 2), Palette.Plum);
                front = i;
            }
            if (!k.Dropping && k.Lane < k.Lanes - 1)
            {
                double speed = Speed(CarsLeft(e));
                double left = k.Dir > 0 ? c.Game.PW - TurnMargin - HeadHalf - e.X : e.X - HeadHalf - TurnMargin;
                if (left / speed <= TurnWarnFrames)
                {
                    int row = (int)Math.Round(FirstLane(c.Game) + LaneStep * (k.Lane + 1));
                    uint colour = left / speed <= TurnTellFrames ? Palette.Tell : Pale;
                    for (int x = 0; x < c.Width; x += LaneDotStep) c.Set(x, row, colour);
                }
            }
            if (!k.Dropping && c.Game.Alive && k.DropLane != k.Lane)
            {
                double reach = DropReach + Speed(CarsLeft(e)) * DropTellFrames;
                for (int i = 1; i < parts.Count; i++)
                    if (parts[i].State != PartState.Dead && Math.Abs(k.X[i] - c.Game.PX) <= reach)
                        c.Set((int)Math.Round(k.X[i]), (int)Math.Round(k.Y[i] + DropBelow), Palette.Tell);
            }
            if (parts[0].State == PartState.Open && k.FireIn <= BlinkFrames)
                c.Rect((int)Math.Round(e.X) - VisorSize / 2, (int)Math.Round(e.Y) - VisorSize / 2, VisorSize, 1, Palette.Tell);   // the visor's top row: the gold under it stays in sight
        }

        // ---------------------------------------------------------------- the voice

        public override string? Line(Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive => "A thread. 1/7",
            VoiceCue.PartLost => "4/7 And furthermore",
            VoiceCue.Phase => "7/7 In conclusion",
            VoiceCue.Dying => "Muted.",
            _ => null,
        };

        /// <summary>The longest post waiting (the oldest of equals): a thread likes a long sentence.</summary>
        public override int Taste(Enemy e, IReadOnlyList<RudePost> waiting)
        {
            int best = 0;
            for (int i = 1; i < waiting.Count; i++) if (waiting[i].Text.Length > waiting[best].Text.Length) best = i;
            return best;
        }

        // a thread is read in instalments: few lines to a page, and a long breath before the next post
        private const int ThreadPace = 8 * Tuning.Fps;

        public override int IdleFrames(Enemy e) => ThreadPace;

        public override int PageCap => SpeechBoxText.MinPageLines;

        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue)
        {
            switch (cue)
            {
                case VoiceCue.PartLost:
                    int lost = e.Parts!.Count(p => p.Name != HeadName && p.State == PartState.Dead);
                    return lost == HalfwayLoss || (Math.Abs(lost - HalfwayLoss) == 1 && g.Rude.Waiting.Count > 0) ? Turn.Wait : Turn.Skip;
                default:
                    return base.TurnFor(g, e, cue);
            }
        }

        /// <summary>The page of the box that is open, over the number of pages; a box of one page has no count.</summary>
        public override string? Prefix(RaiderGame g, Enemy e)
        {
            if (e.SpeechLines is not { Length: > 0 } lines) return null;
            int pages = SpeechBoxText.Pages(lines, e.PageLines);
            if (pages <= 1) return null;
            var (page, _) = SpeechBoxText.Progress(lines, e.PageLines, e.SpeechBoxAge);
            return $"{page + 1}/{pages}";
        }
    }
}
