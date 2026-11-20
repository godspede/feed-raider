using TUImation;

namespace FeedRaider.Engine;

public enum ActionKind { Say, Visit }

/// <summary>What the room decided to do about a post.</summary>
public sealed class FigureAction
{
    public ActionKind Kind { get; init; }
    public string Did { get; init; } = "";
    /// <summary>For a Visit, the figure being walked to.</summary>
    public string? TargetDid { get; init; }
    public string Text { get; init; } = "";
    public long StartMs { get; init; }

    internal bool Resolved;
    internal double FromX, FromY, SideOffset;
    internal long WalkMs;

    internal long TotalMs => Kind == ActionKind.Say ? Room.SpeechBoxMs : 2 * WalkMs + Room.SpeechBoxMs;
}

/// <summary>
/// Room state and simulation. Feed it posts with <see cref="Handle"/>, ask it for ANSI frames with
/// <see cref="Render"/>. Time is always passed in (ms), never read, so it is testable and the
/// wandering is the same everywhere.
/// </summary>
public sealed class Room
{
    public const long SpeechBoxMs = 5000;
    private const int MaxPending = 3;
    /// <summary>A cell is a half-block pair: two pixel rows.</summary>
    private const int PixelsPerCell = 2;
    /// <summary>The bottom row of a frame is the status line, not room.</summary>
    private const int StatusLineRows = 1;
    private const int DefaultCols = 80, DefaultRows = 24, DefaultPixelHeight = (DefaultRows - StatusLineRows) * PixelsPerCell;
    private const double MsPerSecond = 1000;
    private const double WalkPixelsPerSecond = 14.0;
    internal const long MinWalkMs = 1200, MaxWalkMs = 3500;
    /// <summary>Each figure blinks once per period for BlinkMs, at an offset taken from the DID so the room does not blink in unison.</summary>
    private const long BlinkPeriodMs = 4000, BlinkMs = 140;
    private const long StepFrameMs = 160, TalkFlapMs = 250;
    private const int MinCols = 20, MinRows = 8;
    /// <summary>A figure in conversation sorts in front of every idle figure: this many pixels of depth.</summary>
    private const double SpeakerDepthBonus = 1000;
    /// <summary>How far ahead a walking figure looks to find which way he faces.</summary>
    private const long FacingLookAheadMs = 100;
    private const int RudeSpeechBoxWidth = 18, RudeSpeechBoxLines = 3;

    private sealed class Figure
    {
        public required Follow Follow { get; init; }
        public required Look Look { get; init; }
        public int Slot = -1;
        public long LastActiveMs;
        public FigureAction? Current;
        public readonly Queue<FigureAction> Pending = new();
    }

    private enum Phase { Idle, Out, Talk, Back }

    private readonly Dictionary<string, Figure> _figures = new(StringComparer.Ordinal);
    private readonly List<Figure> _order;
    private readonly List<Figure> _slots = [];
    private int _cols = DefaultCols, _pixelHeight = DefaultPixelHeight;
    private (double X, double Y)[] _homes = [];

    public string Status { get; set; } = "";
    /// <summary>The forces of darkness. Always present; it stays empty unless something calls <see cref="Darkness.Offer"/>.</summary>
    public Darkness Dark { get; } = new();

    public int FigureCount => _figures.Count;

    public Room(IEnumerable<Follow> follows)
    {
        foreach (var f in follows)
            if (!_figures.ContainsKey(f.Did))
                _figures[f.Did] = new Figure { Follow = f, Look = Look.From(f.Did, f.Handle) };
        // sorted by DID so every client gives everybody the same seat
        _order = _figures.Values.OrderBy(g => g.Follow.Did, StringComparer.Ordinal).ToList();
        SetSize(DefaultCols, DefaultPixelHeight);
    }

    public FigureAction? CurrentAction(string did) => _figures.TryGetValue(did, out var g) ? g.Current : null;

    /// <summary>Maps a post to what happens: a plain post (or a reply to someone not in the room, or to
    /// oneself) is a speech box in place; a reply to a figure in the room is a walk to that figure. Null if the
    /// author is not in the room.</summary>
    public FigureAction? Plan(PostEvent e, long nowMs)
    {
        if (!_figures.ContainsKey(e.Did)) return null;
        bool visit = e.ReplyParentDid is { } p && p != e.Did && _figures.ContainsKey(p);
        return new FigureAction
        {
            Kind = visit ? ActionKind.Visit : ActionKind.Say,
            Did = e.Did,
            TargetDid = visit ? e.ReplyParentDid : null,
            Text = e.Text,
            StartMs = nowMs,
        };
    }

    public FigureAction? Handle(PostEvent e, long nowMs)
    {
        var action = Plan(e, nowMs);
        if (action is null) return null;
        var figure = _figures[e.Did];
        Advance(figure, nowMs);
        Promote(figure, protect: action.TargetDid);
        if (action.TargetDid is { } t) Promote(_figures[t], protect: e.Did);
        figure.LastActiveMs = nowMs;

        if (figure.Current is null)
            figure.Current = action;
        else if (figure.Current.Kind == ActionKind.Say && action.Kind == ActionKind.Say)
            figure.Current = action;
        else
        {
            figure.Pending.Enqueue(action);
            while (figure.Pending.Count > MaxPending) figure.Pending.Dequeue();
        }
        return action;
    }

    // ---- seats -------------------------------------------------------------------------------

    private void SetSize(int cols, int pixelHeight)
    {
        _cols = cols;
        _pixelHeight = pixelHeight;
        int cap = Math.Min(_figures.Count, Layout.Capacity(cols, pixelHeight));
        while (_slots.Count > cap)
        {
            _slots[^1].Slot = -1;
            _slots.RemoveAt(_slots.Count - 1);
        }
        foreach (var g in _order)
        {
            if (_slots.Count >= cap) break;
            if (g.Slot >= 0) continue;
            g.Slot = _slots.Count;
            _slots.Add(g);
        }
        _homes = Layout.Homes(_slots.Count, cols, pixelHeight);
    }

    /// <summary>A figure without a seat (the room is fuller than the screen) takes the seat of whoever has been quiet longest.</summary>
    private void Promote(Figure figure, string? protect)
    {
        if (figure.Slot >= 0 || _slots.Count == 0) return;
        var candidates = _slots.Where(s => s.Follow.Did != protect).ToList();
        var victim = candidates.Where(s => s.Current is null && s.Pending.Count == 0).OrderBy(s => s.LastActiveMs).FirstOrDefault()
                     ?? candidates.OrderBy(s => s.LastActiveMs).FirstOrDefault();
        if (victim is null) return;
        figure.Slot = victim.Slot;
        _slots[figure.Slot] = figure;
        victim.Slot = -1;
        victim.Current = null;
        victim.Pending.Clear();
    }

    // ---- simulation --------------------------------------------------------------------------

    private (double X, double Y) Home(Figure g, long ms)
    {
        // an unseated figure has no home; callers that reach one (a Visit to an unseated target) get the top-left corner
        if (g.Slot < 0 || g.Slot >= _homes.Length) return (0, 0);
        var (hx, hy) = _homes[g.Slot];
        var slack = Layout.Slack(_pixelHeight);
        var (wx, wy) = Wander.Offset(g.Follow.Did, ms);
        return (Math.Clamp(hx + wx * slack.X, 0, Math.Max(0, _cols - FigureBuilder.Width)),
                Math.Clamp(hy + wy * slack.Y, 0, Math.Max(0, _pixelHeight - FigureBuilder.Height)));
    }

    /// <summary>Where a figure idles at a given time: home seat plus wander. Deterministic.</summary>
    public (double X, double Y)? IdlePosition(string did, long ms) =>
        _figures.TryGetValue(did, out var g) && g.Slot >= 0 ? Home(g, ms) : null;

    private void Resolve(Figure g, FigureAction a)
    {
        if (a.Resolved) return;
        a.Resolved = true;
        (a.FromX, a.FromY) = Home(g, a.StartMs);
        if (a.Kind != ActionKind.Visit) return;
        var (tx, ty) = Home(_figures[a.TargetDid!], a.StartMs);
        a.SideOffset = a.FromX <= tx ? -(FigureBuilder.Width - 1) : FigureBuilder.Width - 1;
        double dist = double.Hypot(tx - a.FromX, ty - a.FromY);
        a.WalkMs = (long)Math.Clamp(dist / WalkPixelsPerSecond * MsPerSecond, MinWalkMs, MaxWalkMs);
    }

    private void Advance(Figure g, long ms)
    {
        while (g.Current is { } cur)
        {
            Resolve(g, cur);
            long end = cur.StartMs + cur.TotalMs;
            if (ms < end) return;
            g.Current = null;
            if (g.Pending.Count == 0) return;
            var next = g.Pending.Dequeue();
            g.Current = new FigureAction { Kind = next.Kind, Did = next.Did, TargetDid = next.TargetDid, Text = next.Text, StartMs = end };
        }
    }

    private static double Ease(double p) => p * p * (3 - 2 * p);

    private (double X, double Y) Position(Figure g, long ms, int depth = 0)
    {
        if (g.Current is not { Kind: ActionKind.Visit } a) return Home(g, ms);
        Resolve(g, a);
        long t = ms - a.StartMs;
        if (t < 0) return Home(g, ms);

        // stand beside the target wherever it currently is (a target mid-visit is followed one level deep)
        var target = _figures[a.TargetDid!];
        var tp = depth > 0 ? Home(target, ms) : Position(target, ms, depth + 1);
        var dest = (X: Math.Clamp(tp.X + a.SideOffset, 0, Math.Max(0, _cols - FigureBuilder.Width)), tp.Y);

        if (t < a.WalkMs)
        {
            double p = Ease((double)t / a.WalkMs);
            return (a.FromX + (dest.X - a.FromX) * p, a.FromY + (dest.Y - a.FromY) * p);
        }
        if (t < a.WalkMs + SpeechBoxMs) return dest;
        double q = Ease(Math.Min(1.0, (double)(t - a.WalkMs - SpeechBoxMs) / a.WalkMs));
        var home = Home(g, ms);
        return (dest.X + (home.X - dest.X) * q, dest.Y + (home.Y - dest.Y) * q);
    }

    private Phase PhaseOf(Figure g, long ms)
    {
        if (g.Current is not { } a) return Phase.Idle;
        Resolve(g, a);
        if (a.Kind == ActionKind.Say) return Phase.Talk;
        long t = ms - a.StartMs;
        if (t < 0) return Phase.Idle;
        if (t < a.WalkMs) return Phase.Out;
        if (t < a.WalkMs + SpeechBoxMs) return Phase.Talk;
        return Phase.Back;
    }

    // ---- rendering ---------------------------------------------------------------------------

    /// <summary>One ANSI frame of <paramref name="cols"/> x <paramref name="rows"/> cells; the last row is a status line.
    /// First moves the room on to <paramref name="ms"/>: it resizes the seats, finishes due actions and updates the enemies.</summary>
    public string AdvanceAndRender(int cols, int rows, long ms)
    {
        cols = Math.Max(cols, MinCols);
        rows = Math.Max(rows, MinRows);
        int roomRows = rows - StatusLineRows, pixelHeight = roomRows * PixelsPerCell;
        if (cols != _cols || pixelHeight != _pixelHeight) SetSize(cols, pixelHeight);

        foreach (var g in _slots) Advance(g, ms);

        var frame = new Frame(cols, roomRows);
        var drawn = _slots.Select(g => (Figure: g, Pos: Position(g, ms))).ToList();
        var shown = new Dictionary<Figure, (double X, double Y)>();
        // draw order is depth: lower on screen is nearer; a figure mid-conversation steps to the front
        drawn.Sort((a, b) =>
        {
            double za = a.Pos.Y + (a.Figure.Current is null ? 0 : SpeakerDepthBonus), zb = b.Pos.Y + (b.Figure.Current is null ? 0 : SpeakerDepthBonus);
            int c = za.CompareTo(zb);
            return c != 0 ? c : string.CompareOrdinal(a.Figure.Follow.Did, b.Figure.Follow.Did);
        });

        Dark.Update(ms, drawn.Select(d => (d.Figure.Follow.Did, d.Pos)).ToList(), cols, pixelHeight);

        foreach (var (g, rawPos) in drawn)
        {
            var flinch = Dark.Flinch(rawPos.X, rawPos.Y, ms);
            var pos = (X: rawPos.X + flinch.X, Y: rawPos.Y + flinch.Y);
            shown[g] = pos;
            var phase = PhaseOf(g, ms);
            int facing = 0;
            if (phase is Phase.Out or Phase.Back)
                facing = Math.Sign(Math.Round(Position(g, ms + FacingLookAheadMs).X - rawPos.X));
            else if (phase == Phase.Talk && g.Current?.TargetDid is { } td)
                facing = Math.Sign(Position(_figures[td], ms).X - rawPos.X);
            bool walking = phase is Phase.Out or Phase.Back;
            bool blink = (ms + (long)(Hashing.MixedFnv64(g.Follow.Did) % BlinkPeriodMs)) % BlinkPeriodMs < BlinkMs;
            var pose = new Pose(walking ? 1 + (int)((ms / StepFrameMs) & 1) : 0, facing, phase == Phase.Talk && ((ms / TalkFlapMs) & 1) == 0, blink);
            int px = (int)Math.Round(pos.X), py = (int)Math.Round(pos.Y);
            frame.Blit(FigureBuilder.Build(g.Look, pose), px, py);
            frame.Label(g.Follow.Handle, px + FigureBuilder.Width / 2, (py + FigureBuilder.Height) / PixelsPerCell);
        }

        foreach (var (g, _) in drawn.Where(d => PhaseOf(d.Figure, ms) == Phase.Talk).OrderBy(d => d.Figure.Current!.StartMs))
        {
            var pos = shown[g];
            frame.SpeechBox(ShortLines.Format(g.Current!.Text), (int)Math.Round(pos.X) + FigureBuilder.Width / 2, (int)Math.Round(pos.Y) / PixelsPerCell);
        }

        // enemies and their text are the top layer: in front of the figures and their speech boxes, whatever the draw order
        foreach (var m in Dark.Enemies)
            if (DarkEnemySprites.Build(m, ms) is { } sprite)
                frame.BlitTop(sprite, (int)Math.Round(m.X), (int)Math.Round(m.Y));
        foreach (var m in Dark.Enemies)
        {
            if (m.Dissolving && ms - m.DissolveStartMs > Darkness.DissolveMs / 2) continue;
            int cx = (int)Math.Round(m.X) + m.Width / 2, top = (int)Math.Round(m.Y) / PixelsPerCell;
            if (m.Kind == DarknessKind.Rude) frame.JaggedSpeechBox(ShortLines.Format(m.Text, RudeSpeechBoxWidth, RudeSpeechBoxLines), cx, top);
            else frame.Whisper(m.Text, cx, top);
        }

        frame.Status(Status.Length > 0 ? Status : $"{_figures.Count} figures");
        return frame.ToAnsi();
    }
}
