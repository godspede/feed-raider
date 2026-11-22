namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>What the toast says to a press on a button whose write has not been answered yet.</summary>
    public const string StillSending = "Still sending…";

    /// <summary>The frame of a press that has not happened: far enough back that any frame minus it is past every wait, and half
    /// the range so the subtraction cannot overflow.</summary>
    private const long NeverPressed = long.MinValue / 2;
    /// <summary>The ring an undo draws: from the icon's half width down to a point, over this many frames, so the icon is seen to go out.</summary>
    private const int UndoRingTo = 1, UndoRingFrames = 8;

    private PostMarks? _pressedMarks;
    private PostAction _pressedAction;
    private long _pressedFrame = NeverPressed, _saidStillSendingFrame = NeverPressed;

    /// <summary>The click of this tick, by mouse or finger, read before the cards move so it lands on the frame the player saw: the bomb button
    /// bombs, and anywhere else is a press on the field.</summary>
    private void UpdateClick()
    {
        var inp = Input;
        if (!inp.HasClick) return;
        inp.HasClick = false;
        if (InBombButton(inp.ClickCol, inp.ClickRow)) inp.BombPressed = true;
        else PressField(inp.ClickCol, inp.ClickRow, inp.ClickTouch);
    }

    /// <summary>A click on the field, the bomb button aside: on a speech box it does nothing, so the card under the box is
    /// not liked by a press meant for the words; on a card's heart or repost it presses that button.</summary>
    private void PressField(double col, double row, bool touch)
    {
        if (!Alive || InSpeechBox(col, row)) return;
        var (x, y) = Layout.ToField(col, row);
        if (Cards.ButtonAt(x, y, touch ? Tuning.TouchPadCells * CardField.PixelsPerCell : 0) is { } hit) PressButton(hit.Card, hit.Action);
    }

    private bool InSpeechBox(double col, double row)
    {
        foreach (var (_, box) in EnemySpeechBox.Placed(this, Layout, Tuning.MaxSpeechBoxes))
            if (col >= box.C && col < box.Right && row >= box.R && row < box.Bottom) return true;
        return false;
    }

    /// <summary>A press on a card's button, by a click or a key. Off, it likes or reposts at once: the burst, the reward and
    /// the write. Lit, it undoes the like or repost by the feed log's own path; marked (signed out, nothing was sent), it clears the mark.
    /// Waiting on its write, it sends nothing and says so, once a second at most, so a like and its undo are never in flight
    /// together. A second press on the same button inside <see cref="Tuning.PressRepeatFrames"/> is ignored. An undo never takes
    /// the reward back: a post pays once however often it is liked.</summary>
    internal void PressButton(Card card, PostAction action)
    {
        var m = card.Marks;
        if (ReferenceEquals(_pressedMarks, m) && _pressedAction == action && Frame - _pressedFrame < Tuning.PressRepeatFrames) return;
        _pressedMarks = m; _pressedAction = action; _pressedFrame = Frame;
        var (x, y) = ButtonCentre(card, action);
        switch (m.Of(action))
        {
            case ButtonState.Pending:
                if (Frame - _saidStillSendingFrame < Tuning.Fps) return;
                _saidStillSendingFrame = Frame;
                Say(StillSending);
                return;
            case ButtonState.Off when CanPress(card, action):
                LikeOrRepost(card, action, x, y);
                return;
            case ButtonState.Off:
                if (!IsLocked(card, action)) return;
                Say(NeedsFreshSignIn(action == PostAction.Like ? "Like" : "Repost"));
                Sound(SfxNames.NoLike, x);
                return;
            default:
                Undo(card, action, x, y);
                return;
        }
    }

    private void Undo(Card card, PostAction action, double x, double y)
    {
        var before = card.Marks.Of(action);
        Toggle(card.Post.Uri, action);
        if (card.Marks.Of(action) == before) return;
        Sound(SfxNames.Undo, x);
        AddRing(x, y, PressLook.MaxIconWidth / 2.0, UndoRingTo, PressLook.Dim(action), UndoRingFrames);
    }

    /// <summary>The card the like and repost keys act on: the one under the ship's centre, else the nearest within
    /// <see cref="Tuning.ShipKeyReachCells"/>; null when the ship is gone or none is that near.</summary>
    public Card? KeyCard => Alive ? Cards.CardNear(PX, PY, Tuning.ShipKeyReachCells * CardField.PixelsPerCell) : null;

    /// <summary>Z and X of this tick, read with the click before the cards move: a press on the like or repost of <see cref="KeyCard"/>,
    /// so a key likes, and undoes, the way a click does. With no card in reach it says so by sound. While the ship is gone a press
    /// waits in its latch and goes stale.</summary>
    private void UpdateShipKeys()
    {
        if (!Alive) return;
        bool like = Input.ShipLikeLatch > 0, repost = Input.ShipRepostLatch > 0;
        if (!like && !repost) return;
        Input.ShipLikeLatch = Input.ShipRepostLatch = 0;
        if (KeyCard is not { } card) { Sound(SfxNames.NoLike, PX); return; }
        if (like) PressButton(card, PostAction.Like);
        if (repost) PressButton(card, PostAction.Repost);
    }
}
