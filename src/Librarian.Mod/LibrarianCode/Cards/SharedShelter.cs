using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Combat;

namespace Librarian.LibrarianCode.Cards;

/// <summary>Resolve the caster's card Block once, then copy it without recipient effect hooks.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-SHARED_SHELTER")]
public sealed class SharedShelter() : LibrarianCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyAlly)
{
    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;
    public override bool GainsBlock => true;
    public override TargetType TargetType => IsMutable && Owner?.Creature.CombatState is { } combat
        && !combat.PlayerCreatures.Any(c => c != Owner.Creature && c.IsAlive)
        ? TargetType.Self : TargetType.AnyAlly;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(9m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var target = play.Target;
        decimal amount = await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
        if (amount <= 0 || CombatManager.Instance.IsOverOrEnding || target is null || target == Owner.Creature
            || !target.IsAlive || target.Player is null || target.Side != Owner.Creature.Side
            || target.CombatState != Owner.Creature.CombatState) return;
        // Explicit copy: bypass Before/Modify/AfterBlock hooks on the recipient (e.g. Vambrace).
        target.GainBlockInternal(decimal.Floor(amount));
        CombatManager.Instance.History.BlockGained(target.CombatState!, target, (int)amount, ValueProp.Unpowered, play);
        Librarian.Mechanics.LibrarianCardVfx050.Block(target, this);
        SfxCmd.Play("event:/sfx/block_gain");
        VfxCmd.PlayOnCreatureCenter(target, "vfx/vfx_block");
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);
}
