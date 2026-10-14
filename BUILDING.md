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

## Unified V1.3.5 development build (Windows)

The current development line compiles the same content source for two exact API
targets and a small `Librarian.Loader` entry assembly. The shared PCK contains
authored production resources, including the Spine banks. Steam V1.3.0 is already
published; this V1.3.5 source update creates no runtime release and does not claim
native validation of a fresh public clone.

Supply legally obtained, matching private dependencies locally:

| Location relative to repository | Required content |
| --- | --- |
| `.research/tools/dotnet/` | .NET SDK 9.0.317, including `dotnet.exe` |
| `.research/tools/megadot/` | `MegaDot_v4.5.1-stable_mono_win64_console.exe` and matching runtime files |
| `.research/ritsu-game/` | isolated copy of Slay the Spire 2 0.111.0 public-beta |
| `.research/channels/stable/workspace/.research/ritsu-game/` | separate isolated copy of Slay the Spire 2 0.107.1 public |
| `.research/tools/ritsulib/v0.6.4/` | complete RitsuLib 0.6.4 bundle, `RitsuLib.References.props`, both `compat/0.107.1/` and `compat/0.111.0/`, `shared/` |

Acquire RitsuLib through their official upstream projects linked in
the README. MegaDot is the game-compatible Godot distribution supplied by the
game's modding toolchain; an arbitrary stock Godot binary is not a substitute.
The engine and its proprietary extensions are not redistributed by this project.

Each game input uses `data_sts2_windows_x86_64/sts2.dll`. Exact identities are:

| Game / commit | SHA256 |
| --- | --- |
| 0.107.1 / 59260271 | `a1f9e653f1e28e4076558fee1e60d218619cb7e057b887c6417f62c62c6d7a52` |
| 0.111.0 / 41cef1ea | `0861bfa1df347538d932f22d580e75420f08082792eb914e53b4882764acdbe9` |

The project rejects mismatched versions and deployment to the original Steam
installation. Do not disable those guards to make an unsupported build pass.

Copy `src/Librarian.Mod/Directory.Build.props.example` to the ignored
`src/Librarian.Mod/Directory.Build.props`, then run with Python 3.12 or newer:

```powershell
python scripts/build_unified_librarian.py --pack
```

NuGet restores Godot.NET.Sdk 4.5.1. BaseLib and ModAnalyzers are absent from the current development package graph. The local
builder expects an existing `.research/nuget-packages/` directory or a populated
user NuGet cache; create the workspace directory for an online fresh restore.
The builder runs the two content compiles and loader compile serially, exports
one PCK, checks every artifact, and deploys only into new
`.research/unified-v1.3.0/runtimes/<game-version>/mods/Librarian/` copies. It does
not replace the two private input games. Logs, target-specific caches and source/
dependency fingerprints remain under `.research/unified-v1.3.0/`.

The candidate at `outputs/revision-v1.3.0/candidate/Librarian/` has exactly six
files: root `Librarian.dll` (internal identity `Librarian.Loader`), `Librarian.json`,
`Librarian.pck`, `librarian-variants.manifest`, and
`compat/0.107.1/Librarian.dll` / `compat/0.111.0/Librarian.dll`. Both content DLLs
retain identity `Librarian, Version=1.3.0.0`. Keep the directory structure intact;
the loader validates SHA-256 and loads only the matching target. Private game,
Godot, Harmony and RitsuLib binaries are neither copied into this mod nor
redistributed.

`--target 0.107.1` or `--target 0.111.0` is a compile-only diagnostic and cannot be
combined with `--pack`. A successful single-target compile is not a complete
unified bundle. Historical beta/stable trees retain their own
`build_librarian.py` workflow; the old BaseLib/test ZIP helper refuses current
unified source and no full ZIP is produced by default.

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
