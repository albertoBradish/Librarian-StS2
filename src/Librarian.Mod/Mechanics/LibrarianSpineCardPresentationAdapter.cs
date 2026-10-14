using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Librarian.Mechanics;

// The accompanying patch keeps the old CardType overload and shared native/VFX presentation body.
internal static partial class LibrarianSpellVisuals
{
    internal static void CardPlayed(Player player, CardPlay play) => LibrarianOrbVfx.Safely(() =>
    {
        // Consume before Instant/Reduced/dead/headless presentation filters: this exact CardPlay
        // is handled even when its pose is suppressed. No fabricated token or time-based debounce.
        bool owned = LibrarianSpineRuntime.TryHandleCard(play, player);
        CardPlayedCore(player, play.Card.Type, owned);
    });
}
