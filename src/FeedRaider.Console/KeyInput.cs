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
    private const string Kernel32 = "kernel32.dll";
    private const int StdInputHandle = -10;
    private const uint EnableProcessedInput = 0x1;
    private const ushort KeyEventType = 1, FocusEventType = 0x10;
    private const int SpiGetKeyboardDelay = 0x16, SpiGetKeyboardSpeed = 0x0A;
    private const int SpiNoParam = 0, SpiNoIniUpdate = 0;   // the keyboard queries take no parameter and write no setting
    private const int WinRecordBatch = 32;
    private const int StdinChunkBytes = 256;
    private static readonly TimeSpan KittyReplyWait = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan EscapeWait = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMilliseconds(100);    // how long the Unix loop waits for a byte when no escape sequence is half-read
    private static readonly TimeSpan ReplyPoll = TimeSpan.FromMilliseconds(20);    // how long the terminal-reply wait blocks on one byte

    private readonly TextWriter _out;
    private readonly nint _inputHandle;
    private uint _savedMode;
    private bool _winRecords, _kitty, _unixRaw, _disposed;
    private string? _savedStty;
    private readonly Lock _gate = new();

    private KeyInput(TextWriter output) { _out = output; _inputHandle = OperatingSystem.IsWindows() ? GetStdHandle(StdInputHandle) : 0; }

    /// <summary>True when key-ups are reported, so no hold timer is needed.</summary>
    public bool HasReleases => _winRecords || _kitty;
    public KeyRepeatTiming Timing { get; private set; } = KeyRepeatTiming.Default;

    public static KeyInput Open(TextWriter output)
    {
        var input = new KeyInput(output);
        if (Console.IsInputRedirected) return input;
        try
        {
            if (OperatingSystem.IsWindows()) input.OpenWindows();
            else input.OpenUnix();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Raw input is optional: if the console cannot be switched, the game falls back to the plain redirected-input loop.
            input.Restore();
        }
        return input;
    }

    /// <summary>Switches the console to key records. A console that refuses either mode call is left as found, and
    /// the game then reads presses through the redirected-input loop.</summary>
    private void OpenWindows()
    {
        if (!GetConsoleMode(_inputHandle, out _savedMode)) return;
        // Processed input only: no line editing, echo, mouse or resize records, and Ctrl+C still cancels.
        if (!SetConsoleMode(_inputHandle, EnableProcessedInput)) return;
        _winRecords = true;
        // The player's own repeat delay and rate only refine the holds; when Windows does not report them, the default timing serves.
        if (SystemParametersInfoW(SpiGetKeyboardDelay, SpiNoParam, out int delay, SpiNoIniUpdate) && SystemParametersInfoW(SpiGetKeyboardSpeed, SpiNoParam, out int speed, SpiNoIniUpdate))
            Timing = KeyRepeatTiming.FromWindows(delay, speed);
    }

    /// <summary>Switches the terminal to raw bytes and asks whether it speaks the kitty keyboard protocol. Without a working stty
    /// the terminal is left as found, and the game then reads presses through the redirected-input loop.</summary>
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
        var waited = Stopwatch.StartNew();
        // The device-attributes reply (ending in 'c') comes after any kitty reply, so it closes the wait.
        while (waited.Elapsed < KittyReplyWait && !reply.ToString().Contains('c'))
        {
            if (_bytes.TryTake(out byte next, ReplyPoll)) reply.Append((char)next);
        }
        _kitty = TerminalKeyParser.IsKittyReply(reply.ToString());
        if (_kitty) { _out.Write(TerminalKeyParser.KittyPush); _out.Flush(); }
    }

    private Thread? _reader;
    private readonly System.Collections.Concurrent.BlockingCollection<byte> _bytes = new();

    private void ReadStdin()
    {
        using var stdin = Console.OpenStandardInput();
        var chunk = new byte[StdinChunkBytes];
        try
        {
            int read;
            while ((read = stdin.Read(chunk, 0, chunk.Length)) > 0) for (int i = 0; i < read; i++) _bytes.Add(chunk[i]);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }   // stdin closed or the reader was disposed: no more keys, and the game's own loop ends the run
    }

    /// <summary>Blocks reading keys until the token is cancelled or the player quits; key events are applied under the game's lock.</summary>
    public void Loop(RaiderGame game, CancellationTokenSource cts, Action? onMute, Action<int> onFeedSpeed)
    {
        var router = new KeyRouter(game.Input, HasReleases, Timing, onMute, onFeedSpeed);
        if (_winRecords) WindowsLoop(game, router, cts);
        else if (_unixRaw) UnixLoop(game, router, cts);
        else RedirectedLoop(game, router, cts);
    }

    /// <summary>Applies one key event under the game's lock and, when the event quits, cancels the run and returns true.</summary>
    private static bool ApplyAndCancelOnQuit(RaiderGame game, KeyRouter router, CancellationTokenSource cts, TermKeyEvent keyEvent)
    {
        bool quit;
        lock (game) quit = router.Apply(keyEvent);
        if (quit) cts.Cancel();
        return quit;
    }

    private void WindowsLoop(RaiderGame game, KeyRouter router, CancellationTokenSource cts)
    {
        var records = new InputRecord[WinRecordBatch];
        while (!cts.IsCancellationRequested)
        {
            // A failed read means the console handle is gone (closed or detached); there is no input left to wait for.
            if (!ReadConsoleInputW(_inputHandle, records, (uint)records.Length, out uint read)) return;
            for (int i = 0; i < read; i++)
            {
                if (records[i].EventType == FocusEventType && records[i].KeyDownOrFocus == 0) lock (game) router.ReleaseAll();
                else if (records[i].EventType == KeyEventType && Win32Keys.Map(records[i].VirtualKey, records[i].KeyDownOrFocus != 0) is { } keyEvent)
                    if (ApplyAndCancelOnQuit(game, router, cts, keyEvent)) return;
            }
        }
    }

    private void UnixLoop(RaiderGame game, KeyRouter router, CancellationTokenSource cts)
    {
        var parser = new TerminalKeyParser();
        var events = new List<TermKeyEvent>();
        while (!cts.IsCancellationRequested)
        {
            if (_bytes.TryTake(out byte next, parser.Pending ? EscapeWait : IdlePoll)) parser.Feed(next, events);
            else parser.Flush(events);
            foreach (var keyEvent in events) if (ApplyAndCancelOnQuit(game, router, cts, keyEvent)) return;
            events.Clear();
        }
    }

    // With no console to read raw (input piped), ReadKey is all there is, and it reports presses only.
    private static void RedirectedLoop(RaiderGame game, KeyRouter router, CancellationTokenSource cts)
    {
        while (!cts.IsCancellationRequested)
        {
            var pressed = Console.ReadKey(true);
            TermKey? mapped = pressed.Key switch
            {
                ConsoleKey.W or ConsoleKey.UpArrow => TermKey.Up,
                ConsoleKey.S or ConsoleKey.DownArrow => TermKey.Down,
                ConsoleKey.A or ConsoleKey.LeftArrow => TermKey.Left,
                ConsoleKey.D or ConsoleKey.RightArrow => TermKey.Right,
                ConsoleKey.Spacebar => TermKey.Fire,
                ConsoleKey.F => TermKey.AutoFire,
                ConsoleKey.B => TermKey.Bomb,
                ConsoleKey.E => TermKey.Like,
                ConsoleKey.R => TermKey.Repost,
                ConsoleKey.Oem4 => TermKey.MarkUp,
                ConsoleKey.Oem6 => TermKey.MarkDown,
                ConsoleKey.M => TermKey.Mute,
                ConsoleKey.OemMinus or ConsoleKey.Subtract => TermKey.SlowerFeed,
                ConsoleKey.OemPlus or ConsoleKey.Add => TermKey.FasterFeed,
                ConsoleKey.Q or ConsoleKey.Escape => TermKey.Quit,
                _ => null,
            };
            if (mapped is { } key && ApplyAndCancelOnQuit(game, router, cts, new(key, KeyAction.Press))) return;
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
            if (_winRecords) { _ = SetConsoleMode(_inputHandle, _savedMode); _winRecords = false; }   // a refusal here means the console is already gone
            if (_unixRaw && _savedStty is not null) { Stty(_savedStty); _unixRaw = false; }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or DllNotFoundException) { }   // restoring a terminal that is already gone: nothing is left to undo
    }

    private static string? Stty(string args)
    {
        try
        {
            // stty reads and sets the terminal on its stdin, so stdin stays the inherited terminal.
            var psi = new ProcessStartInfo("stty", args) { RedirectStandardOutput = true, UseShellExecute = false };
            using var process = Process.Start(psi);
            if (process is null) return null;
            string printed = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? printed : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }   // no stty on PATH: the caller treats null as raw mode being unavailable
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        // KEY_EVENT_RECORD.bKeyDown and FOCUS_EVENT_RECORD.bSetFocus share this offset.
        [FieldOffset(4)] public int KeyDownOrFocus;
        [FieldOffset(10)] public ushort VirtualKey;
    }

    [DllImport(Kernel32)] private static extern nint GetStdHandle(int which);
    [DllImport(Kernel32)] private static extern bool GetConsoleMode(nint handle, out uint mode);
    [DllImport(Kernel32)] private static extern bool SetConsoleMode(nint handle, uint mode);
    [DllImport(Kernel32)] private static extern bool ReadConsoleInputW(nint handle, [Out] InputRecord[] buffer, uint length, out uint read);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfoW(int action, int param, out int value, int winIni);
}
