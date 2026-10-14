# Librarian 1.3.2 loader

The package renames the build output `Librarian.Loader.dll` to its root `Librarian.dll`. Its internal identity remains `Librarian.Loader`; each selected content DLL has identity `Librarian, Version=1.3.2.0`. The loader uses the game's assembly context and already loaded game/Godot/Harmony/RitsuLib modules. Private reference DLLs are never copied or distributed.

`librarian-variants.manifest` is JSON with exactly these fields:

```json
{
  "schema_version": 1,
  "mod_id": "Librarian",
  "mod_version": "1.3.2",
  "variants": [
    { "game_version": "0.107.1", "assembly": "Librarian", "path": "compat/0.107.1/Librarian.dll", "sha256": "<64 hexadecimal digits>" },
    { "game_version": "0.111.0", "assembly": "Librarian", "path": "compat/0.111.0/Librarian.dll", "sha256": "<64 hexadecimal digits>" }
  ]
}
```

Both variant files are validated for SHA-256 and assembly identity before loading either. Relative paths are fixed and symbolic links/junctions beneath the package root are rejected. The release label comes from native `ReleaseInfoManager`, accepting only `0.107.1`, `v0.107.1`, `0.111.0`, and `v0.111.0`. The native manifest must declare the matching mod ID/version. Unknown or missing game metadata stops initialization.

The beta host's native `AssociateAssemblyWithMod` is used immediately. The stable host overwrites its single `Mod.assembly` after returning from a mod initializer, so content association occurs in the matching `OnModDetected` event. RitsuLib's public `ModTypeDiscoveryHub.RegisterModAssembly` is called before `Librarian.LibrarianCode.MainFile.Initialize`; content retains its own Godot script registration order. A successful duplicate initializer call is a no-op; reentry or retries after partial failure require a restart.

The same native event marks initialization failures `Failed` and appends native `MOD_ERROR.EXCEPTION` text. This is necessary because both hosts catch initializer exceptions, append an error, and then otherwise assign `Loaded`. The event subscription only handles the exact current mod instance and removes itself after completion. Runtime acceptance must separately confirm models, custom scripts/scenes, menus, combat, and save/reload in both isolated games.

Build with the bundled .NET 9 runtime and optional `OutputPath` / `BaseIntermediateOutputPath`; there are no deployment targets. Offline tests link only the manifest and initialization gate and do not load private game dependencies:

```powershell
& .research/tools/dotnet/dotnet.exe build src/Librarian.Loader/Librarian.Loader.csproj -c Release
& .research/tools/dotnet/dotnet.exe run --project tests/Librarian.Loader.Tests/Librarian.Loader.Tests.csproj -c Release
```
