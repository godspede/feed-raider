using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>Draws the game into a <see cref="Screen"/>: the pixel field through the quad composer, text overlays on top.</summary>
public sealed class RaiderView
{
    /// <summary>The screen's ground around the field: the sky's colour at its foot.</summary>
    public static readonly uint Ground = Background.SkyBottom;
    public static readonly uint Rail = Surface.Pack(0x1c, 0x3a, 0x6e);
    /// <summary>The rule above the marked feed-log entry.</summary>
    private static readonly uint MarkedRule = Background.BlueskyBlue;
    /// <summary>The feed log's ground, beside the field and on the phone's one-line feed strip, and the colour its post text is dimmed from.</summary>
    public const uint PanelBg = 0x0c1830, PanelText = 0xe6eefc;
    /// <summary>The ground of the HUD: the two readout rows at the top of the field, the shield and weapon pips and the hints.</summary>
    internal const uint HudBg = 0x0a1224;
    private const uint Dim = Palette.Dim, Pink = Palette.Pink, Ice = Palette.Ice;
    /// <summary>What a screen too small for the field says, where and in what colour.</summary>
    internal const string TooSmallMessage = "terminal too small";
    internal const int TooSmallCol = 1, TooSmallRow = 1;
    internal const uint TooSmallText = 0xd8cba3;
    internal const uint ShotGlow = 0xffe29a, Flame = 0xffb347, LiveWaveColour = 0xff6b9a;
    internal const uint PostText = 0xe3c8ff, PostBg = 0x1a1030;
    private const uint EnemySpeechBoxText = 0xffd0e0;
    private const uint ShipDownBg = 0x5a1030, ToastBg = 0x3a1030, BombReady = 0x1a3a6a, BombSpent = 0x16202e;
    /// <summary>How far a flashing sprite is blended toward white.</summary>
    internal const double FlashWhite = 0.85;
    private const int DragHintSeconds = 5, KeysHintSeconds = 10;
    /// <summary>Columns of the top rows the score, shield and weapon readouts take, which the stage readout must leave free.</summary>
    private const int HudStageMargin = 28;
    private const int GunshipMuzzleW = 3;   // the white tell under a gunship's nose, as wide as its barrel tip
    internal const int MineBlinkFrames = 12;   // a mine blinks lit for this long before it arms
    internal const double MineGlint = 0.3, DiverWindupWhite = 0.7, DiverDashGlint = 0.4, TurretWindupWhiteMax = 0.6;
    private const int DiverAimEvery = 3;
    internal const int DiverCrossRadius = 2, TurretReticleMin = 2, TurretReticleSpan = 4;
    /// <summary>The cell rows the HUD takes at the top of the field.</summary>
    internal const int HudRows = CardPainter.HudPixelRows / CardField.PixelsPerCell;
    private const int FirstHealthBarRow = CardPainter.HudPixelRows, BoxesTopRow = HudRows;
    internal const int FlameFlicker = 3;
    private const int PickupBlinkPhaseFrames = 3;
    internal const double PickupDarken = 0.55, PickupLighten = 0.6, PickupGlowBase = 5, PickupGlowSwing = 2;
    internal const double ShieldRadius = 8.5, ShieldInnerGap = 1, ShieldBaseAlpha = 0.37, ShieldPulseAlpha = 0.1, ShieldInnerAlpha = 0.4;
    internal const int ShieldHitFlashFrames = 6;
    private const int SpeechBoxMaxWidth = 20, WrapMargin = 6;
    /// <summary>The most lines a boss's name or a speech box's words wrap to, and a hint, which on a narrow field wraps onto a second line, never a third.</summary>
    internal const int WrapMaxLines = 3, HintWrapLines = 2;
    /// <summary>An enemy says nothing while its centre is above this pixel row, the bottom of the HUD.</summary>
    private const int WordsFromPixelRow = CardPainter.HudPixelRows;
    /// <summary>The cells a gunship's speech box pads its longest line by: one edge cell on each side.</summary>
    private const int SpeechBoxPad = 2;
    private const int PopupTopRow = HudRows;
    /// <summary>The columns from the field's left the shield pips and, below the bombs, the weapon pips start at.</summary>
    internal const int HudShieldCol = 15, HudBombCol = 20;
    private const int ToastRowsFromBottom = 4, HintRowsFromBottom = 2;
    internal const int FastBlinkFrames = 2, MineGlintFrames = 8, StrobeFrames = 3;
    private const int BannerBlinkFrames = 6, BannerBlinkCycle = 4;
    private const int BannerRowTopParts = 2, BannerRowWholeParts = 5;
    /// <summary>The keys hint at its widest, shorter, and shortest: each is shown on a screen with a few cells to spare round it.</summary>
    private const string WideKeysHint = " WASD move  SPACE fire  B bomb  Z like  X repost  F auto ";
    private const string KeysHintWithLike = " WASD SPACE B  Z like  X repost ";
    private const string ShortKeysHint = " WASD SPACE B Z X ";
    private const int HintSpareCols = 4;

    /// <summary>The keys hint for a screen <paramref name="cols"/> wide: the widest that fits with <see cref="HintSpareCols"/> to spare.</summary>
    internal static string KeysHint(int cols) =>
        cols >= WideKeysHint.Length + HintSpareCols ? WideKeysHint : cols >= KeysHintWithLike.Length + HintSpareCols ? KeysHintWithLike : ShortKeysHint;
    internal const double PickupPulseRate = 0.3, PickupAgePulseRate = 0.1, ShieldPulseRate = 0.2;
    /// <summary>A banner holds steady for the first half of its time, then blinks as it fades.</summary>
    private const int BannerSteadyFrames = Tuning.StageBannerFrames / 2;
    internal const int SplitterCrackInner = 2, SplitterCrackOuter = 3;   // columns out from the centre that each crack's two pixels sit at
    internal const int HostileBoltSize = 2, ShotLength = 4;
    /// <summary>The looks of the heavier shots: a heavy line is wider and longer, a slug short, a missile a body with a nose and a
    /// flickering flame, the fan's shots a little longer than a slug. None is the colour of a bolt (crimson, the turret's green), a
    /// lobber's rust or its white shell.</summary>
    internal const int HeavyLength = 6, HeavyWidth = 3, SlugLength = 2, MissileLength = 3, SpreadLength = 3, FlameBlinkFrames = 2;
    internal const uint HeavyRim = 0x5ff0ff, SlugBrass = 0xf2c14e, MissileBody = 0xc8d2e0, MissileFlame = 0xffa040, SpreadViolet = 0xc49bff;
    /// <summary>The weapon's short name sits this many cells after its pips.</summary>
    internal const int HudWeaponNameGap = 1;
    internal const double ParticleFadeRate = 1.6;   // the fade runs this much faster than the life, so a particle holds full colour at first
    internal const double ScreenFlashMaxWhite = 0.5;
    internal const double RingAlphaMin = 0.35, RingAlphaFade = 0.65, RingInnerAlpha = 0.25;
    internal const int RingDoubledFromRadius = 3;   // a ring wider than this gets a fainter second ring inside it
    internal const int MuzzleFlashLong = 5, MuzzleFlashShort = 3;   // the flash is a cross of a wide and a tall bar
    internal const double PickupGlowAlphaBase = 0.3, PickupGlowAlphaSwing = 0.35;
    internal const double ShieldHitFlashAlpha = 0.9, ShieldHitFlashFade = 0.13;
    /// <summary>The blank cells the bomb button's label and dots keep from its left edge.</summary>
    private const string BoxIndent = "  ", BombLabel = BoxIndent + "BOMB" + BoxIndent;
    private const int LogTopRow = 2, LogRowsFromBottom = 2, NarrowLogMinRows = 3, NarrowTextCol = 4;
    internal const int NarrowRepostCol = 2, LikeIconCol = 2, RepostIconCol = 1;
    private const int LogNameCol = 2, CountAfterIcon = 2;
    /// <summary>The cell a like or repost count keeps clear before the next half of the button row or the panel's edge.</summary>
    private const int CountGap = 1;
    /// <summary>The column a log entry's text and note start on; the text keeps the same one cell clear on the right.</summary>
    private const int LogTextCol = 1;
    /// <summary>What a handle is written after.</summary>
    private const string HandleMark = "@";
    /// <summary>A margin given for one side, taken from both.</summary>
    private const int BothSides = 2;
    private const double LogTextDim = 0.2;
    /// <summary>The feed log's colours, each made legible on its ground: the post text and name, the handle, and the counts and note.</summary>
    private static readonly uint LogPostText = Surface.Legible(Surface.Mix(PanelText, PanelBg, LogTextDim), PanelBg),
        LogHandle = Surface.Legible(Ice, PanelBg), LogDim = Surface.Legible(Dim, PanelBg);
    /// <summary>The glyphs of the rails either side of the field, of a full HUD cell (a pip that is had), of
    /// a pip that is not had, of a bomb in hand, and of the mark <see cref="RaiderGame.WaveLabel"/> opens a live or boss wave with.</summary>
    internal const char RailGlyph = '│', PipFull = '█', PipEmpty = Dot, BombDot = Dot, LiveWaveMark = '◉';
    private const char Dot = '•';
    /// <summary>The half blocks the edges of a gunship's speech box alternate between, row by row, so its sides read as a zigzag.</summary>
    private const char SpeechBoxEdgeLeft = '▌', SpeechBoxEdgeRight = '▐';
    /// <summary>The rule above each feed-log entry, and the pointer beside the marked one.</summary>
    private const char LogRuleGlyph = '─';
    internal const char LogMarkGlyph = '▸';
    /// <summary>The column the marked entry's pointer sits on, one cell left of its name.</summary>
    internal const int LogMarkCol = LogNameCol - 1;
    /// <summary>Any fixed seed: the star field is the same on every run.</summary>
    public const ulong DefaultBackgroundSeed = 7;

    private readonly QuadComposer _quad = new();
    private readonly SpeechBoxPainter _speechBoxPainter = new();
    private Surface? _field;
    /// <summary>The pixel field the last frame was drawn into, before the screen shake and the cell reduction; null until a frame
    /// fits the screen.</summary>
    internal Surface? Field => _field;
    private Background? _bg;
    /// <summary>The layout the screen was last fitted to, which the frame being drawn uses: the feed log takes its width from it.</summary>
    private FieldLayout _lay;
    private readonly ulong _seed;

    public RaiderView(ulong seed = DefaultBackgroundSeed) { _seed = seed; }

    /// <summary>True when the game is only a backdrop (the start page's tour): no HUD, no key hints, no toast, no side
    /// panels and no touch button, so nothing that reads as a control shows under the start page. The fight, its banners and what the
    /// enemies say are drawn as ever.</summary>
    public bool Backdrop { get; set; }

    /// <summary>The mark the start page's scripted click is drawn with: an arrow head whose tip is the cell it presses.</summary>
    private const char TourPointerMark = '◤';

    // the field just after the background and the card slabs: the world is drawn over it, and card text goes only where it did not
    private uint[] _under = [];

    /// <summary>Draws a frame of the game onto the screen. It first fits the game to the screen: when the field's layout for the
    /// screen's size differs from the game's, the game is given the new layout.</summary>
    public void FitAndRender(RaiderGame game, Screen scr, string? debug = null)
    {
        scr.Clear(Ground);
        var lay = FieldLayout.Compute(scr.Cols, scr.Rows);
        if (!lay.FitsMinimum)
        {
            scr.Text(TooSmallCol, TooSmallRow, TooSmallMessage, TooSmallText);
            return;
        }
        if (game.Layout != lay) game.SetLayout(lay);
        if (_field is null || _lay != lay)
        {
            _lay = lay;
            _field = new Surface(lay.PW, lay.PH);
            _bg = new Background(lay.PW, lay.PH, _seed);
        }
        var field = _field;
        var (shakeX, shakeY) = game.ShakeOffset();
        _bg!.Draw(field, game.ScrollFrames, game.BurstStrength);
        // the cards shake a whole cell at a time, so a slab never cuts a cell: draw them by the difference between the shake
        // rounded to an even number of pixels and the shake, and the shift of the whole field lands them on that
        CardPainter.DrawCards(field, game, CardPainter.EvenShake(shakeX) - shakeX, CardPainter.EvenShake(shakeY) - shakeY, Math.Max(0, CardPainter.HudPixelRows - shakeY));
        bool words = game.ScreenFlash <= 0 && game.Cards.Cards.Count > 0;
        if (words)
        {
            if (_under.Length != field.P.Length) _under = new uint[field.P.Length];
            Array.Copy(field.P, _under, field.P.Length);
        }
        DrawWorld(game, field);
        _quad.Compose(field, shakeX, shakeY, scr, lay.Col0, 0);
        for (int row = 0; row < scr.Rows; row++)
        {
            scr.Put(lay.Col0 - 1, row, RailGlyph, Rail, Ground);
            scr.Put(lay.Col0 + lay.PCols, row, RailGlyph, Rail, Ground);
        }
        if (words) CardPainter.DrawText(scr, game, _under, field.P, field.W, field.H, lay.Col0, shakeX, shakeY);
        DrawText(game, scr, lay, debug);
        LayUnitsOverSpeechBoxes(game, scr, lay, shakeX, shakeY);
    }

    // ---------------------------------------------------------------- pixels

    /// <summary>What an enemy's own <c>Draw</c> paints with: the field's pixels, in field coordinates, and the game it is drawing.
    /// It clips like the surface it wraps, so a warning shape may run off the edge.</summary>
    public sealed class Canvas(RaiderGame game, Surface field)
    {
        public RaiderGame Game { get; } = game;
        public Surface Surface { get; } = field;
        public int Width => Surface.W;
        public int Height => Surface.H;
        public void Set(int x, int y, uint colour) => Surface.Set(x, y, colour);
        public void Rect(int x, int y, int width, int height, uint colour) => Surface.Rect(x, y, width, height, colour);
        public void Draw(Sprite sprite, int x, int y, double white = 0) => Surface.Draw(sprite, x, y, white);
    }

    /// <summary>The top-left pixel of a thing <paramref name="size"/> pixels across centred on <paramref name="centre"/>.</summary>
    private static int Corner(double centre, int size) => (int)Math.Round(centre - size / 2.0);

    /// <summary>True for the first <paramref name="frames"/> frames of every twice as many: a blink's lit half.</summary>
    private static bool BlinkOn(long frame, int frames) => frame / frames % 2 == 0;

    /// <summary>The art a part shows in its present state; null when it has none to show.</summary>
    internal static Sprite? PartArt(Part part) => part.State == PartState.Dead ? part.Wreck : part.State == PartState.Open ? part.OpenArt ?? part.Closed : part.Closed;

    private static void DrawParts(Surface field, Enemy enemy)
    {
        foreach (var part in enemy.Parts!)
        {
            var art = PartArt(part);
            if (art is null) continue;
            field.Draw(art, Corner(enemy.X + part.OX, art.W), Corner(enemy.Y + part.OY, art.H), part.Flash > 0 ? FlashWhite : 0);
        }
    }

    /// <summary>The pixel row of a health bar over a thing whose highest pixel is on <paramref name="top"/>: the upper pixel of the cell row
    /// above its top cell, so two or three pixels above it. A speech box above the thing ends on that cell row with its bottom edge, so the bar
    /// lies on the edge and never across a line of the words.</summary>
    public static int BarRow(int top) => (top & ~1) - CardField.PixelsPerCell;

    /// <summary>A boss's health bar, as wide as everything it shows and on <see cref="BarRow"/> above its highest pixel, filled by Hp over MaxHp.</summary>
    private static void DrawHealthBar(Surface field, Enemy enemy)
    {
        int left = Corner(enemy.X, enemy.Sprite.W), top = Corner(enemy.Y, enemy.Sprite.H), right = left + enemy.Sprite.W;
        foreach (var part in enemy.Parts ?? [])
        {
            var art = PartArt(part);
            if (art is null) continue;
            int x0 = Corner(enemy.X + part.OX, art.W), y0 = Corner(enemy.Y + part.OY, art.H);
            left = Math.Min(left, x0); top = Math.Min(top, y0); right = Math.Max(right, x0 + art.W);
        }
        int width = right - left;
        int row = Math.Max(FirstHealthBarRow, BarRow(top));   // a boss that holds with its top on row 5 or 6 has the first row under the HUD
        field.Rect(left, row, width, 1, Palette.Plum);
        field.Rect(left, row, (int)(width * Math.Max(0, enemy.Hp) / enemy.MaxHp), 1, Palette.Crimson);
    }
    private Surface? _units;
    private const uint NoUnit = Surface.None;

    /// <summary>Puts the units, shots and bolts back over the speech boxes. The boxes are text cells drawn after the pixel field, so where a ship or
    /// a shot or a bolt passes behind one, the cell is rebuilt from the unit pixels (quadrants, as the field is) and the box colour fills the rest
    /// of that cell. Only the cells of boxes are looked at, and nothing is drawn when no box is up.</summary>
    private void LayUnitsOverSpeechBoxes(RaiderGame game, Screen scr, FieldLayout lay, int shakeX, int shakeY)
    {
        if (_speechBoxPainter.Boxes.Count == 0 && _enemySpeechBoxes.Count == 0) return;
        if (_units is null || _units.W != lay.PW || _units.H != lay.PH) _units = new Surface(lay.PW, lay.PH) { SkipBlends = true };
        var units = _units;
        int top = BoxesTopRow, bottom = Math.Min(lay.Rows, scr.Rows);
        // the pixel rectangle that holds every cell of every box that is on the field: the units are drawn into that and nowhere else
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        BoxBounds(_speechBoxPainter.Boxes, units, lay, shakeX, shakeY, top, bottom, ref minX, ref minY, ref maxX, ref maxY);
        BoxBounds(_enemySpeechBoxes, units, lay, shakeX, shakeY, top, bottom, ref minX, ref minY, ref maxX, ref maxY);
        if (minX > maxX) return;
        units.SetClip(minX, minY, maxX + 1, maxY + 1);
        units.FillClip(NoUnit);
        DrawUnits(game, units);
        DrawRings(game, units);   // blasts and the shield hurt or guard, so they show over a box too
        units.ClearClip();
        LayCells(_speechBoxPainter.Boxes, scr, lay, units, shakeX, shakeY, top, bottom);
        LayCells(_enemySpeechBoxes, scr, lay, units, shakeX, shakeY, top, bottom);
    }

    private static void BoxBounds(List<CellRect> boxes, Surface units, FieldLayout lay, int shakeX, int shakeY, int top, int bottom,
        ref int minX, ref int minY, ref int maxX, ref int maxY)
    {
        foreach (var box in boxes)
        {
            int firstRow = Math.Max(box.R, top), endRow = Math.Min(box.Bottom, bottom), firstCol = Math.Max(box.C, lay.Col0), endCol = Math.Min(box.Right, lay.Col0 + lay.PCols);
            if (endRow <= firstRow || endCol <= firstCol) continue;
            minX = Math.Min(minX, Math.Clamp((firstCol - lay.Col0) * CardField.PixelsPerCell - shakeX, 0, units.W - 1));
            maxX = Math.Max(maxX, Math.Clamp((endCol - lay.Col0) * CardField.PixelsPerCell - 1 - shakeX, 0, units.W - 1));
            minY = Math.Min(minY, Math.Clamp(firstRow * CardField.PixelsPerCell - shakeY, 0, units.H - 1));
            maxY = Math.Max(maxY, Math.Clamp(endRow * CardField.PixelsPerCell - 1 - shakeY, 0, units.H - 1));
        }
    }

    private readonly uint[] _quad4 = new uint[4];

    private void LayCells(List<CellRect> boxes, Screen scr, FieldLayout lay, Surface units, int shakeX, int shakeY, int top, int bottom)
    {
        var quad = _quad4;
        foreach (var box in boxes)
            for (int row = Math.Max(box.R, top); row < Math.Min(box.Bottom, bottom); row++)
                for (int col = Math.Max(box.C, lay.Col0); col < Math.Min(box.Right, lay.Col0 + lay.PCols); col++)
                {
                    int fieldCol = col - lay.Col0;
                    int x0 = Math.Clamp(fieldCol * CardField.PixelsPerCell - shakeX, 0, units.W - 1), x1 = Math.Clamp(fieldCol * CardField.PixelsPerCell + 1 - shakeX, 0, units.W - 1);
                    int y0 = Math.Clamp(row * CardField.PixelsPerCell - shakeY, 0, units.H - 1), y1 = Math.Clamp(row * CardField.PixelsPerCell + 1 - shakeY, 0, units.H - 1);
                    uint topLeft = units.P[y0 * units.W + x0], topRight = units.P[y0 * units.W + x1], bottomLeft = units.P[y1 * units.W + x0], bottomRight = units.P[y1 * units.W + x1];
                    if (topLeft == NoUnit && topRight == NoUnit && bottomLeft == NoUnit && bottomRight == NoUnit) continue;
                    int cell = row * scr.Cols + col;
                    uint back = scr.Bg[cell];
                    quad[0] = topLeft == NoUnit ? back : topLeft; quad[1] = topRight == NoUnit ? back : topRight; quad[2] = bottomLeft == NoUnit ? back : bottomLeft; quad[3] = bottomRight == NoUnit ? back : bottomRight;
                    int mask = QuadComposer.SplitMask(quad, out uint fg, out uint bg);
                    if (mask == Screen.AllQuadrants) bg = fg;   // one colour all through: the split leaves the other group empty
                    scr.Set(col, row, QuadRenderer.GlyphFor(mask), fg, bg);   // a wide char of the box that this cuts in half is blanked
                }
    }

    private readonly List<CellRect> _enemySpeechBoxes = [];

    /// <summary>An enemy of a kind from <see cref="EnemyKind.Mine"/> on, with its tells, parts, health bar and its kind's overlays. All of it is pixels, which <see cref="LayUnitsOverSpeechBoxes"/> lays back over any speech box it passes
    /// behind, so a tell or a warning is never hidden by words.</summary>
    private static void DrawOverlaidEnemy(RaiderGame game, Surface field, Enemy enemy)
    {
        var sprite = enemy.Sprite;
        double white = enemy.Flash > 0 ? FlashWhite : 0;
        if (enemy.Kind == EnemyKind.Mine && !enemy.Armed)
            sprite = enemy.Grace - enemy.Age <= MineBlinkFrames && BlinkOn(game.Frame, FastBlinkFrames) ? Art.Mine : Art.MineCold;
        else if (enemy.Kind == EnemyKind.Mine && BlinkOn(game.Frame, MineGlintFrames) && white == 0) white = MineGlint;
        if (enemy.Kind == EnemyKind.Diver && enemy.Windup > 0 && !enemy.Dashing)
        {
            white = Math.Max(white, BlinkOn(game.Frame, FastBlinkFrames) ? DiverWindupWhite : 0);
            Aim(field, enemy.X, enemy.Y + sprite.H / 2.0, enemy.AimX, enemy.AimY);
            Cross(field, (int)Math.Round(enemy.AimX), (int)Math.Round(enemy.AimY), BlinkOn(game.Frame, StrobeFrames) ? Art.DiverLime : Palette.Tell);
        }
        else if (enemy.Kind == EnemyKind.Diver && enemy.Dashing && white == 0 && BlinkOn(game.Frame, StrobeFrames)) white = DiverDashGlint;   // the dash plates glint: shots bounce
        if (enemy.Kind == EnemyKind.Turret && enemy.Windup > 0)
        {
            double windup = 1 - enemy.Windup / (double)Tuning.TurretWindupFrames;
            white = Math.Max(white, windup * TurretWindupWhiteMax);
            int reticle = TurretReticleMin + (int)Math.Round((1 - windup) * TurretReticleSpan);
            Reticle(field, (int)Math.Round(enemy.AimX), (int)Math.Round(enemy.AimY), reticle, Art.TurretGreen);
        }
        int x0 = Corner(enemy.X, sprite.W), y0 = Corner(enemy.Y, sprite.H);
        field.Draw(sprite, x0, y0, white);
        if (enemy.Kind == EnemyKind.Turret && enemy.Windup > 0) field.Set(x0 + sprite.W / 2, y0 + sprite.H, Palette.Tell);
        if (enemy.Kind == EnemyKind.Splitter && enemy.Hp <= enemy.MaxHp / 2)
        {
            int midX = x0 + sprite.W / 2, midY = y0 + sprite.H / 2;
            field.Set(midX - SplitterCrackInner, midY - 1, Palette.Plum); field.Set(midX - SplitterCrackOuter, midY, Palette.Plum);
            field.Set(midX + SplitterCrackInner, midY + 1, Palette.Plum); field.Set(midX + SplitterCrackOuter, midY, Palette.Plum);
            uint seam = BlinkOn(game.Frame, StrobeFrames) ? Palette.Tell : Art.SplitterPink;
            field.Set(midX, midY - 1, seam); field.Set(midX, midY, seam); field.Set(midX, midY + 1, seam);
        }
        if (enemy.Parts is not null) DrawParts(field, enemy);
        var behaviour = game.BehaviourFor(enemy.Kind);
        if (behaviour.Boss && enemy.Hp < enemy.MaxHp) DrawHealthBar(field, enemy);
        behaviour.Draw(new Canvas(game, field), enemy);   // warnings and overlays last, over the sprite and the parts
    }

    /// <summary>A diver's aim: a dotted line in its lime from (x0, y0) to (x1, y1), one pixel in every <see cref="DiverAimEvery"/>.</summary>
    private static void Aim(Surface field, double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0;
        int steps = (int)Math.Max(Math.Abs(dx), Math.Abs(dy));
        for (int i = DiverAimEvery; i < steps; i += DiverAimEvery)
            field.Set((int)Math.Round(x0 + dx * i / steps), (int)Math.Round(y0 + dy * i / steps), Art.DiverLime);
    }

    /// <summary>The cross a diver aims at, <see cref="DiverCrossRadius"/> pixels out from (x, y) along each diagonal.</summary>
    private static void Cross(Surface field, int x, int y, uint colour)
    {
        for (int i = -DiverCrossRadius; i <= DiverCrossRadius; i++) { field.Set(x + i, y + i, colour); field.Set(x + i, y - i, colour); }
    }

    /// <summary>Four corner brackets around (x, y).</summary>
    private static void Reticle(Surface field, int x, int y, int radius, uint colour)
    {
        for (int signX = -1; signX <= 1; signX += 2)
            for (int signY = -1; signY <= 1; signY += 2)
            {
                field.Set(x + signX * radius, y + signY * radius, colour);
                field.Set(x + signX * (radius - 1), y + signY * radius, colour);
                field.Set(x + signX * radius, y + signY * (radius - 1), colour);
            }
        field.Set(x, y, colour);
    }

    private void DrawWorld(RaiderGame game, Surface field)
    {
        // a press's glow and the keys' outline are part of the card: under every ship, shot and bolt, and they shake with the card
        var (shakeX, shakeY) = game.ShakeOffset();
        CardPainter.DrawPressGlow(field, game, CardPainter.EvenShake(shakeX) - shakeX, CardPainter.EvenShake(shakeY) - shakeY, Math.Max(0, CardPainter.HudPixelRows - shakeY));
        DrawUnits(game, field);
        DrawEffects(game, field);
    }

    /// <summary>The things that must stay readable over a speech box: enemies, pickups, the ship and every shot and bolt.</summary>
    private void DrawUnits(RaiderGame game, Surface field)
    {
        foreach (var enemy in game.Enemies)
        {
            if (enemy.Kind >= EnemyKind.Mine) { DrawOverlaidEnemy(game, field, enemy); continue; }
            field.Draw(enemy.Sprite, Corner(enemy.X, enemy.Sprite.W), Corner(enemy.Y, enemy.Sprite.H), enemy.Flash > 0 ? FlashWhite : 0);
            if (RaiderGame.GunshipVolleyDue(enemy))
                field.Rect((int)Math.Round(enemy.X) - GunshipMuzzleW / 2, Corner(enemy.Y, enemy.Sprite.H) + enemy.Sprite.H, GunshipMuzzleW, 1, Palette.Tell);
            if (enemy.Kind == EnemyKind.Gunship && enemy.Hp < enemy.MaxHp)
            {
                int barWidth = enemy.Sprite.W, x0 = Corner(enemy.X, barWidth), y0 = BarRow(Corner(enemy.Y, enemy.Sprite.H));
                field.Rect(x0, y0, barWidth, 1, Palette.Plum);
                field.Rect(x0, y0, (int)(barWidth * Math.Max(0, enemy.Hp) / enemy.MaxHp), 1, Palette.Crimson);
            }
        }
        foreach (var pickup in game.Pickups) DrawPickup(game, field, pickup);
        if (game.Alive) DrawShip(game, field);
        foreach (var bullet in game.Bullets)
        {
            int x = (int)Math.Round(bullet.X), y = (int)Math.Round(bullet.Y);
            if (bullet.Aimed)
            {
                field.Set(x, y - 1, Art.TurretGreen); field.Set(x - 1, y, Art.TurretGreen); field.Set(x + 1, y, Art.TurretGreen); field.Set(x, y + 1, Art.TurretGreen);
                field.Set(x, y, Palette.White);
            }
            else if (bullet.Reflected) { field.Rect(x - HostileBoltSize / 2, y - HostileBoltSize / 2, HostileBoltSize, HostileBoltSize, Palette.Ice); field.Set(x, y, Palette.White); }   // the ship's own shot, come back
            else if (!bullet.Friendly) { field.Rect(x - HostileBoltSize / 2, y - HostileBoltSize / 2, HostileBoltSize, HostileBoltSize, Palette.Crimson); field.Set(x, y, Palette.White); }
            else DrawShot(field, bullet, x, y, game.Frame);
        }
    }

    private static void DrawShot(Surface field, Bullet shot, int x, int y, long frame)
    {
        switch (shot.Look)
        {
            case ShotLook.Heavy:
                field.Rect(x - HeavyWidth / 2, y - HeavyLength / 2, HeavyWidth, HeavyLength, HeavyRim);
                field.Rect(x, y - HeavyLength / 2, 1, HeavyLength, Palette.White);
                break;
            case ShotLook.Slug:
                field.Rect(x, y - SlugLength / 2, 1, SlugLength, SlugBrass);
                break;
            case ShotLook.Missile:
                field.Rect(x, y - 1, 1, MissileLength, MissileBody);
                field.Set(x, y - 2, Palette.White);
                if (BlinkOn(frame, FlameBlinkFrames)) field.Set(x, y + MissileLength - 1, MissileFlame);
                break;
            case ShotLook.Spread:
                field.Rect(x, y - 1, 1, SpreadLength, SpreadViolet);
                field.Set(x, y - 2, Palette.White);
                break;
            default:
                field.Rect(x, y - ShotLength / 2, 1, ShotLength, Palette.Ice);
                field.Set(x, y - ShotLength / 2 - 1, Palette.White);
                break;
        }
    }

    private static void DrawEffects(RaiderGame game, Surface field)
    {
        DrawRings(game, field);
        foreach (var particle in game.Particles)
        {
            int x = (int)Math.Round(particle.X), y = (int)Math.Round(particle.Y);
            double fade = Math.Min(1, particle.Life / (double)Math.Max(1, particle.MaxLife) * ParticleFadeRate);
            uint colour = fade < 1 ? Surface.Mix(Palette.SkyTop, particle.Colour, fade) : particle.Colour;
            if (particle.Kind == ParticleKind.Heart) DrawArt(field, HeartParticleArt, x - HeartParticleArt[0].Length / 2, y, colour, colour, colour);
            else field.Rect(x, y, particle.Size, particle.Size, colour);
        }
        if (game.ScreenFlash > 0)
        {
            double white = ScreenFlashMaxWhite * game.ScreenFlash / Tuning.ScreenFlashFrames;
            var pixels = field.P;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Surface.Mix(pixels[i], Palette.White, white);
        }
    }

    private static void DrawRings(RaiderGame game, Surface field)
    {
        foreach (var ring in game.Rings)
        {
            double done = 1 - ring.Life / (double)ring.Max, eased = 1 - (1 - done) * (1 - done);
            double radius = ring.R0 + (ring.R1 - ring.R0) * eased;
            double fade = 1 - done;
            field.Ring(ring.X, ring.Y, radius, ring.Colour, RingAlphaMin + RingAlphaFade * fade);
            if (radius > RingDoubledFromRadius) field.Ring(ring.X, ring.Y, radius - 1, ring.Colour, RingInnerAlpha * fade);
        }
    }

    private static void FlameAndMuzzle(Surface field, double shipX, double shipY, Sprite ship, bool muzzle, long frame)
    {
        int x = (int)Math.Round(shipX), y = (int)Math.Round(shipY);
        int flameLength = 1 + (int)(frame % FlameFlicker);
        field.Rect(x, y + ship.H / 2, 1, flameLength, Flame);
        if (muzzle)
        {
            int muzzleY = y - ship.H / 2 - 1;
            field.Rect(x - MuzzleFlashLong / 2, muzzleY - MuzzleFlashShort / 2, MuzzleFlashLong, MuzzleFlashShort, ShotGlow);
            field.Rect(x - MuzzleFlashShort / 2, muzzleY - MuzzleFlashLong / 2, MuzzleFlashShort, MuzzleFlashLong, Palette.White);
        }
    }

    /// <summary>The middle of a pulse's 0 to 1 swing, which is also how far the swing reaches either side of it.</summary>
    internal const double PulseMid = 0.5;

    /// <summary>A sine pulse between 0 and 1 that runs <paramref name="rate"/> radians a frame, shifted by <paramref name="phase"/> radians.</summary>
    private static double Pulse(long frame, double rate, double phase = 0) => PulseMid + PulseMid * Math.Sin(frame * rate + phase);

    /// <summary>A square capsule in the pickup's colour with a pulsing glow ring; it blinks for its last two seconds.</summary>
    private static void DrawPickup(RaiderGame game, Surface field, Pickup pickup)
    {
        if (pickup.Blinking && BlinkOn(game.Frame, PickupBlinkPhaseFrames)) return;
        uint colour = RaiderGame.PickupColour(pickup.Kind), dark = Surface.Mix(colour, Palette.SkyTop, PickupDarken), light = Surface.Mix(colour, Palette.White, PickupLighten);
        double rate = pickup.Kind == PickupKind.Burst ? PickupPulseRate * BurstPickupPulseMul : PickupPulseRate;
        double pulse = Pulse(game.Frame, rate, pickup.Age * PickupAgePulseRate);
        field.Ring(pickup.X, pickup.Y, PickupGlowBase + pulse * PickupGlowSwing, colour, PickupGlowAlphaBase + PickupGlowAlphaSwing * pulse);
        var art = pickup.Kind switch { PickupKind.Bomb => PickupBombArt, PickupKind.Burst => PickupChevronArt, _ => PickupCrossArt };
        DrawArt(field, art, (int)Math.Round(pickup.X) - art[0].Length / 2, (int)Math.Round(pickup.Y) - art.Length / 2, dark, colour, light);
    }

    /// <summary>The pickup capsules, drawn by <see cref="DrawArt"/>: a rounded body with its icon, a cross for a shield or weapon and a
    /// block for a bomb.</summary>
    private static readonly string[] PickupCrossArt =
    [
        " oooo ",
        "o-##-o",
        "o####o",
        "o-##-o",
        "o-##-o",
        " oooo ",
    ];

    private static readonly string[] PickupBombArt =
    [
        " oooo ",
        "o----o",
        "o-##-o",
        "o-##-o",
        "o----o",
        " oooo ",
    ];

    /// <summary>The speed burst: two chevrons pointing up the page.</summary>
    private static readonly string[] PickupChevronArt =
    [
        " oooo ",
        "o-##-o",
        "o#--#o",
        "o-##-o",
        "o#--#o",
        " oooo ",
    ];

    /// <summary>The burst pickup's glow beats this much faster than the others', so it reads as something that will not wait.</summary>
    private const double BurstPickupPulseMul = 2;

    /// <summary>A heart particle, with its point at the bottom.</summary>
    private static readonly string[] HeartParticleArt =
    [
        "#.#",
        "###",
        ".#.",
    ];

    /// <summary>Draws a drawing with its top left at (<paramref name="left"/>, <paramref name="top"/>): 'o' in <paramref name="edge"/>,
    /// '-' in <paramref name="body"/>, '#' in <paramref name="ink"/>; any other character leaves the pixel as it is.</summary>
    private static void DrawArt(Surface field, string[] art, int left, int top, uint edge, uint body, uint ink)
    {
        for (int y = 0; y < art.Length; y++)
            for (int x = 0; x < art[y].Length; x++)
                switch (art[y][x])
                {
                    case 'o': field.Set(left + x, top + y, edge); break;
                    case '-': field.Set(left + x, top + y, body); break;
                    case '#': field.Set(left + x, top + y, ink); break;
                }
    }

    private static void DrawShield(RaiderGame game, Surface field)
    {
        double pulse = Pulse(game.Frame, ShieldPulseRate);
        double alpha = ShieldBaseAlpha + ShieldPulseAlpha * pulse;
        uint shield = RaiderGame.PickupColour(PickupKind.Shield);
        field.Ring(game.PX, game.PY, ShieldRadius, shield, alpha);
        field.Ring(game.PX, game.PY, ShieldRadius - ShieldInnerGap, shield, alpha * ShieldInnerAlpha);
        long since = game.Frame - game.ShieldHitFrame;
        if (since < ShieldHitFlashFrames) field.Ring(game.PX, game.PY, ShieldRadius + since, Palette.White, ShieldHitFlashAlpha - since * ShieldHitFlashFade);
    }

    private static void DrawShip(RaiderGame game, Surface field)
    {
        if (game.Shield > 0) DrawShield(game, field);
        if (game.Invuln > 0 && !game.InvulnIsShield && BlinkOn(game.Frame, StrobeFrames)) return;
        if (game.Bursting) DrawBurstTrail(game, field);
        field.Draw(game.Ship, Corner(game.PX, game.Ship.W), Corner(game.PY, game.Ship.H));
        FlameAndMuzzle(field, game.PX, game.PY, game.Ship, game.MuzzleFrames > 0, game.Frame);
    }

    /// <summary>The burst's afterimages: how many, the pixels between each, and the first one's strength (each next one is fainter
    /// by as much again); and the strength of the cyan-white outline. All scale with <see cref="RaiderGame.BurstStrength"/>.</summary>
    private const int BurstEchoes = 3, BurstEchoStep = 2;
    private const double BurstEchoAlpha = 0.45, BurstOutlineAlpha = 0.85;
    /// <summary>How far an afterimage is tinted from the ship's colours to the burst's.</summary>
    private const double BurstEchoTint = 0.5;

    /// <summary>Drawn under the ship while a burst runs: its afterimages trailing below, then a one-pixel outline in the burst
    /// pickup's colour round it.</summary>
    private static void DrawBurstTrail(RaiderGame game, Surface field)
    {
        var ship = game.Ship;
        int left = Corner(game.PX, ship.W), top = Corner(game.PY, ship.H);
        double strength = game.BurstStrength;
        uint glow = RaiderGame.PickupColour(PickupKind.Burst);
        for (int echo = BurstEchoes; echo >= 1; echo--)
        {
            double alpha = BurstEchoAlpha * (BurstEchoes + 1 - echo) / BurstEchoes * strength;
            for (int y = 0; y < ship.H; y++)
                for (int x = 0; x < ship.W; x++)
                    if (ship.Px[y * ship.W + x] is var px and not 0)
                        field.Blend(left + x, top + y + echo * BurstEchoStep, Surface.Mix(px & Sprite.ColourMask, glow, BurstEchoTint), alpha);
        }
        for (int y = -1; y <= ship.H; y++)
            for (int x = -1; x <= ship.W; x++)
                if (!Solid(ship, x, y) && (Solid(ship, x - 1, y) || Solid(ship, x + 1, y) || Solid(ship, x, y - 1) || Solid(ship, x, y + 1)))
                    field.Blend(left + x, top + y, glow, BurstOutlineAlpha * strength);
    }

    private static bool Solid(Sprite s, int x, int y) => (uint)x < (uint)s.W && (uint)y < (uint)s.H && s.Px[y * s.W + x] != 0;

    // ---------------------------------------------------------------- text

    /// <summary>A label and a row of pips: <paramref name="have"/> filled in <paramref name="colour"/>, the rest dim dots.</summary>
    private static void Pips(Screen scr, int col, int row, char label, int have, int max, uint colour)
    {
        scr.Text(col, row, label.ToString(), have > 0 ? colour : Dim, HudBg);
        int pipsCol = col + 1;
        scr.Text(pipsCol, row, new string(PipFull, have), colour, HudBg);
        scr.Text(pipsCol + have, row, new string(PipEmpty, max - have), Dim, HudBg);
    }

    /// <summary>Whether the weapon's pips and its short name fit on a field of <paramref name="cols"/> columns.</summary>
    public static bool WeaponNameFits(int cols, string shortName) =>
        HudBombCol + WeaponPipsWidth + HudWeaponNameGap + shortName.Length <= cols - HudInset;

    /// <summary>A label cell and a pip per weapon level.</summary>
    internal const int WeaponPipsWidth = 1 + Tuning.MaxWeaponLevel;
    /// <summary>The column the HUD keeps clear at the field's left and right edges.</summary>
    private const int HudInset = 1;

    /// <summary>The weapon's pips under the bombs, then the kind's short name when the field is wide enough; on a narrow field the
    /// pips alone, moved left as far as they must be to stay inside it.</summary>
    private static void DrawWeaponRow(RaiderGame game, Screen scr, int col0, int cols, int row)
    {
        uint colour = RaiderGame.PickupColour(PickupKind.Weapon);
        string name = game.Weapon.Short;
        if (WeaponNameFits(cols, name))
        {
            Pips(scr, col0 + HudBombCol, row, 'W', game.WeaponLevel, Tuning.MaxWeaponLevel, colour);
            scr.Text(col0 + HudBombCol + WeaponPipsWidth + HudWeaponNameGap, row, name, colour, HudBg);
            return;
        }
        Pips(scr, col0 + Math.Min(HudBombCol, cols - HudInset - WeaponPipsWidth), row, 'W', game.WeaponLevel, Tuning.MaxWeaponLevel, colour);
    }

    /// <summary>The HUD's stage readout, as long as <paramref name="room"/> allows: "STAGE 3", "ST 3" on a narrow field, "3" at the narrowest.
    /// While a boss is fought it reads "BOSS 3", "B 3" and "3" in the same way.</summary>
    public static string StageReadout(int stage, int room, bool boss = false)
    {
        int shown = Math.Clamp(stage, 1, Tuning.StageCap);
        if (boss) return room >= BossText[shown].Length ? BossText[shown] : room >= ShortBossText[shown].Length ? ShortBossText[shown] : NumberText[shown];
        return room >= StageText[shown].Length ? StageText[shown] : room >= ShortStageText[shown].Length ? ShortStageText[shown] : NumberText[shown];
    }

    public static string StageTitle(int stage) => StageText[Math.Clamp(stage, 1, Tuning.StageCap)];

    // made once, so the HUD does not format a string every frame
    private static readonly string[] StageText = Enumerable.Range(0, Tuning.StageCap + 1).Select(stage => "STAGE " + stage).ToArray();
    private static readonly string[] ShortStageText = Enumerable.Range(0, Tuning.StageCap + 1).Select(stage => "ST " + stage).ToArray();
    private static readonly string[] BossText = Enumerable.Range(0, Tuning.StageCap + 1).Select(stage => "BOSS " + stage).ToArray();
    private static readonly string[] ShortBossText = Enumerable.Range(0, Tuning.StageCap + 1).Select(stage => "B " + stage).ToArray();
    private static readonly string[] NumberText = Enumerable.Range(0, Tuning.StageCap + 1).Select(stage => stage.ToString()).ToArray();

    /// <summary>A kind's own colour for its hint, or ice when that colour is too dim to read on the banner's backing.</summary>
    internal static uint HintColour(EnemyKind kind)
    {
        uint body = RaiderGame.TableBehaviour(kind).Colour;
        return Surface.Contrast(body, Palette.BannerBack) >= Palette.HintMinContrast ? body : Palette.Ice;
    }

    /// <summary>The cells a banner block pads its longest line by: one on each side.</summary>
    internal const int BlockPad = 2;

    /// <summary>Draws lines as one solid block on a backing of its own: every row as wide as the longest line plus a cell each side, each line centred, the block centred in the field,
    /// on the row nearest <paramref name="row"/> where it covers no speech box.</summary>
    private void DrawBlock(Screen scr, FieldLayout lay, int row, IReadOnlyList<(string Text, uint Fg)> lines, uint back)
    {
        int width = Math.Min(lay.PCols, lines.Max(line => line.Text.Length) + BlockPad);
        var block = new CellRect(lay.Col0 + (lay.PCols - width) / 2, row, width, lines.Count);
        block = block with { R = RowClearOfSpeechBoxes(block, lay) };
        _tags.Add(block);
        for (int line = 0; line < lines.Count; line++)
        {
            scr.Blank(block.C, block.R + line, width, Palette.White, back);
            scr.Text(block.C + (width - lines[line].Text.Length) / 2, block.R + line, lines[line].Text, lines[line].Fg, back);
        }
    }

    /// <summary>Where each pop-up is drawn: its own cell row, unless another already holds a cell of it, then the nearest free row above, or below when none above is free. The ones lowest on the field are placed first, so the first added of a tie stays put.</summary>
    internal static List<(Popup Popup, int Col, int Row)> PopupSpots(IReadOnlyList<Popup> popups, int col0, int cols, Func<double, int> toCol, Func<double, int> toRow)
    {
        var placed = new List<(Popup Popup, int Col, int Row)>();
        foreach (var popup in popups.OrderByDescending(popup => popup.Y))
        {
            int col = Math.Clamp(toCol(popup.X) - popup.Text.Length / 2, col0, col0 + cols - popup.Text.Length);
            int want = Math.Max(PopupTopRow, toRow(popup.Y));
            bool Free(int candidate) => candidate >= PopupTopRow && !placed.Any(other => other.Row == candidate && other.Col < col + popup.Text.Length && col < other.Col + other.Popup.Text.Length);
            int row = want;
            if (!Free(row))
            {
                row = -1;
                for (int candidate = want - 1; candidate >= PopupTopRow && row < 0; candidate--) if (Free(candidate)) row = candidate;
                for (int candidate = want + 1; row < 0; candidate++) if (Free(candidate)) row = candidate;
            }
            placed.Add((popup, col, row));
        }
        return placed;
    }

    /// <summary>Draws each pop-up at its <see cref="PopupSpots"/> with a clear cell either side inside the field, so it never runs
    /// into a card's text, and each letter moved toward white or black as far as it takes to read on the cell beneath it.</summary>
    internal static void DrawPopups(Screen scr, IReadOnlyList<Popup> popups, int col0, int cols, Func<double, int> toCol, Func<double, int> toRow)
    {
        var spots = PopupSpots(popups, col0, cols, toCol, toRow);
        // every clear cell first: a pop-up that ends where another starts on its row keeps its letters
        foreach (var (popup, col, row) in spots)
        {
            const char clear = ' ';
            if (col > col0) scr.Put(col - 1, row, clear, Palette.White);
            if (col + popup.Text.Length < col0 + cols) scr.Put(col + popup.Text.Length, row, clear, Palette.White);
        }
        foreach (var (popup, col, row) in spots)
        {
            if (row >= scr.Rows) continue;   // pushed below the last row by pop-ups holding every row from its own up to the HUD
            for (int at = 0; at < popup.Text.Length; at++) scr.Put(col + at, row, popup.Text[at], Surface.Legible(popup.Colour, scr.BgAt(col + at, row)));
        }
    }

    /// <summary>The row the stage and warning banners start on: two fifths of the way down the field.</summary>
    internal static int BannerRow(FieldLayout lay) => lay.Rows * BannerRowTopParts / BannerRowWholeParts;

    /// <summary>Whether a banner with <paramref name="framesLeft"/> to go is lit this frame: steady at first, then blinking.</summary>
    private static bool BannerShows(long frame, int framesLeft) =>
        framesLeft > BannerSteadyFrames || (framesLeft > 0 && frame / BannerBlinkFrames % BannerBlinkCycle != 0);

    /// <summary>Draws the cell text over the field: the HUD, what the enemies say, the pop-ups, the speech boxes, the banners, the
    /// side panels and the hints; each banner and tag on the row nearest its own where it covers no speech box. What the enemies say and the side panels are drawn by the methods that also record where they
    /// landed (<see cref="DrawEnemyWordsAndRecordBoxes"/>, <see cref="DrawPanelsAndRegisterHits"/>).</summary>
    private void DrawText(RaiderGame game, Screen scr, FieldLayout lay, string? debug)
    {
        int col0 = lay.Col0, cols = lay.PCols;
        int ColOf(double x) => col0 + (int)(x / CardField.PixelsPerCell);
        int RowOf(double y) => (int)(y / CardField.PixelsPerCell);

        if (!Backdrop) DrawHud(game, scr, lay, debug);

        DrawEnemyWordsAndRecordBoxes(game.Enemies, scr, col0, cols, ColOf, RowOf);

        DrawPopups(scr, game.Popups, col0, cols, ColOf, RowOf);

        _speechBoxPainter.Draw(game, scr, lay);
        _tags.Clear();

        if (game.Gate == GateState.Warning)
        {
            // steady, not blinking: the boss is coming and the banner is the whole of the warning
            var block = new List<(string Text, uint Fg)> { ("WARNING", Palette.White) };
            if (game.BossName.Length > 0) block.AddRange(ShortLines.Format(game.BossName, cols - BlockPad, WrapMaxLines).Select(line => (line, Palette.Gold)));
            DrawBlock(scr, lay, BannerRow(lay), block, Palette.WarningBack);
        }
        else if (BannerShows(game.Frame, game.StageBannerLeft))
        {
            var block = new List<(string Text, uint Fg)> { (StageTitle(game.StageShown), Palette.White) };
            foreach (var kind in Ramp.Introduced(game.StageShown))
                block.AddRange(ShortLines.Format(Ramp.HintFor(kind)!, cols - BlockPad, HintWrapLines).Select(line => (line, HintColour(kind))));
            DrawBlock(scr, lay, BannerRow(lay), block, Palette.BannerBack);
        }
        else if (game.KindHint is { } midKind && BannerShows(game.Frame, game.KindHintLeft) && Ramp.HintFor(midKind) is { } mid)
        {
            // a kind that unlocks part way through a stage: its hint alone, with no stage number
            var lines = ShortLines.Format(mid, cols - BlockPad, HintWrapLines).Select(line => (line, HintColour(midKind))).ToList();
            DrawBlock(scr, lay, BannerRow(lay) + 1, lines, Palette.BannerBack);
        }

        if (!Backdrop) DrawPanelsAndRegisterHits(game, scr, lay);

        if (!game.Alive) CentreTag(scr, lay, lay.Rows / 2, ShipDown, Palette.White, ShipDownBg);
        else if (!Backdrop && game.Tick < DragHintSeconds * Tuning.Fps && game.Input.PointerLast) CentreTag(scr, lay, lay.Rows - HintRowsFromBottom, " drag to move ", Dim, HudBg);
        else if (!Backdrop && game.Tick < KeysHintSeconds * Tuning.Fps && !game.Input.PointerLast) CentreTag(scr, lay, lay.Rows - HintRowsFromBottom, KeysHint(cols), Dim, HudBg);
        if (Backdrop && game.TourPointer is { } pointer)
        {
            int col = ColOf(pointer.X), row = RowOf(pointer.Y);
            if (row >= 0 && row < scr.Rows) scr.Put(col, row, TourPointerMark, Surface.Legible(Palette.White, scr.BgAt(col, row)));
        }
        if (Backdrop) return;
        if (game.Toast is { } toast) CentreTag(scr, lay, lay.Rows - ToastRowsFromBottom, $" {toast} ", Palette.White, ToastBg);
        if (game.Input.PointerLast)
        {
            var (bombCol, bombRow, bombWidth, bombHeight) = game.BombButton;
            uint bombBg = game.Bombs > 0 ? BombReady : BombSpent;
            for (int offset = 0; offset < bombHeight; offset++) scr.Blank(bombCol, bombRow + offset, bombWidth, Ice, bombBg);
            int labelCol = bombCol + 1;
            scr.Text(labelCol, bombRow, BombLabel, game.Bombs > 0 ? Ice : Dim, bombBg);
            scr.Text(labelCol, bombRow + 1, (BoxIndent + new string(BombDot, game.Bombs)).PadRight(BombLabel.Length), game.Bombs > 0 ? Ice : Dim, bombBg);
        }
    }

    /// <summary>Draws what each enemy without a label says, a carrier's words on one row above it and a gunship's in a speech box
    /// below it, and replaces <c>_enemySpeechBoxes</c> with the cells they take, which <see cref="LayUnitsOverSpeechBoxes"/> reads.</summary>
    private void DrawEnemyWordsAndRecordBoxes(List<Enemy> enemies, Screen scr, int col0, int cols, Func<double, int> toCol, Func<double, int> toRow)
    {
        _enemySpeechBoxes.Clear();
        foreach (var enemy in enemies)
        {
            if (enemy.Text is not { Length: > 0 } words || enemy.Y < WordsFromPixelRow || enemy.Header is not null) continue;   // a labelled enemy speaks in a speech box
            if (enemy.Kind == EnemyKind.Carrier)
            {
                int col = Math.Clamp(toCol(enemy.X) - words.Length / 2, col0, col0 + cols - words.Length);
                int wordsRow = Math.Max(PopupTopRow, toRow(enemy.Y - enemy.Sprite.H / 2.0) - 1);
                scr.Text(col, wordsRow, words, PostText, PostBg);
                _enemySpeechBoxes.Add(new CellRect(col, wordsRow, words.Length, 1));
            }
            else if (enemy.Kind == EnemyKind.Gunship)
            {
                var lines = ShortLines.Format(words, Math.Min(SpeechBoxMaxWidth, cols - WrapMargin), WrapMaxLines);
                int width = lines.Max(line => line.Length) + SpeechBoxPad;
                int col = Math.Clamp(toCol(enemy.X) - width / 2, col0 + 1, col0 + cols - width - 1);
                int row = toRow(enemy.Y + enemy.Sprite.H / 2.0) + 1;
                const int edge = SpeechBoxPad / BothSides;
                for (int line = 0; line < lines.Length; line++)
                {
                    var (leftEdge, rightEdge) = line % 2 == 0 ? (SpeechBoxEdgeLeft, SpeechBoxEdgeRight) : (SpeechBoxEdgeRight, SpeechBoxEdgeLeft);
                    scr.Put(col, row + line, leftEdge, Palette.Crimson, Palette.Plum);
                    scr.Text(col + edge, row + line, lines[line].PadRight(width - SpeechBoxPad), EnemySpeechBoxText, Palette.Plum);
                    scr.Put(col + width - edge, row + line, rightEdge, Palette.Crimson, Palette.Plum);
                }
                _enemySpeechBoxes.Add(new CellRect(col, row, width, lines.Length));
            }
        }
    }

    /// <summary>The two rows at the top: score, shield, bombs, weapon, wave and stage.</summary>
    private void DrawHud(RaiderGame game, Screen scr, FieldLayout lay, string? debug)
    {
        int col0 = lay.Col0, cols = lay.PCols;
        const int top = 0, second = top + 1;
        for (int row = top; row < HudRows; row++) scr.Blank(col0, row, cols, Palette.White, HudBg);
        scr.Text(col0 + HudInset, top, $"SCORE {game.Score:D6}", Palette.White, HudBg);
        Pips(scr, col0 + HudShieldCol, top, 'S', game.Shield, Tuning.ShieldHits, RaiderGame.PickupColour(PickupKind.Shield));
        scr.Text(col0 + HudBombCol, top, "B" + new string(BombDot, game.Bombs), Ice, HudBg);
        DrawWeaponRow(game, scr, col0, cols, second);
        string wave = game.WaveLabel();
        scr.Text(col0 + HudInset, second, wave, wave[0] == LiveWaveMark ? LiveWaveColour : Dim, HudBg);
        string stage = StageReadout(game.Stage, cols - HudStageMargin, game.Gate == GateState.Fighting);
        int stageCol = col0 + cols - stage.Length - HudInset;
        scr.Text(stageCol, top, stage, game.Stage >= Tuning.StageCap ? Pink : Ice, HudBg);
        if (debug is not null) scr.Text(stageCol - debug.Length - 1, top, debug, Dim, HudBg);
    }

    /// <summary>A one-row tag centred on the field, on the row nearest <paramref name="row"/> where it covers no speech box.</summary>
    private void CentreTag(Screen scr, FieldLayout lay, int row, string text, uint fg, uint bg)
    {
        var tag = new CellRect(lay.Col0 + Math.Max(0, (lay.PCols - text.Length) / 2), row, text.Length, 1);
        tag = tag with { R = RowClearOfSpeechBoxes(tag, lay) };
        _tags.Add(tag);
        scr.Text(tag.C, tag.R, text, fg, bg);
    }

    private readonly List<CellRect> _tags = [];

    /// <summary>The cells of the banners (the stage banner, the warning, a kind's hint) and the tags (SHIP DOWN, the hints, the
    /// toast) drawn by the last frame.</summary>
    public IReadOnlyList<CellRect> Tags => _tags;

    /// <summary>The row nearest <paramref name="tag"/>'s own, searched down and up in turn, at which it covers none of the speech
    /// boxes drawn this frame, a labelled enemy's and a gunship's or carrier's alike; its own row when there is none. A banner
    /// or tag is drawn over the boxes, so this keeps their words whole.</summary>
    private int RowClearOfSpeechBoxes(CellRect tag, FieldLayout lay)
    {
        int last = lay.Rows - tag.H;
        for (int step = 0; step <= lay.Rows; step++)
            foreach (int row in (ReadOnlySpan<int>)[tag.R + step, tag.R - step])
            {
                var moved = tag with { R = row };
                if (row >= HudRows && row <= last && !_speechBoxPainter.Boxes.Exists(moved.Overlaps) && !_enemySpeechBoxes.Exists(moved.Overlaps)) return row;
            }
        return tag.R;
    }

    /// <summary>The tag drawn while the ship is down.</summary>
    public const string ShipDown = " SHIP DOWN ";

    /// <summary>The cells of the speech boxes drawn by the last frame.</summary>
    public IReadOnlyList<CellRect> SpeechBoxes => _speechBoxPainter.Boxes;

    /// <summary>Draws the feed log in the side panel, or the phone's one-line feed strip, and replaces the click targets <see cref="LogHit"/> reads
    /// with the button rows this frame draws.</summary>
    private void DrawPanelsAndRegisterHits(RaiderGame game, Screen scr, FieldLayout lay)
    {
        _hits.Clear();
        if (lay.PanelCols > 0) DrawLog(game, scr, lay.Col0 - Tuning.SideGapCols - lay.PanelCols, LogTopRow, lay.Rows - LogRowsFromBottom);
        else DrawNarrowLog(game, scr, lay);
    }

    private readonly record struct Hit(int Col, int Row, int Width, LogEntry Entry);
    private readonly List<Hit> _hits = new();

    /// <summary>The entry and button under a cell of the last drawn feed log: the left half of a button row is the like, the right half
    /// the repost. Anything else, and every cell when no log was drawn, is nothing.</summary>
    public (LogEntry Entry, PostAction Action)? LogHit(int col, int row)
    {
        foreach (var hit in _hits)
            if (hit.Row == row && col >= hit.Col && col < hit.Col + hit.Width)
                return (hit.Entry, col - hit.Col < hit.Width / 2 ? PostAction.Like : PostAction.Repost);
        return null;
    }

    /// <summary>A click on a cell of the screen: on a button of the feed log's last drawn frame it toggles that like or repost,
    /// anywhere else it does nothing.</summary>
    public void ToggleLogButtonAt(RaiderGame game, int col, int row)
    {
        if (LogHit(col, row) is { } hit) game.Toggle(hit.Entry.Post.Uri, hit.Action);
    }

    /// <summary>How far a marked button's icon is mixed from the feed log's ground toward its lit colour.</summary>
    internal const double MarkedIconStrength = 0.5;

    /// <summary>The colour of a button's icon: lit in its own colour, a marked one at half strength, a waiting one blinking, an off one dim.</summary>
    private static uint IconColour(ButtonState state, uint lit, long frame) => state switch
    {
        ButtonState.Lit => lit,
        ButtonState.Marked => Surface.Mix(PanelBg, lit, MarkedIconStrength),
        ButtonState.Pending => BlinkOn(frame, CardPainter.BlinkFrames) ? lit : Dim,
        _ => Dim,
    };

    /// <summary>The phone's one-line feed strip: the marked post's two icons in their states, then its handle and first words.</summary>
    private void DrawNarrowLog(RaiderGame game, Screen scr, FieldLayout lay)
    {
        var marked = game.Log.Marked;
        if (marked is null || lay.Rows < NarrowLogMinRows) return;
        int row = lay.Rows - 1, col0 = lay.Col0, width = lay.Cols - col0;
        scr.Blank(col0, row, Math.Max(0, width), Palette.White, PanelBg);
        var marks = marked.Marks;
        bool litLike = marks.Like is ButtonState.Lit or ButtonState.Pending;
        scr.Put(col0, row, litLike ? CardLayout.HeartOn : CardLayout.HeartOff, IconColour(marks.Like, CardColours.LikeLit, game.Frame), PanelBg);
        scr.Put(col0 + NarrowRepostCol, row, CardLayout.LoopMark, IconColour(marks.Repost, CardColours.RepostLit, game.Frame), PanelBg);
        string line = HandleText(marked.Post) + ": " + CellText.Clean(marked.Post.Text);
        scr.Text(col0 + NarrowTextCol, row, Clip(line, width - NarrowTextCol), LogHandle, PanelBg);
    }

    /// <summary>The wide screen's left side panel as a feed: a heading, then the posts that have left the field, the newest ending at
    /// <paramref name="bottom"/> and the older ones above it, cut off under the heading. Each button row drawn is registered as a
    /// click target.</summary>
    private void DrawLog(RaiderGame game, Screen scr, int col, int top, int bottom)
    {
        scr.Blank(col, top, _lay.PanelCols, Palette.White, PanelBg);
        scr.Text(col + LogTextCol, top, "FEED LOG", Dim, PanelBg);
        var entries = game.Log.Entries;
        int y = bottom + 1, firstEntryRow = top + 1;
        for (int index = entries.Count - 1; index >= 0 && y > firstEntryRow; index--)
        {
            var text = LogTextFor(entries[index].Post);
            if (text.Height == 0) continue;
            y -= text.Height;
            DrawLogEntry(game, scr, entries[index], text, col, y, firstEntryRow, bottom);
        }
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<FeedPost, LogText> _logText = new();

    /// <summary>What an entry shows besides its rule: the display name (null when there is none), the post's wrapped text and the
    /// attachment note (null when there is none).</summary>
    private sealed record LogText(int Width, string? Name, string[] Lines, string? Note)
    {
        private const int FixedRows = 3;   // the rule, the handle row and the button row
        /// <summary>The rule, the name row when there is one, the handle row, the button row, the text and the note. An entry with neither text nor
        /// note (all of it was what the terminal cannot draw) is nothing: no rule, no handle over an empty box.</summary>
        public int Height => Lines.Length == 0 && Note is null ? 0 : FixedRows + (Name is null ? 0 : 1) + Lines.Length + (Note is null ? 0 : 1);
    }

    /// <summary>The entry's parts, worked out once per post and width.</summary>
    private LogText LogTextFor(FeedPost post)
    {
        int width = _lay.PanelCols - BothSides * LogTextCol;
        if (_logText.TryGetValue(post, out var text) && text.Width == width) return text;
        var lines = CellText.Wrap(CellText.Clean(post.Text), width).ToArray();   // by cells: a wide char takes two, and is never cut at the edge
        string name = CellText.Clean(post.DisplayName);
        string? note = CardLayout.NoteText(post) is { } noteText && CellText.Clean(noteText) is { Length: > 0 } clean ? clean : null;
        text = new LogText(width, name.Length == 0 ? null : Clip(name, width), lines, note is null ? null : Clip(note, width));
        _logText.AddOrUpdate(post, text);
        return text;
    }

    /// <summary>One entry: a rule, the display name when there is one, the handle with the likes beside it, the post and the
    /// attachment note when there is one. A button row that is drawn is registered as a click target.</summary>
    private void DrawLogEntry(RaiderGame game, Screen scr, LogEntry logEntry, LogText logText, int col, int row, int top, int bottom)
    {
        var post = logEntry.Post;
        bool marked = ReferenceEquals(logEntry, game.Log.Marked);
        var lines = logText.Lines;
        int height = logText.Height;
        for (int at = Math.Max(row, top); at < Math.Min(row + height, bottom + 1); at++) scr.Blank(col, at, _lay.PanelCols, Palette.White, PanelBg);
        bool InView(int at) => at >= top && at <= bottom;
        if (InView(row)) for (int cell = 0; cell < _lay.PanelCols; cell++) scr.Put(col + cell, row, LogRuleGlyph, marked ? MarkedRule : Rail, PanelBg);
        int next = row + 1;
        if (logText.Name is not null)
        {
            if (InView(next)) scr.Text(col + LogNameCol, next, logText.Name, LogPostText, PanelBg);
            next++;
        }
        int markRow = logText.Name is null ? next : next - 1;
        if (marked && InView(markRow)) scr.Put(col + LogMarkCol, markRow, LogMarkGlyph, Palette.White, PanelBg);
        if (InView(next)) scr.Text(col + LogNameCol, next, Clip(HandleText(post), _lay.PanelCols - BothSides * LogNameCol), LogHandle, PanelBg);
        next++;
        if (InView(next)) DrawButtonsAndRegisterHit(game, scr, logEntry, col, next);
        next++;
        for (int line = 0; line < lines.Length; line++)
            if (InView(next + line)) scr.Text(col + LogTextCol, next + line, lines[line], LogPostText, PanelBg);
        if (logText.Note is not null && InView(next + lines.Length)) scr.Text(col + LogTextCol, next + lines.Length, logText.Note, LogDim, PanelBg);
    }

    /// <summary>The button row: the like icon and count in the left half, the repost in the right, each in its state's colour. A repost
    /// the sign-in may not make shows a lock. The row is registered as a click target for <see cref="LogHit"/>.</summary>
    private void DrawButtonsAndRegisterHit(RaiderGame game, Screen scr, LogEntry logEntry, int col, int row)
    {
        var marks = logEntry.Marks;
        int half = _lay.PanelCols / 2;
        bool likeOn = marks.Like is ButtonState.Lit or ButtonState.Pending;
        int likeCount = LikeIconCol + CountAfterIcon, repostCount = half + RepostIconCol + CountAfterIcon;
        scr.Put(col + LikeIconCol, row, likeOn ? CardLayout.HeartOn : CardLayout.HeartOff, IconColour(marks.Like, CardColours.LikeLit, game.Frame), PanelBg);
        scr.Text(col + likeCount, row, Clip(CardPainter.CountText(logEntry.Post, marks, PostAction.Like), half - likeCount - CountGap), LogDim, PanelBg);
        scr.Put(col + half + RepostIconCol, row, CardLayout.LoopMark, IconColour(marks.Repost, CardColours.RepostLit, game.Frame), PanelBg);
        bool locked = !game.MayWrite(PostAction.Repost) && marks.Repost == ButtonState.Off;
        scr.Text(col + repostCount, row, Clip(locked ? "lock" : CardPainter.CountText(logEntry.Post, marks, PostAction.Repost), _lay.PanelCols - repostCount - CountGap), LogDim, PanelBg);
        _hits.Add(new Hit(col, row, _lay.PanelCols, logEntry));
    }

    /// <summary>The post's author as the log shows them: their handle after an at sign.</summary>
    private static string HandleText(FeedPost post) => HandleMark + CellText.Clean(post.AuthorHandle);

    private static string Clip(string text, int width) => CellText.Clip(text, Math.Max(1, width), true);
}
