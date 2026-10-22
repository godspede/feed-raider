using System.Diagnostics;
using System.Text;
using FeedRaider.Engine.Raider;

/// <summary>The headless --bench mode: the DemoRun session (fixed seed, demo posts, autopilot) timed per frame with no browser.
/// The timed part is what the web host does each tick bar the sim step: <c>View.Render</c> then <c>DiffEncoder.Encode</c>. Reports
/// mean and p95 ms, ANSI size per frame (UTF-16 chars, which is what xterm.js is handed, and UTF-8 bytes) and bytes allocated
/// on this thread. The first <c>Warmup</c> frames are not counted (JIT, the full first frame).</summary>
static class Bench
{
    const int Warmup = 60;

    public static int Run(int frames, int cols, int rows)
    {
        var game = new RaiderGame(42);
        var view = new RaiderView();
        var scr = new Screen(cols, rows);
        var enc = new DiffEncoder();
        game.LoggedIn = true; game.OnLike = _ => { };
        var script = DemoRun.Script(game);
        var render = new List<double>(frames);
        var encode = new List<double>(frames);
        long chars = 0, utf8 = 0, alloc = 0, changed = 0;
        double step = 0;
        for (int f = 0; f < Warmup + frames; f++)
        {
            foreach (var (at, act) in script) if (at == f % 600) act();
            DemoRun.Autopilot(game);
            long t0 = Stopwatch.GetTimestamp();
            game.Step();
            long t1 = Stopwatch.GetTimestamp();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            view.Render(game, scr);
            long t2 = Stopwatch.GetTimestamp();
            string s = enc.Encode(scr);
            long t3 = Stopwatch.GetTimestamp();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            if (f < Warmup) continue;
            step += Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
            render.Add(Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds);
            encode.Add(Stopwatch.GetElapsedTime(t2, t3).TotalMilliseconds);
            alloc += a1 - a0;
            chars += s.Length;
            utf8 += Encoding.UTF8.GetByteCount(s);
            changed += enc.LastChanged;
        }
        var total = render.Zip(encode, (a, b) => a + b).ToList();
        double p95 = Pct(total, 0.95);
        Console.WriteLine($"bench {cols}x{rows}, {frames} frames (+{Warmup} warmup), seed 42");
        Console.WriteLine($"  step          mean {step / frames:F3} ms");
        Console.WriteLine($"  render        mean {render.Average():F3} ms   p95 {Pct(render, 0.95):F3} ms");
        Console.WriteLine($"  encode        mean {encode.Average():F3} ms   p95 {Pct(encode, 0.95):F3} ms");
        Console.WriteLine($"  render+encode mean {total.Average():F3} ms   p95 {p95:F3} ms");
        Console.WriteLine($"  ansi/frame    {chars / frames} chars, {utf8 / frames} utf8 bytes, {changed / frames} cells of {cols * rows}");
        Console.WriteLine($"  alloc/frame   {alloc / frames} bytes (render+encode)");
        return 0;
    }

    static double Pct(List<double> v, double p)
    {
        var s = v.OrderBy(x => x).ToList();
        return s[Math.Min(s.Count - 1, (int)(s.Count * p))];
    }
}
