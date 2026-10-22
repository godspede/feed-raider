namespace FeedRaider.Engine.Raider;

public enum GameKey { Left, Right, Up, Down, Fire }

/// <summary>
/// What the player is doing, from any of the three devices. The host translates raw events into these calls; the game reads
/// the result once per sim frame. One-shot presses (bomb, like) are latched so a frozen frame (hit-stop) cannot swallow
/// them, and a like press that nothing consumes goes stale after <see cref="LikeLatchFrames"/> so it cannot fire later.
/// Pointer and drag positions are in terminal cells; the game maps them onto the field.
/// </summary>
public sealed class InputState
{
    public const int LikeLatchFrames = 8;
    private const int HoldFrames = (Tuning.KeyHoldMs * Tuning.Fps + 999) / 1000;

    private readonly bool[] _down = new bool[5];
    private readonly int[] _hold = new int[5];

    /// <summary>F toggles this: keep firing without holding Space.</summary>
    public bool AutoToggle { get; private set; }
    /// <summary>The last thing touched was the mouse or a finger: those fire by themselves.</summary>
    public bool PointerLast { get; private set; }

    public bool PointerActive { get; private set; }
    /// <summary>A press is down on the field: the like charge may be running.</summary>
    public bool Holding { get; private set; }
    public double HoldCol { get; private set; }
    public double HoldRow { get; private set; }
    public double PointerCol { get; private set; }
    public double PointerRow { get; private set; }

    internal bool BombPressed;
    internal int LikeLatch;
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

    /// <summary>A key press with no release event (a console only sees presses): counts as held for a moment.</summary>
    public void Press(GameKey k)
    {
        _hold[(int)k] = HoldFrames;
        PointerLast = false;
    }

    public void ToggleAutoFire() { AutoToggle = !AutoToggle; PointerLast = false; }
    public void Bomb() => BombPressed = true;
    public void Like() => LikeLatch = LikeLatchFrames;

    public void Pointer(double col, double row)
    {
        PointerActive = true; PointerCol = col; PointerRow = row; PointerLast = true;
        if (Holding) { HoldCol = col; HoldRow = row; }
    }

    /// <summary>The left button or one finger went down at this cell and is still down. Liking takes the hold to fill
    /// (<see cref="Tuning.LikeHoldMs"/>) on a wingman; a release or a drag first (<see cref="HoldEnd"/>) cancels it.</summary>
    public void HoldStart(double col, double row)
    {
        Holding = true; HoldCol = col; HoldRow = row; PointerLast = true;
    }

    public void HoldEnd() => Holding = false;

    public void PointerGone() => PointerActive = false;

    /// <summary>Relative finger drag in cells; the plane moves by the same amount.</summary>
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
    }
}
