using Librarian.LibrarianCode.Integration;
using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.LibrarianCode.Relics;

public sealed class TatteredSpellScroll : LibrarianRelic
{
    public override string PackedIconPath => "res://Librarian/images/relics/v0.4.0/tattered_scroll.png";
    protected override string PackedIconOutlinePath => "res://Librarian/images/relics/v0.4.0/tattered_scroll_outline.png";
    protected override string BigIconPath => "res://Librarian/images/relics/v0.4.0/big/tattered_scroll.png";
    public override RelicRarity Rarity => RelicRarity.Starter;
    public override RelicModel GetUpgradeReplacement() => ModelDb.Relic<RestoredSpellScroll>();
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Growth", 1m)];

    // First player-side start supplies the synchronized choice context needed by the bridge.
    public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (!participants.Contains(Owner.Creature)) return;
        Flash();
        var session = LibrarianRuntime.Get(Owner);
        await LibrarianRuntime.Dispatch(session, choiceContext,
            session.Orbs.Strengthen(session.Orbs.Snapshot().Orbs.Single(orb => orb.IsForeground).Kind,
                DynamicVars["Growth"].IntValue, OrbScope.Foreground, new(Id.ToString())));
    }
}
