> Archive version 0.4.0: runtime dependencies are BaseLib 3.4.5、STS2-RitsuLib 0.6.2. The notice bundle spans project history; listed licenses do not imply all components are required. The asset inventory describes the current source snapshot, not every historical asset. Identifiable upstream portions in this archived build retain upstream rights.

# Third-party notices / 第三方许可

Project-owned content follows the custom [LICENSE](LICENSE), not MIT. The
following upstream components retain their own permissions; project restrictions
do not override upstream MIT rights.

| Component | Version / evidence | License | Relationship |
| --- | --- | --- | --- |
| BaseLib | 3.4.5; tag `22757933ba10adc4322a628519a233a567507d87`; NuGet source commit `4a97642d7843309cdf35c46a11e3f46132cee049` | [MIT](licenses-BaseLib-MIT.txt) | Separate runtime; not bundled |
| RitsuLib | 0.6.2, commit `1bebdb0365c34f8dc9066ded81ac4587df145449` | [MIT, copyright 2026 OLC](licenses-RitsuLib-MIT.txt) | Separate complete runtime; not bundled |
| Alchyr CharacterModTemplate | 2.5.2, commit `55ca2c606e6c78dd39689a5cf979b243a49652e7` | [MIT](licenses-Alchyr-Template-MIT.txt) | Scaffold/helpers modified for this project; one unchanged image identified |
| Alchyr.Sts2.ModAnalyzers | 0.1.9, NuGet commit `46c6a91ff24d47062d6b28cb734a8f855e1da0b6` | MIT, package metadata | Build-time analyzer; not bundled |
| Harmony | 0Harmony reference from fixed game installation | [MIT, Andreas Pardeike](licenses-Harmony-MIT.txt) | Separate game-provided runtime; not bundled; exact assembly version not asserted |
| Godot .NET SDK / Godot | 4.5.1 | [MIT](licenses-Godot-MIT.txt) | Build/runtime reference; engine not bundled |

Upstream sources:

- https://github.com/Alchyr/BaseLib-StS2
- https://github.com/BAKAOLC/STS2-RitsuLib/tree/v0.6.2
- https://github.com/Alchyr/ModTemplate-StS2/tree/55ca2c606e6c78dd39689a5cf979b243a49652e7
- https://www.nuget.org/packages/Alchyr.Sts2.ModAnalyzers/0.1.9
- https://github.com/pardeike/Harmony
- https://github.com/godotengine/godot/tree/4.5.1-stable

The template package's project file explicitly declares MIT. No standalone
template LICENSE was present in the pinned snapshot; standard MIT text is
provided without inventing an upstream copyright line. Attribution to Alchyr is
retained here. [Template asset inventory](licenses-template-assets.json) records
exact paths/hashes. Upstream portions of modified template code retain upstream
MIT rights; project-authored additions follow the root license.

BaseLib's tag and NuGet metadata record different commits. Both licenses were
checked and agree; they are not represented as identical source snapshots.
This review does not replace notices in the separate upstream distributions.

The game, MegaDot proprietary extensions and game art/audio are not relicensed.
Game resources are resolved from the player's installation at runtime. Do not
include game DLLs, extracted resources/source or third-party executables in the
public tree or mod-only release. Review licenses again when updating dependencies.
