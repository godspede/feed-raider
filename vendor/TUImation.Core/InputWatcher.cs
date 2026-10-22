using System.Diagnostics;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TUImation;

/// <summary>
/// Raw-stdin input for interactive sprites that react to the pointer: quit keys
/// (q / Esc) plus xterm SGR mouse reporting (mode 1003 "any motion" + 1006 SGR
/// coordinates). A drop-in richer cousin of <see cref="QuitWatcher"/> for
/// sprites that want the mouse.
///
/// Unix/pty only — the target is a Linux host viewed over SSH in a terminal that
/// speaks xterm mouse (e.g. Windows Terminal). It puts the tty in cbreak mode
/// via `stty` (byte-at-a-time input, no echo) but leaves OUTPUT post-processing
/// on, so the renderer's "\n" row separators still become CR+LF and the redraw
/// is undisturbed. No-ops if input is redirected or `stty` can't run (no real
/// tty), so --dump-ppm and piped runs are unaffected.
///
/// Input is read from fd 0 DIRECTLY via a <see cref="FileStream"/> rather than
/// <c>Console.OpenStandardInput()</c>. .NET's Unix console-input layer drives
/// stdin with a poll() loop that ignores the tty's VMIN/VTIME, so under our
/// cbreak settings (VMIN 0 / VTIME 1) a read with no pending bytes blocks
/// forever instead of timing out — the read loop never wakes, and mouse motion
/// never registers. A plain FileStream over fd 0 honours the cbreak timeout
/// (a byteless read returns 0), which is exactly what the loop below expects.
///
/// Start() enables mouse reporting; Stop() disables it and restores the tty.
/// Always call Stop() (the sprite's RestoreTerminal does) or the shell is left
/// in cbreak with mouse reporting still on.
///
/// The reported pointer position is exposed as <see cref="MouseCol"/> /
/// <see cref="MouseRow"/> in 1-based terminal cells (0 = no report seen yet).
/// </summary>
public static class InputWatcher
{
    public static volatile bool Quit;
    public static volatile int MouseCol; // 1-based terminal cell; 0 = no report yet
    public static volatile int MouseRow;

    private static string? _savedStty;
    private static volatile bool _started;

    public static void Start()
    {
        if (Console.IsInputRedirected) return;
        _savedStty = Stty("-g");                        // capture current tty settings
        if (string.IsNullOrEmpty(_savedStty)) return;   // not a real tty — stay a no-op
        Stty("-icanon -echo min 0 time 1");             // cbreak; 0.1s read timeout (VMIN 0 / VTIME 1)
        Console.Write("\x1b[?1003h\x1b[?1006h");         // any-motion + SGR extended mouse
        _started = true;
        new Thread(ReadLoop) { IsBackground = true }.Start();
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        Console.Write("\x1b[?1003l\x1b[?1006l");         // disable mouse reporting
        if (!string.IsNullOrEmpty(_savedStty)) Stty(_savedStty!); // restore original tty settings
    }

    private static void ReadLoop()
    {
        // fd 0 directly — NOT Console.OpenStandardInput(). See the class remarks:
        // the console layer's poll() ignores VMIN/VTIME and would block forever.
        var stdin = new FileStream(new SafeFileHandle((IntPtr)0, ownsHandle: false), FileAccess.Read, 1);
        var buf = new List<byte>(64);
        var tmp = new byte[128];
        try
        {
            while (!Quit)
            {
                int n = stdin.Read(tmp, 0, tmp.Length);
                if (n <= 0)
                {
                    // Read timed out (VTIME) with nothing new. A lone pending ESC
                    // that never became a CSI is a real Esc-to-quit keypress.
                    if (buf.Count == 1 && buf[0] == 0x1b) Quit = true;
                    continue;
                }
                for (int i = 0; i < n; i++) buf.Add(tmp[i]);
                Parse(buf);
            }
        }
        catch { /* tty closed */ }
    }

    // Consume complete tokens from the front of buf; leave any incomplete tail
    // (a half-arrived CSI) for the next read to finish.
    private static void Parse(List<byte> buf)
    {
        int i = 0;
        while (i < buf.Count)
        {
            byte b = buf[i];
            if (b == (byte)'q' || b == (byte)'Q') { Quit = true; i++; continue; }
            if (b == 0x1b)
            {
                if (i + 1 >= buf.Count) break;                                     // need more bytes
                if (buf[i + 1] != (byte)'[') { Quit = true; i += 2; continue; }    // ESC + other key
                int j = i + 2;
                while (j < buf.Count && (buf[j] < 0x40 || buf[j] > 0x7e)) j++;     // scan to CSI final byte
                if (j >= buf.Count) break;                                         // incomplete CSI; wait
                ParseCsi(buf, i + 2, j);
                i = j + 1;
                continue;
            }
            i++; // ignore anything else
        }
        if (i > 0) buf.RemoveRange(0, i);
    }

    // CSI body is buf[start..finalIdx); the final byte is buf[finalIdx].
    // SGR mouse reports look like: ESC [ < btn ; col ; row (M press/motion | m release).
    private static void ParseCsi(List<byte> buf, int start, int finalIdx)
    {
        if (start >= finalIdx || buf[start] != (byte)'<') return; // only care about SGR mouse
        var sb = new StringBuilder();
        for (int k = start + 1; k < finalIdx; k++) sb.Append((char)buf[k]);
        var parts = sb.ToString().Split(';');
        if (parts.Length == 3
            && int.TryParse(parts[1], out int col)
            && int.TryParse(parts[2], out int row))
        {
            MouseCol = col;
            MouseRow = row;
        }
    }

    // Runs `stty <args>` against the inherited tty (our stdin). For "-g" the
    // captured stdout is the settings string to restore later; for a set it's
    // empty. Returns null on failure (no tty / stty missing).
    private static string? Stty(string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("stty", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                // stdin is left inherited = the real tty, which stty reads and writes.
            });
            if (p is null) return null;
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return outp.Trim();
        }
        catch { return null; }
    }
}
