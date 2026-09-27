# License review / 发布前协议核查

Reviewed: **2026-09-27**. Baseline: Librarian **1.0-beta3**, game **0.111.0 public-beta**.

## Decision

BaseLib 3.4.5 and RitsuLib 0.6.2 use MIT. Neither requires this mod's original
code, setting or artwork to be open-sourced or MIT-licensed. Preserve notices for
included upstream copies/substantial portions. GitHub Releases/Issues do not
require an open-source license.

The author selected **Librarian Personal Use and Original Content License 1.0**
(`LicenseRef-Librarian-Personal-Use-1.0`): publicly readable source for personal
study/builds, reserving adaptation, redistribution and commercial/secondary
monetization rights. This is **not OSI open source**. Upstream MIT portions retain
their original rights and cannot be made noncommercial by project terms.
See [LICENSE](LICENSE), [asset scope](ASSET-LICENSE.md) and [notices](THIRD-PARTY-NOTICES.md).

## Evidence

| Input | Primary evidence |
| --- | --- |
| BaseLib 3.4.5 | [tag LICENSE](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/LICENSE.txt); local NuGet `.nuspec` declares MIT and commit `4a97642d7843309cdf35c46a11e3f46132cee049`, whose LICENSE also agrees |
| RitsuLib 0.6.2 | [version LICENSE](https://github.com/BAKAOLC/STS2-RitsuLib/blob/v0.6.2/LICENSE), agrees with local pinned source |
| Template 2.5.2 | [pinned project metadata](https://github.com/Alchyr/ModTemplate-StS2/blob/55ca2c606e6c78dd39689a5cf979b243a49652e7/Alchyr.Sts2.Templates.csproj), `PackageLicenseExpression=MIT` |
| ModAnalyzers 0.1.9 | [versioned NuGet metadata](https://www.nuget.org/packages/Alchyr.Sts2.ModAnalyzers/0.1.9), also checked locally |
| Harmony | [upstream MIT](https://github.com/pardeike/Harmony/blob/master/LICENSE); DLL stays separate |
| Godot 4.5.1 | [versioned MIT](https://github.com/godotengine/godot/blob/4.5.1-stable/LICENSE.txt); tools/runtime stay separate |

License hashes and retrieval URLs are in `licenses/sources.json`. No dependency
upgrade or fresh native runtime validation is implied. `Krafs.Publicizer` is
conditional and disabled (`Publicize=False`); PckPacker is commented out.
Review both before enabling. Engine extensions/transitive binaries are not bundled.

## Mega Crit boundary

The official [Content Policy](https://megacrit.com/content-policy/) permits modding
subject to its content/platform conditions and forbids implying official affiliation.
It permits donations for modding work but prohibits other mod monetization,
including paywalls and stretch-goal fundraising, even for charity. Project permission
cannot waive those restrictions. Project terms additionally prevent unauthorized
reuse of original content and fundraising in its name.

The policy treats gameplay videos and merchandise separately. Those permissions
do not freely license this mod's original assets. The merchandise permission is
limited and does not cover generative-AI art; no blanket merchandise permission
is granted here. Recheck the live policy before any commercial proposal.

中文结论：前置 MIT 不强迫本模组开源。原创内容采用自定义限制性许可，源码公开
可读但不称为开源；原创设定、美术、文案等未经许可不得改编、再发布或二次盈利。
Mega Crit 的禁止事项不能被本项目作者的许可豁免。上游第三方已有 MIT 权利保留。
