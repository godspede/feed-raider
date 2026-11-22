namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Spotlight (boss 5): a gantry hung with two lamp batteries and, below them, a lens. It slides over the ship for
    /// <see cref="SpotlightBehaviour.TrackFrames"/> frames while the batteries lob slow bolts, then stops, locks the column it stands over
    /// and opens the lens for the charge: <see cref="SpotlightBehaviour.ChargeFrames"/> frames, each dead battery adding
    /// <see cref="SpotlightBehaviour.ChargePerBattery"/>. The lens is the one thing that kills it and is open only in the charge and the
    /// cool-down after the beam. The charge is a line down the column that is dotted, then solid, then wide and white; nothing is lethal
    /// until the beam fires (<see cref="SpotlightBehaviour.FireFrames"/> frames, from the lens to the floor). Under
    /// 1/<see cref="SpotlightBehaviour.BrokenShare"/> of the lens's hit points the beam leans toward the side the ship stood on at the lock,
    /// as <see cref="SpotlightBehaviour.Lean"/> tells, for <see cref="SpotlightBehaviour.SweepFrames"/> frames more. It speaks one post, the
    /// loudest, and says it again at every lock.
    /// </summary>
    private sealed class SpotlightBehaviour : EnemyBehaviour
    {
        private const int Enter = 0, Track = 1, Charge = 2, Fire = 3, Cool = 4;
        private const int TrackFrames = 70, ChargeFrames = 40, ChargePerBattery = 8, FireFrames = 18, SweepFrames = 40, CoolFrames = 12;
        /// <summary>The track frames the batteries fire on, and the count of enemy bolts in flight at which a battery holds its fire.</summary>
        private const int FirstVolleyAge = 20, SecondVolleyAge = 50, MaxHostileBolts = 24;
        /// <summary>The values of <see cref="Lights.Side"/>: the beam leans toward smaller x, or toward larger x.</summary>
        private const int LeanLeft = -1, LeanRight = 1;
        /// <summary>A battery's muzzle is white for this many frames before each of its volleys.</summary>
        private const int VolleyTellFrames = 12;
        private const double LensHp = 22, BatteryHp = 18, SlideSpeed = 0.8, EnterSpeed = 0.6, LeaveSpeed = 0.8, BoltSpeed = 1.1;
        private const double BeamReach = 5, SweepReach = 24, SweepRate = 0.6, HoldRow = 0.24;
        private const int BeamHalf = 4, ShortBelow = 48, ShortHold = 12;
        /// <summary>Where the lens and hull sit and how tall the mast and batteries are, on a tall body and on a short one.</summary>
        private const int TallLensY = 12, TallHullY = 8, TallRows = 6, ShortLensY = 10, ShortHullY = 6, ShortRows = 4;
        private const double HoldMin = 14, HoldMax = 30;
        /// <summary>The charge line is drawn in <see cref="LineStages"/> stages (dotted, solid, wide), and the arrow head's arms are <see cref="ChevronArm"/> rows long, its tip <see cref="ChevronReach"/> columns off the line.</summary>
        private const int LineStages = 3, ChevronArm = 5, ChevronReach = 13;
        /// <summary>The first two stages of the charge line; the last one is wide, <see cref="WideHalf"/> pixels either side of the column.</summary>
        private const int DottedStage = 0, SolidStage = 1, WideHalf = 1;
        /// <summary>The beam leans once the lens is under 1/<see cref="BrokenShare"/> of its hit points at the lock.</summary>
        private const int BrokenShare = 2;
        /// <summary>The mode age the spotlight gives a mode on entering it, so a mode's frames count from one.</summary>
        private const int ModeFirstFrame = 1;
        private const int KillScore = 6000;
        /// <summary>The dotted stage of the charge line lights one row in every <see cref="DotStep"/>, and each arm of the arrow head is
        /// <see cref="ChevronThickness"/> pixels thick.</summary>
        private const int DotStep = 2, ChevronThickness = 2;
        /// <summary>The rows of the drawing the short form leaves out: the top and bottom rows of the lamps, which keep their middle row.</summary>
        private const int LampTopRow = 3, LampBottomRow = 5;
        /// <summary>The cuts of the drawing the parts are made from: the gantry across the top, the mast and the lens down the middle, the hull
        /// round the lens, and a battery of lamps at each end; every part below the gantry starts <see cref="BelowGantry"/> rows down.</summary>
        private const int GantryW = 26, BelowGantry = 2, MastX = 11, MastW = 3, LensX = 11, LensW = 3, LensH = 4, HullX = 7, HullW = 11, HullH = 8;
        private const int BatteryW = 7, RightBatteryX = 18, BatteryScore = 400;
        private const string BatteryPrefix = "battery";
        private const double ArmourHp = 1, SoftVolume = 0.5, BeamVolume = 1;
        /// <summary>The lens sits on the middle of a pixel row: a whole row plus this.</summary>
        private const double RowMiddle = 0.5;
        private const uint Lamp = 0xfff3a0, Dim = 0x8a7a3a, Amber = 0xe0a030;

        // 26 by 17: 25 columns and one clear one, so that the width is even and the parts and the body round alike
        private static readonly string[] Rows =
        [
            "#########################.",
            "#ooooooooooooooooooooooo#.",
            "############o############.",
            "#LrrrL#....#o#....#LrrrL#.",
            "#LryrL#....#o#....#LryrL#.",
            "#LrrrL#....#o#....#LrrrL#.",
            ".#LLL#.....#o#.....#LLL#..",
            "..###......#o#......###...",
            "..........#####..........",
            ".........#ooooo#.........",
            "........#oo+++oo#........",
            ".......#oo+ooo+oo#.......",
            ".......#o+o###o+o#.......",
            ".......#o+o###o+o#.......",
            ".......#oo+###+oo#.......",
            "........#oo###oo#........",
            ".........##...##.........",
        ];

        // the dark palette paints '.' opaque black; a gap between the lamps and the mast must show the field, so '.' is taken out of this one
        private static readonly Dictionary<char, uint> BodyPalette = MakeBodyPalette();
        private static readonly Sprite Full = Sprite.FromArt(Rows, BodyPalette);
        // the short form for a field shorter than ShortBelow: two rows of the lamps are dropped
        private static readonly Sprite Short = Sprite.FromArt([.. Rows.Where((_, i) => i != LampTopRow && i != LampBottomRow)], BodyPalette);
        private static readonly Sprite LensOpen = Sprite.FromArt(["rrr", "ryr", "ryr", "rrr"], BodyPalette);

        /// <summary>What one spotlight remembers: where it is to a fraction (<see cref="ExactX"/>, <see cref="ExactY"/>); the whole
        /// column and half row of its last pose (<see cref="PosedX"/>, <see cref="PosedY"/>), so a move made from outside shows as a
        /// difference; the column it locked; the side to sweep and whether it does; whether it has said its phase line; and its lens.</summary>
        private sealed class Lights
        {
            public double ExactX, ExactY, PosedX = double.NaN, PosedY = double.NaN, Column;
            public int Side;
            public bool Sweep, Phased;
            public Part Lens = null!;
        }

        private static Dictionary<char, uint> MakeBodyPalette()
        {
            var palette = Art.With(('L', Dim));
            palette.Remove('.');
            return palette;
        }

        public override EnemyKind Kind => EnemyKind.Spotlight;
        public override string Name => "SPOTLIGHT";
        public override Sprite Body => Full;
        public override uint Colour => Amber;
        public override double Hp => LensHp;
        public override int Score => KillScore;
        public override bool Boss => true;

        // ---------------------------------------------------------------- voice

        /// <summary>The loudest waiting post: the largest share of its letters in capitals, then the most exclamation marks, then the oldest.</summary>
        public override int Taste(Enemy e, IReadOnlyList<RudePost> waiting)
        {
            int best = 0;
            double bestLoud = double.NegativeInfinity;
            int bestMarks = int.MinValue;
            for (int i = 0; i < waiting.Count; i++)
            {
                string text = waiting[i].Text;
                int letters = text.Count(char.IsLetter);
                double loud = letters == 0 ? 0 : text.Count(char.IsUpper) / (double)letters;
                int marks = text.Count(character => character == '!');
                if (loud > bestLoud || loud == bestLoud && marks > bestMarks) { best = i; bestLoud = loud; bestMarks = marks; }
            }
            return best;
        }

        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue) => cue switch
        {
            // each time the light comes on it has a new post for the crowd, whatever was on show
            VoiceCue.Opens or VoiceCue.Phase or VoiceCue.ShipDied or VoiceCue.Dying or VoiceCue.Leaving => Turn.Cut,
            VoiceCue.PartLost => Turn.Skip,
            _ => Turn.Wait,
        };

        public override string? Line(Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive => "Today's main character: you.",
            VoiceCue.Phase => "Do not leave. They are watching.",
            VoiceCue.Dying => "Fade out.",
            _ => null,
        };

        // ---------------------------------------------------------------- parts

        private static Part MakePart(Sprite body, string name, int left, int top, int w, int h, PartState state, bool vital, double hp, int score, Sprite? open = null)
        {
            var art = body.Cut(left, top, w, h);
            return new Part
            {
                Name = name,
                OX = left + w / 2.0 - body.W / 2.0,
                OY = top + h / 2.0 - body.H / 2.0,
                HW = (w + 1) / 2,
                HH = (h + 1) / 2,
                Closed = art,
                OpenArt = open,
                Wreck = Art.Wrecked(open ?? art),
                State = state,
                Vital = vital,
                Hp = hp,
                MaxHp = hp,
                Score = score,
            };
        }

        public override void Spawn(RaiderGame g, Enemy e)
        {
            bool small = g.PH < ShortBelow;
            var body = small ? Short : Full;
            e.Sprite = body;
            e.Y = -body.H;
            int lensY = small ? ShortLensY : TallLensY, hullY = small ? ShortHullY : TallHullY, rows = small ? ShortRows : TallRows;
            var lens = MakePart(body, "lens", LensX, lensY, LensW, LensH, PartState.Armoured, true, e.Hp, 0, LensOpen);
            g.SetParts(e,
                MakePart(body, "gantry", 0, 0, GantryW, BelowGantry, PartState.Armoured, false, ArmourHp, 0),
                MakePart(body, "mast", MastX, BelowGantry, MastW, rows, PartState.Armoured, false, ArmourHp, 0),
                MakePart(body, "hull", HullX, hullY, HullW, HullH, PartState.Armoured, false, ArmourHp, 0),
                MakePart(body, BatteryPrefix + "-l", 0, BelowGantry, BatteryW, rows, PartState.Open, false, BatteryHp, BatteryScore),
                MakePart(body, BatteryPrefix + "-r", RightBatteryX, BelowGantry, BatteryW, rows, PartState.Open, false, BatteryHp, BatteryScore),
                lens);
            e.Kit = new Lights { Lens = lens };
        }

        // ---------------------------------------------------------------- the machine

        private static double HoldFor(RaiderGame g) => g.PH < ShortBelow ? ShortHold - RowMiddle : Math.Round(Math.Clamp(HoldRow * g.PH, HoldMin, HoldMax)) + RowMiddle;

        private static int DeadBatteries(Enemy e) => e.Parts!.Count(part => part.Name.StartsWith(BatteryPrefix, StringComparison.Ordinal) && part.State == PartState.Dead);

        private static int ModeFrames(Enemy e, Lights k) => e.Mode switch
        {
            Track => TrackFrames,
            Charge => ChargeFrames + ChargePerBattery * DeadBatteries(e),
            Fire => FireFrames + (k.Sweep ? SweepFrames : 0),
            Cool => CoolFrames,
            _ => int.MaxValue,
        };

        /// <summary>The column the spotlight follows and leans by: the ship's, or the middle of the field while there is no ship.</summary>
        private static double ShipXOrMiddle(RaiderGame g) => g.Alive ? g.PX : g.PW / 2.0;

        private static double BeamAt(Enemy e, Lights k) => e.X + k.Lens.OX;

        private static double LensBottom(Enemy e, Lights k) => e.Y + k.Lens.OY + k.Lens.HH;

        /// <summary>How far to the side the foot of the beam has leaned this frame: nothing for <see cref="FireFrames"/> frames, then <see cref="SweepRate"/> a frame up to
        /// <see cref="SweepReach"/>.</summary>
        private static double Lean(Enemy e, Lights k) =>
            !k.Sweep || e.Mode != Fire ? 0 : k.Side * Math.Min(SweepReach, SweepRate * Math.Max(0, e.ModeAge - FireFrames));

        /// <summary>The x of the beam's centre at a row: the beam hangs from the lens and leans more the lower it goes.</summary>
        private static double BeamCentre(RaiderGame g, Enemy e, Lights k, double y)
        {
            double top = LensBottom(e, k), lean = Lean(e, k);
            if (lean == 0) return k.Column;
            return k.Column + lean * Math.Clamp((y - top) / Math.Max(1, g.PH - top), 0, 1);
        }

        public override void Update(RaiderGame g, Enemy e)
        {
            var k = (Lights)e.Kit!;
            if (e.X != k.PosedX) k.ExactX = e.X;   // put there from outside (a test, leaving)
            if (e.Y != k.PosedY) k.ExactY = e.Y;
            double hold = HoldFor(g);

            if (e.Leaving)
            {
                k.Lens.State = PartState.Armoured;
                k.ExactY -= LeaveSpeed;
                Pose(g, e, k);
                return;
            }
            if (e.Mode == Enter)
            {
                k.ExactY += EnterSpeed;
                if (Math.Floor(k.ExactY) + RowMiddle < hold) { Pose(g, e, k); return; }
                k.ExactY = hold;
                Pose(g, e, k);
                e.Mode = Track; e.ModeAge = ModeFirstFrame;
            }
            else if (e.ModeAge > ModeFrames(e, k))
            {
                AdvanceMode(g, e, k);
            }

            switch (e.Mode)
            {
                case Track:
                    k.ExactX += Math.Clamp(ShipXOrMiddle(g) - k.ExactX, -SlideSpeed, SlideSpeed);
                    Pose(g, e, k);
                    if (e.ModeAge is FirstVolleyAge or SecondVolleyAge) Volley(g, e);
                    break;
                default:
                    Pose(g, e, k);
                    break;
            }
        }

        /// <summary>Sets the pose from the fractions: the spotlight stands on a whole column and on a half row, so that its parts, which are
        /// cut from the body at half offsets, are drawn on the same pixels as the body.</summary>
        private static void Pose(RaiderGame g, Enemy e, Lights k)
        {
            k.ExactX = Math.Clamp(k.ExactX, e.Sprite.W / 2.0, g.PW - e.Sprite.W / 2.0);
            e.X = k.PosedX = Math.Round(k.ExactX, MidpointRounding.AwayFromZero);
            e.Y = k.PosedY = Math.Floor(k.ExactY) + RowMiddle;
        }

        private static void AdvanceMode(RaiderGame g, Enemy e, Lights k)
        {
            e.ModeAge = ModeFirstFrame;
            switch (e.Mode)
            {
                case Track:
                    e.Mode = Charge;
                    k.Column = BeamAt(e, k);
                    double ship = ShipXOrMiddle(g);
                    k.Side = ship < k.Column ? LeanLeft : ship > k.Column ? LeanRight : k.Column < g.PW / 2.0 ? LeanRight : LeanLeft;
                    k.Sweep = k.Lens.Hp < k.Lens.MaxHp / BrokenShare;
                    k.Lens.State = PartState.Open;
                    g.Sound(SfxNames.Charge, e.X, SoftVolume);
                    g.Sound(SfxNames.Open, e.X, SoftVolume);
                    if (k.Sweep && !k.Phased) { k.Phased = true; g.Cue(e, VoiceCue.Phase); }
                    g.Cue(e, VoiceCue.Opens);
                    break;
                case Charge:
                    e.Mode = Fire;
                    k.Lens.State = PartState.Armoured;
                    g.Sound(SfxNames.Beam, e.X, BeamVolume);
                    break;
                case Fire:
                    e.Mode = Cool;
                    k.Lens.State = PartState.Open;
                    break;
                default:
                    e.Mode = Track;
                    k.Lens.State = PartState.Armoured;
                    g.Sound(SfxNames.Shut, e.X, SoftVolume);
                    break;
            }
        }

        /// <summary>Each living battery fires one slow bolt at the ship.</summary>
        private static void Volley(RaiderGame g, Enemy e)
        {
            if (!g.Alive) return;
            foreach (var battery in e.Parts!)
            {
                if (battery.State == PartState.Dead || !battery.Name.StartsWith(BatteryPrefix, StringComparison.Ordinal)) continue;
                if (g.Bullets.Count(b => !b.Friendly) >= MaxHostileBolts) return;
                g.EnemyBolt(e.X + battery.OX, e.Y + battery.OY + battery.HH, g.PX, g.PY, spread: 0, speed: BoltSpeed);
            }
        }

        private static bool VolleyDue(int modeAge, int volley) => modeAge < volley && modeAge >= volley - VolleyTellFrames;

        // ---------------------------------------------------------------- the beam

        /// <summary>Lethal in the beam's column and below the lens during the fire, and at no other time: not in the charge, not in the cool-down.</summary>
        public override bool Touches(RaiderGame g, Enemy e)
        {
            if (e.Leaving || e.Mode != Fire) return false;
            var k = (Lights)e.Kit!;
            if (g.PY < LensBottom(e, k)) return false;
            return Math.Abs(g.PX - BeamCentre(g, e, k, g.PY)) <= BeamReach;
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Leaving || e.Y < 0) return;
            var k = (Lights)e.Kit!;
            var surface = c.Surface;
            int top = (int)Math.Floor(LensBottom(e, k)) + 1, bottom = c.Game.PH - 1;
            int column = (int)Math.Floor(k.Column);
            if (e.Mode == Track && c.Game.Alive && (VolleyDue(e.ModeAge, FirstVolleyAge) || VolleyDue(e.ModeAge, SecondVolleyAge)))
                foreach (var battery in e.Parts!)
                    if (battery.State != PartState.Dead && battery.Name.StartsWith(BatteryPrefix, StringComparison.Ordinal))
                        surface.Set((int)Math.Round(e.X + battery.OX), (int)Math.Round(e.Y + battery.OY + battery.HH), Palette.Tell);
            if (e.Mode == Charge)
            {
                int chargeFrames = ModeFrames(e, k), stage = Math.Min(LineStages - 1, (e.ModeAge - ModeFirstFrame) * LineStages / chargeFrames);
                for (int y = top; y <= bottom; y++)
                {
                    if (stage == DottedStage) { if ((y - top) % DotStep == 0) surface.Set(column, y, Dim); }
                    else if (stage == SolidStage) surface.Set(column, y, Dim);
                    else for (int x = column - WideHalf; x <= column + WideHalf; x++) surface.Set(x, y, Palette.Tell);
                }
                if (k.Sweep) Chevron(surface, column, k.Side, bottom);
            }
            else if (e.Mode == Fire)
            {
                for (int y = top; y <= bottom; y++)
                {
                    int centre = (int)Math.Floor(BeamCentre(c.Game, e, k, y));
                    for (int x = centre - BeamHalf; x <= centre + BeamHalf; x++) surface.Set(x, y, Lamp);
                }
            }
        }

        /// <summary>An arrow head at the foot of the line, pointing the way the beam will lean.</summary>
        private static void Chevron(Surface surface, int column, int side, int bottom)
        {
            for (int dy = 0; dy <= ChevronArm; dy++)
                for (int layer = 0; layer < ChevronThickness; layer++)
                {
                    int x = column + side * (ChevronReach - dy - layer);
                    surface.Set(x, bottom - ChevronArm - dy, Lamp);
                    surface.Set(x, bottom - ChevronArm - 1 + dy, Lamp);
                }
        }
    }
}
