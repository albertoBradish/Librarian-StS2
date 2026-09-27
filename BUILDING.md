# Building Librarian / 构建说明

The public tree contains the mod's authored source and production assets. It does
not include game files, decompiled sources, dependency DLLs, private configuration,
saves, original planning spreadsheets, or the internal research history.

## Pure rules, without the game

Install **.NET SDK 9.0.317** (`global.json` disables roll-forward), then run:

```powershell
dotnet run --project tests/Librarian.Core.Tests/Librarian.Core.Tests.csproj -c Release
```

The console test runner exits nonzero on failure. CI runs this suite only; it does
not build the proprietary game integration or certify native gameplay.

## Full mod build (Windows)

Supply legally obtained, matching private dependencies locally:

| Location relative to repository | Required content |
| --- | --- |
| `.research/tools/dotnet/` | .NET SDK 9.0.317, including `dotnet.exe` |
| `.research/tools/megadot/` | `MegaDot_v4.5.1-stable_mono_win64_console.exe` and matching runtime files |
| `.research/compatibility-game/` | isolated copy of Slay the Spire 2 0.107.1 stable |
| `.research/compatibility-game/mods/BaseLib/` | official BaseLib 3.4.5 runtime |
| `.research/tools/ritsulib/v0.6.2/` | complete RitsuLib 0.6.2 bundle, `RitsuLib.References.props`, `compat/0.107.1/`, `shared/` |

Acquire BaseLib and RitsuLib through their official upstream projects linked in
the README. MegaDot is the game-compatible Godot distribution supplied by the
game's modding toolchain; an arbitrary stock Godot binary is not a substitute.
The engine and its proprietary extensions are not redistributed by this project.

The expected game assembly is
`data_sts2_windows_x86_64/sts2.dll`, SHA256
`a1f9e653f1e28e4076558fee1e60d218619cb7e057b887c6417f62c62c6d7a52`.
The project rejects mismatched versions and deployment to the original Steam
installation. Do not disable those guards to make an unsupported build pass.

Copy `src/Librarian.Mod/Directory.Build.props.example` to the ignored
`src/Librarian.Mod/Directory.Build.props`, then run with Python 3.10 or newer:

```powershell
python scripts/build_librarian.py --pack
```

NuGet restores BaseLib 3.4.5, ModAnalyzers 0.1.9 and Godot.NET.Sdk 4.5.1. The local
builder expects an existing `.research/nuget-packages/` directory or a populated
user NuGet cache; create the workspace directory for an online fresh restore.
Output is limited to `.research/compatibility-game/mods/Librarian/`. Build logs
and source/dependency fingerprints remain under `.research/logs/`.

Historical `Development*.cs` audit helpers remain compiled because production
entry points reference them. Some opt-in audits contain maintainer-specific
output paths and need review before use on another computer. They are disabled
in normal play. Compilation alone does not certify menu/new-run/combat/save/reload
or multiplayer operation. A fresh full public-clone native build has not yet been
performed; the release baseline has prior isolated native validation.

Before running an isolated game, independently verify save paths and Steam Cloud
isolation. Do not run the internal recovery modes as a normal player install step.

Use the official RitsuLib `STS2.RitsuLib.Compat.0.107.1.0.6.2.github.zip` bundle. `LIBRARIAN_DOTNET_ROOT` may point to a shared read-only SDK. Keep each game channel in a separate checkout with its own private game copy, dependencies, userdata and output. The stable adapter lives in `Mechanics/LibrarianStableApi.cs`; shared gameplay changes should be reviewed and ported between branches with both native checks. Never copy the whole beta build over stable.
