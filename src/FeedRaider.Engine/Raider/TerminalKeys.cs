using System.Text;

namespace FeedRaider.Engine.Raider;

/// <summary>What a terminal key means to the game, whatever device reported it.</summary>
public enum TermKey { Up, Down, Left, Right, Fire, AutoFire, Bomb, Like, Quit, Mute }

public enum KeyAction { Press, Repeat, Release }

public readonly record struct TermKeyEvent(TermKey Key, KeyAction Action);

/// <summary>
/// How long a held key stays held when the terminal reports presses only. The OS repeats a held key after a delay and then
/// at an interval, so the first press must bridge the delay and each repeat only the gap to the next one.
/// </summary>
public readonly record struct KeyRepeatTiming(int DelayMs, int IntervalMs)
{
    public const int DefaultDelayMs = 500;
    public const int DefaultIntervalMs = 40;
    /// <summary>Repeats arrive unevenly through a terminal; a hold of exactly one interval would flicker.</summary>
    private const int JitterMs = 20;
    // SPI_GETKEYBOARDDELAY is 0..3 for 250..1000 ms; SPI_GETKEYBOARDSPEED is 0..31 for 2.5..30 repeats a second.
    private const int WinDelayStepMs = 250, WinDelayMax = 3, WinSpeedMax = 31;
    private const double WinMinRate = 2.5, WinMaxRate = 30;

    public static KeyRepeatTiming Default => new(DefaultDelayMs, DefaultIntervalMs);

    /// <summary>The hold after each repeat: the gap to the next one, plus slack.</summary>
    public int RepeatHoldMs => IntervalMs + JitterMs;
    /// <summary>The hold after the first press: the repeat delay, then the first repeat's gap.</summary>
    public int FirstHoldMs => DelayMs + RepeatHoldMs;

    public static KeyRepeatTiming FromWindows(int delayIndex, int speedIndex)
    {
        int delay = (Math.Clamp(delayIndex, 0, WinDelayMax) + 1) * WinDelayStepMs;
        double rate = WinMinRate + Math.Clamp(speedIndex, 0, WinSpeedMax) * (WinMaxRate - WinMinRate) / WinSpeedMax;
        return new KeyRepeatTiming(delay, (int)Math.Round(1000 / rate));
    }
}

/// <summary>Windows virtual-key codes (KEY_EVENT_RECORD.wVirtualKeyCode) to game keys.</summary>
public static class Win32Keys
{
    private const int VkEscape = 0x1B, VkSpace = 0x20, VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;

    public static TermKeyEvent? Map(int virtualKey, bool keyDown)
    {
        TermKey? k = virtualKey switch
        {
            VkLeft or 'A' => TermKey.Left,
            VkRight or 'D' => TermKey.Right,
            VkUp or 'W' => TermKey.Up,
            VkDown or 'S' => TermKey.Down,
            VkSpace => TermKey.Fire,
            'F' => TermKey.AutoFire,
            'B' => TermKey.Bomb,
            'E' => TermKey.Like,
            'M' => TermKey.Mute,
            'Q' or VkEscape => TermKey.Quit,
            _ => null,
        };
        return k is { } key ? new TermKeyEvent(key, keyDown ? KeyAction.Press : KeyAction.Release) : null;
    }
}

/// <summary>
/// Turns terminal input bytes into key events. It reads the kitty keyboard protocol (press, repeat and release in the
/// second field of CSI u and of the arrow keys) and the legacy sequences every terminal sends (press only).
/// </summary>
public sealed class TerminalKeyParser
{
    public const string KittyQuery = "\x1b[?u";
    /// <summary>Flags: 1 disambiguate, 2 report event types, 8 report all keys as escapes; without 8 letters and Space send no releases.</summary>
    public const string KittyPush = "\x1b[>11u";
    public const string KittyPop = "\x1b[<u";
    /// <summary>Every terminal answers this; it marks the end of the replies to a feature query.</summary>
    public const string DeviceAttributes = "\x1b[c";

    private const char Esc = '\x1b';
    private const int EtxCtrlC = 3, CodeEscape = 27, CodeSpace = 32, CtrlModBit = 4, MaxSequence = 64;
    private const int EventRepeat = 2, EventRelease = 3;

    private enum State { Ground, Esc, Csi, Ss3 }
    private State _state;
    private readonly StringBuilder _seq = new();

    public void Feed(ReadOnlySpan<byte> bytes, List<TermKeyEvent> output)
    {
        foreach (byte b in bytes) Feed(b, output);
    }

    public void Feed(byte b, List<TermKeyEvent> output)
    {
        switch (_state)
        {
            case State.Ground:
                if (b == Esc) _state = State.Esc;
                else if (b == EtxCtrlC) output.Add(new(TermKey.Quit, KeyAction.Press));
                else if (b < 0x80 && FromCode(b) is { } k) output.Add(new(k, KeyAction.Press));
                break;
            case State.Esc:
                if (b == '[') { _state = State.Csi; _seq.Clear(); }
                else if (b == 'O') _state = State.Ss3;
                else
                {
                    // Not a sequence: the Escape key itself, then this byte as typed.
                    _state = State.Ground;
                    output.Add(new(TermKey.Quit, KeyAction.Press));
                    Feed(b, output);
                }
                break;
            case State.Ss3:
                _state = State.Ground;
                if (Arrow((char)b) is { } a) output.Add(new(a, KeyAction.Press));
                break;
            case State.Csi:
                if (b >= 0x40 && b <= 0x7E) { _state = State.Ground; EndCsi((char)b, output); }
                else if (_seq.Length >= MaxSequence) _state = State.Ground;
                else _seq.Append((char)b);
                break;
        }
    }

    /// <summary>True while an Escape has arrived with nothing after it yet: the Escape key, or the start of a sequence.</summary>
    public bool Pending => _state == State.Esc;

    /// <summary>No byte followed an Escape for a while, so it was the Escape key and not the start of a sequence.</summary>
    public void Flush(List<TermKeyEvent> output)
    {
        if (_state != State.Esc) return;
        _state = State.Ground;
        output.Add(new(TermKey.Quit, KeyAction.Press));
    }

    private void EndCsi(char final, List<TermKeyEvent> output)
    {
        string s = _seq.ToString();
        if (s.Length > 0 && s[0] is '?' or '>' or '<' or '=') return; // a reply to a query, not a key
        string[] fields = s.Split(';');
        int[] keyField = ValueAndEventType(fields, 0), modField = ValueAndEventType(fields, 1);
        int mods = modField[0] > 0 ? modField[0] - 1 : 0;
        KeyAction action = modField[1] switch { EventRepeat => KeyAction.Repeat, EventRelease => KeyAction.Release, _ => KeyAction.Press };
        TermKey? key = final == 'u' ? FromCode(keyField[0]) : Arrow(final);
        if (final == 'u' && (mods & CtrlModBit) != 0)
        {
            // Ctrl+C arrives as a key here, not as a signal; any other Ctrl chord is not ours.
            if (keyField[0] == 'c') output.Add(new(TermKey.Quit, action));
            return;
        }
        if (key is { } k) output.Add(new(k, action));
    }

    /// <summary>Field <paramref name="i"/> split on ':' into [value, event type]; a missing value is 0 and a missing event type is a press (1).</summary>
    private static int[] ValueAndEventType(string[] fields, int i)
    {
        var r = new[] { 0, 1 };
        if (i >= fields.Length) return r;
        string[] parts = fields[i].Split(':');
        if (int.TryParse(parts[0], out int a)) r[0] = a;
        if (parts.Length > 1 && int.TryParse(parts[1], out int b)) r[1] = b;
        return r;
    }

    private static TermKey? Arrow(char c) => c switch
    {
        'A' => TermKey.Up,
        'B' => TermKey.Down,
        'C' => TermKey.Right,
        'D' => TermKey.Left,
        _ => null,
    };

    private static TermKey? FromCode(int code) => code is < 0 or > 0xFFFF ? null : char.ToLowerInvariant((char)code) switch
    {
        'w' => TermKey.Up,
        's' => TermKey.Down,
        'a' => TermKey.Left,
        'd' => TermKey.Right,
        (char)CodeSpace => TermKey.Fire,
        'f' => TermKey.AutoFire,
        'b' => TermKey.Bomb,
        'e' => TermKey.Like,
        'm' => TermKey.Mute,
        'q' or (char)CodeEscape => TermKey.Quit,
        _ => null,
    };

    /// <summary>True when the text holds a kitty reply (CSI ? flags u) to <see cref="KittyQuery"/>.</summary>
    public static bool IsKittyReply(string received)
    {
        const string Prefix = "\x1b[?";
        for (int i = received.IndexOf(Prefix, StringComparison.Ordinal); i >= 0; i = received.IndexOf(Prefix, i + 1, StringComparison.Ordinal))
        {
            int j = i + Prefix.Length;
            while (j < received.Length && char.IsAsciiDigit(received[j])) j++;
            if (j > i + Prefix.Length && j < received.Length && received[j] == 'u') return true;
        }
        return false;
    }
}

/// <summary>
/// Applies key events to the game input. With release events a key is down exactly while the terminal says so; without
/// them it is held by timer (see <see cref="KeyRepeatTiming"/>). One-shot keys (bomb, like, auto-fire) fire on a fresh
/// press only, not on the repeats of a held key, when releases are known. Mute is one-shot too; it does nothing without an action.
/// </summary>
public sealed class KeyRouter(InputState input, bool hasReleases, KeyRepeatTiming timing, Action? onMute = null)
{
    private readonly bool[] _down = new bool[Enum.GetValues<TermKey>().Length];

    /// <summary>Returns true when the player asked to quit.</summary>
    public bool Apply(TermKeyEvent e)
    {
        bool fresh = e.Action == KeyAction.Press && !(hasReleases && _down[(int)e.Key]);
        _down[(int)e.Key] = e.Action != KeyAction.Release;
        if (GameKeyOf(e.Key) is { } gk)
        {
            if (hasReleases) input.Key(gk, e.Action != KeyAction.Release);
            else if (e.Action != KeyAction.Release) input.Press(gk, timing);
            return false;
        }
        if (!fresh) return false;
        switch (e.Key)
        {
            case TermKey.AutoFire: input.ToggleAutoFire(); break;
            case TermKey.Bomb: input.Bomb(); break;
            case TermKey.Like: input.Like(); break;
            case TermKey.Mute: onMute?.Invoke(); break;
            case TermKey.Quit: return true;
        }
        return false;
    }

    /// <summary>The terminal lost focus: its key-ups will never arrive.</summary>
    public void ReleaseAll()
    {
        Array.Clear(_down);
        foreach (var gk in Enum.GetValues<GameKey>()) input.Key(gk, false);
    }

    private static GameKey? GameKeyOf(TermKey k) => k switch
    {
        TermKey.Up => GameKey.Up,
        TermKey.Down => GameKey.Down,
        TermKey.Left => GameKey.Left,
        TermKey.Right => GameKey.Right,
        TermKey.Fire => GameKey.Fire,
        _ => null,
    };
}
