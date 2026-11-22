using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

/// <summary>What the card tests share: the screen, a signed-in game on it with no boss gates, the ship's places, the units
/// they put over a card, and a frame shaken by a chosen size.</summary>
internal static class CardRig
{
    public const int ScrCols = EnemyKit.BigCols, ScrRows = EnemyKit.BigRows;
    /// <summary>The card's top row in a scene that names none: on the field, clear of the HUD.</summary>
    public const int DefaultCardTop = 10;
    /// <summary>A ship x left of the field, so the ship is never over what a test reads.</summary>
    public const double ParkedShipX = -60.0;
    /// <summary>A ship position on the field: over the card's columns, and below a card high on the field.</summary>
    public const double ShipOverCardX = 64.0, ShipBelowCardY = 84.0;
    /// <summary>A particle's colour; any colour serves.</summary>
    public const uint ParticleColour = 0xffaa33;
    /// <summary>A particle's life in frames and its size in pixels, a fresh spark big enough to touch a cell.</summary>
    public const int ParticleLife = 9, ParticleSize = 2;
    /// <summary>A ring's radii and its life part way through, so it is drawn at a size that spans a few cells.</summary>
    public const int RingStartRadius = 2, RingEndRadius = 12, RingLife = 3, RingMaxLife = 6;
    /// <summary>The most frames searched for one whose shake reads the wanted size.</summary>
    private const long ShakeSearchFrames = 5000;

    /// <summary>A signed-in game laid out on the screen, with no boss gates, before its first step.</summary>
    public static RaiderGame Game()
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };
        game.SetLayout(FieldLayout.Compute(ScrCols, ScrRows));
        return game;
    }

    /// <summary>Takes every unit and pop-up off the field and parks the ship left of it, leaving the cards and the sky.</summary>
    public static RaiderGame ClearAndParkShip(RaiderGame game)
    {
        EnemyKit.Clear(game);
        PressRig.Force(game, nameof(RaiderGame.PX), ParkedShipX);
        return game;
    }

    /// <summary>Sets a frame at which the shake reads <paramref name="want"/> pixels both ways.</summary>
    public static void ShakeBy(RaiderGame game, int want)
    {
        PressRig.Force(game, nameof(RaiderGame.Shake), (double)want);
        for (long frame = 0; frame < ShakeSearchFrames; frame++)
        {
            PressRig.Force(game, nameof(RaiderGame.Frame), frame);
            var (shakeX, shakeY) = game.ShakeOffset();
            if (Math.Abs(shakeX) == want && Math.Abs(shakeY) == want) return;
        }
        throw new InvalidOperationException("no frame shakes by that much");
    }
}
