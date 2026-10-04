using System.Reflection;
using Librarian.Core;
using Librarian.LibrarianCode.Powers.Implemented;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Librarian.Mechanics;

internal sealed record LibrarianBlockPreview(int? Minimum, int? Maximum, string? Uncertainty, long ExpiringBlock = 0)
{
    internal bool Exact => Minimum.HasValue && Minimum == Maximum;
    internal static LibrarianBlockPreview Unknown(string reason) => new(null, null, reason);
}

/// <summary>Project through the Core end-turn resolver; never call native commands, hooks or live RNG.</summary>
internal static class LibrarianEndTurnPreview
{
    internal static LibrarianBlockPreview Read(LibrarianSession session)
    {
        if (session.ResolvingEndTurn || session.Orbs.IsFaulted) return LibrarianBlockPreview.Unknown("resolving");
        var player = session.Player;
        var creature = player.Creature;
        if (creature.IsDead || CombatManager.Instance.IsOverOrEnding) return new(0, 0, null);
        if (LibrarianBottomPlay041.PeekBottom(player) is { } bottom && LibrarianBottomPlay041.IsMainstem(bottom)
            || creature.GetPower<FuelTheFirePower>() is { Amount: > 0 })
            return LibrarianBlockPreview.Unknown("bottom");
        if (session.EndTasks.Count != session.EndTaskPreviews.Count || session.EndTaskPreviews.Any(p => p is null))
            return LibrarianBlockPreview.Unknown("effects");
        var powers = creature.Powers.ToArray();
        var relics = player.Relics.Where(r => !r.IsMelted).ToArray();
        if (powers.OfType<IOrbEndTurnListener>().Any(p => p is not SedimentationPower and not LifeSymphonyPendingPower and not BlazingChapterPower)
            || relics.OfType<IOrbEndTurnListener>().Any(r => r is not LibrarianRarePlaceholderOne and not LibrarianShopPlaceholder)
            || powers.OfType<IOrbAfterEndTurnListener>().Any()
            || relics.OfType<IOrbSettlementListener>().Any(r => r is not LibrarianUncommonPlaceholderOne and not LibrarianRarePlaceholderTwo)
            || powers.OfType<IOrbEventListener>().Any(p => p is not LifelinePower and not EmberBookmarkPower and not WaterSpiritPower
                and not AncientCatalogPower and not ShiftingPagesPower and not TidalMarkPower and not PracticeMakesPerfectPower))
            return LibrarianBlockPreview.Unknown("effects");
        // Inspect override identities, never execute even an apparently pure third-party hook.
        foreach (var model in creature.CombatState!.IterateHookListeners())
        {
            if (model is DexterityPower or FrailPower or LibrarianSpellVisualHooks040
                || model.GetType() == typeof(MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel))
                continue; // fixed native modifiers exclude Unpowered Block; our visual hook only presents it
            foreach (string name in new[] { "ModifyBlockAdditive", "ModifyBlockMultiplicative", "BeforeBlockGained", "AfterBlockGained", "AfterModifyingBlockAmount" })
                if (Overrides(model, name))
                    return LibrarianBlockPreview.Unknown("modifiers");
            if (model.GetType().Assembly != typeof(CardModel).Assembly && model.GetType().Assembly != typeof(LibrarianRuntime).Assembly
                && (Overrides(model, "BeforeSideTurnEnd") || Overrides(model, "AfterSideTurnEnd")))
                return LibrarianBlockPreview.Unknown("effects");
        }
        var providers = powers.OfType<IOrbEndTurnRuleProvider>().ToArray();
        var rules = new EndTurnRules(OrbScope.All, providers.Any(p => p.PreserveActivation),
            !providers.Any(p => p.SettleAllActivated), providers.Any(p => p.PreserveBackgroundActivation));
        int branches = relics.OfType<LibrarianRarePlaceholderOne>().Any() ? 3 : 1;
        var results = new List<EndTurnBlockProjection>();
        try
        {
            for (int branch = 0; branch < branches; branch++)
            {
                var copy = new EndTurnBlockProjection(session.Orbs, session.Waves, creature.GetPower<EndlessTidePower>() is null);
                copy.Waves.Retained = creature.GetPower<RidgeWardPower>() is not null;
                copy.Waves.RetentionFloor = creature.GetPower<UnretreatingTidePower>()?.Amount ?? 0;
                copy.OnOperation = operation =>
                {
                    foreach (var change in operation.Events)
                        foreach (var listener in powers.OfType<IOrbEventListener>())
                        {
                            if (listener is LifelinePower lifeline && change.Kind == OrbEventKind.Imbued)
                                copy.Dispatch(copy.Orbs.Strengthen(copy.Orbs.Foreground, lifeline.Amount, OrbScope.All));
                            if (listener is EmberBookmarkPower bookmark && change.Kind == OrbEventKind.Extinguished && change.Orb == OrbKind.Fire)
                                copy.Dispatch(copy.Orbs.Gain(OrbKind.Fire, bookmark.Amount));
                        }
                };
                copy.AfterSettlement = request =>
                {
                    foreach (var listener in relics.OfType<IOrbSettlementListener>())
                        if (listener is LibrarianUncommonPlaceholderOne && request.Orb == OrbKind.Fire)
                            copy.Dispatch(copy.Orbs.Strengthen(OrbKind.Fire, 2, OrbScope.All));
                };
                int selectedBranch = branch;
                copy.Resolve(rules, projection =>
                {
                    foreach (var preview in session.EndTaskPreviews) preview!(projection);
                    foreach (var listener in powers.OfType<IOrbEndTurnListener>())
                    {
                        if (listener is SedimentationPower sedimentation)
                            foreach (var kind in copy.Orbs.Select(OrbScope.Background, ActivationFilter.Inactive).ToArray())
                                copy.Dispatch(copy.Orbs.Strengthen(kind, sedimentation.Amount, OrbScope.All));
                        if (listener is LifeSymphonyPendingPower symphony)
                            copy.Orbs.QueueExtraSettlement(OrbSelector.Named(OrbKind.Growth, OrbScope.All), symphony.Amount, "preview-symphony");
                        if (listener is BlazingChapterPower blazing)
                            copy.Orbs.QueueExtraSettlement(OrbSelector.Named(OrbKind.Fire, OrbScope.All), blazing.Amount, "preview-blazing");
                    }
                    foreach (var listener in relics.OfType<IOrbEndTurnListener>())
                    {
                        if (listener is LibrarianRarePlaceholderOne)
                        {
                            var candidates = copy.Orbs.Snapshot().Orbs.Where(o => !o.IsLocked && !o.IsActivated).ToArray();
                            if (candidates.Length > 0) copy.Dispatch(copy.Orbs.Activate(candidates[selectedBranch % candidates.Length].Kind, OrbScope.All));
                        }
                        if (listener is LibrarianShopPlaceholder && copy.Orbs.Snapshot().Orbs.All(o => o.IsActivated))
                            copy.Orbs.SettleImmediatelyAsync(OrbSelector.Named(copy.Orbs.Foreground, OrbScope.All), 1,
                                copy.Settle, "preview-badge").GetAwaiter().GetResult();
                    }
                }, creature.GetPower<CooldownPower>() is not null).GetAwaiter().GetResult();
                // Killing the last opponent interrupts real payouts. Do not promise an exact amount in that case.
                long damage = copy.PotentialDamage + (relics.OfType<LibrarianRarePlaceholderTwo>().Any() ? 7L * copy.GrowthSettlements : 0);
                if (damage > 0 && creature.CombatState.HittableEnemies.Any(e => e.CurrentHp + e.Block <= damage))
                    return LibrarianBlockPreview.Unknown("combat");
                results.Add(copy);
            }
            return new(results.Min(p => p.NewTidalBlock), results.Max(p => p.NewTidalBlock),
                results.Select(p => p.NewTidalBlock).Distinct().Count() > 1 ? "random" : null, results[0].ExpiringBlock);
        }
        catch (PreviewRandomRequiredException) { return LibrarianBlockPreview.Unknown("random"); }
        catch (OverflowException) { return LibrarianBlockPreview.Unknown("effects"); }
    }

    private static bool Overrides(AbstractModel model, string name)
        => model.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).Any(m => m.Name == name
            && m.GetBaseDefinition().DeclaringType == typeof(AbstractModel) && m.DeclaringType != typeof(AbstractModel));
}
