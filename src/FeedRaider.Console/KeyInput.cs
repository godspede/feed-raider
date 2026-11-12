using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FeedRaider.Engine.Raider;

/// <summary>
/// Reads the keyboard for the terminal game and feeds a <see cref="KeyRouter"/>. It takes the best source the terminal has:
/// Win32 console records (real key-ups) on Windows, the kitty keyboard protocol elsewhere, and otherwise presses only
/// with timed holds. Everything it changes about the terminal is undone by <see cref="Dispose"/>.
/// </summary>
sealed class KeyInput : IDisposable
{
    private const int StdInputHandle = -10;
    private const uint EnableProcessedInput = 0x1;
    private const ushort KeyEventType = 1, FocusEventType = 0x10;
    private const int Spi_GetKeyboardDelay = 0x16, Spi_GetKeyboardSpeed = 0x0A;
    private const int WinRecordBatch = 32;
    private static readonly TimeSpan KittyReplyWait = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan EscapeWait = TimeSpan.FromMilliseconds(40);
    private const int IdlePollMs = 100;       // how long the Unix loop waits for a byte when no escape sequence is half-read
    private const int ReplyPollMs = 20;       // how long the terminal-reply wait blocks on one byte

    private readonly TextWriter _out;
    private readonly nint _hIn;
    private uint _savedMode;
    private bool _winRecords, _kitty, _unixRaw, _disposed;
    private string? _savedStty;
    private readonly object _gate = new();

    private KeyInput(TextWriter output) { _out = output; _hIn = OperatingSystem.IsWindows() ? GetStdHandle(StdInputHandle) : 0; }

    /// <summary>True when key-ups are reported, so no hold timer is needed.</summary>
    public bool HasReleases => _winRecords || _kitty;
    public KeyRepeatTiming Timing { get; private set; } = KeyRepeatTiming.Default;

    public static KeyInput Open(TextWriter output)
    {
        var k = new KeyInput(output);
        if (Console.IsInputRedirected) return k;
        try
        {
            if (OperatingSystem.IsWindows()) k.OpenWindows();
            else k.OpenUnix();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            k.Restore();
        }
        return k;
    }

    private void OpenWindows()
    {
        if (!GetConsoleMode(_hIn, out _savedMode)) return;
        // Processed input only: no line editing, echo, mouse or resize records, and Ctrl+C still cancels.
        if (!SetConsoleMode(_hIn, EnableProcessedInput)) return;
        _winRecords = true;
        if (SystemParametersInfoW(Spi_GetKeyboardDelay, 0, out int delay, 0) && SystemParametersInfoW(Spi_GetKeyboardSpeed, 0, out int speed, 0))
            Timing = KeyRepeatTiming.FromWindows(delay, speed);
    }

    private void OpenUnix()
    {
        _savedStty = Stty("-g")?.Trim();
        if (string.IsNullOrEmpty(_savedStty)) return;
        // Raw bytes, but signals stay on so Ctrl+C still cancels.
        if (Stty("-icanon -echo min 1") is null) return;
        _unixRaw = true;
        _reader = new Thread(ReadStdin) { IsBackground = true };
        _reader.Start();
        _out.Write(TerminalKeyParser.KittyQuery + TerminalKeyParser.DeviceAttributes); _out.Flush();
        var reply = new StringBuilder();
        var deadline = DateTime.UtcNow + KittyReplyWait;
        // The device-attributes reply (ending in 'c') comes after any kitty reply, so it closes the wait.
        while (DateTime.UtcNow < deadline && !reply.ToString().Contains('c'))
        {
            if (_bytes.TryTake(out byte b, ReplyPollMs)) reply.Append((char)b);
        }
        _kitty = TerminalKeyParser.IsKittyReply(reply.ToString());
        if (_kitty) { _out.Write(TerminalKeyParser.KittyPush); _out.Flush(); }
    }

    private Thread? _reader;
    private readonly System.Collections.Concurrent.BlockingCollection<byte> _bytes = new();

    private void ReadStdin()
    {
        using var stdin = Console.OpenStandardInput();
        var buf = new byte[256];
        try
        {
            int n;
            while ((n = stdin.Read(buf, 0, buf.Length)) > 0) for (int i = 0; i < n; i++) _bytes.Add(buf[i]);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }   // stdin closed or the reader was disposed: no more keys, and the game's own loop ends the run
    }

    /// <summary>Blocks reading keys until the token is cancelled or the player quits; key events are applied under the game's lock.</summary>
    public void Loop(RaiderGame game, CancellationTokenSource cts, Action? onMute = null)
    {
        var router = new KeyRouter(game.Input, HasReleases, Timing, onMute);
        if (_winRecords) WindowsLoop(game, router, cts);
        else if (_unixRaw) UnixLoop(game, router, cts);
        else RedirectedLoop(game, router, cts);
    }

    private static bool Dispatch(RaiderGame game, KeyRouter router, CancellationTokenSource cts, TermKeyEvent e)
    {
        bool quit;
        lock (game) quit = router.Apply(e);
        if (quit) cts.Cancel();
        return quit;
    }

    private void WindowsLoop(RaiderGame game, KeyRouter router, CancellationTokenSource cts)
    {
        var recs = new InputRecord[WinRecordBatch];
        while (!cts.IsCancellationRequested)
        {
            if (!ReadConsoleInputW(_hIn, recs, (uint)recs.Length, out uint n)) return;
            for (int i = 0; i < n; i++)
            {
                if (recs[i].EventType == FocusEventType && recs[i].KeyDownOrFocus == 0) lock (game) router.ReleaseAll();
                else if (recs[i].EventType == KeyEventType && Win32Keys.Map(recs[i].VirtualKey, recs[i].KeyDownOrFocus != 0) is { } e)
                    if (Dispatch(game, router, cts, e)) return;
            }
        }
    }

    private void UnixLoop(RaiderGame game, KeyRouter router, CancellationTokenSource cts)
    {
        var parser = new TerminalKeyParser();
        var events = new List<TermKeyEvent>();
        while (!cts.IsCancellationRequested)
        {
            if (_bytes.TryTake(out byte b, parser.Pending ? (int)EscapeWait.TotalMilliseconds : IdlePollMs)) parser.Feed(b, events);
            else parser.Flush(events);
            foreach (var e in events) if (Dispatch(game, router, cts, e)) return;
            events.Clear();
        }
    }

    // With no console to read raw (input piped), ReadKey is all there is, and it reports presses only.
    private static void RedirectedLoop(RaiderGame game, KeyRouter router, CancellationTokenSource cts)
    {
        while (!cts.IsCancellationRequested)
        {
            var k = Console.ReadKey(true);
            TermKey? tk = k.Key switch
            {
                ConsoleKey.W or ConsoleKey.UpArrow => TermKey.Up,
                ConsoleKey.S or ConsoleKey.DownArrow => TermKey.Down,
                ConsoleKey.A or ConsoleKey.LeftArrow => TermKey.Left,
                ConsoleKey.D or ConsoleKey.RightArrow => TermKey.Right,
                ConsoleKey.Spacebar => TermKey.Fire,
                ConsoleKey.F => TermKey.AutoFire,
                ConsoleKey.B => TermKey.Bomb,
                ConsoleKey.E => TermKey.Like,
                ConsoleKey.M => TermKey.Mute,
                ConsoleKey.Q or ConsoleKey.Escape => TermKey.Quit,
                _ => null,
            };
            if (tk is { } key && Dispatch(game, router, cts, new(key, KeyAction.Press))) return;
        }
    }

    /// <summary>Puts the terminal back as found. Safe to call twice, and from a process-exit handler after a crash.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Restore();
        }
    }

    private void Restore()
    {
        try
        {
            if (_kitty) { _out.Write(TerminalKeyParser.KittyPop); _out.Flush(); _kitty = false; }
            if (_winRecords) { SetConsoleMode(_hIn, _savedMode); _winRecords = false; }
            if (_unixRaw && _savedStty is not null) { Stty(_savedStty); _unixRaw = false; }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or DllNotFoundException) { }   // restoring a terminal that is already gone: nothing is left to undo
    }

    private static string? Stty(string args)
    {
        try
        {
            // Inherits the terminal as stdin; only stdout is captured.
            var psi = new ProcessStartInfo("stty", args) { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p is null) return null;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? o : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        // KEY_EVENT_RECORD.bKeyDown and FOCUS_EVENT_RECORD.bSetFocus share this offset.
        [FieldOffset(4)] public int KeyDownOrFocus;
        [FieldOffset(10)] public ushort VirtualKey;
    }

    [DllImport("kernel32.dll")] private static extern nint GetStdHandle(int which);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(nint h, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(nint h, uint mode);
    [DllImport("kernel32.dll")] private static extern bool ReadConsoleInputW(nint h, [Out] InputRecord[] buffer, uint length, out uint read);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfoW(int action, int param, out int value, int winIni);
}
