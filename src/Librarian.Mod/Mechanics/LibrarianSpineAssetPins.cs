namespace Librarian.Mechanics;

// Exact V1.3.12 delayed ribbons authored bank and independently restored native resource identities.
internal sealed record LibrarianSpineAssetPins(string AssetRoot, string ReceiptSha256,
    string SkeletonSha256, string AtlasSha256, string BankSha256, string ContractSha256)
{
    internal const string SceneRoot = "res://Librarian/scenes/character/ribbons-20261017";
    internal string ReceiptPath => AssetRoot + "/runtime-receipt.json";
    internal string SkeletonPath => AssetRoot + "/skeleton-file.spskel";
    internal string AtlasPath => AssetRoot + "/atlas.spatlas";
    internal string BankPath => AssetRoot + "/bank.res";
    internal static string CombatScene => SceneRoot + "/combat.tscn";
    internal static string MerchantScene => SceneRoot + "/shop.tscn";
    internal static string RestScene => SceneRoot + "/rest.tscn";
    internal static LibrarianSpineAssetPins Prepared() => new(
        "res://Librarian/spine/v1.3.12-ribbons",
        "f3502fdee5b80bad9cb0cd9bf68359a6b0ecd9ec57724951de2b97e8f5298e2d", "26bde91966732edf1b0c50288c5d7c29ebbc2410ed547181b28addf7b3d4b971", "94c9fd6f56801bac991fafead2f47f8b42626fccab1eea3af0d140d37826dd1b",
        "5f1852b217cb2f003aa77464729e0a56435faca04842f352ddd42947fe6f9d0c", "920c4c635e2188ebd06d063e095e07ede162711da6378bef358ea808cc5e6372");
}
