namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>What the Mirror and the Glint share: the glass palettes and the drawing of a lit pane and a dull one. The return of a
    /// shot is the Glint's alone: its lit pane sends a ship's shot straight back down the column it came up, slower than the ship,
    /// and at most once every <see cref="ReturnGap"/> frames; the shots it does not return only ping.</summary>
    private static class Glass
    {
        /// <summary>Glass a shot can break is silver; the blue of <see cref="Shade"/> is for the eye behind its lid, which nothing hurts.</summary>
        public const uint Silver = 0x8f8a9a, Sheen = 0xd8d4e0, Shade = 0x5f7f9f;
        /// <summary>The mark in the middle of every pane of glass that can break: gold ringed in crimson, <see cref="MarkSize"/> square.</summary>
        public const int MarkSize = 3;
        public static readonly Sprite Mark = Sprite.FromArt(["rrr", "ryr", "rrr"], Art.DarkPal);
        /// <summary>The speed of a returned shot.</summary>
        public const double ReturnSpeed = 1.3;
        public const int ReturnGap = 6, ShimmerFrames = 24;

        /// <summary>A pane that can be hurt: outline in plum, body in two silvers round the mark.</summary>
        public static readonly Dictionary<char, uint> DullPalette = Art.With(('I', Silver), ('i', Sheen));
        /// <summary>A lit pane throws shots back, so it is a shield: all ice inside a shield-blue edge, its mark hidden.</summary>
        public static readonly Dictionary<char, uint> LitPalette = Art.With(('I', Palette.Ice), ('i', Palette.Ice), ('#', Palette.ShieldGlow), ('r', Palette.Ice), ('y', Palette.Ice));

        /// <summary>Turns a ship's shot that struck a lit pane into a hostile one that falls from where it struck. Does nothing when
        /// <paramref name="allowed"/> is false: the shot is then stopped and pings, as armour stops it.</summary>
        public static void ReturnShot(RaiderGame g, Bullet b, bool allowed)
        {
            if (!allowed) return;
            g.Shrug(b.X, b.Y);
            b.VX = 0; b.VY = ReturnSpeed;
            b.Friendly = false; b.Reflected = true;
        }
    }

    /// <summary>The geometry and the art of a Mirror whose panes have <see cref="K"/> facets each, in its tall form or its short
    /// one, drawn once per size. The facets stand edge to edge and step down toward the eye, so the whole reads as one wide wall of
    /// glass.</summary>
    private sealed class MirrorLayout
    {
        // the eye between the panes, tall and short: a gold pupil in a crimson iris, in a violet socket
        private static readonly string[] EyeRowsTall =
        [
            "###########",
            "#o+++++++o#",
            "#+ooooooo+#",
            "#+orrrrro+#",
            "#+orrrrro+#",
            "#+orryrro+#",
            "#+oryyyro+#",
            "#+orryrro+#",
            "#+orrrrro+#",
            "#+orrrrro+#",
            "#+ooooooo+#",
            "#o+++++++o#",
            ".#o+++++o#.",
            "..#######..",
        ];
        private static readonly string[] EyeRowsShort =
        [
            "###########",
            "#o+++++++o#",
            "#+orrrrro+#",
            "#+orryrro+#",
            "#+oryyyro+#",
            "#+orryrro+#",
            "#+orrrrro+#",
            "#o+++++++o#",
            ".#o+++++o#.",
            "..#######..",
        ];

        /// <summary>A facet and the eye are as wide as each other, so every seam of the wall is the same.</summary>
        public const int FacetW = 11, EyeW = 11;
        /// <summary>A field shorter than this has room only for the short form.</summary>
        public const int ShortFieldH = 44;
        /// <summary>The fewest and the most facets a pane has, and the columns of field each facet asks for.</summary>
        private const int MinFacets = 2, MaxFacets = 4, FieldPerFacet = 28;
        /// <summary>A facet's height, and the rows each facet (and then the eye) hangs below its outer neighbour: tall form, then short.</summary>
        private const int FacetHTall = 14, StepTall = 2, FacetHShort = 10, StepShort = 1;
        /// <summary>The sheen on the glass: a slanted streak this many pixels thick, repeated every this many.</summary>
        private const int SheenEvery = 7, SheenThick = 2;
        /// <summary>The eye's pupil: its column in the eye's art, and how far above the art's middle row its top is.</summary>
        public const int PupilCol = 4, PupilSize = 3, PupilRise = 2;

        public readonly int K, W, H, FacetH, Step;
        /// <summary>What the enemy's own sprite shows: the empty frame of every facet, so a broken facet leaves a hole in the wall and the wall keeps its size.</summary>
        public readonly Sprite Body;
        /// <summary>The whole Mirror at rest, for the roster: every facet dull and the eye shut.</summary>
        public readonly Sprite Picture;
        public readonly Sprite LitLeft, LitRight, DullLeft, DullRight, EyeOpen, EyeShut;

        private MirrorLayout(int k, bool isShort)
        {
            K = k; FacetH = isShort ? FacetHShort : FacetHTall; Step = isShort ? StepShort : StepTall;
            W = 2 * FacetW * k + EyeW; H = FacetH + Step * k;
            DullLeft = Sprite.FromArt(FacetRows(true), Glass.DullPalette);
            DullRight = Sprite.FromArt(FacetRows(false), Glass.DullPalette);
            LitLeft = Sprite.FromArt(FacetRows(true), Glass.LitPalette);
            LitRight = Sprite.FromArt(FacetRows(false), Glass.LitPalette);
            var eyeRows = isShort ? EyeRowsShort : EyeRowsTall;
            EyeOpen = Sprite.FromArt(eyeRows, Glass.DullPalette);
            EyeShut = Sprite.FromArt(eyeRows, Art.With(('r', Glass.Shade), ('y', Palette.Ice)));
            var frame = Sprite.FromArt(FrameRows(), Glass.DullPalette);
            Body = new Sprite(W, H);
            Picture = new Sprite(W, H);
            for (int i = 0; i < k; i++)
            {
                Stamp(Body, frame, LeftX(i), FacetY(i));
                Stamp(Body, frame, RightX(i), FacetY(i));
                Stamp(Picture, DullLeft, LeftX(i), FacetY(i));
                Stamp(Picture, DullRight, RightX(i), FacetY(i));
            }
            Stamp(Picture, EyeShut, EyeX, EyeY);
        }

        /// <summary>Where facet <paramref name="i"/> of a pane is in the drawing, counting from the outside in.</summary>
        public int LeftX(int i) => FacetW * i;
        public int RightX(int i) => W - FacetW * (i + 1);
        public int FacetY(int i) => Step * i;
        public int EyeX => FacetW * K;
        public const int MarkX = (FacetW - Glass.MarkSize) / 2;
        public int MarkY => (FacetH - Glass.MarkSize) / 2;
        public int EyeY => Step * K;

        /// <summary>A facet: a plum frame round glass with a slanted sheen that leans toward the eye.</summary>
        private string[] FacetRows(bool left)
        {
            var rows = new string[FacetH];
            for (int y = 0; y < FacetH; y++)
            {
                var row = new char[FacetW];
                for (int x = 0; x < FacetW; x++)
                {
                    int across = left ? x : FacetW - 1 - x;
                    int mx = x - MarkX, my = y - MarkY;
                    bool inMark = mx >= 0 && mx < Glass.MarkSize && my >= 0 && my < Glass.MarkSize;
                    row[x] = OnEdge(x, y) ? '#' : inMark ? (mx == Glass.MarkSize / 2 && my == Glass.MarkSize / 2 ? 'y' : 'r') : (across + y) % SheenEvery < SheenThick ? 'i' : 'I';
                }
                rows[y] = new string(row);
            }
            return rows;
        }

        /// <summary>A letter no palette holds, so the pixel stays clear and the field shows through a broken facet.</summary>
        private const char Clear = ' ';

        /// <summary>The frame a facet sits in: its outline, and a shard left in each corner.</summary>
        private string[] FrameRows()
        {
            var rows = new string[FacetH];
            for (int y = 0; y < FacetH; y++)
            {
                var row = new char[FacetW];
                for (int x = 0; x < FacetW; x++)
                {
                    bool corner = (x == 1 || x == FacetW - 2) && (y == 1 || y == FacetH - 2);
                    row[x] = OnEdge(x, y) ? '#' : corner ? 'o' : Clear;
                }
                rows[y] = new string(row);
            }
            return rows;
        }

        private bool OnEdge(int x, int y) => x == 0 || y == 0 || x == FacetW - 1 || y == FacetH - 1;

        private static void Stamp(Sprite onto, Sprite art, int x0, int y0)
        {
            for (int y = 0; y < art.H; y++)
                for (int x = 0; x < art.W; x++)
                    if (art.Px[y * art.W + x] != 0) onto.Px[(y0 + y) * onto.W + x0 + x] = art.Px[y * art.W + x];
        }

        private static readonly MirrorLayout[] Tall = [.. Enumerable.Range(MinFacets, MaxFacets - MinFacets + 1).Select(k => new MirrorLayout(k, false))];
        private static readonly MirrorLayout[] Short = [.. Enumerable.Range(MinFacets, MaxFacets - MinFacets + 1).Select(k => new MirrorLayout(k, true))];

        /// <summary>The layout rule: a pane has <c>clamp((PW - 11) / 28, 2, 4)</c> facets, and the form is the short one on a field under 44 pixels tall.</summary>
        public static MirrorLayout For(int pw, int ph) =>
            (ph < ShortFieldH ? Short : Tall)[Math.Clamp((pw - EyeW) / FieldPerFacet, MinFacets, MaxFacets) - MinFacets];
    }

    /// <summary>
    /// The Mirror (boss 9): one wide wall of glass, two panes of facets either side of an eye. Each pane in turn warns in white and
    /// then lights, and while it is lit its glare burns every column under it: the ship is hurt for where it stands, whether it
    /// fires or not, and is safe under the dull pane and under the eye. A shot that strikes a lit facet only pings. The eye is shut
    /// under a blue lid while the Mirror steps toward the ship, and open, gold on crimson, while a pane warns and burns. A facet
    /// is silver with a gold mark on crimson while a shot can break it, and ice blue while it is lit. A facet broken is a column the glare never falls on again. It says back the run's echoes.
    /// </summary>
    private sealed class MirrorBehaviour : EnemyBehaviour
    {
        /// <summary>One pane's turn: the Mirror steps with everything dull, stands still while the pane warns, then the pane burns.
        /// The warning is long enough for a ship at 1.5 pixels a frame to cross the widest pane (44 pixels) with frames to spare,
        /// and the burn is no longer than the ship's invulnerability after a hit, so one burn hurts a ship once at most.</summary>
        private const int RestFrames = 35, TellFrames = 40, BurnFrames = Tuning.InvulnFrames;
        private const int TurnFrames = RestFrames + TellFrames + BurnFrames, CycleFrames = 2 * TurnFrames;
        private const int StepEvery = 4, StepPixels = 2;
        private const double EnterSpeed = 1.0, LeaveSpeed = 0.8;
        private const int PaneHp = 30, PaneScore = 720, KillScore = 8000;
        private const int FireGap = 70, FireGapAngry = 50, BlinkFrames = 24;
        private const double SpreadSpeed = 1.15, SpreadAngle = 0.28;
        /// <summary>The row the drawing's top holds: one higher on a short field.</summary>
        private const int TopRow = 6, TopRowShort = 5;
        /// <summary>A facet's hit box is a pixel wider than its art, so no shot slips between two facets; the eye's is its art.</summary>
        private const int FacetHalfW = 6, EyeHalfW = 5;
        /// <summary>The glare reaches half a pixel past a facet's art either side: what the ship's centre must clear.</summary>
        private const double GlareReach = MirrorLayout.FacetW / 2.0 + 0.5;
        /// <summary>The warning grows in three steps: dotted lines at a facet's edges, whole lines, then lines with a scatter of white between them.</summary>
        private const int TellSteps = 3, ScatterEvery = 4;
        /// <summary>How white a warning facet is drawn at each step.</summary>
        private static readonly double[] TellWhite = [0.3, 0.55, 0.8];
        /// <summary>The eye blinks on and off every this many frames until the last third of its warning.</summary>
        private const int EyeBlinkFrames = 2;
        /// <summary>The eye holds open, unblinking, for the last third of its warning.</summary>
        private const int EyeSteadyFrames = BlinkFrames / 3;
        /// <summary>The Mirror steps toward the ship only while the pane is more than this many pixels off it across, so it never jitters over the ship.</summary>
        private const int StepDeadZone = 1;
        /// <summary>The warning's dots and the glare's ice stripes are this many pixels apart.</summary>
        private const int DotPitch = 2;
        /// <summary>The scatter between the warning's lines leans this many columns for every row down.</summary>
        private const int ScatterSlant = 2;
        /// <summary>The hit points the eye holds, which are the Mirror's own.</summary>
        private const double BossHp = 72;
        /// <summary>The size the sound of a pane starting to burn is given.</summary>
        private const double BurnSound = 1;
        /// <summary>The field the roster's picture of it is drawn for: three facets a pane.</summary>
        private const int RosterFieldW = 96, RosterFieldH = 120;

        /// <summary>The modes in <see cref="Enemy.Mode"/>: coming on, then the three parts of each pane's turn.</summary>
        public const int Entering = 0, LeftRest = 1, LeftTell = 2, LeftBurn = 3, RightRest = 4, RightTell = 5, RightBurn = 6;

        private sealed class Glassworks
        {
            public MirrorLayout Layout = null!;
            public int Top;
            public int X0;
            public int Clock;
            public int Fire = FireGap;
            public bool PaneBroke, Both, BothJust;
            public VoiceCue? Pending;
            public int EchoNext;
            public string? EchoText;
        }

        public override EnemyKind Kind => EnemyKind.Mirror;
        public override string Name => "MIRROR";
        public override Sprite Body => MirrorLayout.For(RosterFieldW, RosterFieldH).Picture;
        public override uint Colour => Palette.Ice;
        public override double Hp => BossHp;
        public override int Score => KillScore;
        public override bool Boss => true;
        public override double DropChance => 1;

        public override void Spawn(RaiderGame g, Enemy e)
        {
            var lay = MirrorLayout.For(g.PW, g.PH);
            var k = new Glassworks { Layout = lay, Top = g.PH < MirrorLayout.ShortFieldH ? TopRowShort : TopRow };
            k.X0 = Math.Clamp((g.PW - lay.W - 1) / 2, 0, MaxX0(g, lay)) & ~1;
            e.Kit = k;
            e.Sprite = lay.Body;
            e.X = e.BaseX = k.X0 + lay.W / 2.0;
            e.Y = -lay.H;
            e.Mode = Entering;
            var parts = new List<Part>();
            int hp = (PaneHp + lay.K - 1) / lay.K, score = PaneScore / lay.K;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < lay.K; i++)
                {
                    bool left = side == 0;
                    parts.Add(new Part
                    {
                        Name = (left ? LeftFacet : RightFacet) + i,
                        OX = (left ? lay.LeftX(i) : lay.RightX(i)) + MirrorLayout.FacetW / 2.0 - lay.W / 2.0,
                        OY = lay.FacetY(i) + lay.FacetH / 2.0 - lay.H / 2.0,
                        HW = FacetHalfW,
                        HH = lay.FacetH / 2,
                        Closed = left ? lay.LitLeft : lay.LitRight,
                        OpenArt = left ? lay.DullLeft : lay.DullRight,
                        Wreck = null,
                        State = PartState.Open,
                        Vital = false,
                        Hp = hp,
                        MaxHp = hp,
                        Score = score,
                    });
                }
            parts.Add(new Part
            {
                Name = EyeName,
                OX = 0,
                OY = lay.EyeY + lay.FacetH / 2.0 - lay.H / 2.0,
                HW = EyeHalfW,
                HH = lay.FacetH / 2,
                Closed = lay.EyeShut,
                OpenArt = lay.EyeOpen,
                State = PartState.Armoured,
                Vital = true,
                Hp = e.Hp,
                MaxHp = e.Hp,
                Score = 0,   // the kill scores the Mirror's own KillScore
            });
            g.SetParts(e, [.. parts]);
        }

        private const string LeftFacet = "facet-l-", RightFacet = "facet-r-", EyeName = "eye";
        private static bool IsLeft(Part p) => p.Name.StartsWith(LeftFacet, StringComparison.Ordinal);
        private static bool IsEye(Part p) => p.Name == EyeName;
        private static Part Eye(Enemy e) => e.Parts![^1];

        private static Glassworks Works(Enemy e) => (Glassworks)e.Kit!;

        /// <summary>The furthest right the drawing's left edge may stand, on an even column.</summary>
        private static int MaxX0(RaiderGame g, MirrorLayout lay) => Math.Max(0, g.PW - lay.W) & ~1;

        private static bool PaneBroken(Enemy e, bool left)
        {
            foreach (var p in e.Parts!)
                if (!IsEye(p) && IsLeft(p) == left && p.State != PartState.Dead) return false;
            return true;
        }

        private static bool OnLeft(int mode) => mode is LeftRest or LeftTell or LeftBurn;
        private static bool IsRest(int mode) => mode is LeftRest or RightRest;
        private static bool IsTell(int mode) => mode is LeftTell or RightTell;
        private static bool IsBurn(int mode) => mode is LeftBurn or RightBurn;

        /// <summary>The mode a frame of the cycle is in: the left pane's turn, then the right's.</summary>
        private static int ModeAt(int clock)
        {
            int into = clock % TurnFrames, part = into < RestFrames ? 0 : into < RestFrames + TellFrames ? 1 : 2;
            return (clock < TurnFrames ? LeftRest : RightRest) + part;
        }

        private static bool Angry(Enemy e, Glassworks k) => k.Both || e.Hp < e.MaxHp / 2;

        public override void Update(RaiderGame g, Enemy e)
        {
            var k = Works(e);
            var lay = k.Layout;
            Speak(g, e, k);
            e.X = e.BaseX = k.X0 + lay.W / 2.0;
            if (e.Leaving) { e.Y -= LeaveSpeed; GoDark(e); return; }
            double hold = k.Top + lay.H / 2.0;
            if (e.Y < hold)
            {
                e.Y = Math.Min(hold, e.Y + EnterSpeed);
                GoDark(e);
                if (e.Y < hold) return;
            }
            int mode = ModeAt(k.Clock);
            k.Clock = (k.Clock + 1) % CycleFrames;
            bool left = OnLeft(mode);
            if (mode != e.Mode)
            {
                if (IsTell(mode)) g.Sound(SfxNames.Open, e.X);
                else if (IsRest(mode) && e.Mode != Entering) g.Sound(SfxNames.Shut, e.X);
                else if (IsBurn(mode) && !PaneBroken(e, left)) g.Sound(SfxNames.Beam, e.X, BurnSound);
                e.Mode = mode; e.ModeAge = 0;
            }
            // a pane is lit as one, whatever is left of it; every other facet that lives is dull
            foreach (var p in e.Parts!)
            {
                if (p.State == PartState.Dead) continue;
                bool shielded = IsEye(p) ? IsRest(mode) : IsBurn(mode) && IsLeft(p) == left;
                p.State = shielded ? PartState.Armoured : PartState.Open;
            }

            if (g.Alive && IsRest(mode) && e.Age % StepEvery == 0)
            {
                double paneX = e.X + (left ? -1 : 1) * (MirrorLayout.FacetW * lay.K + MirrorLayout.EyeW) / 2.0, dx = g.PX - paneX;
                if (Math.Abs(dx) > StepDeadZone) k.X0 = Math.Clamp(k.X0 + Math.Sign(dx) * StepPixels, 0, MaxX0(g, lay));
                e.X = e.BaseX = k.X0 + lay.W / 2.0;
            }

            int gap = Angry(e, k) ? FireGapAngry : FireGap;
            if (k.Fire > gap) k.Fire = gap;
            if (--k.Fire == BlinkFrames - 1) g.Sound(SfxNames.Charge, e.X);
            if (k.Fire > 0) return;
            k.Fire = gap;
            if (!g.Alive) return;
            double fx = e.X, fy = e.Y + Eye(e).OY, aim = Math.Atan2(g.PY - fy, g.PX - fx);
            for (int i = -1; i <= 1; i++)
                g.Bullets.Add(new Bullet { X = fx, Y = fy, VX = Math.Cos(aim + i * SpreadAngle) * SpreadSpeed, VY = Math.Sin(aim + i * SpreadAngle) * SpreadSpeed });
        }

        /// <summary>While it comes on or leaves, no pane is lit and the eye is shut.</summary>
        private static void GoDark(Enemy e)
        {
            foreach (var p in e.Parts!)
                if (p.State != PartState.Dead) p.State = IsEye(p) ? PartState.Armoured : PartState.Open;
        }

        public override void PartDown(RaiderGame g, Enemy e, Part p)
        {
            if (IsEye(p)) return;
            var k = Works(e);
            g.Sound(SfxNames.Part, e.X + p.OX);
            bool left = IsLeft(p);
            if (!PaneBroken(e, left)) return;
            k.PaneBroke = true;
            if (!PaneBroken(e, !left)) return;
            k.Both = true; k.BothJust = true;
            g.Cue(e, VoiceCue.Phase);
        }

        // ---------------------------------------------------------------- the glare

        /// <summary>The living facets of the pane whose turn it is: the columns its warning marks and its glare burns.</summary>
        private static IEnumerable<Part> TurnFacets(Enemy e)
        {
            bool left = OnLeft(e.Mode);
            foreach (var p in e.Parts!)
                if (!IsEye(p) && p.State != PartState.Dead && IsLeft(p) == left) yield return p;
        }

        /// <summary>Lethal under every living facet of the burning pane, from the facet down to the floor, and at no other time and
        /// in no other place. What the ship fires has no say in it.</summary>
        public override bool Touches(RaiderGame g, Enemy e)
        {
            if (e.Leaving || !IsBurn(e.Mode)) return false;
            foreach (var p in TurnFacets(e))
                if (g.PY > e.Y + p.OY + p.HH && Math.Abs(g.PX - (e.X + p.OX)) <= GlareReach) return true;
            return false;
        }

        // ---------------------------------------------------------------- the voice

        public override string? Line(Enemy e, VoiceCue cue) => cue switch
        {
            VoiceCue.Arrive => "No, you.",
            VoiceCue.PartLost => "You broke the discourse.",
            VoiceCue.Phase => "Fine. Me, then.",
            VoiceCue.Dying => "No. Me.",
            _ => null,
        };

        /// <summary>It has nothing of its own to say, so on arriving, when a facet breaks and when its box has been shut a while it says back
        /// the run's echoes, newest first. With none to say back, a fresh waiting post, else its own line. A facet that is not the last
        /// of its pane gets a turn only when there is something to say.</summary>
        public override Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue)
        {
            var k = Works(e);
            bool echo = g.Rude.Echoes.Count > 0;
            switch (cue)
            {
                case VoiceCue.PartLost:
                    if (k.BothJust) { k.BothJust = false; k.PaneBroke = false; return Turn.Skip; }   // the Phase turn has the box
                    bool pane = TakePaneBroke(k);
                    if (echo) return OweEcho(k, cue);
                    return pane || g.Rude.Waiting.Count > 0 ? Turn.Wait : Turn.Skip;
                case VoiceCue.Arrive or VoiceCue.Phase or VoiceCue.Idle:
                    return echo ? OweEcho(k, cue) : Turn.Wait;
                default:
                    return base.TurnFor(g, e, cue);
            }
        }

        /// <summary>Reads whether a pane broke since the last turn, and clears the mark.</summary>
        private static bool TakePaneBroke(Glassworks k)
        {
            bool pane = k.PaneBroke;
            k.PaneBroke = false;
            return pane;
        }

        /// <summary>Records the cue whose echo is to be said once the box is read, and gives no turn now.</summary>
        private static Turn OweEcho(Glassworks k, VoiceCue cue)
        {
            k.Pending = cue;
            return Turn.Skip;
        }

        /// <summary>Says the echo it owes once the box on show has been read and it holds the floor.</summary>
        private void Speak(RaiderGame g, Enemy e, Glassworks k)
        {
            if (k.Pending is null || !ReferenceEquals(g.Floor(), e) || g.Reading(e, this)) return;
            k.Pending = null;
            var echoes = g.Rude.Echoes;
            if (echoes.Count == 0) return;
            var post = echoes[k.EchoNext++ % echoes.Count];
            EnemySpeechBox.Say(e, PostHeader(post), post.Text);
            k.EchoText = post.Text;
        }

        /// <summary>"RE:" stands before the header while an echo is on show.</summary>
        public override string? Prefix(RaiderGame g, Enemy e)
        {
            var k = e.Kit as Glassworks;
            return k?.EchoText is { } text && e.Text == text ? "RE:" : null;
        }

        // ---------------------------------------------------------------- pixels

        public override void Draw(RaiderView.Canvas c, Enemy e)
        {
            var k = Works(e);
            var lay = k.Layout;
            if (e.Leaving || e.Mode == Entering) return;
            if (IsTell(e.Mode) || IsBurn(e.Mode))
            {
                int step = Math.Min(TellSteps - 1, e.ModeAge * TellSteps / TellFrames);
                foreach (var p in TurnFacets(e))
                {
                    int x0 = (int)Math.Round(e.X + p.OX - MirrorLayout.FacetW / 2.0), y0 = (int)Math.Round(e.Y + p.OY - lay.FacetH / 2.0);
                    if (IsBurn(e.Mode)) Glare(c, x0, y0 + lay.FacetH);
                    else
                    {
                        c.Draw(p.OpenArt!, x0, y0, TellWhite[step]);
                        c.Draw(Glass.Mark, x0 + MirrorLayout.MarkX, y0 + lay.MarkY);   // the pane whitens round its mark: it can still be broken
                        Warning(c, x0, y0 + lay.FacetH, step);
                    }
                }
            }
            if (k.Fire > BlinkFrames) return;
            if (k.Fire > EyeSteadyFrames && k.Fire / EyeBlinkFrames % 2 != 0) return;
            var eye = Eye(e);
            int ex = (int)Math.Round(e.X + eye.OX - MirrorLayout.EyeW / 2.0), ey = (int)Math.Round(e.Y + eye.OY) - MirrorLayout.PupilRise;
            // a white ring round the pupil, so the gold stays in sight on its crimson
            int left = ex + MirrorLayout.PupilCol - 1, top = ey - 1, ring = MirrorLayout.PupilSize + 2;
            c.Rect(left, top, ring, 1, Palette.Tell);
            c.Rect(left, top + ring - 1, ring, 1, Palette.Tell);
            c.Rect(left, top, 1, ring, Palette.Tell);
            c.Rect(left + ring - 1, top, 1, ring, Palette.Tell);
        }

        /// <summary>The white warning under one facet, from <paramref name="top"/> to the floor: its two edge columns, dotted at
        /// first and then whole, and at the last step a scatter of white between them.</summary>
        private static void Warning(RaiderView.Canvas c, int x0, int top, int step)
        {
            int right = x0 + MirrorLayout.FacetW - 1;
            for (int y = top; y < c.Height; y++)
            {
                if (step > 0 || (y - top) % DotPitch == 0) { c.Set(x0, y, Palette.Tell); c.Set(right, y, Palette.Tell); }
                if (step < TellSteps - 1) continue;
                for (int x = x0 + 1; x < right; x++)
                    if ((x - x0 + ScatterSlant * (y - top)) % ScatterEvery == 0 && (y - top) % DotPitch == 0) c.Set(x, y, Palette.Tell);
            }
        }

        /// <summary>The glare under one lit facet, from <paramref name="top"/> to the floor: white edges and stripes of ice, so
        /// what is behind it still shows.</summary>
        private static void Glare(RaiderView.Canvas c, int x0, int top)
        {
            int right = x0 + MirrorLayout.FacetW - 1;
            for (int y = top; y < c.Height; y++)
                for (int x = x0; x <= right; x++)
                {
                    if (x == x0 || x == right) c.Set(x, y, Palette.Tell);
                    else if ((x - x0) % DotPitch == 0) c.Set(x, y, Palette.Ice);
                }
        }
    }
}
