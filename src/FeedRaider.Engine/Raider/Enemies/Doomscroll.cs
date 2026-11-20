namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// Doomscroll (boss 10, the last): a feed seen from below, three fights in one body. First its crown of guns, which blink for 24
    /// frames and then shoot at the ship; shoot them all and the crown comes down as dust and the mouth begins, shut for 70 frames and
    /// open for 60, spitting a scroll bar (a wall of slow bolts with one gap of 14, marked in gold from the moment the mouth shuts) while
    /// it is shut; shoot the mouth open and the husk falls away from the heart, which hops between five anchors and throws a ring of ten
    /// bolts as it lands, with a scroll bar after every third hop. It stands still for the first two fights, so the screen changes only
    /// by its bolts and the ship; it speaks the feed's rude posts in order, and each scroll bar cuts the post on show to start the next.
    /// </summary>
    private sealed class DoomscrollBehaviour : EnemyBehaviour
    {
        private const int Crown = 0, CrownFalls = 1, MawShut = 2, MawOpen = 3, HuskFalls = 4, Heart = 5;
        private const int CrownFallFrames = 60, ShutFrames = 70, OpenFrames = 60, HuskFallFrames = 45;
        private const int BarFrame = 40, TeethWarn = 20, Blink = 24, GunGap = 20, GunRest = 60;
        private const int HopCycle = 62, HopRest = 50, HopFrames = 12, HopWarn = 24, BarEvery = 3, RingBolts = 10;
        private const double EnterSpeed = 0.6, GunSpeed = 1.15, BarSpeed = 0.8, RingSpeed = 1.0, TotalHp = GunsHp + MawHp + HeartHp, HeartHp = 54;
        /// <summary>The share of the guns', the maw's and the heart's hit points together that the boss is given, so its fight sits at the short end of the time a boss fight should take.</summary>
        private const double HpScale = 0.46;
        private const double GunsHp = 126, MawHp = 84, LeaveSpeed = 0.8;
        /// <summary>An armoured part is never hurt; its hit point only keeps it among the living parts.</summary>
        private const double ShellHp = 1;
        private const int KillScore = 20000;
        private const uint BodyColour = 0xc2185b;
        /// <summary>How far short of its row a landing may stop: a sum of 0.6s can fall a hair short.</summary>
        private const double LandingSlack = 1e-9;
        /// <summary>A gun's warning pixel blinks until this many frames before it fires, then holds.</summary>
        private const int GunSolidFrames = 8;
        /// <summary>The field the whole drawing fits, three gun segments a side and tall: the body the roster shows.</summary>
        private const int SketchFieldW = 96, SketchFieldH = 120;
        /// <summary>The short form: the chin's two rows from row 13, the upper 29 wide with its sides drawn, the lower 27 of outline; above them the drawing lacks its two rows of spikes.</summary>
        private const int ChinRow = 13, ChinTopW = 29, ChinBottomW = 27, SpikeRows = 2;
        /// <summary>The drawing across: a cap of 3 columns, then its 3 gun segments a side of <see cref="GunPitch"/> each, then the middle.</summary>
        private const int CapCols = 3, SketchSegments = 3, SketchMiddleCol = CapCols + GunPitch * SketchSegments;
        /// <summary>How far a bar's gap keeps from the field's edges, and how far past the middle it may reach into the other half.</summary>
        private const int GapMargin = 5;
        private const int GunsScore = 3600, MawScore = 3000, MawW = 27, MawH = 7, GunW = 7, GunH = 6, TeethRow = 3, MirrorCol = 90, DownBursts = 90;

        /// <summary>The drawing's size rule: a fixed core of 37 columns plus 18 for each gun segment (one to five), a tall body of 29 rows or a short one of 15, and a field is tall from 80 rows.</summary>
        private const int CoreWidth = 37, SegmentWidth = 18, MaxSegments = 5, TallBodyRows = 29, ShortBodyRows = 15, TallFieldRows = 80;
        /// <summary>Where the parts sit in the drawing: the gun pitch, the rows of the crown, the mouth and the jaw, and the jaw's size.</summary>
        private const int GunPitch = 9, LeftGunInset = 4, RightGunBase = 35, MawInset = 5, JawInset = 3;
        private const int CrownRowsTall = 7, CrownRowsShort = 5, MawRowTall = 8, MawRowShort = 6, JawTop = 15, JawW = 31, JawH = 14;
        /// <summary>The floor under the body: the larger of 13 rows and 36 hundredths of the field; the body's top is at most on row 6.</summary>
        private const int FloorMinRows = 13, MaxTopRow = 6;
        private const double FloorShare = 0.36;
        /// <summary>Rows below a gun's top where its bolt leaves and where the warning pixel shows.</summary>
        private const int GunMuzzleRows = 6, GunFlashRows = 4;
        /// <summary>Behind the open teeth every fourth pixel of the crimson glow is gold, from the second.</summary>
        private const int MawMarkEvery = 4, MawMarkAt = 1;
        private const double MaxAimAngle = 1.1;
        /// <summary>The sizes sounds are given: a part or a phase is large, a warning middling.</summary>
        private const double BigSound = 2, MidSound = 1;
        /// <summary>The heart's anchors: the highest row its centre may take, how far the odd anchors drop on a tall field, its gap above the floor and the field's edge margin.</summary>
        private const int HeartMinRow = 11, HeartDrop = 14, HeartAnchorFloorGap = 8, AnchorEdge = 8, HeartRadius = 7, HeartHalfBox = 8;
        private const double HeartArcHeight = 6;
        /// <summary>A scroll bar's bolt spacing and gap width, where the bar from the heart starts, and the mouth's reach either side of its centre.</summary>
        private const int BarSpacing = 6, BarGap = 14, HeartBarRow = 6, MouthHalfWidth = 15, MouthSpan = 30, GapMarkerOffset = 7, GapMarkerTopRow = 8;
        private const int WarnDots = 32, WarnRingRadius = 12;
        private const double BurstSpeedMin = 0.4, BurstSpeedSpread = 2.0, BurstLift = 0.3, BurstSpread = 10, BurstSizeTwoShare = 0.3, BurstCrimsonShare = 0.4, BurstGoldShare = 0.5;
        private const int BurstLife = 20, BurstLifeSpread = 30;
        /// <summary>What one of its bolts takes from the ship, whether a gun's, a scroll bar's or the heart's ring.</summary>
        private const double BoltDmg = 1;
        /// <summary>The least drop a gun aims over, so a ship level with or above the muzzle is still shot at downward.</summary>
        private const double MinAimDrop = 1;
        /// <summary>A gun's warning pixel, while it blinks, is lit every this many frames.</summary>
        private const int GunBlinkPeriod = 2;
        /// <summary>The gold column that marks a bar's gap is dotted, a pixel every this many rows.</summary>
        private const int MarkerDotStep = 2;
        /// <summary>The heart's warning is white for the last third of <see cref="HopWarn"/>.</summary>
        private const int HopWarnWhiteFrames = HopWarn / 3;
        /// <summary>The colour of a sprite pixel without its opacity bits.</summary>
        private const uint RgbMask = 0xFFFFFF;
        /// <summary>The names its parts go by: each gun's starts with the prefix.</summary>
        private const string GunNamePrefix = "gun-", MawName = "maw", CrownName = "crown", JawName = "jaw", HeartName = "heart";

        /// <summary>The tall drawing, for a crown of three guns a side; every other size is cut from it by the rule in <see cref="SketchChar"/>.</summary>
        private static readonly string[] TallSketch =
        [
            "#......#........#........#.................#.#.#.................#........#........#......#",
            "##....###......###......###..............#########..............###......###......###....##",
            "######ooo######ooo######ooo######ooooooooooooooooooooooooo######ooo######ooo######ooo######",
            "#ooooo+ooo+oooo+ooo+oooo+ooo+oooo+ooooo+ooooooooooo+ooooo+oooo+ooo+oooo+ooo+oooo+ooo+ooooo#",
            "#o+o+++o+++oo+++o+++oo+++o+++oo+++o+o+++o+o+o+o+o+++o+o+++o+oo+++o+++oo+++o+++oo+++o+++o+o#",
            "#ooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooooo#",
            ".#########################################################################################.",
            "....#ooroo#..#ooroo#..#ooroo#.#ooooooooooooooooooooooooooooo#.#ooroo#..#ooroo#..#ooroo#....",
            "....#oryro#..#oryro#..#oryro#.#o###########################o#.#oryro#..#oryro#..#oryro#....",
            "....#orrro#..#orrro#..#orrro#.#o#o#o#o#o#o#o#o#o#o#o#o#o#o#o#.#orrro#..#orrro#..#orrro#....",
            ".....#oro#....#oro#....#oro#..#oo#o#o#o#o#o#o#o#o#o#o#o#o#oo#..#oro#....#oro#....#oro#.....",
            "......#r#......#r#......#r#...#o###########################o#...#r#......#r#......#r#......",
            ".......#........#........#....#oo#o#o#o#o#o#o#o#o#o#o#o#o#oo#....#........#........#.......",
            "..............................#o#o#o#o#o#o#o#o#o#o#o#o#o#o#o#..............................",
            "..............................#o###########################o#..............................",
            "..............................#ooooooooooooooooooooooooooooo#..............................",
            "...............................#ooo+++++++++++++++++++++ooo#...............................",
            "...............................#oooo+ooooooooooooooooo+oooo#...............................",
            "................................#oooo+++++++++++++++++oooo#................................",
            "................................#ooooo+ooooooooooooo+ooooo#................................",
            ".................................#ooooo+++++++++++++ooooo#.................................",
            "..................................#ooooo+ooooooooo+ooooo#..................................",
            "...................................#ooooo+++++++++ooooo#...................................",
            "....................................##ooooo+ooo+ooooo##....................................",
            "......................................##ooooooooooo##......................................",
            "........................................##ooooooo##........................................",
            "..........................................##ooo##..........................................",
            "............................................###............................................",
            ".............................................#.............................................",
        ];

        private static readonly string[] HeartRows =
        [
            ".....#####.....",
            "...##rrrrr##...",
            "..#rrrrrrrrr#..",
            ".#rrryyyyyrrr#.",
            ".#rryyyyyyyrr#.",
            "#rryyyrrryyyrr#",
            "#rryyrrrrryyrr#",
            "#rryyrrrrryyrr#",
            "#rryyrrrrryyrr#",
            "#rryyyrrryyyrr#",
            ".#rryyyyyyyrr#.",
            ".#rrryyyyyrrr#.",
            "..#rrrrrrrrr#..",
            "...##rrrrr##...",
            ".....#####.....",
        ];

        private static readonly Sprite HeartArt = Sprite.FromArt(HeartRows, Art.DarkPal);
        /// <summary>The heart while the husk still falls off it and no bolt reaches it: all crimson, its gold not yet lit.</summary>
        private static readonly Sprite HeartUnlitArt = Sprite.FromArt(HeartRows, Art.With(('y', Palette.WeakBody)));
        private static readonly Sprite HeartField = new(HeartArt.W, HeartArt.H);   // the heart is its own part; the body it hangs from has nothing to show
        private static readonly double[] AnchorShare = [0.50, 0.18, 0.82, 0.34, 0.66];

        /// <summary>Where everything of it lies on one field, worked out from the size of the field.</summary>
        private sealed class Geo
        {
            public readonly int Pw, Ph, N, W, H, Left, Top, CrownH, GunTop, MawTop, Floor;
            public readonly bool Tall;

            public Geo(int pw, int ph)
            {
                Pw = pw; Ph = ph;
                N = Math.Clamp((pw - CoreWidth) / SegmentWidth, 1, MaxSegments);
                Tall = ph >= TallFieldRows;
                W = CoreWidth + SegmentWidth * N;
                H = Tall ? TallBodyRows : ShortBodyRows;
                Left = (pw - W) / 2;
                Floor = (int)(ph - Math.Max(FloorMinRows, FloorShare * ph));
                Top = Math.Min(MaxTopRow, Floor - H + 1);
                CrownH = Tall ? CrownRowsTall : CrownRowsShort;
                GunTop = CrownH;
                MawTop = Tall ? MawRowTall : MawRowShort;
            }

            public double Cx => Left + W / 2.0;
            public double Cy => Top + H / 2.0;
            public int GunLeft(bool right, int i) => right ? RightGunBase + GunPitch * N + GunPitch * i : LeftGunInset + GunPitch * i;
            public int MawLeft => MawInset + GunPitch * N;
            public int JawLeft => JawInset + GunPitch * N;
            public int GunFlashRow => Top + GunTop + GunFlashRows;

            public bool InGun(int x, int y)
            {
                if (y < GunTop || y >= GunTop + GunH) return false;
                for (int i = 0; i < N; i++)
                {
                    int l = GunLeft(false, i), r = GunLeft(true, i);
                    if (x >= l && x < l + GunW || x >= r && x < r + GunW) return true;
                }
                return false;
            }

            public bool InPlate(int x, int y) => y < CrownH || InGun(x, y);
            public bool InMaw(int x, int y) => x >= MawLeft && x < MawLeft + MawW && y >= MawTop && y < MawTop + MawH;
            public bool InJaw(int x, int y) => Tall && x >= JawLeft && x < JawLeft + JawW && y >= JawTop && y < JawTop + JawH;
        }

        /// <summary>The drawing rebuilt by its own rule: a cap, n gun segments, the maw and the mirror; the short form drops the two rows of spikes and closes the mouth with a chin.</summary>
        private static char SketchChar(Geo g, int x, int y)
        {
            int w = g.W;
            if (!g.Tall && y >= ChinRow)
            {
                int len = y == ChinRow ? ChinTopW : ChinBottomW, cx = x - (w - len) / 2;
                if (cx < 0 || cx >= len) return '.';
                return y == ChinRow ? (cx == 0 || cx == len - 1 ? '#' : 'o') : '#';
            }
            int ty = g.Tall ? y : y + SpikeRows;
            bool right = x > (w - 1) / 2;   // the right half reads the drawing's right half: the maw is not quite its own mirror
            int xl = right ? w - 1 - x : x;
            int sx = xl < CapCols ? xl : xl < CapCols + GunPitch * g.N ? CapCols + (xl - CapCols) % GunPitch : SketchMiddleCol + (xl - CapCols - GunPitch * g.N);
            return TallSketch[ty][right ? MirrorCol - sx : sx];
        }

        private static Sprite BuildSprite(Geo g)
        {
            var rows = new string[g.H];
            for (int y = 0; y < g.H; y++)
            {
                var row = new char[g.W];
                for (int x = 0; x < g.W; x++) row[x] = SketchChar(g, x, y);
                rows[y] = new string(row);
            }
            return Sprite.FromArt(rows, Art.DarkPal);
        }

        /// <summary>What one Doomscroll remembers.</summary>
        private sealed class Fight(Geo geo, double scale)
        {
            public readonly Geo G = geo;
            public readonly double Scale = scale;
            public Sprite Whole = null!, Frame = null!, ShutArt = null!, WarnArt = null!, OpenArt = null!, JawArt = null!;
            public Part Maw = null!;
            public Part? Jaw;
            public bool Landed;
            public int CycleFrame;                              // frames of the crown's cycle, 0 on the step it comes to rest
            public readonly List<(int X, double Y)> Pending = [];   // bolts of a bar still behind the jaw
            public bool HaveLast, LastRight;
            public int GapLeft;
            public PixelDust? Dust;
            public (int X, int Y)[] Anchors = [];
            public double HuskX, HuskY;
        }

        public override EnemyKind Kind => EnemyKind.Doomscroll;
        public override string Name => "DOOMSCROLL";
        public override Sprite Body { get; } = BuildSprite(new Geo(SketchFieldW, SketchFieldH));
        public override uint Colour => BodyColour;
        public override double Hp => TotalHp * HpScale;
        public override int Score => KillScore;
        public override bool Boss => true;

        // ---------------------------------------------------------------- the voice

        public override string? Line(Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive => "Keep scrolling.",
            VoiceCue.Phase => "Just one more.",
            VoiceCue.Opens => "You can stop any time.",
            VoiceCue.Dying => "Refresh.",
            _ => null,
        };

        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.PartLost => Turn.Skip,   // a gun going is not an event: the crown's fall is
            // a scroll bar flicks the post on show away for the next (the mouth raises Idle for it); in the heart nothing cuts in
            VoiceCue.Idle => e.Mode is MawShut or MawOpen ? Turn.Cut : e.Mode is HuskFalls ? Turn.Skip : Turn.Wait,
            _ => base.TurnFor(g, e, cue),
        };

        // ---------------------------------------------------------------- birth

        public override void Spawn(RaiderGame g, Enemy e)
        {
            var geo = new Geo(g.PW, g.PH);
            var k = new Fight(geo, e.Hp / TotalHp) { Whole = BuildSprite(geo) };
            e.Kit = k;
            Cut(k);
            e.Sprite = k.Frame;
            e.X = e.BaseX = geo.Cx;
            e.Y = -geo.H / 2.0;

            double gunHp = GunsHp * k.Scale / (2 * geo.N);
            int gunScore = GunsScore / (2 * geo.N);
            var parts = new List<Part> { MakePart(geo, CrownName, 0, 0, geo.W, geo.CrownH, k.Whole.Cut(0, 0, geo.W, geo.CrownH), PartState.Armoured, false, ShellHp, 0) };
            foreach (bool right in new[] { false, true })
                for (int i = 0; i < geo.N; i++)
                {
                    var art = k.Whole.Cut(geo.GunLeft(right, i), geo.GunTop, GunW, GunH);
                    var gun = MakePart(geo, $"{GunNamePrefix}{(right ? 'r' : 'l')}-{i}", geo.GunLeft(right, i), geo.GunTop, GunW, GunH, art, PartState.Open, true, gunHp, gunScore);
                    gun.Wreck = Art.Wrecked(art);
                    parts.Add(gun);
                }
            k.Maw = MakePart(geo, MawName, geo.MawLeft, geo.MawTop, MawW, MawH, k.ShutArt, PartState.Armoured, true, MawHp * k.Scale, MawScore);
            k.Maw.OpenArt = k.OpenArt;
            parts.Add(k.Maw);
            if (geo.Tall)
            {
                k.Jaw = MakePart(geo, JawName, geo.JawLeft, JawTop, JawW, JawH, k.JawArt, PartState.Armoured, false, ShellHp, 0);
                parts.Add(k.Jaw);
            }
            k.Anchors = Anchors(geo);
            g.SetParts(e, [.. parts]);
        }

        private static Part MakePart(Geo geo, string name, int l, int t, int w, int h, Sprite art, PartState state, bool vital, double hp, int score) => new()
        {
            Name = name,
            OX = l + w / 2.0 - geo.W / 2.0,
            OY = t + h / 2.0 - geo.H / 2.0,
            HW = w / 2,
            HH = h / 2,
            Closed = art,
            State = state,
            Vital = vital,
            Hp = hp,
            MaxHp = hp,
            Score = score,
        };

        /// <summary>Cuts the drawing into what each part shows: the frame is what no part owns, the mouth has a shut, a warning and an open face.</summary>
        private static void Cut(Fight k)
        {
            var geo = k.G;
            var frame = new Sprite(geo.W, geo.H);
            for (int y = 0; y < geo.H; y++)
                for (int x = 0; x < geo.W; x++)
                    if (!geo.InPlate(x, y) && !geo.InMaw(x, y) && !geo.InJaw(x, y)) frame.Px[y * geo.W + x] = k.Whole.Px[y * geo.W + x];
            k.Frame = frame;
            k.ShutArt = k.Whole.Cut(geo.MawLeft, geo.MawTop, MawW, MawH);
            k.WarnArt = k.ShutArt.Cut(0, 0, MawW, MawH);
            k.OpenArt = k.ShutArt.Cut(0, 0, MawW, MawH);
            for (int x = 0; x < MawW; x++)
            {
                if (x % 2 == 0) k.WarnArt.Px[TeethRow * MawW + x] = Palette.Crimson | Sprite.Opaque;   // the teeth part by a pixel
                k.OpenArt.Px[TeethRow * MawW + x] = (x % MawMarkEvery == MawMarkAt ? Palette.WeakMark : Palette.WeakBody) | Sprite.Opaque;   // and show the glow behind, gold on crimson
            }
            if (geo.Tall) k.JawArt = k.Whole.Cut(geo.JawLeft, JawTop, JawW, JawH);
        }

        /// <summary>Five anchors for the heart: three on the row where bosses 1 to 7 hold and, on a tall field, two 14 pixels lower.</summary>
        private static (int X, int Y)[] Anchors(Geo geo)
        {
            double row = BossRowFor(geo.Ph);
            if (!geo.Tall) row = Math.Max(HeartMinRow, Math.Min(row, geo.Floor - HeartAnchorFloorGap));
            int top = (int)Math.Round(row);
            var a = new (int, int)[AnchorShare.Length];
            for (int i = 0; i < a.Length; i++)
                a[i] = ((int)Math.Clamp(Math.Round(AnchorShare[i] * geo.Pw), AnchorEdge, geo.Pw - AnchorEdge), geo.Tall && i % 2 == 1 ? top + HeartDrop : top);
            return a;
        }

        // ---------------------------------------------------------------- the fight

        public override void Update(RaiderGame g, Enemy e)
        {
            if (e.Kit is not Fight k || e.Parts is null) return;
            if (e.Leaving) { e.Y -= LeaveSpeed; return; }
            Release(k, g);
            switch (e.Mode)
            {
                case Crown: UpdateCrown(g, e, k); break;
                case CrownFalls:
                    Hold(e, k);
                    if (e.ModeAge >= CrownFallFrames) Shut(g, e, k);
                    else k.Dust?.Shed(g, e.ModeAge);
                    break;
                case MawShut:
                case MawOpen: UpdateMaw(g, e, k); break;
                case HuskFalls: UpdateHusk(g, e, k); break;
                case Heart: UpdateHeart(g, e, k); break;
            }
        }

        private static void Hold(Enemy e, Fight k)
        {
            e.BaseX = e.X = k.G.Cx;
            e.Y = k.G.Cy;   // a big boss does not sway, and a shove does not move it
        }

        private static List<Part> LiveGuns(Enemy e) =>
            e.Parts!.Where(p => p.Name.StartsWith(GunNamePrefix, StringComparison.Ordinal) && p.State != PartState.Dead).ToList();

        private static void UpdateCrown(RaiderGame g, Enemy e, Fight k)
        {
            if (!k.Landed)
            {
                e.BaseX = e.X = k.G.Cx;
                e.Y = Math.Min(k.G.Cy, e.Y + EnterSpeed);
                if (e.Y < k.G.Cy - LandingSlack) return;
                e.Y = k.G.Cy;
                k.Landed = true;
                k.CycleFrame = -1;   // the step it comes to rest is the first of the cycle
            }
            Hold(e, k);
            var live = LiveGuns(e);
            if (live.Count == 0) return;
            if (++k.CycleFrame >= GunGap * (live.Count - 1) + Blink + GunRest) k.CycleFrame = 0;
            for (int i = 0; i < live.Count; i++)
            {
                int rem = Blink + GunGap * i - k.CycleFrame;
                if (rem == Blink) g.Sound(SfxNames.Charge, e.X + live[i].OX);
                else if (rem == 0) Fire(g, e, k, live[i]);
            }
        }

        /// <summary>One aimed bolt from the muzzle at the ship's place now, at most a little over a steep slope from straight down.</summary>
        private static void Fire(RaiderGame g, Enemy e, Fight k, Part gun)
        {
            double x = Math.Floor(e.X + gun.OX), y = k.G.Top + k.G.GunTop + GunMuzzleRows;
            double ang = Math.Clamp(Math.Atan2(g.PX - x, Math.Max(MinAimDrop, g.PY - y)), -MaxAimAngle, MaxAimAngle);
            g.Bullets.Add(new Bullet { X = x, Y = y, VX = Math.Sin(ang) * GunSpeed, VY = Math.Cos(ang) * GunSpeed, Dmg = BoltDmg, Aimed = true });
            g.Sound(SfxNames.Beam, x);
        }

        // ---------------------------------------------------------------- the mouth

        private static void Shut(RaiderGame g, Enemy e, Fight k)
        {
            e.Mode = MawShut;
            e.ModeAge = 0;
            e.Untouchable = false;
            k.Dust = null;
            k.Maw.State = PartState.Armoured;
            k.Maw.Closed = k.ShutArt;
            if (k.Jaw is not null) k.Jaw.State = PartState.Armoured;
            PickGap(g, k);
            g.Sound(SfxNames.Shut, e.X, BigSound);
        }

        private static void UpdateMaw(RaiderGame g, Enemy e, Fight k)
        {
            Hold(e, k);
            if (e.Mode == MawShut)
            {
                if (e.ModeAge == BarFrame)
                {
                    SpitBar(g, e, k, k.G.Top + k.G.CrownH, true);
                    g.Cue(e, VoiceCue.Idle);
                }
                else if (e.ModeAge == ShutFrames - TeethWarn)
                {
                    k.Maw.Closed = k.WarnArt;
                    g.Sound(SfxNames.Warn, e.X, MidSound);
                }
                else if (e.ModeAge >= ShutFrames)
                {
                    e.Mode = MawOpen;
                    e.ModeAge = 0;
                    k.Maw.State = PartState.Open;
                    k.Maw.Closed = k.ShutArt;
                    if (k.Jaw is not null) k.Jaw.State = PartState.Dead;   // the jaw drops away from the mouth: the shots come through, and Draw shows it hanging
                    g.Sound(SfxNames.Open, e.X, BigSound);
                }
            }
            else if (e.ModeAge >= OpenFrames) Shut(g, e, k);
        }

        /// <summary>The next bar's gap, on the far side of the field from the last one: its left bolt, so the gap is the <see cref="BarGap"/> from there.</summary>
        private static void PickGap(RaiderGame g, Fight k)
        {
            bool right = k.HaveLast ? !k.LastRight : g.R() < 0.5;
            int pw = g.PW, lo = right ? pw / 2 - GapMargin : GapMargin, hi = right ? pw - BarGap - GapMargin : pw / 2 + GapMargin - BarGap;
            k.GapLeft = lo + (int)(g.R() * (hi - lo + 1));
            k.LastRight = right;
            k.HaveLast = true;
        }

        /// <summary>A bar of bolts 6 apart across the field but for the 14 of the gap. Bolts of a bar spat from the mouth wait behind the jaw until they clear the chin.</summary>
        private static void SpitBar(RaiderGame g, Enemy e, Fight k, double y, bool fromMouth)
        {
            int gl = k.GapLeft, mawLeft = (int)Math.Round(e.X + k.Maw.OX - MouthHalfWidth), mawRight = mawLeft + MouthSpan;
            var xs = new List<int>();
            for (int x = gl; x >= 0; x -= BarSpacing) xs.Add(x);
            for (int x = gl + BarGap; x <= g.PW; x += BarSpacing) xs.Add(x);
            foreach (int x in xs)
            {
                if (fromMouth && x >= mawLeft && x <= mawRight) k.Pending.Add((x, y + BarSpeed));
                else g.Bullets.Add(new Bullet { X = x, Y = y, VX = 0, VY = BarSpeed, Dmg = BoltDmg });
            }
            g.Sound(SfxNames.Blast, e.X, BigSound);
        }

        /// <summary>Moves the bolts that are still behind the jaw as the game would, and lets each go once it is under the chin.</summary>
        private static void Release(Fight k, RaiderGame g)
        {
            for (int i = k.Pending.Count - 1; i >= 0; i--)
            {
                var (x, y) = k.Pending[i];
                y += BarSpeed;
                if (y >= k.G.Top + k.G.H)
                {
                    k.Pending.RemoveAt(i);
                    g.Bullets.Add(new Bullet { X = x, Y = y - BarSpeed, VX = 0, VY = BarSpeed, Dmg = BoltDmg });   // the game moves it once more this frame
                }
                else k.Pending[i] = (x, y);
            }
        }

        // ---------------------------------------------------------------- falls

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            if (e.Kit is not Fight k) return;
            if (p.Name.StartsWith(GunNamePrefix, StringComparison.Ordinal) && e.Mode == Crown && LiveGuns(e).Count == 0) CrownFall(g, e, k);
            else if (p.Name == MawName && e.Mode is MawShut or MawOpen) HuskFall(g, e, k);
        }

        private static void CrownFall(RaiderGame g, Enemy e, Fight k)
        {
            e.Mode = CrownFalls;
            e.ModeAge = 0;
            e.Untouchable = true;
            g.Bullets.RemoveAll(b => !b.Friendly);
            var dust = k.Dust = new PixelDust(k.G.Left, k.G.Top, k.G.W, k.G.H, k.Whole, k.G.InPlate, CrownFallFrames);
            g.SetParts(e, k.Jaw is null ? [k.Maw] : [k.Jaw, k.Maw]);
            dust.Shed(g, 0);
            g.Sound(SfxNames.Part, e.X, BigSound);
            g.Cue(e, VoiceCue.Phase);
        }

        private static void HuskFall(RaiderGame g, Enemy e, Fight k)
        {
            k.HuskX = e.X + k.Maw.OX;
            k.HuskY = e.Y + k.Maw.OY;
            e.Mode = HuskFalls;
            e.ModeAge = 0;
            e.Untouchable = true;
            g.Bullets.RemoveAll(b => !b.Friendly);
            k.Pending.Clear();
            var dust = k.Dust = new PixelDust(k.G.Left, k.G.Top, k.G.W, k.G.H, k.Whole, (x, y) => !k.G.InPlate(x, y), HuskFallFrames);
            double hp = HeartHp * k.Scale;
            var heart = new Part
            {
                Name = HeartName,
                OX = 0,
                OY = 0,
                HW = HeartRadius,
                HH = HeartRadius,
                Closed = HeartUnlitArt,
                OpenArt = HeartArt,
                Wreck = Art.Wrecked(HeartArt),
                State = PartState.Armoured,
                Vital = true,
                Hp = hp,
                MaxHp = hp,
                Score = 0,   // the kill pays the boss's own KillScore
            };
            e.Sprite = HeartField;
            e.X = e.BaseX = k.HuskX;
            e.Y = k.HuskY;
            g.SetParts(e, heart);
            dust.Shed(g, 0);
            g.Sound(SfxNames.Part, e.X, BigSound);
            g.Cue(e, VoiceCue.Opens);
        }

        private void UpdateHusk(RaiderGame g, Enemy e, Fight k)
        {
            var home = k.Anchors[0];
            if (e.ModeAge >= HuskFallFrames)
            {
                e.Mode = Heart;
                e.ModeAge = 0;
                e.Untouchable = false;
                e.Parts![0].State = PartState.Open;
                k.Dust = null;
                e.BaseX = e.X = home.X;
                e.Y = home.Y;
                return;
            }
            double ease = Ease(e.ModeAge / (double)HuskFallFrames);
            e.BaseX = e.X = k.HuskX + (home.X - k.HuskX) * ease;
            e.Y = k.HuskY + (home.Y - k.HuskY) * ease;
            k.Dust?.Shed(g, e.ModeAge);
        }

        /// <summary>Smoothstep: 0 to 1, slow at both ends.</summary>
        private static double Ease(double s) => s * s * (3 - 2 * s);

        // ---------------------------------------------------------------- the heart

        /// <summary>The heart at frame <paramref name="m"/> of its fight: 50 frames at an anchor, then 12 of hop to the next, on a small arc and eased.</summary>
        private static (double X, double Y) HeartAt(Fight k, int m)
        {
            int c = m / HopCycle, t = m % HopCycle;
            var a = k.Anchors[c % k.Anchors.Length];
            var b = k.Anchors[(c + 1) % k.Anchors.Length];
            if (t < HopRest) return (a.X, a.Y);
            if (t >= HopRest + HopFrames - 1) return (b.X, b.Y);
            double s = (t - HopRest + 1) / (double)HopFrames, ease = Ease(s);
            double arc = Math.Min(HeartArcHeight, Math.Max(0, Math.Min(a.Y, b.Y) - HeartMinRow));   // never above HeartMinRow, so the heart's top stays under the HUD
            return (a.X + (b.X - a.X) * ease, a.Y + (b.Y - a.Y) * ease - arc * Math.Sin(Math.PI * s));
        }

        private static void UpdateHeart(RaiderGame g, Enemy e, Fight k)
        {
            int m = e.ModeAge, c = m / HopCycle, t = m % HopCycle;
            var (x, y) = HeartAt(k, m);
            e.BaseX = e.X = x;
            e.Y = y;
            bool barHop = (c + 1) % BarEvery == 0;
            if (t == 0 && barHop) PickGap(g, k);
            if (t == HopCycle - 1 - HopWarn) g.Sound(SfxNames.Warn, x, MidSound);
            if (t != HopCycle - 1) return;
            double turn = c % 2 == 0 ? 0 : Math.PI / RingBolts;
            for (int i = 0; i < RingBolts; i++)
            {
                double a = turn + i * Math.Tau / RingBolts;
                g.Bullets.Add(new Bullet { X = x, Y = y, VX = Math.Cos(a) * RingSpeed, VY = Math.Sin(a) * RingSpeed, Dmg = BoltDmg });
            }
            g.Sound(SfxNames.Blast, x, MidSound);
            if (barHop) SpitBar(g, e, k, HeartBarRow, false);
        }

        public override void Down(RaiderGame g, Enemy e)
        {
            for (int i = 0; i < DownBursts; i++)
            {
                double a = g.R() * Math.Tau, sp = BurstSpeedMin + g.R() * BurstSpeedSpread;
                uint col = g.R() < BurstCrimsonShare ? Palette.Crimson : g.R() < BurstGoldShare ? Palette.Gold : Palette.White;
                g.AddParticle(new Particle
                {
                    X = e.X + (g.R() - 0.5) * BurstSpread,
                    Y = e.Y + (g.R() - 0.5) * BurstSpread,
                    VX = Math.Cos(a) * sp,
                    VY = Math.Sin(a) * sp - BurstLift,
                    Life = BurstLife + (int)(g.R() * BurstLifeSpread),
                    Colour = col,
                    Size = g.R() < BurstSizeTwoShare ? 2 : 1,
                    Kind = ParticleKind.Debris,
                });
            }
        }

        // ---------------------------------------------------------------- what it shows

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Kit is not Fight k || e.Parts is null) return;
            var geo = k.G;
            switch (e.Mode)
            {
                case Crown when k.Landed:
                    var live = LiveGuns(e);
                    for (int i = 0; i < live.Count; i++)
                    {
                        int rem = Blink + GunGap * i - k.CycleFrame;
                        if (rem >= 1 && rem <= Blink && (rem <= GunSolidFrames || rem % GunBlinkPeriod == 0)) c.Set((int)Math.Floor(e.X + live[i].OX), geo.GunFlashRow, Palette.Tell);
                    }
                    break;
                case CrownFalls:
                case HuskFalls:
                    if (k.Dust is { } d)
                        for (int f = Math.Max(0, e.ModeAge); f < d.Frames; f++)
                            foreach (var (x, y, col) in d.ByFrame[f]) c.Set(x, y, col);
                    break;
                case MawShut:
                    if (e.ModeAge < BarFrame) DrawMarker(c, k.GapLeft + GapMarkerOffset, geo.Top + geo.H + 1, null);
                    break;
                case MawOpen:
                    if (k.JawArt is not null) c.Draw(k.JawArt, geo.Left + geo.JawLeft, geo.Top + JawTop);
                    break;
                case Heart:
                    DrawHeartWarning(c, e, k);
                    break;
            }
        }

        /// <summary>A dotted gold column where a bar's gap will be; <paramref name="heart"/>, when given, leaves the heart's own box clear.</summary>
        private static void DrawMarker(RaiderView.Canvas c, int x, int y0, (double X, double Y)? heart)
        {
            for (int y = y0; y < c.Game.PH; y += MarkerDotStep)
            {
                if (heart is { } h && Math.Abs(x - h.X) <= HeartHalfBox && Math.Abs(y - h.Y) <= HeartHalfBox) continue;
                c.Set(x, y, Palette.Gold);
            }
        }

        /// <summary>The heart's outline at the anchor it hops to and a dotted ring of the radius of its bolts, crimson and then white for the last third.</summary>
        private static void DrawHeartWarning(RaiderView.Canvas c, Enemy e, Fight k)
        {
            int m = e.ModeAge, cycle = m / HopCycle, t = m % HopCycle;
            if ((cycle + 1) % BarEvery == 0 && t < HopCycle - 1) DrawMarker(c, k.GapLeft + GapMarkerOffset, GapMarkerTopRow, (e.X, e.Y));
            int last = HopCycle - 1;
            if (t < last - HopWarn || t >= last) return;
            var dest = k.Anchors[(cycle + 1) % k.Anchors.Length];
            uint col = last - t <= HopWarnWhiteFrames ? Palette.Tell : Palette.Crimson;
            for (int y = 0; y < HeartArt.H; y++)
                for (int x = 0; x < HeartArt.W; x++)
                    if ((HeartArt.Px[y * HeartArt.W + x] & RgbMask) == Palette.Plum && ((x + y) & 1) == 0)
                        c.Set(dest.X - HeartArt.W / 2 + x, dest.Y - HeartArt.H / 2 + y, col);
            for (int i = 0; i < WarnDots; i++)
            {
                double a = i * Math.Tau / WarnDots;
                c.Set((int)Math.Round(dest.X + WarnRingRadius * Math.Cos(a)), (int)Math.Round(dest.Y + WarnRingRadius * Math.Sin(a)), col);
            }
        }
    }
}
