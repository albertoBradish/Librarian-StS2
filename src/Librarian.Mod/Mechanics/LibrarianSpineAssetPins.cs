namespace Librarian.Mechanics;

// Exact V1.3.5 feedback authored bank and independently restored native resource identities.
internal sealed record LibrarianSpineAssetPins(string AssetRoot, string ReceiptSha256,
    string SkeletonSha256, string AtlasSha256, string BankSha256, string ContractSha256)
{
    internal const string SceneRoot = "res://Librarian/scenes/character/feedback-20261013";
    internal string ReceiptPath => AssetRoot + "/runtime-receipt.json";
    internal string SkeletonPath => AssetRoot + "/skeleton-file.spskel";
    internal string AtlasPath => AssetRoot + "/atlas.spatlas";
    internal string BankPath => AssetRoot + "/bank.res";
    internal static string CombatScene => SceneRoot + "/combat.tscn";
    internal static string MerchantScene => SceneRoot + "/shop.tscn";
    internal static string RestScene => SceneRoot + "/rest.tscn";
    internal static LibrarianSpineAssetPins Prepared() => new(
        "res://Librarian/spine/v1.3.5-feedback",
        "d94a83647c9e75d24fa6415fd8e12cc8f1e45d1d243241535dea225603782d24", "ab2aec0d7381e5b4e96b328d8b9012288a2beace8f5fa6f0662cb4beb77d3dbe", "1c982d71291edc423d85dbe78b60e7bdcf5f2660d03701c6c70b809befb1446a",
        "9a05864c3fa14f007ffaf1943b1dba238df21d4f360fa7a455238fce37b70f12", "61bb2ea1860848a3d5c8845509c564e380ded4619cbb97824985747d63c3c7f4");
}
