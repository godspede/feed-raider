namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>Pixels of a sprite that come away over a number of frames (a boss's plates, its husk), each with the frame it goes on.</summary>
    internal sealed class PixelDust
    {
        /// <summary>How many pixels of one frame become debris; the rest of that frame's pixels only vanish.</summary>
        internal const int DebrisPerFrame = 8;

        /// <summary>The colour of a pixel without its alpha byte.</summary>
        private const uint RgbMask = 0xFFFFFF;

        // The three numbers that pick a pixel's frame scatter neighbouring pixels across different frames, so a sprite
        // comes apart in a speckle and not in sweeping bands.
        private const int FrameHashX = 37;
        private const int FrameHashY = 101;
        private const int FrameHashSalt = 7;

        /// <summary>How far in from a pixel's corner its middle lies, in pixels: debris starts in the middle of the pixel it came from.</summary>
        private const double PixelCentre = 0.5;

        /// <summary>The widest sideways speed of a piece of debris, in pixels per frame, either way.</summary>
        private const double SpreadSpeed = 1.4;

        /// <summary>The fastest upward speed of a piece of debris, in pixels per frame.</summary>
        private const double RiseSpeed = 0.8;

        /// <summary>The fewest frames a piece of debris lasts.</summary>
        private const int MinLife = 24;

        /// <summary>How many frames beyond <see cref="MinLife"/> a piece of debris may last, at most.</summary>
        private const int LifeSpread = 30;

        /// <summary>The share of debris drawn two pixels across; the rest is one pixel.</summary>
        private const double BigChance = 0.3;

        public readonly int Frames;
        public readonly List<(int X, int Y, uint Col)>[] ByFrame;

        /// <param name="left">Field column of the sprite's left edge.</param>
        /// <param name="top">Field row of the sprite's top edge.</param>
        /// <param name="take">Which pixels (by sprite column and row) come away.</param>
        public PixelDust(int left, int top, int width, int height, Sprite whole, Func<int, int, bool> take, int frames)
        {
            Frames = frames;
            ByFrame = new List<(int, int, uint)>[frames];
            for (int i = 0; i < frames; i++) ByFrame[i] = [];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    uint v = whole.Px[y * width + x];
                    if (v != 0 && take(x, y)) ByFrame[(x * FrameHashX + y * FrameHashY + FrameHashSalt) % frames].Add((left + x, top + y, v & RgbMask));
                }
        }

        /// <summary>Lets go of the pixels that fall away on frame <paramref name="age"/>: a few of them as debris, the rest only vanish
        /// (the particle limit caps it). A frame outside the fall sheds nothing.</summary>
        public void Shed(RaiderGame g, int age)
        {
            if (age < 0 || age >= Frames) return;
            var list = ByFrame[age];
            int step = Math.Max(1, list.Count / DebrisPerFrame);
            for (int i = 0; i < list.Count; i += step)
            {
                var (x, y, col) = list[i];
                g.AddParticle(new Particle
                {
                    X = x + PixelCentre,
                    Y = y + PixelCentre,
                    VX = (g.R() - 0.5) * SpreadSpeed,
                    VY = -g.R() * RiseSpeed,
                    Life = MinLife + (int)(g.R() * LifeSpread),
                    Colour = col,
                    Size = g.R() < BigChance ? 2 : 1,
                    Kind = ParticleKind.Debris,
                });
            }
        }
    }
}
