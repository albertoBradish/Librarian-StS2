# Building Librarian / 构建说明

The public tree contains the mod's authored source and production assets. It does
not include game files, decompiled sources, dependency DLLs, private configuration,
saves, original planning spreadsheets, or the internal research history.

## Pure rules, without the game

Install **.NET SDK 9.0.317** (`global.json` disables roll-forward), then run:

```powershell
dotnet run --project tests/Librarian.Core.Tests/Librarian.Core.Tests.csproj -c Release
```

The console test runner exits nonzero on failure. CI runs Core and Progress JSON
tests, plus the offline Loader and bundle tests when their files are present.
It does not build the proprietary game integration or certify native gameplay.

## Unified V1.4.11 development build (Windows)

The six content DLLs share one source tree. The loader selects the DLL for the installed game's exact release version. The shared PCK contains the production art and animations covered by the asset license.

Install .NET SDK 9.0.317 in `.research/tools/dotnet/`, MegaDot 4.5.1 mono in `.research/tools/megadot/`, and provision these private game copies from legally obtained installations. The builder checks each game assembly against its SHA-256 before deployment.

| Game | Commit | Local SDK directory |
| --- | --- | --- |
| 0.107.1 | 59260271 | `.research/channels/stable/workspace/.research/ritsu-game/` |
| 0.111.0 | 41cef1ea | `.research/ritsu-game/` |
| 0.111.1 | cc1f9462 | `.research/game-sdk-20261116/steam-0.111.1/` |
| 0.113.0 | 16ac0fe3 | `.research/game-sdk-20261112/steam-0.113.0/` |
| 0.113.1 | d09d48e5 | `.research/game-sdk-20261114/steam-0.113.1/` |
| 0.114.0 | a7b9a9c0 | `.research/game-sdk-20261125/steam-0.114.0/` |

Supply the full upstream RitsuLib GitHub bundles in `.research/tools/ritsulib/v0.6.13/` and `.research/tools/ritsulib/v0.6.16/`. The builder uses 0.6.16 to compile the 0.114.0 variant and run all isolated targets. It compiles the other variants against 0.6.13. The exact per-file checksums are in [ritsulib-build-inputs.json](scripts/ritsulib-build-inputs.json). Copy `src/Librarian.Mod/Directory.Build.props.example` to its ignored sibling `Directory.Build.props`. Run from the repository with Python 3.12 or later.

```powershell
python scripts/build_unified_librarian.py --pack
```

NuGet restores Godot.NET.Sdk 4.5.1. The builder compiles all six targets and the loader, exports the PCK, checks the bundle, and deploys to `.research/unified-v1.4.11/runtimes/<game-version>/`. The candidate at `outputs/revision-v1.4.11/candidate/Librarian/` contains four root files and six `compat/<game-version>/Librarian.dll` files. A single `--target <game-version>` performs a diagnostic compile. Keep the original Steam game and its saves separate from all test runs.

Historical `Development*.cs` audit helpers remain compiled because production
entry points reference them. Some opt-in audits contain maintainer-specific
output paths and need review before use on another computer. They are disabled
in normal play. Compilation alone does not certify menu/new-run/combat/save/reload
or multiplayer operation. A release must retain its own menu/new-run/combat/save/reload
evidence for both fixed game channels. A fresh public-clone native build remains
separate from the maintainer's local evidence.

Before running an isolated game, independently verify save paths and Steam Cloud
isolation. Do not run the internal recovery modes as a normal player install step.
The maintainer's `run_unified_game.py` and runtime-log policy tool are internal
native-validation infrastructure and are not in the public export allowlist.
Their local results do not certify a separate public-clone runtime. Offline
loader tests use `dotnet run --project tests/Librarian.Loader.Tests/Librarian.Loader.Tests.csproj -c Release`;
bundle tests use `python tests/test_librarian_bundle.py`.
