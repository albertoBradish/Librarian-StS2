# 图书管理员 · Librarian

An original playable character mod for **Slay the Spire 2**: a floating magical
archive with three elemental orbs, 91 active cards, 9 relics and 3 potions.
Includes Simplified Chinese and English, independent language settings, and
custom translation packs.

图书管理员通过烈焰、潮涌、翠叶三种法球的注魔、结算与锁定组织战斗。
本仓库用于源码维护、独立版本下载和玩家反馈。

- [Releases / 独立下载](https://github.com/albertoBradish/Librarian-StS2/releases)
- [V1.0.0 / stable 正式版](https://github.com/albertoBradish/Librarian-StS2/releases/tag/v1.0.0-stable)
- [public-beta / 1.0-beta3](https://github.com/albertoBradish/Librarian-StS2/releases/tag/v1.0-beta3)
- [Historical release index / 历史版本与校验来源](release-history.json)
- [Migration verification / 22 个版本、352 个附件下载核验](release-history/verification.json)
- [Issues / 问题与建议](https://github.com/albertoBradish/Librarian-StS2/issues)
- [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3809003723)
- [Build / 构建](BUILDING.md) · [Release / 发版](RELEASING.md) · [Contributing](CONTRIBUTING.md)
- [Translation packs / 翻译包](TRANSLATIONS.md)

## Compatibility / 兼容范围

Current source baseline: **1.0.0**, game **stable 0.107.1**. See [release metadata](release-metadata.json).

`main` maintains stable; [`codex/beta`](https://github.com/albertoBradish/Librarian-StS2/tree/codex/beta) preserves game public-beta 0.111.0. Do not mix packages or enable both Workshop items. Historical tag `v1.0.0` remains an older bilingual test candidate; the formal release uses **`v1.0.0-stable`**.

| Component | Verified baseline |
| --- | --- |
| Slay the Spire 2 | **0.107.1 stable**, commit 59260271 |
| [BaseLib](https://github.com/Alchyr/BaseLib-StS2/releases/tag/v3.4.5) | 3.4.5 |
| [RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib/releases/tag/v0.6.2) | 0.6.2, complete bundle for game API 0.107.1 |

Manifest minimum versions are not a promise of compatibility with newer game
or dependency versions. Single-player is the primary supported path. Multiplayer
integration exists, but real multi-client/reconnect testing and long-term balance
validation remain incomplete. Visual/audio feedback is welcome.

## Install / 安装

1. Use the game branch/version required by the chosen release.
2. Install BaseLib and the complete RitsuLib bundle from their official releases;
   they are separate dependencies and are not bundled here.
3. Download `Librarian.dll`, `Librarian.json`, and `Librarian.pck` from the **same**
   GitHub release and place them in `mods/Librarian/` under your game installation.
   Keep the accompanying licenses/notices and verify `SHA256SUMS.txt` if provided.
4. Avoid loading both Workshop and manual copies of Librarian. Exit the game
   before changing mod files, back up saves, and use a spare profile/new run when
   testing a beta. Follow the game's mod activation/restart prompts.

The automatically generated GitHub **Source code** archives are source snapshots,
not installable mod packages. Download the three named runtime attachments.
Historical binary archive tags contain provenance/notices only when a matching
complete historical source tree is unavailable; they do not substitute current
source for old versions. See the per-version release notes and provenance.
Players do not need the .NET SDK or Godot editor. Workshop and GitHub are separate
distribution channels; each release states its own version and compatibility.

请勿混用不同版本的 DLL/JSON/PCK，也不要同时加载工坊与手动安装的重复副本。
卸载前退出游戏；含本角色内容的存档需要对应模组才能继续。

## Feedback / 反馈

Use an [issue template](https://github.com/albertoBradish/Librarian-StS2/issues/new/choose)
and include game branch/build, mod and dependency versions, installation channel,
other mods, reproduction steps, expected/actual behavior, and screenshots/logs
where useful. Review logs for account names, save data and tokens before posting.

## License / 许可证

**Source-available, not open source.** Project-owned code, setting, writing and
artwork follow the [Librarian Personal Use and Original Content License](LICENSE).
Personal study/builds are permitted; unauthorized adaptation, redistribution,
commercial use and secondary monetization are prohibited. See [original asset
scope](ASSET-LICENSE.md). Upstream MIT portions retain their own permissions:
[notices](THIRD-PARTY-NOTICES.md), [license review](LICENSE-AUDIT.md).

源码公开可读，不代表开源或可自由二创。原创设定、美术、文案等未经书面许可不得
改编、再发布、商用或二次盈利。所有许可首先服从 Mega Crit 的模组政策，作者的
授权不能豁免官方禁止的盈利行为。
This is an unofficial community mod, not affiliated with or endorsed by Mega Crit.
