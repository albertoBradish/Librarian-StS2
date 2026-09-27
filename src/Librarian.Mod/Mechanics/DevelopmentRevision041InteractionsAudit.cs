using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

/// <summary>
/// V0.4.1 native interaction audit. It supplements the model/value checks with
/// real CardCmd autoplay, draw-pile, block, intent, power, and preview behavior.
/// </summary>
internal static class DevelopmentRevision041InteractionsAudit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(freshFight);
        var context = new ThrowingPlayerChoiceContext();
        int checks = 0;
        LibrarianSession Session() => LibrarianRuntime.Get(player);

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("041 interactions: " + label);
            checks++;
            MainFile.Logger.Info("INTERACTIONS041_CHECK_PASS " + label);
        }

        async Task Reset()
        {
            await freshFight();
            for (int frame = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; frame++)
            {
                if (frame > 1200) throw new TimeoutException("041 interactions did not reach Play phase");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (player.Creature.CombatState is null) throw new InvalidOperationException("041 interactions has no combat");
            foreach (Creature enemy in player.Creature.CombatState.HittableEnemies)
                await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
            await PlayerCmd.SetEnergy(99, player);
        }

        CardModel Create<T>(bool upgraded = false) where T : CardModel
        {
            CardModel card = player.Creature.CombatState!.CreateCard<T>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }

        async Task Play(CardModel card, Creature? target = null)
        {
            await PlayerCmd.SetEnergy(99, player);
            target ??= card.TargetType is TargetType.AnyEnemy or TargetType.RandomEnemy
                ? player.Creature.CombatState!.HittableEnemies.FirstOrDefault()
                : null;
            await CardCmd.AutoPlay(context, card, target, skipCardPileVisuals: true);
        }

        static bool IsAttackIntent(Creature creature) => creature.Monster?.NextMove.Intents.Any(intent =>
            intent.IntentType == IntentType.Attack || intent.IntentType == IntentType.DeathBlow) == true;

        try
        {
            // Residual Warmth branches from the selected enemy's current intent;
            // mixed intent targets must not be collapsed into a random target.
            await Reset();
            // FreshFight deliberately uses a single Tunneler. Add a second real
            // creature and set only this disposable fixture's intent lists.
            var attackTarget = player.Creature.CombatState!.HittableEnemies.Single();
            var nonAttackTarget = await CreatureCmd.Add<Tunneler>(player.Creature.CombatState!);
            await CreatureCmd.SetMaxAndCurrentHp(nonAttackTarget, 10000);
            var intentProperty = AccessTools.Property(typeof(MoveState), nameof(MoveState.Intents));
            intentProperty.SetValue(attackTarget.Monster!.NextMove, new AbstractIntent[] { new SingleAttackIntent(1), new BuffIntent() });
            intentProperty.SetValue(nonAttackTarget.Monster!.NextMove, new AbstractIntent[] { new BuffIntent() });
            Check(IsAttackIntent(attackTarget) && !IsAttackIntent(nonAttackTarget),
                "mixed attack/non-attack enemy intents available");
            int fireBefore = Session().Orbs.Value(OrbKind.Fire);
            int blockBefore = player.Creature.Block;
            await Play(Create<ResidualWarmth>(), nonAttackTarget);
            Check(Session().Orbs.Value(OrbKind.Fire) == fireBefore + 8 && player.Creature.Block == blockBefore,
                "ResidualWarmth non-attack selected target gains Fire");
            blockBefore = player.Creature.Block;
            await Play(Create<ResidualWarmth>(), attackTarget);
            Check(player.Creature.Block == blockBefore + 8 && Session().Orbs.Value(OrbKind.Fire) == fireBefore + 8,
                "ResidualWarmth attack selected target gains Block only");

            // Ember Pierce always hits before its optional remaining-Block clear.
            await Reset();
            Creature target = player.Creature.CombatState!.HittableEnemies.First();
            target.GainBlockInternal(10);
            int noImbueHp = target.CurrentHp;
            await Play(Create<EmberPierce>(), target);
            Check(target.Block == 2 && target.CurrentHp == noImbueHp,
                "EmberPierce unimbued attack leaves remaining Block and does not clear");

            await Reset();
            target = player.Creature.CombatState!.HittableEnemies.First();
            await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Gain(OrbKind.Fire, 1));
            target.GainBlockInternal(5);
            int imbuedHp = target.CurrentHp;
            await Play(Create<EmberPierce>(), target);
            Check(target.Block == 0 && target.CurrentHp == imbuedHp - 3,
                "EmberPierce imbued attack damages through Block then clears remainder");

            // Zero Search uses the combat-local distinct lock history and the
            // native draw command for every count, including zero.
            for (int expected = 0; expected <= 3; expected++)
            {
                await Reset();
                foreach (var card in player.PlayerCombatState!.Hand.Cards.ToArray())
                    await CardPileCmd.Add(card, PileType.Discard);
                foreach (OrbKind kind in Enum.GetValues<OrbKind>().Take(expected))
                    await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Lock(kind));
                if (expected > 0)
                    await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Lock(OrbKind.Fire));
                for (int i = 0; i < 8; i++)
                    await CardPileCmd.AddGeneratedCardToCombat(Create<LibrarianDefend>(), PileType.Draw, player, CardPilePosition.Top);
                int handBefore = player.PlayerCombatState!.Hand.Cards.Count;
                await Play(Create<ZeroSearch>());
                Check(Session().Orbs.LockedKindsThisCombatCount == expected,
                    $"ZeroSearch lock history count {expected}");
                Check(player.PlayerCombatState.Hand.Cards.Count - handBefore == expected + 3,
                    $"ZeroSearch native Draw{expected + 3}");
            }

            // Rekindle's clone preserves upgrade state but is a separate mutable
            // card and the source is not consumed by an Exhaust keyword.
            await Reset();
            CardModel rekindle = Create<Rekindle>(upgraded: true);
            await Play(rekindle);
            CardModel? rekindleCopy = player.PlayerCombatState!.DrawPile.Cards.LastOrDefault();
            Check(rekindleCopy is Rekindle && !ReferenceEquals(rekindleCopy, rekindle) && rekindleCopy.IsUpgraded,
                "Rekindle creates an independent upgraded-state-preserving copy");
            Check(!rekindle.Keywords.Contains(CardKeyword.Exhaust) && !rekindleCopy!.Keywords.Contains(CardKeyword.Exhaust),
                "Rekindle source and copy are not consumed");
            int sourceFire = rekindle.DynamicVars["Fire"].IntValue;
            rekindleCopy!.AddKeyword(CardKeyword.Retain);
            Check(rekindle.DynamicVars["Fire"].IntValue == sourceFire &&
                !rekindle.Keywords.Contains(CardKeyword.Retain), "Rekindle copy mutation is independent");

            // Transcribe clones the actual last-played instance twice, adding
            // Ethereal and Exhaust to each clone without sharing mutable state.
            await Reset();
            CardModel source = Create<LibrarianDefend>();
            await Play(source);
            int handBeforeTranscribe = player.PlayerCombatState!.Hand.Cards.Count;
            CardModel transcribe = Create<Transcribe>();
            await Play(transcribe);
            CardModel? handCopy = player.PlayerCombatState.Hand.Cards.LastOrDefault(card => card.Id == source.Id);
            CardModel? bottomCopy = player.PlayerCombatState.DrawPile.Cards.LastOrDefault(card => card.Id == source.Id);
            Check(handCopy is not null && bottomCopy is not null && !ReferenceEquals(handCopy, bottomCopy) &&
                player.PlayerCombatState.Hand.Cards.Count == handBeforeTranscribe + 1,
                "Transcribe creates two independent pile copies");
            Check(handCopy!.Keywords.Contains(CardKeyword.Ethereal) && handCopy.Keywords.Contains(CardKeyword.Exhaust) &&
                bottomCopy!.Keywords.Contains(CardKeyword.Ethereal) && bottomCopy.Keywords.Contains(CardKeyword.Exhaust),
                "Transcribe copies receive Ethereal and Exhaust");
            handCopy.AddKeyword(CardKeyword.Retain);
            Check(!bottomCopy.Keywords.Contains(CardKeyword.Retain), "Transcribe copy keywords are independent");

            // Plain and upgraded Thorn Burst stack 3 + 4 turns. The native
            // BeforeHandDraw hook is owner-scoped; a foreign player cannot consume it.
            await Reset();
            await Play(Create<ThornBurst>());
            await Play(Create<ThornBurst>(upgraded: true));
            ThornBurstPower thorn = player.Creature.GetPower<ThornBurstPower>()!;
            Check(thorn.Amount == 7, "ThornBurst plain plus upgraded duration is seven");
            int growthBefore = Session().Orbs.Value(OrbKind.Growth);
            await Hook.BeforeHandDraw(player.Creature.CombatState!, player, context);
            Check(thorn.Amount == 6 && Session().Orbs.Value(OrbKind.Growth) == growthBefore + 6,
                "ThornBurst triggers six Growth on owner hand draw");
            Player? other = player.Creature.CombatState!.Players.FirstOrDefault(candidate => candidate != player);
            if (other is not null)
            {
                int amountBefore = thorn.Amount;
                int valueBefore = Session().Orbs.Value(OrbKind.Growth);
                await thorn.BeforeHandDraw(other, context, player.Creature.CombatState!);
                Check(thorn.Amount == amountBefore && Session().Orbs.Value(OrbKind.Growth) == valueBefore,
                    "ThornBurst ignores a non-owner hand draw");
            }

            // A foreign player's real Fuel card participates in the shared end
            // hook, but its session remains a no-orb state. The ally is
            // disposable and is removed before this audit returns.
            await Reset();
            var combat = (CombatState)player.Creature.CombatState!;
            var foreign = Player.CreateForNewRun<Ironclad>(UnlockState.all, 941041);
            foreign.RunState = player.RunState;
            foreign.ResetCombatState();
            combat.AddPlayer(foreign);
            if (NCombatRoom.Instance!.GetCreatureNode(foreign.Creature) is null)
                NCombatRoom.Instance.AddCreature(foreign.Creature);
            try
            {
                Check(!LibrarianRuntime.TryGet(foreign, out _),
                    "foreign Fuel fixture starts without a Librarian session");
                await PlayerCmd.SetEnergy(99, foreign);
                CardModel fuel = combat.CreateCard<FuelTheFire>(foreign);
                await CardCmd.AutoPlay(context, fuel, null, skipCardPileVisuals: true);
                Check(foreign.Creature.GetPower<FuelTheFirePower>()?.Amount == 1,
                    "foreign Fuel card applies its native power");
                Check(LibrarianRuntime.TryGet(foreign, out LibrarianSession? foreignSession)
                    && foreignSession is { HasCharacterOrbs: false }
                    && foreignSession.Orbs.Positions.All(kind => foreignSession.Orbs.Value(kind) == 0
                        && !foreignSession.Orbs.IsActivated(kind) && !foreignSession.Orbs.IsLocked(kind)),
                    "foreign Fuel creates a no-orb mechanics session");
                CardModel foreignDefend = combat.CreateCard<DefendIronclad>(foreign);
                await CardPileCmd.AddGeneratedCardToCombat(foreignDefend, PileType.Draw, foreign, CardPilePosition.Bottom);
                int foreignBlock = foreign.Creature.Block;
                await Hook.BeforeSideTurnEnd(combat, CombatSide.Player, new[] { foreign.Creature });
                Check(foreignDefend.Pile?.Type != PileType.Draw && foreignDefend.Pile?.Type != PileType.Play
                    && foreign.Creature.Block == foreignBlock + 5,
                    "foreign Fuel native side-end hook plays bottom Defend for Block");
                Check(foreign.Creature.GetPower<FuelTheFirePower>()?.Amount == 1
                    && foreignSession!.Orbs.Positions.All(kind => foreignSession.Orbs.Value(kind) == 0
                        && !foreignSession.Orbs.IsActivated(kind) && !foreignSession.Orbs.IsLocked(kind)),
                    "foreign Fuel bottom play preserves power layer and no-orb state");
            }
            finally
            {
                combat.RemoveCreature(foreign.Creature);
            }

            // Archive Bulwark's preview and native Block amount both use the
            // current background total; upgrading doubles the multiplier.
            foreach (bool upgraded in new[] { false, true })
            {
                await Reset();
                await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Gain(OrbKind.Fire, 3));
                await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Gain(OrbKind.Tide, 4));
                await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Gain(OrbKind.Growth, 5));
                CardModel bulwark = Create<ArchiveBulwark>(upgraded);
                int expected = upgraded ? 14 : 7;
                // A native calculated preview only sees CombatState for a card
                // in a combat pile. Creation alone leaves Pile null.
                await CardPileCmd.AddGeneratedCardToCombat(bulwark, PileType.Hand, player);
                bulwark.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, bulwark.DynamicVars);
                Check(bulwark.DynamicVars.CalculatedBlock.PreviewValue == expected,
                    $"ArchiveBulwark CalculatedBlock preview {expected}");
                int block = player.Creature.Block;
                await Play(bulwark);
                Check(player.Creature.Block == block + expected,
                    $"ArchiveBulwark native Block {expected}");
            }

            MainFile.Logger.Info($"INTERACTIONS041_NATIVE_AUDIT_PASS checks={checks}");
        }
        catch (Exception error)
        {
            MainFile.Logger.Error($"INTERACTIONS041_NATIVE_AUDIT_FAIL checks={checks} {error}");
            throw;
        }
    }
}
