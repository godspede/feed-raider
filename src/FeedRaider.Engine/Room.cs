using TUImation;

namespace FeedRaider.Engine;

public enum ActionKind { Say, Visit }

/// <summary>What the room decided to do about a post.</summary>
public sealed class GuyAction
{
    public ActionKind Kind { get; init; }
    public string Did { get; init; } = "";
    /// <summary>For a Visit, the guy being walked to.</summary>
    public string? TargetDid { get; init; }
    public string Text { get; init; } = "";
    public long StartMs { get; init; }

    internal bool Resolved;
    internal double FromX, FromY, SideOffset;
    internal long WalkMs;

    internal long TotalMs => Kind == ActionKind.Say ? Room.BubbleMs : 2 * WalkMs + Room.BubbleMs;
}

/// <summary>
/// Room state and simulation. Feed it posts with <see cref="Handle"/>, ask it for ANSI frames with
/// <see cref="Render"/>. Time is always passed in (ms), never read, so it is testable and the
/// wandering is the same everywhere.
/// </summary>
public sealed class Room
{
    public const long BubbleMs = 5000;
    private const int MaxPending = 3;

    private sealed class Guy
    {
        public required Follow Follow { get; init; }
        public required Look Look { get; init; }
        public int Slot = -1;
        public long LastActiveMs;
        public GuyAction? Current;
        public readonly Queue<GuyAction> Pending = new();
    }

    private enum Phase { Idle, Out, Talk, Back }

    private readonly Dictionary<string, Guy> _guys = new(StringComparer.Ordinal);
    private readonly List<Guy> _order;
    private readonly List<Guy> _slots = [];
    private int _cols = 80, _pixelHeight = 46;
    private (double X, double Y)[] _homes = [];

    public string Status { get; set; } = "";
    /// <summary>The forces of darkness. Always present; it stays empty unless something calls <see cref="Haunt"/>.</summary>
    public Darkness Dark { get; } = new();

    /// <summary>Summon a monster from an anonymous labelled post.</summary>
    public void Haunt(DarkPost post) => Dark.Offer(post);
    public int GuyCount => _guys.Count;

    public Room(IEnumerable<Follow> follows)
    {
        foreach (var f in follows)
            if (!_guys.ContainsKey(f.Did))
                _guys[f.Did] = new Guy { Follow = f, Look = Look.From(f.Did, f.Handle) };
        // sorted by DID so every client gives everybody the same seat
        _order = _guys.Values.OrderBy(g => g.Follow.Did, StringComparer.Ordinal).ToList();
        SetSize(80, 46);
    }

    public bool Contains(string did) => _guys.ContainsKey(did);

    public Look LookOf(string did) => _guys[did].Look;

    public GuyAction? CurrentAction(string did) => _guys.TryGetValue(did, out var g) ? g.Current : null;

    /// <summary>Maps a post to what happens: a plain post (or a reply to someone not in the room, or to
    /// oneself) is a bubble in place; a reply to a guy in the room is a walk to that guy. Null if the
    /// author is not in the room.</summary>
    public GuyAction? Plan(PostEvent e, long nowMs)
    {
        if (!_guys.ContainsKey(e.Did)) return null;
        bool visit = e.ReplyParentDid is { } p && p != e.Did && _guys.ContainsKey(p);
        return new GuyAction
        {
            Kind = visit ? ActionKind.Visit : ActionKind.Say,
            Did = e.Did,
            TargetDid = visit ? e.ReplyParentDid : null,
            Text = e.Text,
            StartMs = nowMs,
        };
    }

    public GuyAction? Handle(PostEvent e, long nowMs)
    {
        var action = Plan(e, nowMs);
        if (action is null) return null;
        var guy = _guys[e.Did];
        Advance(guy, nowMs);
        Promote(guy, protect: action.TargetDid);
        if (action.TargetDid is { } t) Promote(_guys[t], protect: e.Did);
        guy.LastActiveMs = nowMs;

        if (guy.Current is null)
            guy.Current = action;
        else if (guy.Current.Kind == ActionKind.Say && action.Kind == ActionKind.Say)
            guy.Current = action;
        else
        {
            guy.Pending.Enqueue(action);
            while (guy.Pending.Count > MaxPending) guy.Pending.Dequeue();
        }
        return action;
    }

    // ---- seats -------------------------------------------------------------------------------

    private void SetSize(int cols, int pixelHeight)
    {
        _cols = cols;
        _pixelHeight = pixelHeight;
        int cap = Math.Min(_guys.Count, Layout.Capacity(cols, pixelHeight));
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

    /// <summary>A guy without a seat (the room is fuller than the screen) takes the seat of whoever has been quiet longest.</summary>
    private void Promote(Guy guy, string? protect)
    {
        if (guy.Slot >= 0 || _slots.Count == 0) return;
        var candidates = _slots.Where(s => s.Follow.Did != protect).ToList();
        var victim = candidates.Where(s => s.Current is null && s.Pending.Count == 0).OrderBy(s => s.LastActiveMs).FirstOrDefault()
                     ?? candidates.OrderBy(s => s.LastActiveMs).FirstOrDefault();
        if (victim is null) return;
        guy.Slot = victim.Slot;
        _slots[guy.Slot] = guy;
        victim.Slot = -1;
        victim.Current = null;
        victim.Pending.Clear();
    }

    // ---- simulation --------------------------------------------------------------------------

    private (double X, double Y) Home(Guy g, long ms)
    {
        if (g.Slot < 0 || g.Slot >= _homes.Length) return (0, 0);
        var (hx, hy) = _homes[g.Slot];
        var slack = Layout.Slack(_slots.Count, _cols, _pixelHeight);
        var (wx, wy) = Wander.Offset(g.Follow.Did, ms);
        return (Math.Clamp(hx + wx * slack.X, 0, Math.Max(0, _cols - GuyBuilder.Width)),
                Math.Clamp(hy + wy * slack.Y, 0, Math.Max(0, _pixelHeight - GuyBuilder.Height)));
    }

    /// <summary>Where a guy idles at a given time: home seat plus wander. Deterministic.</summary>
    public (double X, double Y)? IdlePosition(string did, long ms) =>
        _guys.TryGetValue(did, out var g) && g.Slot >= 0 ? Home(g, ms) : null;

    private void Resolve(Guy g, GuyAction a)
    {
        if (a.Resolved) return;
        a.Resolved = true;
        (a.FromX, a.FromY) = Home(g, a.StartMs);
        if (a.Kind != ActionKind.Visit) return;
        var (tx, ty) = Home(_guys[a.TargetDid!], a.StartMs);
        a.SideOffset = a.FromX <= tx ? -(GuyBuilder.Width - 1) : GuyBuilder.Width - 1;
        double dist = Math.Sqrt((tx - a.FromX) * (tx - a.FromX) + (ty - a.FromY) * (ty - a.FromY));
        a.WalkMs = (long)Math.Clamp(dist / 14.0 * 1000, 1200, 3500);
    }

    private void Advance(Guy g, long ms)
    {
        while (g.Current is { } cur)
        {
            Resolve(g, cur);
            long end = cur.StartMs + cur.TotalMs;
            if (ms < end) return;
            g.Current = null;
            if (g.Pending.Count == 0) return;
            var next = g.Pending.Dequeue();
            g.Current = new GuyAction { Kind = next.Kind, Did = next.Did, TargetDid = next.TargetDid, Text = next.Text, StartMs = end };
        }
    }

    private static double Ease(double p) => p * p * (3 - 2 * p);

    private (double X, double Y) Position(Guy g, long ms) => Position(g, ms, 0);

    private (double X, double Y) Position(Guy g, long ms, int depth)
    {
        if (g.Current is not { Kind: ActionKind.Visit } a) return Home(g, ms);
        Resolve(g, a);
        long t = ms - a.StartMs;
        if (t < 0) return Home(g, ms);

        // stand beside the target wherever it currently is (a target mid-visit is followed one level deep)
        var target = _guys[a.TargetDid!];
        var tp = depth > 0 ? Home(target, ms) : Position(target, ms, depth + 1);
        var dest = (X: Math.Clamp(tp.X + a.SideOffset, 0, Math.Max(0, _cols - GuyBuilder.Width)), tp.Y);

        if (t < a.WalkMs)
        {
            double p = Ease((double)t / a.WalkMs);
            return (a.FromX + (dest.X - a.FromX) * p, a.FromY + (dest.Y - a.FromY) * p);
        }
        if (t < a.WalkMs + BubbleMs) return dest;
        double q = Ease(Math.Min(1.0, (double)(t - a.WalkMs - BubbleMs) / a.WalkMs));
        var home = Home(g, ms);
        return (dest.X + (home.X - dest.X) * q, dest.Y + (home.Y - dest.Y) * q);
    }

    private Phase PhaseOf(Guy g, long ms)
    {
        if (g.Current is not { } a) return Phase.Idle;
        Resolve(g, a);
        if (a.Kind == ActionKind.Say) return Phase.Talk;
        long t = ms - a.StartMs;
        if (t < 0) return Phase.Idle;
        if (t < a.WalkMs) return Phase.Out;
        if (t < a.WalkMs + BubbleMs) return Phase.Talk;
        return Phase.Back;
    }

    // ---- rendering ---------------------------------------------------------------------------

    /// <summary>One ANSI frame of <paramref name="cols"/> x <paramref name="rows"/> cells; the last row is a status line.</summary>
    public string Render(int cols, int rows, long ms)
    {
        cols = Math.Max(cols, 20);
        rows = Math.Max(rows, 8);
        int pixelHeight = (rows - 1) * 2;
        if (cols != _cols || pixelHeight != _pixelHeight) SetSize(cols, pixelHeight);

        foreach (var g in _slots) Advance(g, ms);

        var frame = new Frame(cols, rows - 1);
        var drawn = _slots.Select(g => (Guy: g, Pos: Position(g, ms))).ToList();
        var shown = new Dictionary<Guy, (double X, double Y)>();
        // draw order is depth: lower on screen is nearer; a guy mid-conversation steps to the front
        drawn.Sort((a, b) =>
        {
            double za = a.Pos.Y + (a.Guy.Current is null ? 0 : 1000), zb = b.Pos.Y + (b.Guy.Current is null ? 0 : 1000);
            int c = za.CompareTo(zb);
            return c != 0 ? c : string.CompareOrdinal(a.Guy.Follow.Did, b.Guy.Follow.Did);
        });

        Dark.Update(ms, drawn.Select(d => (d.Guy.Follow.Did, d.Pos)).ToList(), cols, pixelHeight);

        foreach (var (g, rawPos) in drawn)
        {
            var flinch = Dark.Flinch(rawPos.X, rawPos.Y, ms);
            var pos = (X: rawPos.X + flinch.X, Y: rawPos.Y + flinch.Y);
            shown[g] = pos;
            var phase = PhaseOf(g, ms);
            int facing = 0;
            if (phase is Phase.Out or Phase.Back)
                facing = Math.Sign(Math.Round(Position(g, ms + 100).X - rawPos.X));
            else if (phase == Phase.Talk && g.Current?.TargetDid is { } td)
                facing = Math.Sign(Position(_guys[td], ms).X - rawPos.X);
            bool walking = phase is Phase.Out or Phase.Back;
            bool blink = (ms + (long)(Hashing.Fnv64(g.Follow.Did) % 4000)) % 4000 < 140;
            var pose = new Pose(walking ? 1 + (int)((ms / 160) & 1) : 0, facing, phase == Phase.Talk && ((ms / 250) & 1) == 0, blink);
            int px = (int)Math.Round(pos.X), py = (int)Math.Round(pos.Y);
            frame.Blit(GuyBuilder.Build(g.Look, pose), px, py);
            frame.Label(g.Follow.Handle, px + GuyBuilder.Width / 2, (py + GuyBuilder.Height) / 2);
        }

        foreach (var (g, _) in drawn.Where(d => PhaseOf(d.Guy, ms) == Phase.Talk).OrderBy(d => d.Guy.Current!.StartMs))
        {
            var pos = shown[g];
            frame.Bubble(BubbleText.Format(g.Current!.Text), (int)Math.Round(pos.X) + GuyBuilder.Width / 2, (int)Math.Round(pos.Y) / 2);
        }

        // monsters and their text are the top layer: in front of the guys and their bubbles, whatever the draw order
        foreach (var m in Dark.Monsters)
            if (MonsterSprites.Build(m, ms) is { } sprite)
                frame.BlitTop(sprite, (int)Math.Round(m.X), (int)Math.Round(m.Y));
        foreach (var m in Dark.Monsters)
        {
            if (m.Dissolving && ms - m.DissolveStartMs > Darkness.DissolveMs / 2) continue;
            int cx = (int)Math.Round(m.X) + m.Width / 2, top = (int)Math.Round(m.Y) / 2;
            if (m.Kind == DarknessKind.Rude) frame.JaggedBubble(BubbleText.Format(m.Text, 18, 3), cx, top);
            else frame.Whisper(m.Text, cx, top);
        }

        frame.Status(Status.Length > 0 ? Status : $"{_guys.Count} guys");
        return frame.ToAnsi();
    }
}
