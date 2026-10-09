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
| `.research/ritsu-game/` | isolated copy of Slay the Spire 2 0.111.0 public-beta |
| `.research/tools/ritsulib/v0.6.4/` | complete RitsuLib 0.6.4 bundle, `RitsuLib.References.props`, `compat/0.111.0/`, `shared/` |

Acquire RitsuLib through their official upstream projects linked in
the README. MegaDot is the game-compatible Godot distribution supplied by the
game's modding toolchain; an arbitrary stock Godot binary is not a substitute.
The engine and its proprietary extensions are not redistributed by this project.

The expected game assembly is
`data_sts2_windows_x86_64/sts2.dll`, SHA256
`0861bfa1df347538d932f22d580e75420f08082792eb914e53b4882764acdbe9`.
The project rejects mismatched versions and deployment to the original Steam
installation. Do not disable those guards to make an unsupported build pass.

Copy `src/Librarian.Mod/Directory.Build.props.example` to the ignored
`src/Librarian.Mod/Directory.Build.props`, then run with Python 3.10 or newer:

```powershell
python scripts/build_librarian.py --pack
```

NuGet restores Godot.NET.Sdk 4.5.1. BaseLib and ModAnalyzers are absent from the current development package graph. The local
builder expects an existing `.research/nuget-packages/` directory or a populated
user NuGet cache; create the workspace directory for an online fresh restore.
Output is limited to `.research/ritsu-game/mods/Librarian/`. Build logs
and source/dependency fingerprints remain under `.research/logs/`.

Historical `Development*.cs` audit helpers remain compiled because production
entry points reference them. Some opt-in audits contain maintainer-specific
output paths and need review before use on another computer. They are disabled
in normal play. Compilation alone does not certify menu/new-run/combat/save/reload
or multiplayer operation. The full migration has isolated native menu/new-run/combat/save/reload validation in both fixed game channels. A fresh public-clone native build remains separate from that local evidence.

Before running an isolated game, independently verify save paths and Steam Cloud
isolation. Do not run the internal recovery modes as a normal player install step.
