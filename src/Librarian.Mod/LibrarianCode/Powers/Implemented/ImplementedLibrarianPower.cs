using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>Combat-owned helpers. Event subscriptions live on the owner's Power collection, never static delegates.</summary>
public abstract class ImplementedLibrarianPower : LibrarianPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected OrbOrigin Origin => new(Id.ToString());
    protected bool BelongsTo(LibrarianSession session) => Owner == session.Player.Creature;
    protected bool OwnEvent(LibrarianSession session, OrbEvent change)
        => BelongsTo(session) && change.OwnerId == session.Orbs.OwnerId;
    protected Task Gain(PlayerChoiceContext context, LibrarianSession session, OrbKind kind, int amount)
        => LibrarianRuntime.Dispatch(session, context, session.Orbs.Gain(kind, amount, Origin));
    protected Task PassiveBlock(int amount)
        => CreatureCmd.GainBlock(Owner, amount, ValueProp.Unpowered, null, fast: true);
}
