namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// The Brood (boss 6): a fat sac over <see cref="BroodBehaviour.HatchCount"/> hatches. A hatch is armoured while shut and is hurt only
    /// while open. One after another the hatches bulge for <see cref="BroodBehaviour.BulgeFrames"/> frames, open for
    /// <see cref="BroodBehaviour.OpenFrames"/> and let <see cref="BroodBehaviour.GnatsPerHatch"/> gnats out (never more than
    /// <see cref="BroodBehaviour.GnatCap"/> of its gnats live at once). Under 1/<see cref="BroodBehaviour.BrokenShare"/> of the sac's hit
    /// points a hatch opens every <see cref="BroodBehaviour.FrenzyGap"/> frames instead of every <see cref="BroodBehaviour.LayGap"/>; with
    /// every hatch dead the sac charges a ring of spores and throws it, as <see cref="BroodBehaviour.Barren"/> tells.
    /// A dead hatch is a hole: shots fly on through it to the sac. It shoots nothing while it lays, so the gnats are the whole threat.
    /// </summary>
    private sealed class BroodBehaviour : EnemyBehaviour
    {
        private const int HatchCount = 4, GnatCap = 10, LayGap = 70, FrenzyGap = 45, BulgeFrames = 24, OpenFrames = 45;
        private const int ChargeFrames = 30, RingGap = 80, RingSpores = 8, BrokenShare = 2;
        private const double SacHp = 48, HatchHp = 10, SporeSpeed = 0.85, RingRadius = 8, SwayRate = 0.03, SwayShare = 0.1, LeaveSpeed = 0.8;
        private const int HatchScore = 300;
        private const int ModeLaying = 1, ModeFrenzy = 2, ModeBarren = 3;
        private const int GnatsPerHatch = 2;
        private const double GnatSideOffset = 1.5, GnatDropRows = 3;
        /// <summary>The puff of a gnat that dies with its sac: how many specks, their speed range, and their life range in frames.</summary>
        private const int PuffSpecks = 3, PuffLife = 10, PuffLifeSpread = 8;
        private const double PuffSpeed = 0.3, PuffSpeedSpread = 0.9;
        private const double HalfPixel = 0.5;

        // 33 by 13. Rows 10 to 12 are the hatches, which are parts drawn over a bare hull: the hull stays whole when one is shot away.
        private static readonly string[] HullRows =
        [
            "...........###########...........",
            ".......####ooooooooooo####.......",
            "....###oooogggooooogggoooo###....",
            "..##ooogggooooogggooooogggooo##..",
            ".#oogggoooooggorrroggooooogggoo#.",
            "#ooggoooogggoooryrooogggooooggoo#",
            "#oooooggoooooggrrrggoooooggooooo#",
            "#oggooooooggoooooooooggooooooggo#",
            ".#ooooooooooooooooooooooooooooo#.",
            "..#############################..",
            ".................................",
            ".................................",
            ".................................",
        ];

        private static readonly string[] ShutRows =
        [
            "#####",
            "#ooo#",
            "#o#o#",
            ".###.",
        ];

        private static readonly string[] MouthRows =
        [
            "#####",
            "#rrr#",
            "#ryr#",
            ".#.#.",
        ];

        private static readonly Dictionary<char, uint> Pal = GnatBehaviour.ClearDotPalette();
        private static readonly Sprite Hull = Sprite.FromArt(HullRows, Pal);
        private static readonly Sprite SacArt = Hull.Cut(0, 0, Hull.W, SacRows);
        private static readonly Sprite ShutArt = Sprite.FromArt(ShutRows, Pal);
        private static readonly Sprite MouthArt = Sprite.FromArt(MouthRows, Pal);

        /// <summary>The columns of the hatches in the drawing, and the drawing's row they hang from.</summary>
        private static readonly int[] HatchLeft = [3, 10, 18, 25];
        private const int HatchRow = 9;
        /// <summary>The sac is the hull down to the hatch row; its hit box reaches the outline on either side.</summary>
        private const int SacRows = HatchRow + 1, SacHalfW = 16, SacHalfH = SacRows / 2;
        /// <summary>The sac is the first part and the hatches follow it in order, so hatch i is part <c>FirstHatch + i</c>.</summary>
        private const int SacIndex = 0, FirstHatch = SacIndex + 1;
        /// <summary>No hatch is bulging.</summary>
        private const int NoHatch = -1;
        /// <summary>A hatch's hit box, in pixels either way of its centre: it keeps inside the shut drawing.</summary>
        private const int HatchHalfW = 2, HatchHalfH = 2;
        /// <summary>The bulge lights the plate's 'o' pixels in the shut drawing: a row from this corner, this wide, and the pixel under each end.</summary>
        private const int BulgeLeft = 1, BulgeTop = 1, BulgeW = 3;
        /// <summary>The bulge blinks on and off every this many frames.</summary>
        private const int BlinkFrames = 3;
        /// <summary>A bulge whitens for the last 1/<see cref="WhiteShare"/> of its frames, and the charging ring for the same share of the charge.</summary>
        private const int WhiteShare = 3, BulgeWhiteFrames = BulgeFrames / WhiteShare, RingWhiteFrom = ChargeFrames - ChargeFrames / WhiteShare;
        /// <summary>The side in pixels of one spore of the charging ring.</summary>
        private const int SporeDot = 2;
        private const int KillScore = 6000;
        /// <summary>The sac's part name; the hatches are the other parts.</summary>
        private const string SacName = "sac";

        /// <summary>What one Brood remembers: the frames since a hatch last opened, the hatch bulging now, which hatch comes next, and the
        /// barren sac's charge count and rings thrown.</summary>
        private sealed class Laying
        {
            public int Clock, Bulging = NoHatch, Next, Charge, Rings;
        }

        public override EnemyKind Kind => EnemyKind.Brood;
        public override string Name => "BROOD";
        public override Sprite Body => Hull;
        public override uint Colour => GnatBehaviour.Bile;
        public override double Hp => SacHp;
        public override int Score => KillScore;
        public override bool Boss => true;
        public override double DropChance => 0;

        public override void Spawn(RaiderGame g, Enemy e)
        {
            var parts = new List<Part>
            {
                new()
                {
                    Name = SacName, OX = 0, OY = SacHalfH - Hull.H / 2.0, HW = SacHalfW, HH = SacHalfH, Closed = SacArt, State = PartState.Open,
                    Vital = true, Hp = e.Hp, MaxHp = e.Hp, Score = 0,   // the kill scores the Brood's own KillScore
                },
            };
            for (int i = 0; i < HatchCount; i++)
                parts.Add(new Part
                {
                    Name = $"hatch-{i + 1}",
                    OX = HatchLeft[i] + ShutArt.W / 2.0 - Hull.W / 2.0,
                    OY = HatchRow + ShutArt.H / 2.0 - Hull.H / 2.0,
                    HW = HatchHalfW,
                    HH = HatchHalfH,
                    Closed = ShutArt,
                    OpenArt = MouthArt,
                    Wreck = null,   // no wreck: a dead hatch is a hole the shots go through
                    State = PartState.Armoured,
                    Vital = false,
                    Hp = HatchHp,
                    MaxHp = HatchHp,
                    Score = HatchScore,
                });
            g.SetParts(e, [.. parts]);
            e.Kit = new Laying();
        }

        /// <summary>The row the centre holds: on a short field the top sits on row 5, under the health bar and the HUD; on a
        /// tall one a quarter of the way down. Always on a half pixel, so the body and the parts cut from it round the same way.</summary>
        private static double HoldY(RaiderGame g) => Math.Floor(BossHoldRow(g, Hull.H)) + HalfPixel;

        private static double Snap(double v) => Math.Floor(v) + HalfPixel;

        private static bool Living(Part p) => p.Name != SacName && p.State != PartState.Dead;

        public override void Update(RaiderGame g, Enemy e)
        {
            var k = (Laying)e.Kit!;
            double half = Hull.W / 2.0;
            if (e.Leaving) { e.Y -= LeaveSpeed; return; }
            double hold = HoldY(g);
            if (e.Y < hold)
            {
                e.Y = Snap(e.Y);
                if ((e.Age & 1) == 0) e.Y = Math.Min(hold, e.Y + 1);   // half speed in whole steps, so the body keeps to its half-pixel grid
            }
            e.X = Snap(Math.Clamp(e.BaseX + Math.Sin(e.Age * SwayRate + e.Phase) * g.PW * SwayShare, half, g.PW - half));
            if (e.Y < hold) return;

            var parts = e.Parts!;
            var sac = parts[SacIndex];
            if (!parts.Any(Living)) { Barren(g, e, k); return; }

            int mode = sac.Hp < sac.MaxHp / BrokenShare ? ModeFrenzy : ModeLaying;
            if (e.Mode != mode) { e.Mode = mode; e.ModeAge = 0; }
            foreach (var p in parts)
                if (p.Name != SacName && p.State == PartState.Open && --p.Timer <= 0) { p.State = PartState.Armoured; p.Timer = 0; }

            k.Clock++;
            if (k.Bulging != NoHatch)
            {
                var h = parts[FirstHatch + k.Bulging];
                if (h.State != PartState.Armoured) k.Bulging = NoHatch;   // shot away while bulging: its turn is lost
                else if (--h.Timer <= 0) OpenHatch(g, e, k, h);
            }
            else if (k.Clock >= (mode == ModeFrenzy ? FrenzyGap : LayGap) - BulgeFrames)
            {
                for (int i = 0; i < HatchCount; i++)
                {
                    int at = (k.Next + i) % HatchCount;
                    var h = parts[FirstHatch + at];
                    if (h.State != PartState.Armoured) continue;
                    k.Bulging = at; k.Next = (at + 1) % HatchCount;
                    h.Timer = BulgeFrames;
                    g.Sound(SfxNames.Open, e.X + h.OX);
                    break;
                }
            }
        }

        /// <summary>How many more gnats this Brood may have out at once.</summary>
        private static int Room(RaiderGame g, Enemy e) =>
            GnatCap - (g.Enemies.Count(x => x.Kind == EnemyKind.Gnat && ReferenceEquals(x.Parent, e))
                + g._born.Count(x => x.Kind == EnemyKind.Gnat && ReferenceEquals(x.Parent, e)));

        private static void OpenHatch(RaiderGame g, Enemy e, Laying k, Part h)
        {
            h.State = PartState.Open;
            h.Timer = OpenFrames;
            k.Bulging = NoHatch;
            k.Clock = 0;
            g.Sound(SfxNames.Hatch, e.X + h.OX);
            int room = Room(g, e);
            for (int i = 0; i < GnatsPerHatch && i < room; i++)
                g.SpawnAdd(EnemyKind.Gnat, e.X + h.OX + (i == 0 ? -GnatSideOffset : GnatSideOffset), e.Y + h.OY + GnatDropRows, e);
        }

        /// <summary>No hatch left: the sac charges a ring of <see cref="RingSpores"/> spores for <see cref="ChargeFrames"/> frames and throws it,
        /// every <see cref="RingGap"/>.</summary>
        private static void Barren(RaiderGame g, Enemy e, Laying k)
        {
            if (e.Mode != ModeBarren) { e.Mode = ModeBarren; e.ModeAge = 0; k.Charge = 0; k.Bulging = NoHatch; }
            k.Charge++;
            int ph = k.Charge % RingGap;
            if (ph == 0 || k.Charge == 1) g.Sound(SfxNames.Charge, e.X);
            if (ph != ChargeFrames) return;
            var (cx, cy) = RingCentre(e);
            int thrown = k.Rings++;
            for (int i = 0; i < RingSpores; i++)
            {
                double a = SporeAngle(thrown, i);
                g.Bullets.Add(new Bullet
                {
                    X = cx + Math.Cos(a) * RingRadius,
                    Y = cy + Math.Sin(a) * RingRadius,
                    VX = Math.Cos(a) * SporeSpeed,
                    VY = Math.Sin(a) * SporeSpeed,
                });
            }
            g.Sound(SfxNames.Lob, e.X);
        }

        private static (double X, double Y) RingCentre(Enemy e) => (e.X, e.Y + e.Parts![SacIndex].OY);

        /// <summary>The angle of one spore in the ring that follows <paramref name="thrown"/> rings: each ring is turned half a gap from the last.</summary>
        private static double SporeAngle(int thrown, int spore) => (thrown & 1) * Math.PI / RingSpores + spore * Math.Tau / RingSpores;

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            if (p.Name == SacName) return;
            g.Sound(SfxNames.Part, e.X + p.OX);
            if (!e.Parts!.Any(Living)) g.Cue(e, VoiceCue.Phase);   // barren, said before the game cues the loss of the hatch
        }

        public override void Down(RaiderGame g, Enemy e)
        {
            foreach (var x in g.Enemies)
            {
                if (x.Kind != EnemyKind.Gnat || !ReferenceEquals(x.Parent, e)) continue;
                for (int i = 0; i < PuffSpecks; i++)
                {
                    double a = g.R() * Math.Tau, sp = PuffSpeed + g.R() * PuffSpeedSpread;
                    g.AddParticle(new Particle { X = x.X, Y = x.Y, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp, Life = PuffLife + (int)(g.R() * PuffLifeSpread), Colour = GnatBehaviour.Bile, Kind = ParticleKind.Debris });
                }
                x.Leaving = true;
                x.Y = -x.Sprite.H - 1;   // out of the field: the game removes it without a kill or a score
            }
        }

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            if (e.Parts is null || e.Parts.Count <= HatchCount) return;
            foreach (var p in e.Parts)
            {
                if (p.Name == SacName || p.State != PartState.Armoured || p.Timer <= 0) continue;
                bool white = p.Timer <= BulgeWhiteFrames && Room(c.Game, e) > 0;   // white only when a gnat will come out
                if (!white && p.Timer / BlinkFrames % 2 != 0) continue;
                int x0 = (int)Math.Round(e.X + p.OX - ShutArt.W / 2.0), y0 = (int)Math.Round(e.Y + p.OY - ShutArt.H / 2.0);
                uint colour = white ? Palette.Tell : GnatBehaviour.Bile;
                c.Rect(x0 + BulgeLeft, y0 + BulgeTop, BulgeW, 1, colour);
                c.Set(x0 + BulgeLeft, y0 + BulgeTop + 1, colour);
                c.Set(x0 + BulgeLeft + BulgeW - 1, y0 + BulgeTop + 1, colour);
            }
            if (e.Mode != ModeBarren) return;
            var k = (Laying)e.Kit!;
            int ph = k.Charge % RingGap;
            if (k.Charge == 0 || ph >= ChargeFrames) return;
            var (cx, cy) = RingCentre(e);
            for (int i = 0; i < RingSpores; i++)
            {
                double a = SporeAngle(k.Rings, i);
                int x = (int)Math.Round(cx + Math.Cos(a) * RingRadius), y = (int)Math.Round(cy + Math.Sin(a) * RingRadius);
                c.Rect(x, y, SporeDot, SporeDot, ph >= RingWhiteFrom ? Palette.Tell : GnatBehaviour.Bile);
            }
        }

        // ---------------------------------------------------------------- the voice: it feeds on what is fresh, and is soon on to the next

        public override int Taste(Enemy e, IReadOnlyList<RudePost> waiting) => waiting.Count - 1;

        /// <summary>How long its speech box stays shut before the Idle cue: shorter than <see cref="Tuning.VoiceIdleFrames"/>, so it is soon on to the next.</summary>
        private const int BroodPace = 5 * Tuning.Fps;

        public override int IdleFrames(Enemy e) => BroodPace;

        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue)
        {
            if (cue == VoiceCue.PartLost && e.Parts is not null && !e.Parts.Any(Living)) return Turn.Skip;   // the last hatch speaks for the phase
            return base.TurnFor(g, e, cue);
        }

        public override string? Line(Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive => "Follow for follow.",
            VoiceCue.PartLost => "Engagement is down.",
            VoiceCue.Phase => "You will regret the reach.",
            VoiceCue.Dying => "Unfollowed.",
            _ => null,
        };
    }
}
