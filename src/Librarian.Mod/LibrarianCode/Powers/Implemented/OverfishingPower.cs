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

/// <summary>Each remaining owner turn doubles the orb that is foreground at that turn's start.</summary>
public sealed class OverfishingPower : ImplementedLibrarianPower
{
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Remaining", 0m)];
    public override int DisplayAmount => NormalNotes + UpgradedNotes;
    public void RegisterTurns(int turns)
    {
        AssertMutable();
        if (turns < 0) throw new ArgumentOutOfRangeException(nameof(turns));
        DynamicVars["Remaining"].BaseValue = checked(DynamicVars["Remaining"].IntValue + turns);
        InvokeDisplayAmountChanged();
    }
    public bool TryConsumeTurn()
    {
        AssertMutable();
        if (DynamicVars["Remaining"].IntValue <= 0) return false;
        DynamicVars["Remaining"].BaseValue -= 1;
        InvokeDisplayAmountChanged();
        return true;
    }
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty] public int NormalNotes { get; set; }
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty] public int UpgradedNotes { get; set; }
    public void RegisterNotes(bool upgraded) { AssertMutable(); if (upgraded) UpgradedNotes++; else NormalNotes++; }
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext context, ICombatState combatState)
    {
        if (player != Owner.Player || Owner.IsDead) return;
        for (int i = 0; i < NormalNotes + UpgradedNotes; i++)
        {
            var note = combatState.CreateCard<Librarian.LibrarianCode.Cards.StudyNotes>(player);
            if (i >= NormalNotes) { note.UpgradeInternal(); note.FinalizeUpgradeInternal(); }
            CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(note, PileType.Hand, player), 1.2f);
        }
    }
}
