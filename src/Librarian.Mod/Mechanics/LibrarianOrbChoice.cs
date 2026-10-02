using System.Runtime.CompilerServices;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

/// <summary>Native synchronized choice screen; temporary previews never enter a combat pile or reward pool.</summary>
internal static class LibrarianOrbChoice
{
    internal sealed record Choice(OrbKind Kind, bool RestorePreview, int Current, int Highest);
    internal static readonly ConditionalWeakTable<CardModel, Choice> Choices = new();

    internal static async Task<OrbKind?> Choose(PlayerChoiceContext context, CardModel source, IReadOnlyList<OrbKind> candidates)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        var previews = CreatePreviews(source, candidates);
        var selected = await CardSelectCmd.FromChooseACardScreen(context, previews, source.Owner);
        return selected is not null && Choices.TryGetValue(selected, out var choice) ? choice.Kind : null;
    }

    internal static CardModel[] CreatePreviews(CardModel source, IReadOnlyList<OrbKind> candidates)
    {
        var session = LibrarianRuntime.Get(source.Owner);
        return candidates.Select(kind =>
        {
            CardModel card = kind switch
            {
                OrbKind.Fire => ModelDb.Card<Spark>().ToMutable(),
                OrbKind.Tide => ModelDb.Card<Trickle>().ToMutable(),
                _ => ModelDb.Card<Renewal>().ToMutable()
            };
            card.Owner = source.Owner;
            // Native boss choices use a negative, non-X cost to hide the badge.
            // Only these temporary previews change; the real starter cards keep their cost.
            card.EnergyCost.SetCustomBaseCost(-1);
            Choices.Add(card, new(kind, source is ReRead, session.Orbs.Value(kind), session.Orbs.HighestValueThisCombat(kind)));
            return card;
        }).ToArray();
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Title), MethodType.Getter)]
internal static class OrbChoiceTitle
{
    [HarmonyPostfix] private static void Postfix(CardModel __instance, ref string __result)
    {
        if (LibrarianOrbChoice.Choices.TryGetValue(__instance, out var choice))
            __result = new LocString("cards", $"LIBRARIAN-ORB_CHOICE_{choice.Kind.ToString().ToUpperInvariant()}.title").GetFormattedText();
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Description), MethodType.Getter)]
internal static class OrbChoiceDescription
{
    [HarmonyPostfix] private static void Postfix(CardModel __instance, ref LocString __result)
    {
        if (LibrarianOrbChoice.Choices.TryGetValue(__instance, out var choice))
        {
            if (choice.RestorePreview)
            {
                __result = new LocString("cards", "LIBRARIAN-ORB_CHOICE_REREAD.description");
                __result.Add("Current", choice.Current);
                __result.Add("Highest", choice.Highest);
                __result.Add("Restore", Math.Max(0, choice.Highest - choice.Current));
                return;
            }
            __result = new LocString("cards", $"LIBRARIAN-ORB_CHOICE_{choice.Kind.ToString().ToUpperInvariant()}.description");
        }
    }
}
