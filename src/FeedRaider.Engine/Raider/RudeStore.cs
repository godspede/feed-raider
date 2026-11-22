namespace FeedRaider.Engine.Raider;

/// <summary>A rude post kept to be spoken: cleaned text, the label it came with, and the frame it arrived on.</summary>
public record RudePost(ulong Seed, string Text, DarknessSource Source, string? Label, long Arrived);

/// <summary>
/// The feed's rude posts, waiting for a speaker. A rude post makes no enemy: it waits here, oldest first, until a boss, a mini-boss or a
/// lesser gunship takes it and says it. A post is held until taken; a seed already held, or among the last
/// <see cref="Tuning.RudeSeen"/> taken, is refused, so a post offered twice is taken once. A post taken stays known for the rest of
/// the run (<see cref="Heard"/>), and a speaker with nothing waiting says one of those again (<see cref="Recall"/>).
/// <see cref="Holds"/> is the join with the background cards: the feed shows a post in one place at a time.
/// </summary>
public sealed class RudeStore
{
    private readonly List<RudePost> _waiting = [];
    private readonly Queue<ulong> _seen = new();
    private readonly List<RudePost> _echoes = [];
    private readonly List<RudePost> _heard = [];
    private readonly HashSet<ulong> _round = [];

    /// <summary>The posts not yet spoken, the oldest first.</summary>
    public IReadOnlyList<RudePost> Waiting => _waiting;

    /// <summary>The last posts taken, the newest first. The mirror reads these.</summary>
    public IReadOnlyList<RudePost> Echoes => _echoes;

    /// <summary>The posts the run has heard, in the order they were first said; the last <see cref="Tuning.RudeHeard"/> are kept.</summary>
    public IReadOnlyList<RudePost> Heard => _heard;

    /// <summary>True while the post is waiting or among the last taken.</summary>
    public bool Holds(ulong seed) => _seen.Contains(seed) || _waiting.Exists(waiting => waiting.Seed == seed);

    /// <summary>Keeps a post. Refused when its text is empty or its seed is held; at the cap the oldest waiting post is dropped.</summary>
    public bool Add(RudePost post)
    {
        if (post.Text.Length == 0 || Holds(post.Seed)) return false;
        if (_waiting.Count >= Tuning.RudeWaiting) _waiting.RemoveAt(0);
        _waiting.Add(post);
        return true;
    }

    /// <summary>Hands out the post at <paramref name="index"/> (0 is the oldest); null when there is none there.</summary>
    public RudePost? Take(int index)
    {
        if (index < 0 || index >= _waiting.Count) return null;
        var post = _waiting[index];
        _waiting.RemoveAt(index);
        _seen.Enqueue(post.Seed);
        while (_seen.Count > Tuning.RudeSeen) _seen.Dequeue();
        _echoes.Insert(0, post);
        if (_echoes.Count > Tuning.RudeEchoes) _echoes.RemoveAt(_echoes.Count - 1);
        _heard.Add(post);
        _round.Add(post.Seed);
        if (_heard.Count > Tuning.RudeHeard)
        {
            _round.Remove(_heard[0].Seed);
            _heard.RemoveAt(0);
        }
        return post;
    }

    /// <summary>Hands out a heard post to be said again. The run goes through the heard posts in rounds: a post said in this round (taken
    /// fresh, or handed out here) is not offered again until every other has been, so each speaker carries on where the last stopped.
    /// <paramref name="taste"/> picks among those offered (the oldest heard first; an index past the list is held to it), and the post whose
    /// text is <paramref name="onShow"/> is never offered. Null when no other post is known.</summary>
    public RudePost? Recall(Func<IReadOnlyList<RudePost>, int> taste, string? onShow)
    {
        var offered = _heard.FindAll(heard => heard.Text != onShow && !_round.Contains(heard.Seed));
        if (offered.Count == 0)
        {
            _round.Clear();
            offered = _heard.FindAll(heard => heard.Text != onShow);
            if (offered.Count == 0) return null;
        }
        var post = offered[Math.Clamp(taste(offered), 0, offered.Count - 1)];
        _round.Add(post.Seed);
        return post;
    }
}
