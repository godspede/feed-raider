namespace FeedRaider.Engine.Raider;

public enum GameKey { Left, Right, Up, Down, Fire }

/// <summary>
/// What the player is doing, from any of the three devices. The host translates raw events into these calls; the game reads
/// the result once per sim frame. One-shot presses (bomb, the feed-log keys) are latched so a frozen frame (hit-stop) cannot swallow
/// them, and a like or repost press that nothing consumes goes stale after <see cref="LikeLatchFrames"/> so it cannot fire later.
/// Pointer and drag positions are in terminal cells; the game maps them onto the field.
/// </summary>
public sealed class InputState
{
    public const int LikeLatchFrames = 8;
    private static readonly int KeyCount = Enum.GetValues<GameKey>().Length;

    private readonly bool[] _down = new bool[KeyCount];
    private readonly int[] _hold = new int[KeyCount];

    /// <summary>A span in milliseconds as whole sim frames, rounded up.</summary>
    private static int MsToFrames(int ms) => (ms * Tuning.Fps + 999) / 1000;

    /// <summary>F toggles this: keep firing without holding Space.</summary>
    public bool AutoToggle { get; private set; }
    /// <summary>The last thing touched was the mouse or a finger: those fire by themselves.</summary>
    public bool PointerLast { get; private set; }

    public bool PointerActive { get; private set; }
    public double PointerCol { get; private set; }
    public double PointerRow { get; private set; }

    internal bool BombPressed;
    /// <summary>E and R: toggle the like or repost of the marked feed-log entry. Latched like a bomb press, and stale after the same span.</summary>
    internal int LikeLatch, RepostLatch;
    /// <summary>[ and ]: move the feed log's mark to the older or newer entry.</summary>
    internal bool MarkUpPressed, MarkDownPressed;
    internal double DragCols, DragRows;
    internal bool HasTap;
    internal double TapCol, TapRow;

    public bool Held(GameKey k) => _down[(int)k] || _hold[(int)k] > 0;

    /// <summary>Space held, or auto-fire on by F, or the pointer/touch was the last device.</summary>
    public bool Firing => Held(GameKey.Fire) || AutoToggle || PointerLast;

    /// <summary>A key went down or up (hosts that see releases: the browser).</summary>
    public void Key(GameKey k, bool down)
    {
        _down[(int)k] = down;
        if (down) PointerLast = false;
    }

    /// <summary>A press from a terminal that reports no releases. A key not yet held is the first press and holds long enough to
    /// bridge the OS repeat delay; a key still held is an auto-repeat and holds until the next one is due.</summary>
    public void Press(GameKey k, KeyRepeatTiming timing)
    {
        int ms = _hold[(int)k] > 0 ? timing.RepeatHoldMs : timing.FirstHoldMs;
        _hold[(int)k] = Math.Max(_hold[(int)k], MsToFrames(ms));
        PointerLast = false;
    }

    public void ToggleAutoFire() { AutoToggle = !AutoToggle; PointerLast = false; }
    public void Bomb() => BombPressed = true;
    public void Like() => LikeLatch = LikeLatchFrames;
    public void Repost() => RepostLatch = LikeLatchFrames;
    public void MarkUp() => MarkUpPressed = true;
    public void MarkDown() => MarkDownPressed = true;

    public void Pointer(double col, double row)
    {
        PointerActive = true; PointerCol = col; PointerRow = row; PointerLast = true;
    }

    public void PointerGone() => PointerActive = false;

    /// <summary>Relative finger drag in cells; the ship moves by the same amount.</summary>
    public void Drag(double dCols, double dRows)
    {
        DragCols += dCols; DragRows += dRows; PointerLast = true; PointerActive = false;
    }

    /// <summary>A single-finger tap or click: presses the on-screen bomb button. It never likes.</summary>
    public void Tap(double col, double row)
    {
        HasTap = true; TapCol = col; TapRow = row; PointerLast = true;
    }

    internal void EndFrame()
    {
        for (int i = 0; i < _hold.Length; i++) if (_hold[i] > 0) _hold[i]--;
        if (LikeLatch > 0) LikeLatch--;
        if (RepostLatch > 0) RepostLatch--;
    }
}
