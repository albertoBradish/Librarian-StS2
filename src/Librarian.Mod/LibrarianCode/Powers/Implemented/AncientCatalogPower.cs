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

/// <summary>v0.4.0: each explicit channel or positive Growth gain strengthens one random other orb exactly once.</summary>
public sealed class AncientCatalogPower : ImplementedLibrarianPower, IOrbEventListener
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Vigor", 0m)];
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int VigorAmount { get => DynamicVars["Vigor"].IntValue; set => DynamicVars["Vigor"].BaseValue = value; }
    public async Task OnOrbEvent(LibrarianSession session, PlayerChoiceContext context, OrbEvent change)
    {
        bool triggers = (change.Kind == OrbEventKind.Lost && change.ActualAmount > 0)
            || change.Kind == OrbEventKind.Locked;
        if (!OwnEvent(session, change) || !triggers
            || !session.OwnTurnOpen
            || !session.Orbs.TryMarkFirstThisTurn(Id.ToString())) return;
        Flash();
        await PassiveBlock(Amount);
        await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.VigorPower>(context, Owner, VigorAmount, Owner, null);
    }
}
