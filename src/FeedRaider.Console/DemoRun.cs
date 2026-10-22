using System.Diagnostics;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

/// <summary>The headless --frames mode: a fixed-seed scripted session (darkness events, wingmen, an autopilot that likes) so
/// the PNG frames and the timing are repeatable without a network.</summary>
static class DemoRun
{
    const string Handle = "demo.bsky.social";

    static FeedPost Post(int n, string text, int likes) =>
        new($"at://did:plc:demo{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:demo{n}", $"user{n}.bsky.social", text, likes, 3, 1, null, null);

    public static int Run(int frames, string outDir, int cols, int rows, int every)
    {
        var game = new RaiderGame(42);
        var view = new RaiderView();
        var scr = new Screen(cols, rows);
        var enc = new DiffEncoder();
        int likes = 0;
        game.LoggedIn = true; game.OnLike = _ => likes++;
        var script = Script(game);
        var sw = new Stopwatch();
        long bytes = 0;
        for (int f = 0; f < frames; f++)
        {
            foreach (var (at, act) in script) if (at == f) act();
            Autopilot(game);
            game.Step();
            sw.Start();
            view.Render(game, scr);
            bytes += enc.Encode(scr).Length;
            sw.Stop();
            if (f % every == 0) FrameImage.Write(scr, Path.Combine(outDir, $"frame{f:D4}.png"));
        }
        Console.Error.WriteLine($"{frames} frames, {sw.Elapsed.TotalMilliseconds / frames:F2} ms/frame render+encode, {bytes / frames} bytes/frame, score {game.Score}, likes {likes}");
        return 0;
    }


    /// <summary>The scripted posts and darkness events, by frame number.</summary>
    public static (int At, Action Do)[] Script(RaiderGame game) =>
        new (int At, Action Do)[]
        {
            (20, () => game.OfferPost(Post(1, "just shipped the thing we have been working on for months, tell me what you think", 240))),
            (60, () => game.OfferDark(new DarkPost(DarknessKind.Spam, 11, "buy followers cheap click my profile now"))),
            (200, () => game.OfferDark(new DarkPost(DarknessKind.Rude, 12, "you are all idiots and nobody likes your takes"))),
            (260, () => game.OfferPost(Post(2, "unpopular opinion: the best part of any feed is the quiet posts nobody reshared", 1800))),
            (420, () => game.OfferDark(new DarkPost(DarknessKind.Spam, 13, "free crypto giveaway dm me"))),
            (520, () => game.OfferPost(Post(3, "good morning everyone, coffee first and then the rest of the world", 35))),
        };
    // Fly at the nearest wingman, like when prompted, otherwise sweep and fire.
    public static void Autopilot(RaiderGame g)
    {
        var inp = g.Input;
        inp.Key(GameKey.Fire, true);
        double tx = g.PW / 2.0 + Math.Sin(g.Frame / 40.0) * g.PW * 0.3, ty = g.PH - 14;
        var w = g.Wings.FirstOrDefault(x => !x.Liked && x.Phase == WingPhase.Fight);
        if (w is not null) { tx = w.X; ty = w.Y + 8; }
        inp.Key(GameKey.Left, g.PX > tx + 1.5); inp.Key(GameKey.Right, g.PX < tx - 1.5);
        inp.Key(GameKey.Up, g.PY > ty + 1.5); inp.Key(GameKey.Down, g.PY < ty - 1.5);
        if (g.Prompt is not null && g.Frame % 20 == 0) inp.Like();
    }
}
