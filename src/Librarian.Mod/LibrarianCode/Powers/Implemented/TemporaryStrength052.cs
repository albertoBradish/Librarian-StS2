using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.Stateful;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Scaffolding.Content.Patches;

namespace Librarian.LibrarianCode.Powers.Implemented;

// Native temporary Strength owns stacking, debuff prevention and side-turn restoration.
public abstract class LibrarianTemporaryStrengthLossPower : TemporaryStrengthPower, IModPowerAssetOverrides
{
    public PowerAssetProfile AssetProfile => PowerAssetProfile.Empty;
    protected override bool IsPositive => false;
    public string CustomIconPath => "res://images/atlases/power_atlas.sprites/piercing_wail_power.tres";
    public string CustomBigIconPath => "res://images/powers/piercing_wail_power.png";
}
public sealed class RootbindPower : LibrarianTemporaryStrengthLossPower
{
    public override AbstractModel OriginModel => ModelDb.Card<Rootbind>();
}
public sealed class BookwormPower : LibrarianTemporaryStrengthLossPower
{
    public override AbstractModel OriginModel => ModelDb.Card<Bookworm>();
}
