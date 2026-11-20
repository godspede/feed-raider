namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>
    /// A brick the Pile-On has thrown (stage 8): a small enemy that falls straight down at one pixel a frame and hurts the ship on touch.
    /// It takes two hits and is worth 20. It belongs to the Pile-On that threw it: when that is gone the brick goes with it, so a boss
    /// that has just been killed leaves nothing to dodge. It never flies in a wave of its own.
    /// </summary>
    private sealed class BrickBehaviour : EnemyBehaviour
    {
        private const double FallSpeed = 1.0;
        /// <summary>How far below the field's bottom edge a brick whose Pile-On is gone is put, so the field's cull takes it at once.</summary>
        private const double OffFieldMarginPx = 20;

        /// <summary>The brick's light face and its dark edge shading.</summary>
        internal const uint Light = 0x9a8fb0, Dark = 0x5f5670;

        // 7 by 5, the same drawing as the brick a wall is made of
        private static readonly string[] Rows =
        [
            "#######",
            "#MmmmM#",
            "#mryrm#",
            "#MmmmM#",
            "#######",
        ];

        /// <summary>The wall's brick drawing; the Pile-On cuts its parts from this too.</summary>
        internal static readonly Sprite Art7x5 = Sprite.FromArt(Rows, Art.With(('m', Light), ('M', Dark)));

        public override EnemyKind Kind => EnemyKind.Brick;
        public override string Name => "BRICK";
        public override Sprite Body => Art7x5;
        public override uint Colour => Light;
        public override double Hp => 2;
        public override int Score => 20;
        public override double DropChance => 0;

        public override void Update(RaiderGame g, Enemy e)
        {
            e.Y += FallSpeed;
            // its Pile-On is gone (killed, or left): the brick goes too, off the bottom, where the field's cull takes it
            if (e.Parent is { } parent && !g.Enemies.Contains(parent)) e.Y = Math.Max(e.Y, g.PH + OffFieldMarginPx);
        }
    }
}
