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

/// <summary>v0.4.0: positive Tide gains grant other living players unpowered Block, never Tide.</summary>
public sealed class WaterSpiritPower : ImplementedLibrarianPower, IOrbEventListener
{
    public override PowerStackType StackType => PowerStackType.Single;
    public async Task OnOrbEvent(LibrarianSession session, PlayerChoiceContext context, OrbEvent change)
    {
        if (!OwnEvent(session, change) || change.Kind != OrbEventKind.Gained || change.Orb != OrbKind.Tide || change.ActualAmount <= 0) return;
        foreach (var teammate in CombatState.GetTeammatesOf(Owner).Where(c => c.IsAlive && c.IsPlayer && c != Owner).ToArray())
        {
            var gained = await CreatureCmd.GainBlock(teammate, change.ActualAmount, ValueProp.Unpowered, null);
            if (gained > 0) LibrarianCardVfx050.AllyBlock(teammate, session.Player, SpellElement050.Water, nameof(WaterSpiritPower));
        }
    }
}
