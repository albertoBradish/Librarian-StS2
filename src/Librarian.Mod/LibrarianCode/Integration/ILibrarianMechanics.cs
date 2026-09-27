using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.LibrarianCode.Integration;

public enum LibrarianElement { Fire, Water, Earth }

/// <summary>Boundary between game content and the independently developed orb rules.</summary>
public interface ILibrarianMechanics
{
    Task GainAsync(PlayerChoiceContext choiceContext, Player player, LibrarianElement element,
        decimal amount, AbstractModel source, CardPlay? cardPlay = null);
}

public static class LibrarianMechanicsBridge
{
    private static ILibrarianMechanics _current = new UnconnectedMechanics();
    public static ILibrarianMechanics Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static bool IsConnected => _current is not UnconnectedMechanics;

    private sealed class UnconnectedMechanics : ILibrarianMechanics
    {
        public Task GainAsync(PlayerChoiceContext choiceContext, Player player, LibrarianElement element,
            decimal amount, AbstractModel source, CardPlay? cardPlay = null)
        {
            MainFile.Logger.Info($"MECHANICS_NOT_CONNECTED: {source.Id} requested {element} +{amount} for player {player.NetId}. No orb effect was applied.");
            return Task.CompletedTask;
        }
    }
}
