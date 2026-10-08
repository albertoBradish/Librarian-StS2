using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode.Cards.OrbUtility;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace Librarian.LibrarianCode.Cards;

[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-ANCIENT_SPARK")]
public sealed class AncientSpark() : OrbUtilityCard(1, CardType.Skill, CardRarity.Ancient, TargetType.Self)
{
    public override string PortraitPath => "res://Librarian/images/card_portraits/ancient_spark.png";
    public override string CustomPortraitPath => "res://Librarian/images/card_portraits/ancient_spark.png";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Fire", 7m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        await Gain(context, session, OrbKind.Fire, Amount("Fire"));
        await Settle(context, session, OrbKind.Fire, 2);
    }
    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}

// Extend the native starter transformation table: native upgrade/enchantment preservation,
// event availability and Dusty Tome exclusion then follow the game's existing path.
[HarmonyPatch(typeof(ArchaicTooth), "TranscendenceUpgrades", MethodType.Getter)]
internal static class LibrarianAncientSparkReplacement
{
    [HarmonyPostfix]
    private static void Postfix(Dictionary<ModelId, CardModel> __result)
        => __result[ModelDb.Card<Spark>().Id] = ModelDb.Card<AncientSpark>();
}
