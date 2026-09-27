using Librarian.Core;
using Librarian.Mechanics;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.LibrarianCode.Cards.PowerCards;

public abstract class ImplementedPowerCard(int cost, CardRarity rarity) : LibrarianCard(cost, CardType.Power, rarity, TargetType.Self)
{
    protected LibrarianSession Session => LibrarianRuntime.Get(Owner);
    protected Task<T?> Apply<T>(PlayerChoiceContext context, decimal amount) where T : PowerModel
        => PowerCmd.Apply<T>(context, Owner.Creature, amount, Owner.Creature, this);
}
