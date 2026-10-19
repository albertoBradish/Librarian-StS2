using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards;

/// <summary>V1.3.11 approved 20-card revision. Published model identity is retained.</summary>
public sealed class MultiplayerPlaceholderB() : LibrarianCard(1, CardType.Skill, CardRarity.Rare, TargetType.AnyAlly)
{
    public override string PortraitPath => "res://Librarian/images/card_portraits/binding.png";
    public override string CustomPortraitPath => PortraitPath;
    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public override TargetType TargetType => IsMutable && Owner?.Creature.CombatState is { } combat
        && !combat.PlayerCreatures.Any(c => c != Owner.Creature && c.IsAlive) ? TargetType.Self : TargetType.AnyAlly;
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var target = play.Target?.Player ?? Owner;
        LibrarianRuntime.Get(Owner).Bindings.Add((Owner, target));
        return Task.CompletedTask;
    }
    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
}
