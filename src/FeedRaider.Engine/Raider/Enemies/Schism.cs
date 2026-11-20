namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Schism (stage 7 boss): one body that is two halves, ice on one side and rose on the other, round a gold seam. While it is
    /// whole the halves are shut behind armour, ice in shield blue, and only the seam can be shot; the seam gives way when it is shot or after three
    /// hundred frames, and the halves come apart to a quarter and three quarters of the field, rose a little lower than ice.
    /// Ice, bare frost once apart, whitens its brow and muzzle for twelve frames and fans five slow bolts at the ship. Rose blinks and marks a dotted path to where the ship stood for twenty-four frames,
    /// then sends one fast bolt down the path. Every 360 frames the halves cross over (announced thirty frames ahead by both
    /// flashing, with dotted lanes). A half that dies mends for 240 frames and comes back with forty per cent, unless the other
    /// half dies first; while it mends the other shoots forty per cent faster and nothing crosses.
    /// The two halves speak in turn, each cutting the other off: ice takes the post that has waited longest, rose the newest.
    /// </summary>
    private sealed class SchismBehaviour : EnemyBehaviour
    {
        // modes the machine and the tests read
        private const int Entering = 0, Joined = 1, Apart = 2, Swapping = 3, Mending = 4;

        private const double IceHp = 14, RoseHp = 14, SeamHp = 5;   // a boss is never scaled by the stage
        private const double EnterSpeed = 0.6, SlideSpeed = 0.6, SwapSpeed = 0.8, GlideSpeed = 0.3, ReturnShare = 0.4, MendFaster = 1.4;
        private const int JoinedFrames = 300, SwapEvery = 360, SwapWarn = 30, MendFrames = 240, MaxHostile = 24;
        private const int IceEvery = 70, RoseEvery = 50, RoseWarn = 24, FanWidth = 5;
        private const double IceSpeed = 0.85, RoseSpeed = 1.6, FanStep = 0.22;
        private const int ScoreHalf = 3000;
        private const double LeaveSpeed = 0.8;
        /// <summary>Where the halves stand apart, as shares of the field's width: the near side is a quarter in, the far side three quarters.</summary>
        private const double NearShare = 0.25, FarShare = 0.75;
        /// <summary>The hold height: a fixed row on a short field, else a share of the height between two limits. Rose hangs lower than ice by up to <see cref="StaggerMax"/>.</summary>
        private const int ShortField = 48;
        private const double ShortHoldY = 11.5, HoldShare = 0.24, HoldMin = 14, HoldMax = 30, HalfDrop = 6.5, StaggerMax = 10;
        private const double FloorMin = 13, FloorShare = 0.36;
        private const int TickEvery = 60;
        /// <summary>How long each half waits before its first bolt, and how long ice waits to retry when the field is full.</summary>
        private const int IceFirstCd = 50, RoseFirstCd = 30, FullRetry = 4;
        private const uint ColourMask = 0xFFFFFF;
        private const uint RoseLight = 0xff7fa8, RoseDark = 0x9f2f5a, Husk = 0x3a2c4f;
        /// <summary>The ice half out of its armour: the blue is a shield's, so a half that can be hurt wears pale frost.</summary>
        private const uint FrostLight = 0xd8d0e8, FrostDark = 0x8a7fa6;
        /// <summary>Each half's drawing is <see cref="HalfArtW"/> by <see cref="ArtH"/>, and the seam's sits at <see cref="SeamX"/>, <see cref="SeamY"/> in the whole drawing.</summary>
        private const int HalfArtW = 15, ArtH = 13, SeamX = 13, SeamY = 5, SeamW = 5, SeamH = 3;
        /// <summary>A part's box is half its drawing, rounded down; each half stands 8 pixels from the middle, and a part sits half a pixel below the owner's middle.</summary>
        private const int HalfBoxW = HalfArtW / 2, HalfBoxH = ArtH / 2, HalfOffset = 8;
        private const double HalfPixel = 0.5, ArrivedWithin = 0.01, ScaleEpsilon = 1.0001;
        /// <summary>The crossing's warning flashes both halves for <see cref="WarnFlash"/> frames of every <see cref="WarnBlinkPeriod"/>.</summary>
        private const int WarnBlinkPeriod = 4, WarnFlash = 2, LaneDashStep = 3, PathStart = 4, PathDashStep = 2, BossScore = 7000;
        /// <summary>How far below a half's middle its bolt leaves and the aim line starts, and where the crimson above each eye's gold sits (from the art's top left): the warning whitens that, so the gold stays in sight.</summary>
        private const int IceMuzzleDrop = 6, RoseMuzzleDrop = 5, RoseAimDrop = 4, IceEyeX = 3, RoseEyeX = 11, EyeY = 5;
        /// <summary>Ice's brow and muzzle are white for this many frames before its fan leaves.</summary>
        private const int IceTellFrames = 12;
        /// <summary>Rose's eye blinks in steps of <see cref="EyeBlinkFrames"/>, and is solid for the last <see cref="EyeSolidFrames"/> before the bolt.</summary>
        private const int EyeBlinkFrames = 2, EyeSolidFrames = RoseWarn / 3;

        private static readonly string[] Rows =
        [
            "......#########.#########......",
            "....##CCCCCCCC#.#SSSSSSSS##....",
            "..##CCcccccCC#...#SSsssssSS##..",
            ".#CCccccCCC##.....##SSSssssSS#.",
            "#CCcccCC###.........###SSsssSS#",
            "#CeeeC##......ryr......##SfffS#",
            "#CeEec#......ryyyr......#sfFfS#",
            "#CeeeC##......ryr......##SfffS#",
            "#CCcccCC###.........###SSsssSS#",
            ".#CCccccCCC##.....##SSSssssSS#.",
            "..##CCcccccCC#...#SSsssssSS##..",
            "....##CCCCCCCC#.#SSSSSSSS##....",
            "......#########.#########......",
        ];

        // whole, the ice half is shield blue and both eyes are shut; apart, ice is bare frost and each eye is gold in a crimson ring
        private static readonly Sprite Whole = Sprite.FromArt(Rows, Art.With(
            ('c', Palette.ShieldGlow), ('C', Palette.ShieldDeep), ('e', Palette.ShieldDeep), ('E', Palette.Shield),
            ('s', RoseLight), ('S', RoseDark), ('f', RoseDark), ('F', Husk)));
        private static readonly Sprite Bare = Sprite.FromArt(Rows, Art.With(
            ('c', FrostLight), ('C', FrostDark), ('e', Palette.WeakBody), ('E', Palette.WeakMark),
            ('s', RoseLight), ('S', RoseDark), ('f', Palette.WeakBody), ('F', Palette.WeakMark))).Without(SeamX, SeamY, SeamW, SeamH);
        // the knot is the seam's alone: neither half nor the body under them carries a pixel of it, so it goes when the seam does
        private static readonly Sprite Unknotted = Whole.Without(SeamX, SeamY, SeamW, SeamH);
        private static readonly Sprite IceArt = Unknotted.Cut(0, 0, HalfArtW, ArtH);
        private static readonly Sprite RoseArt = Unknotted.Cut(Whole.W - HalfArtW, 0, HalfArtW, ArtH);
        private static readonly Sprite IceOpenArt = Bare.Cut(0, 0, HalfArtW, ArtH);
        private static readonly Sprite RoseOpenArt = Bare.Cut(Whole.W - HalfArtW, 0, HalfArtW, ArtH);
        private static readonly Sprite SeamArt = Whole.Cut(SeamX, SeamY, SeamW, SeamH);
        private static readonly Sprite IceWreck = Art.Wrecked(IceArt);
        private static readonly Sprite RoseWreck = Art.Wrecked(RoseArt);
        // one clear row above the art makes the body 14 tall, so a whole pixel row sits where each part's half-pixel offset puts it
        // and the parts land exactly over the body at every position the owner can hold
        private static readonly Sprite Padded = Unknotted.Cut(0, -1, Whole.W, Whole.H + 1);
        private static readonly Sprite Nothing = new(1, 1);

        private enum Who { None, Ice, Rose }

        /// <summary>What one schism remembers.</summary>
        private sealed class Split
        {
            public Part Ice = null!, Rose = null!, Seam = null!;
            public bool Scaled;
            public bool IceRight;   // the side ice is on, or is crossing to
            public int Clock;       // frames apart since the last crossing began
            public double IceCd, RoseCd;
            public Who Speaker;
        }

        public override EnemyKind Kind => EnemyKind.Schism;
        public override string Name => "SCHISM";
        public override Sprite Body => Whole;
        public override uint Colour => RoseLight;
        public override double Hp => IceHp + RoseHp;
        public override int Score => BossScore;
        public override bool Boss => true;

        private static Split StateOf(Enemy e) => (Split)e.Kit!;

        private static double HoldY(RaiderGame g) => g.PH < ShortField ? ShortHoldY : Math.Clamp(HoldShare * g.PH, HoldMin, HoldMax);
        private static double FloorRow(RaiderGame g) => g.PH - Math.Max(FloorMin, FloorShare * g.PH);
        private static double Stagger(RaiderGame g) => Math.Min(StaggerMax, FloorRow(g) - (HoldY(g) + HalfDrop));

        public override void Spawn(RaiderGame g, Enemy e)
        {
            var k = new Split();
            e.Kit = k;
            Part Half(string name, int ox, Sprite art, Sprite open, Sprite wreck, double hp) => new()
            {
                Name = name,
                OX = ox,
                OY = HalfPixel,
                HW = HalfBoxW,
                HH = HalfBoxH,
                Closed = art,
                OpenArt = open,
                Wreck = wreck,
                State = PartState.Armoured,
                Vital = true,
                Hp = hp,
                MaxHp = hp,
                Score = ScoreHalf,
            };
            k.Ice = Half("ice", -HalfOffset, IceArt, IceOpenArt, IceWreck, IceHp);
            k.Rose = Half("rose", HalfOffset, RoseArt, RoseOpenArt, RoseWreck, RoseHp);
            k.Seam = new Part
            {
                Name = "seam",
                OX = 0,
                OY = HalfPixel,
                HW = SeamW / 2,
                HH = SeamH / 2,
                Closed = SeamArt,
                OpenArt = SeamArt,
                State = PartState.Open,
                Vital = false,
                Hp = SeamHp,
                MaxHp = SeamHp,
                Score = 0,
            };
            g.SetParts(e, k.Ice, k.Rose, k.Seam);
            e.Sprite = Padded;
            e.Mode = Entering;
            k.IceCd = IceFirstCd; k.RoseCd = RoseFirstCd;
        }

        // ---------------------------------------------------------------- the machine

        public override void Update(RaiderGame g, Enemy e)
        {
            var k = StateOf(e);
            if (!k.Scaled)
            {
                // a later lap makes the boss tougher by writing its hit points over the owner's: spread that over the parts
                k.Scaled = true;
                double sum = k.Ice.MaxHp + k.Rose.MaxHp;
                if (e.MaxHp > sum * ScaleEpsilon)
                {
                    double f = e.MaxHp / sum;
                    foreach (var p in e.Parts!) { p.Hp *= f; p.MaxHp *= f; }
                }
            }
            if (e.Leaving) { e.Y -= LeaveSpeed; Glide(g, e, k); return; }

            double cx = Math.Floor(g.PW / 2.0) + HalfPixel;   // so the body and its parts round alike
            e.BaseX = cx; e.X = cx; e.KX = 0; e.KY = 0;
            double hold = HoldY(g);
            switch (e.Mode)
            {
                case Entering:
                    e.Y = Math.Min(hold, e.Y + EnterSpeed);
                    if (e.Y >= hold) { e.Mode = Joined; e.ModeAge = 0; }
                    return;
                case Joined:
                    e.Y = hold;
                    if (e.ModeAge >= JoinedFrames) { k.Seam.State = PartState.Dead; Break(g, e, k); }
                    break;
                default:
                    e.Y = hold;
                    Glide(g, e, k);
                    ApartTurn(g, e, k);
                    break;
            }
            Fire(g, e, k);
        }

        /// <summary>The seam is gone: the halves open, and come apart.</summary>
        private static void Break(RaiderGame g, Enemy e, Split k)
        {
            e.Mode = Apart;
            e.ModeAge = 0;
            k.Clock = 0;
            if (k.Ice.State == PartState.Armoured) k.Ice.State = PartState.Open;
            if (k.Rose.State == PartState.Armoured) k.Rose.State = PartState.Open;
            e.Sprite = Nothing;
            g.Cue(e, VoiceCue.Phase);
        }

        private static void Glide(RaiderGame g, Enemy e, Split k)
        {
            if (e.Mode < Apart) return;
            double speed = e.Mode == Swapping ? SwapSpeed : SlideSpeed;
            double stagger = Stagger(g);
            // a dead half lies where it fell
            if (k.Ice.State != PartState.Dead) Seek(k.Ice, SideX(g, k.IceRight) - e.X, 0, speed);
            if (k.Rose.State != PartState.Dead) Seek(k.Rose, SideX(g, !k.IceRight) - e.X, stagger, speed);
        }

        /// <summary>The x a half stands at: the far side of the field when <paramref name="right"/>, else the near side.</summary>
        private static double SideX(RaiderGame g, bool right) => (right ? FarShare : NearShare) * g.PW;

        private static void Seek(Part p, double ox, double oy, double speed)
        {
            p.OX += Math.Clamp(ox - p.OX, -speed, speed);
            p.OY += Math.Clamp(oy - p.OY, -GlideSpeed, GlideSpeed);
        }

        private static bool Arrived(Enemy e, Split k, RaiderGame g) =>
            Math.Abs(k.Ice.OX - (SideX(g, k.IceRight) - e.X)) < ArrivedWithin && Math.Abs(k.Rose.OX - (SideX(g, !k.IceRight) - e.X)) < ArrivedWithin;

        /// <summary>The count to a crossing, the warning, the crossing, and the mending of a dead half.</summary>
        private void ApartTurn(RaiderGame g, Enemy e, Split k)
        {
            if (e.Mode == Mending)
            {
                if (e.ModeAge % TickEvery == 0 && e.ModeAge > 0) g.Sound(SfxNames.Tick, e.X);
                if (e.ModeAge >= MendFrames)
                {
                    var half = k.Ice.State == PartState.Dead ? k.Ice : k.Rose;
                    g.Revive(e, half, ReturnShare * half.MaxHp);
                    if (half == k.Ice) k.IceCd = IceEvery; else { k.RoseCd = RoseEvery - RoseWarn; k.Rose.Timer = 0; }
                    e.Mode = Apart;
                    e.ModeAge = 0;
                    g.Sound(SfxNames.Open, e.X + half.OX);
                }
                return;
            }
            k.Clock++;
            if (e.Mode == Swapping)
            {
                if (Arrived(e, k, g)) { e.Mode = Apart; e.ModeAge = 0; }
                return;
            }
            if (e.Windup > 0)
            {
                e.Windup--;
                if (e.Windup % WarnBlinkPeriod < WarnBlinkPeriod / 2)
                {
                    if (k.Ice.State != PartState.Dead) k.Ice.Flash = Math.Max(k.Ice.Flash, WarnFlash);
                    if (k.Rose.State != PartState.Dead) k.Rose.Flash = Math.Max(k.Rose.Flash, WarnFlash);
                }
                if (e.Windup == 0)
                {
                    e.Mode = Swapping;
                    e.ModeAge = 0;
                    k.Clock = 0;
                    k.IceRight = !k.IceRight;
                    g.Sound(SfxNames.Charge, e.X);
                }
            }
            else if (k.Clock >= SwapEvery - SwapWarn)
            {
                e.Windup = SwapWarn;
                g.Sound(SfxNames.Warn, e.X);
            }
        }

        // ---------------------------------------------------------------- fire

        private void Fire(RaiderGame g, Enemy e, Split k)
        {
            double faster = e.Mode == Mending ? MendFaster : 1;
            double mul = Ramp.BoltSpeedMul(g.Stage);
            double tx = g.Alive ? g.PX : g.PW / 2.0, ty = g.Alive ? g.PY : g.PH - Tuning.ShipStartAbove;

            if (k.Ice.State != PartState.Dead && (k.IceCd -= faster) <= 0)
            {
                if (g.Bullets.Count(b => !b.Friendly) + FanWidth > MaxHostile) k.IceCd = FullRetry;   // the field is full: it waits
                else
                {
                    k.IceCd = IceEvery;
                    double x = e.X + k.Ice.OX, y = e.Y + k.Ice.OY + IceMuzzleDrop, a = Math.Atan2(ty - y, tx - x), v = IceSpeed * mul;
                    for (int i = 0; i < FanWidth; i++)
                    {
                        double b = a + (i - FanWidth / 2) * FanStep;
                        g.Bullets.Add(new Bullet { X = x, Y = y, VX = Math.Cos(b) * v, VY = Math.Sin(b) * v });
                    }
                    g.Sound(SfxNames.Lob, x);
                }
            }

            var rose = k.Rose;
            if (rose.State == PartState.Dead) return;
            if (rose.Timer > 0)
            {
                if (rose.Timer > 1) rose.Timer--;
                else if (g.Bullets.Count(b => !b.Friendly) < MaxHostile)
                {
                    rose.Timer = 0;
                    k.RoseCd = Math.Max(0, RoseEvery / faster - RoseWarn);
                    double x = e.X + rose.OX, y = e.Y + rose.OY + RoseMuzzleDrop, a = Math.Atan2(e.AimY - y, e.AimX - x), v = RoseSpeed * mul;
                    g.Bullets.Add(new Bullet { X = x, Y = y, VX = Math.Cos(a) * v, VY = Math.Sin(a) * v, Aimed = true });
                    g.Sound(SfxNames.Beam, x);
                }
            }
            else if (--k.RoseCd <= 0)
            {
                e.AimX = tx; e.AimY = ty;   // where the ship stands now, not where it will be
                rose.Timer = RoseWarn;
                g.Sound(SfxNames.Charge, e.X + rose.OX);
            }
        }

        // ---------------------------------------------------------------- the halves

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            var k = StateOf(e);
            g.Sound(SfxNames.Part, e.X + p.OX);
            if (p == k.Seam)
            {
                if (e.Mode == Joined) Break(g, e, k);
                return;
            }
            p.Score = 0;   // the first death of a half pays; the second pays nothing
            p.Timer = 0;
            p.Flash = 0;   // the husk shows as a husk at once
            e.Mode = Mending;
            e.ModeAge = 0;
            e.Windup = 0;
        }

        // ---------------------------------------------------------------- the voice

        // the halves argue: neither lets the other's post stand for long
        private const int ArguePace = 4 * Tuning.Fps;

        public override int IdleFrames(Enemy e) => ArguePace;

        /// <summary>Hands the floor to the other half (the one left alone while mending).</summary>
        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue)
        {
            var k = StateOf(e);
            if (cue == VoiceCue.ShipDied) return Turn.Wait;   // the half that was speaking gloats, once its post is read
            if (cue == VoiceCue.PartLost && e.Mode != Mending) return Turn.Skip;
            if (e.Mode == Mending) k.Speaker = k.Ice.State == PartState.Dead ? Who.Rose : Who.Ice;   // the half that is left speaks alone
            else k.Speaker = k.Speaker == Who.Ice ? Who.Rose : Who.Ice;
            if (e.Voice is { } v) v.Prefix = Prefix(g, e);   // the name shows with the words, not a frame after them
            return Turn.Cut;
        }

        public override int Taste(Enemy e, IReadOnlyList<RudePost> waiting)
        {
            return StateOf(e).Speaker == Who.Rose ? waiting.Count - 1 : 0;
        }

        public override string? Line(Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive => "Both sides have a point.",
            VoiceCue.Phase => "No they do not.",
            VoiceCue.PartLost => "We are working on it.",
            VoiceCue.Dying => "Agree to dis-",
            _ => null,
        };

        public override string? Prefix(RaiderGame g, Enemy e) => StateOf(e).Speaker switch
        {
            Who.Ice => "FROST",   // the letters ICE over a stranger's words read as the name of an agency
            Who.Rose => "ROSE",
            _ => null,
        };

        // ---------------------------------------------------------------- warnings

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            var k = StateOf(e);
            var g = c.Game;
            if (e.Mode == Mending)
            {
                var dead = k.Ice.State == PartState.Dead ? k.Ice : k.Rose.State == PartState.Dead ? k.Rose : null;
                if (dead is not null && dead.Wreck is { } wreck)
                {
                    uint dark = dead == k.Ice ? FrostDark : RoseDark;
                    int filled = Math.Min(wreck.H, wreck.H * e.ModeAge / MendFrames);
                    int x0 = (int)Math.Round(e.X + dead.OX - wreck.W / 2.0), y0 = (int)Math.Round(e.Y + dead.OY - wreck.H / 2.0);
                    for (int row = wreck.H - filled; row < wreck.H; row++)
                        for (int col = 0; col < wreck.W; col++)
                            if ((wreck.Px[row * wreck.W + col] & ColourMask) == Husk) c.Set(x0 + col, y0 + row, dark);
                }
            }
            var ice = k.Ice;
            if (ice.State != PartState.Dead && k.IceCd <= IceTellFrames && e.Mode != Entering)
            {
                int left = (int)Math.Round(e.X + ice.OX - IceArt.W / 2.0), top = (int)Math.Round(e.Y + ice.OY - IceArt.H / 2.0);
                c.Set(left + IceEyeX, top + EyeY, Palette.Tell);
                c.Set((int)Math.Round(e.X + ice.OX), (int)Math.Round(e.Y + ice.OY + IceMuzzleDrop), Palette.Tell);
            }
            var rose = k.Rose;
            if (rose.Timer > 0 && rose.State != PartState.Dead)
            {
                bool on = rose.Timer <= EyeSolidFrames || rose.Timer / EyeBlinkFrames % 2 == 0;
                if (on)
                {
                    int ex = (int)Math.Round(e.X + rose.OX - RoseArt.W / 2.0) + RoseEyeX, ey = (int)Math.Round(e.Y + rose.OY - RoseArt.H / 2.0) + EyeY;
                    c.Set(ex, ey, Palette.Tell);
                }
                double fx = e.X + rose.OX, fy = e.Y + rose.OY + RoseAimDrop;
                int steps = (int)Math.Max(Math.Abs(e.AimX - fx), Math.Abs(e.AimY - fy));
                for (int i = PathStart; i <= steps; i += PathDashStep)
                    c.Set((int)Math.Round(fx + (e.AimX - fx) * i / steps), (int)Math.Round(fy + (e.AimY - fy) * i / steps), Palette.Tell);
            }
            if (e.Windup > 0)
            {
                Lane(c, e, k.Ice, SideX(g, !k.IceRight), FrostLight);
                Lane(c, e, k.Rose, SideX(g, k.IceRight), RoseLight);
            }
        }

        private static void Lane(RaiderView.Canvas c, Enemy e, Part p, double to, uint colour)
        {
            if (p.State == PartState.Dead) return;
            int y = (int)Math.Round(e.Y + p.OY), from = (int)Math.Round(e.X + p.OX), end = (int)Math.Round(to), dir = Math.Sign(end - from);
            for (int x = from; dir != 0 && (end - x) * dir > 0; x += dir * LaneDashStep) c.Set(x, y, colour);
        }
    }
}
