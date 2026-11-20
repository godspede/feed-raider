using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests;

// One hit point and one shield: is that fair? Two pilots fly stages 1 to 3 at the default feed speed, signed out, on a phone-sized
// field and on a desktop one, over a set of seeds. One rests on the heart of about one post in three; the other never looks at
// the feed. The numbers they come back with are what the tuning in Tuning.cs is judged by, and the test prints them.
public class FairnessTests(ITestOutputHelper output)
{
    /// <summary>A pilot of middling skill. It looks at the field every <c>Reaction</c> frames (a third of a second), misses one
    /// look in four (<c>Lapse</c>) and keeps its keys until the next; it sees <c>Horizon</c> frames ahead (0.4 s) and knows bullets
    /// and hulls, not beams or marked ground. It fires all the time, collects pickups, and bombs when it is about to lose its ship
    /// with no way out. The one that likes goes for the heart of every third post once the post is well down the field.</summary>
    internal sealed class Pilot(bool holds, ulong seed)
    {
        private const int Reaction = 10, Horizon = 12;
        private const double Lapse = 0.25;
        private const double HitHalfWidth = 1.8, HitHalfHeight = 2.2;   // the ship's real hit box
        private const double HighestY = 10;   // the pilot keeps its ship below this row
        private const double EnemyMarginX = 1, EnemyMarginY = 2;   // the pilot's margin around an enemy's hit box, wider in y because enemies drift down
        private const double EdgeMargin = 1;   // the pilot keeps its ship this far inside the field's sides and bottom
        private const double Deadband = 1;   // within this distance of the goal the pilot stops steering that way
        private const double CautionMargin = 1.0;   // how much wider than the real hit box the pilot thinks its ship is
        private const int RememberedCap = 400;   // the remembered enemies are dropped past this many, so gone ones do not pile up
        private const double BottomMargin = 6;   // a heart this close to the bottom edge is not worth the trip
        private const double PickupTopY = 12;   // a pickup above this row is still too far up the field to chase
        private const double LowEnemyGap = 8;   // the enemy to sit under is at least this far above the ship
        private const double HoldBelow = 0.45;   // it goes for a heart only once the post is this far down the field, clear of where enemies come in
        private const int LikeEvery = 3;   // the pilot who likes goes for one post in this many
        private const ulong TickHashStep = 0x9E3779B97F4A7C15UL;   // 2^64 over the golden ratio: spreads consecutive ticks over the whole hash input
        private static readonly double Diagonal = Math.Sqrt(2);   // a diagonal step is this much longer, so its speed is divided by it

        private readonly Dictionary<Enemy, (double X, double Y)> _lastPositions = [];
        private readonly HashSet<Card> _wanted = [];
        private int _cards, _posts, _dx, _dy;
        private bool _hooked;

        public int Likes { get; private set; }

        public void Drive(RaiderGame g)
        {
            if (!_hooked)
            {
                _hooked = true;
                g.Cards.Entered += c => { if (holds && _cards++ % LikeEvery == 0) _wanted.Add(c); };
                g.Cards.Left += c => _wanted.Remove(c);
            }
            while (g.Posts.Count < Tuning.PostQueueLow) g.Posts.Offer(Post(++_posts));
            var inp = g.Input;
            inp.Key(GameKey.Fire, true);
            if (g.Alive && g.Tick % Reaction == 0 && Hashing.Unit(Hashing.Mix(seed ^ (ulong)g.Tick * TickHashStep), 0) >= Lapse) Decide(g);
            inp.Key(GameKey.Left, _dx < 0); inp.Key(GameKey.Right, _dx > 0);
            inp.Key(GameKey.Up, _dy < 0); inp.Key(GameKey.Down, _dy > 0);
            foreach (var e in g.Enemies) _lastPositions[e] = (e.X, e.Y);
            if (_lastPositions.Count > RememberedCap) _lastPositions.Clear();
        }

        private void Decide(RaiderGame g)
        {
            var (tx, ty) = GoalAndCountLikes(g);
            int bestDx = 0, bestDy = 0, bestHit = -1;
            double bestGap = double.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int hit = FirstHit(g, dx, dy);
                    double n = dx != 0 && dy != 0 ? Diagonal : 1;
                    double x = g.PX + dx / n * Tuning.ShipSpeed * Reaction, y = g.PY + dy / n * Tuning.ShipSpeed * Reaction;
                    double gap = Math.Abs(x - tx) + Math.Abs(y - ty);
                    if (hit > bestHit || hit == bestHit && gap < bestGap) { bestHit = hit; bestGap = gap; bestDx = dx; bestDy = dy; }
                }
            _dx = bestDx; _dy = bestDy;
            if (Math.Abs(g.PX - tx) <= Deadband && _dx != 0 && bestHit > Horizon && FirstHit(g, 0, _dy) > Horizon) _dx = 0;
            if (Math.Abs(g.PY - ty) <= Deadband && _dy != 0 && bestHit > Horizon && FirstHit(g, _dx, 0) > Horizon) _dy = 0;
            if (bestHit <= Reaction && g.Shield <= 0 && g.Invuln == 0 && g.Bombs > 0) g.Input.Bomb();
        }

        /// <summary>Where the pilot would like to be: on the heart it is after, on a pickup, or low down under something to shoot.</summary>
        private (double X, double Y) GoalAndCountLikes(RaiderGame g)
        {
            double home = g.PH - Tuning.ShipStartAbove;
            foreach (var c in g.Cards.Cards)
            {
                if (!_wanted.Contains(c)) continue;
                if (c.Marks.Like != ButtonState.Off) { _wanted.Remove(c); Likes++; break; }
                var button = g.Cards.ButtonBox(c, PostAction.Like);
                if (g.CanHold(c, PostAction.Like) && button.Y >= g.PH * HoldBelow && button.Y + button.H < g.PH - BottomMargin)
                    return (button.X + button.W / 2.0, button.Y + button.H / 2.0);
            }
            foreach (var p in g.Pickups)
                if (p.Y > PickupTopY && p.Y < g.PH) return (p.X, p.Y);
            if (g.Boss is { Leaving: false } boss)
            {
                double x = boss.X;
                if (boss.Parts?.Find(p => p.State == PartState.Open) is { } open) x += open.OX;
                return (x, home);
            }
            Enemy? low = null;
            foreach (var e in g.Enemies)
                if (!e.Leaving && e.Kind != EnemyKind.Mine && e.Y > 0 && e.Y < g.PY - LowEnemyGap && (low is null || e.Y > low.Y)) low = e;
            return (low?.X ?? g.PW / 2.0, home);
        }

        /// <summary>The first frame, counted from now, on which the ship would be hit if it kept going this way; past the horizon
        /// when nothing the pilot knows of would hit it.</summary>
        private int FirstHit(RaiderGame g, int dx, int dy)
        {
            double n = dx != 0 && dy != 0 ? Diagonal : 1;
            double x = g.PX, y = g.PY;
            double loX = g.Ship.W / 2.0 + EdgeMargin, hiX = g.PW - g.Ship.W / 2.0 - EdgeMargin, hiY = g.PH - g.Ship.H / 2.0 - EdgeMargin;
            for (int t = 1; t <= Horizon; t++)
            {
                x = Math.Clamp(x + dx / n * Tuning.ShipSpeed, loX, hiX);
                y = Math.Clamp(y + dy / n * Tuning.ShipSpeed, HighestY, hiY);
                foreach (var b in g.Bullets)
                    if (!b.Friendly && Math.Abs(b.X + b.VX * t - x) <= HitHalfWidth + CautionMargin && Math.Abs(b.Y + b.VY * t - y) <= HitHalfHeight + CautionMargin) return t;
                foreach (var e in g.Enemies)
                {
                    if (e.Hp <= 0 || e.Untouchable) continue;
                    var (vx, vy) = _lastPositions.TryGetValue(e, out var w) ? (e.X - w.X, e.Y - w.Y) : (0, 0);
                    double ex = e.X + vx * t, ey = e.Y + vy * t;
                    if (e.Parts is null)
                    {
                        if (Math.Abs(ex - x) <= e.Sprite.W / 2.0 + EnemyMarginX + CautionMargin && Math.Abs(ey - y) <= e.Sprite.H / 2.0 + EnemyMarginY + CautionMargin) return t;
                    }
                    else
                    {
                        foreach (var p in e.Parts)
                            if (p.State != PartState.Dead && Math.Abs(x - (ex + p.OX)) <= p.HW + EnemyMarginX + CautionMargin && Math.Abs(y - (ey + p.OY)) <= p.HH + EnemyMarginY + CautionMargin) return t;
                    }
                }
            }
            return Horizon + 1;
        }

        private static readonly string[] Words = ["morning", "coffee", "the", "harbour", "again", "and", "nobody", "asked", "but", "here", "is", "my", "garden", "today"];
        private const int MinWords = 4, WordSpread = 40;   // a post has 4 to 43 words
        private const int PicturedEvery = 3, MaxPictures = 4;
        private const int MaxLikeCount = 300, Reposts = 3, Replies = 1;
        // The picture count and the like count divide the hash first, so they read higher bits than the word count does.
        private const ulong PictureHashDivisor = 64, LikeCountHashDivisor = 4096;

        /// <summary>The feed: short and long posts, every third with one to four pictures, so the cards are of every height.</summary>
        private static FeedPost Post(int n)
        {
            ulong h = Hashing.Mix((ulong)n);
            int words = MinWords + (int)(h % WordSpread);
            var text = string.Join(' ', Enumerable.Range(0, words).Select(i => Words[(int)(Hashing.Mix(h + (ulong)i) % (ulong)Words.Length)]));
            string[] names = [.. BuiltinPictures.Names];
            PostPicture[]? pictures = n % PicturedEvery == 0 ? [.. Enumerable.Range(0, 1 + (int)(h / PictureHashDivisor % MaxPictures)).Select(i => BuiltinPictures.Picture(names[(n + i) % names.Length]))] : null;
            return new($"at://did:plc:f{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:f{n}", $"f{n}.test", text, (int)(h / LikeCountHashDivisor % MaxLikeCount), Reposts, Replies, null, null,
                DisplayName: $"Flyer {n}", HasEmbed: pictures is not null, Attachment: pictures is not null ? AttachmentKind.Picture : AttachmentKind.None, Pictures: pictures);
        }
    }

    internal const int Stages = 3;
    /// <summary>How much more than the pilot who likes the pilot who ignores the feed loses, at the least: part drops feed both pilots,
    /// so the gap is narrow on the phone field.</summary>
    private const double LonerShare = 1.1;
    private const int MaxMinutes = 20;   // a run still short of the third boss after this long is stuck
    private const int MaxFrames = MaxMinutes * 60 * Tuning.Fps;
    private const double FewestShipsAStage = 0.6, MostShipsAStage = 3.0;   // what the pilot who likes loses, on the mean
    private const int FewestLikesARun = 6;

    /// <summary>One run from a fresh start to the end of stage 3 (its boss down, or gone): ships lost in each stage, its boss fight
    /// counted in, and the likes the pilot landed.</summary>
    internal static (int[] Lost, int Likes, bool Finished) Fly(ulong seed, bool holds, int cols, int rows)
    {
        var g = new RaiderGame(seed) { SignedIn = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.FeedSpeed = FeedSpeed.Default;
        var pilot = new Pilot(holds, seed);
        int[] lost = new int[Stages];
        int deaths = 0;
        for (int f = 0; f < MaxFrames && g.BossesBeaten < Stages; f++)
        {
            pilot.Drive(g);
            g.Step();
            if (g.Deaths > deaths) { lost[Math.Min(Stages - 1, g.BossesBeaten)] += g.Deaths - deaths; deaths = g.Deaths; }
        }
        return (lost, pilot.Likes, g.BossesBeaten >= Stages);
    }

    internal const int Seeds = 32;

    /// <summary>Mean ships lost in each stage over the seeds, and mean likes in a run.</summary>
    internal static (double[] Lost, double Likes, int Finished) Mean(bool holds, int cols, int rows)
    {
        double[] lost = new double[Stages];
        double likes = 0;
        int finished = 0;
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            var run = Fly(seed, holds, cols, rows);
            for (int s = 0; s < Stages; s++) lost[s] += run.Lost[s] / (double)Seeds;
            likes += run.Likes / (double)Seeds;
            if (run.Finished) finished++;
        }
        return (lost, likes, finished);
    }

    // Measured with 72 seeds, with the numbers as they are in Tuning.cs (ships lost in stage 1, 2, 3):
    //   phone 40x70     likes one in three 0.56 0.59 1.03    ignores the feed 0.81 0.84 0.78
    //   desktop 130x46  likes one in three 2.12 1.47 1.91    ignores the feed 1.53 2.22 4.09
    // A sharper pilot (a look every 8 frames, one in seven missed) loses a quarter of a ship a stage, a duller one (every 12
    // frames, three in ten missed) two to five, so the pilot's skill moves these far more than any number in Tuning.cs does. What
    // the test holds is the shape: about one to two ships a stage for the pilot who likes, and clearly more for the one who does not.
    [Theory]
    [InlineData("phone", 40, 70)]
    [InlineData("desktop", 130, 46)]
    public void The_pilot_who_likes_one_post_in_three_loses_one_to_three_ships_a_stage_and_the_one_who_ignores_the_feed_loses_clearly_more(string field, int cols, int rows)
    {
        var liker = Mean(true, cols, rows);
        var loner = Mean(false, cols, rows);
        output.WriteLine($"{field} {cols}x{rows}, {Seeds} seeds, ships lost in stages 1 to 3");
        output.WriteLine($"  likes one in three: {string.Join("  ", liker.Lost.Select(v => v.ToString("F2")))}   ({liker.Likes:F1} likes a run, {liker.Finished} runs finished)");
        output.WriteLine($"  ignores the feed:   {string.Join("  ", loner.Lost.Select(v => v.ToString("F2")))}   ({loner.Likes:F1} likes a run, {loner.Finished} runs finished)");
        Assert.Equal(Seeds, liker.Finished);
        Assert.Equal(Seeds, loner.Finished);
        double likerAll = liker.Lost.Sum() / Stages, lonerAll = loner.Lost.Sum() / Stages;
        Assert.InRange(likerAll, FewestShipsAStage, MostShipsAStage);
        Assert.True(lonerAll >= likerAll * LonerShare, $"{field}: ignoring the feed costs {lonerAll:F2} ships a stage against {likerAll:F2}");
        Assert.True(liker.Likes >= FewestLikesARun, $"{field}: the pilot landed {liker.Likes:F1} likes a run");
    }
}
