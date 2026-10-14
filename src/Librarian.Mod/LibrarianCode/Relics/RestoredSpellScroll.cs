using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace Librarian.LibrarianCode.Relics;

/// <summary>Temporary upgraded starter, acquired through native Touch of Orobas replacement.</summary>
public sealed class RestoredSpellScroll : LibrarianPlaceholderRelic
{
    public override RelicRarity Rarity => RelicRarity.Starter;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Growth", 1m)];

    public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (!participants.Contains(Owner.Creature)) return;
        Flash();
        var session = LibrarianRuntime.Get(Owner);
        foreach (var kind in Enum.GetValues<OrbKind>())
        {
            await LibrarianRuntime.Dispatch(session, choiceContext,
                session.Orbs.Strengthen(kind, DynamicVars["Growth"].IntValue, OrbScope.All, new(Id.ToString())));
        }
        await LibrarianRuntime.Dispatch(session, choiceContext,
            session.Orbs.Activate(session.Orbs.Foreground, OrbScope.All, new(Id.ToString())));
    }
}

