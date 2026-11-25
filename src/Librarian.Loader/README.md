# Librarian loader

The package renames `Librarian.Loader.dll` to its root `Librarian.dll`. The bootstrap references the game and Godot. The native manifest starts only the bootstrap and declares no hard dependencies. On supported hosts the bootstrap waits for a usable RitsuLib and mounts the content PCK before initializing the selected DLL.

`librarian-variants.manifest` is JSON with exactly these fields:

```json
{
  "schema_version": 1,
  "mod_id": "Librarian",
  "mod_version": "1.4.11",
  "variants": [
    { "game_version": "0.107.1", "assembly": "Librarian", "path": "compat/0.107.1/Librarian.dll", "sha256": "<64 hexadecimal digits>" },
    { "game_version": "0.111.0", "assembly": "Librarian", "path": "compat/0.111.0/Librarian.dll", "sha256": "<64 hexadecimal digits>" },
    { "game_version": "0.111.1", "assembly": "Librarian", "path": "compat/0.111.1/Librarian.dll", "sha256": "<64 hexadecimal digits>" },
    { "game_version": "0.113.0", "assembly": "Librarian", "path": "compat/0.113.0/Librarian.dll", "sha256": "<64 hexadecimal digits>" },
    { "game_version": "0.113.1", "assembly": "Librarian", "path": "compat/0.113.1/Librarian.dll", "sha256": "<64 hexadecimal digits>" },
    { "game_version": "0.114.0", "assembly": "Librarian", "path": "compat/0.114.0/Librarian.dll", "sha256": "<64 hexadecimal digits>" }
  ]
}
```

Every variant file is validated for SHA-256 and assembly identity before loading the selected DLL. Relative paths are fixed and symbolic links/junctions beneath the package root are rejected. The release label comes from native `ReleaseInfoManager`. Each listed version can initialize its own DLL, with or without a leading `v`. Other labels and missing metadata leave the game content disabled and queue a native main-menu notice. Confirming the notice records it for that game version under `user://Librarian/compatibility-notices`.

Newer hosts use native `AssociateAssemblyWithMod`. The 0.107.1 host assigns its single `Mod.assembly` after returning from a mod initializer. Content association therefore occurs at the matching `OnModDetected` event or immediately when RitsuLib finished loading later. RitsuLib's `ModTypeDiscoveryHub.RegisterModAssembly` runs before the content initializer. A successful duplicate initializer call is a no-op. Reentry or a retry after partial failure requires a restart.

The same native event marks initialization failures `Failed` and appends native `MOD_ERROR.EXCEPTION` text. This is necessary because the hosts catch initializer exceptions, append an error, and then otherwise assign `Loaded`. The event subscription only handles the exact current mod instance and removes itself after completion. Runtime acceptance must separately confirm models, custom scripts/scenes, menus, combat, and save/reload in the isolated games.

Build with the bundled .NET 9 runtime and optional `OutputPath` / `BaseIntermediateOutputPath`; there are no deployment targets. Offline tests link only the manifest and initialization gate and do not load private game dependencies:

```powershell
& .research/tools/dotnet/dotnet.exe build src/Librarian.Loader/Librarian.Loader.csproj -c Release
& .research/tools/dotnet/dotnet.exe run --project tests/Librarian.Loader.Tests/Librarian.Loader.Tests.csproj -c Release
```
