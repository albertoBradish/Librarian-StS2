using BaseLib.Abstracts;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

/// <summary>Native combat notifications, presentation only. No damage or targeting is changed.</summary>
public sealed class LibrarianSpellVisualHooks040 : CustomSingletonModel
{
    public LibrarianSpellVisualHooks040() : base(HookType.Combat) { }

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (cardPlay.Player.Character is LibrarianCharacter)
        {
            LibrarianSpellVisuals.CardPlayed(cardPlay.Player, cardPlay.Card.Type);
            LibrarianCardVfx050.Cast(cardPlay);
        }
        return Task.CompletedTask;
    }

    public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target,
        DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        // Native Move marks attack-card/enemy attacks. Exclude direct HP loss,
        // relic/power damage, poison, and zero damage/immunity presentations.
        if ((props & ValueProp.Move) == 0 || (props & ValueProp.Unblockable) != 0)
            return Task.CompletedTask;
        if (result.TotalDamage > 0 && cardSource?.Type == CardType.Attack
            && cardSource.Owner.Character is LibrarianCharacter && dealer == cardSource.Owner.Creature)
            LibrarianCardVfx050.Impact(cardSource, target);
        if (target.Player?.Character is LibrarianCharacter && result.BlockedDamage > 0
            && result.UnblockedDamage == 0 && result.WasFullyBlocked)
            LibrarianSpellVisuals.PerfectBlock(target, result);
        return Task.CompletedTask;
    }

    public override Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
    { if (amount > 0) LibrarianCardVfx050.Block(creature, cardSource); return Task.CompletedTask; }
    public override Task AfterPowerAmountChanged(PlayerChoiceContext context, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    { if (amount > 0) LibrarianCardVfx050.Status(power, cardSource); return Task.CompletedTask; }
    public override Task AfterCardDrawn(PlayerChoiceContext context, CardModel card, bool fromHandDraw)
    { LibrarianCardVfx050.Page(card.Owner, "draw", card.GetType().Name); return Task.CompletedTask; }
    public override Task AfterCardGeneratedForCombat(CardModel card, MegaCrit.Sts2.Core.Entities.Players.Player? creator)
    { LibrarianCardVfx050.Page(card.Owner, "generated", card.GetType().Name); return Task.CompletedTask; }
    public override Task AfterCardExhausted(PlayerChoiceContext context, CardModel card, bool causedByEthereal)
    { LibrarianCardVfx050.Page(card.Owner, "exhaust", card.GetType().Name); return Task.CompletedTask; }
}
