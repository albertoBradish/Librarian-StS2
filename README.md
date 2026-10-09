# 图书管理员 · Librarian

**V1.2.0-beta11 仅需 RitsuLib 0.6.4，已移除 BaseLib 前置。历史发行按各自原清单安装。**

**V1.2.0-beta11 requires RitsuLib 0.6.4 only. BaseLib is no longer required. Historical releases retain their original dependencies.**

<p align="center">
  <img src="assets/librarian-avatar.png" alt="图书管理员 Q版头像" width="240" height="240">
</p>

<p align="center">浮空藏书馆 · 三元素法球 · Slay the Spire 2 原创角色</p>

本分支提供游戏 **public-beta 0.111.0** 的 **V1.2.0-beta11** 源码与[独立 GitHub 预发行包](https://github.com/albertoBradish/Librarian-StS2/releases/tag/v1.2.0-beta11)。全量内容已迁移至 RitsuLib，保留原 ID 与现有美术；修复深海屏障回合末新增浪潮的格挡结算，以及四张攻击牌的附魔伤害显示，并包含回合末格挡净变化预览与 12 步实战教学。Steam 同步至 beta11；范围与验证限制见 [源码与发布状态](RELEASE-BATCHES.md)。

This branch provides **V1.2.0-beta11 source and a [separate GitHub pre-release](https://github.com/albertoBradish/Librarian-StS2/releases/tag/v1.2.0-beta11) for public-beta 0.111.0**. All content now uses RitsuLib, retaining published IDs and existing artwork. It fixes Deep Sea Barrier's end-turn Waves/Block settlement and enchanted damage displays on four attacks, and includes the net Block preview and 12-step tutorial. Steam Workshop is also beta11. [`main`](https://github.com/albertoBradish/Librarian-StS2/tree/main) retains stable 0.107.1 and its own maintenance.

An original playable character mod for **Slay the Spire 2**: a floating magical
archive with three elemental orbs, 91 active cards, 9 relics and 3 potions.
Includes Simplified Chinese and English, independent language settings, and
custom translation packs.

图书管理员通过烈焰、波涛、翠叶三种法球的注魔、结算与锁定组织战斗。
本仓库用于源码维护、独立版本下载和玩家反馈。

- [Releases / 独立下载](https://github.com/albertoBradish/Librarian-StS2/releases)
- [Latest GitHub pre-release / 最近的 GitHub 测试发行 1.2.0-beta11](https://github.com/albertoBradish/Librarian-StS2/releases/tag/v1.2.0-beta11)。下载同一发行的三份运行文件与许可。Download the three runtime files and notices from the same release.
- [Source and release status / 源码与发布状态](RELEASE-BATCHES.md)。Steam V1.2.0-beta11 已经服务器核验；订阅端收到新版仍待确认。Steam beta11 is server-verified; subscriber delivery remains unverified.
- [Historical release index / 历史版本与校验来源](release-history.json)
- [Migration verification / 22 个版本、352 个附件下载核验](release-history/verification.json)
- [Issues / 问题与建议](https://github.com/albertoBradish/Librarian-StS2/issues)
- [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3801958367)
- [Build / 构建](BUILDING.md) · [Release / 发版](RELEASING.md) · [Contributing](CONTRIBUTING.md)
- [Translation packs / 翻译包](TRANSLATIONS.md)

## A story in seven chapters / 七章原创时间线

七章中英故事随现有解锁进度逐步展开，搭配七张章节插图、原生多色文字强调与少量文字动效。从旧藏书馆中的沉睡，到独自整理散卷、研究药剂，浮空书体用自己的方式记录旅程。故事属于模组原创补叙；阅读章节不增加新的解锁要求。

Seven original chapters in Simplified Chinese and English unfold through the existing unlock progression. Seven illustrations, native colored emphasis and restrained text animation accompany a floating book's journey through scattered records and practical experiments. This is original mod fiction, with no additional chapter-unlock requirements.

<p align="center"><img src="src/Librarian.Mod/Librarian/images/timeline/v1.0.2-beta2/epoch-01.png" alt="入馆：旧藏书馆中的浮空书体 / Entry: the floating book in an old archive" width="760"></p>

### Three elements, scattered scrolls / 三元素与散落书卷

烈焰、波涛、翠叶通过注魔、结算与锁定相互配合。锁定显示可选默认的红色负数回合、旧回合显示，或球心原值加下方回合；显示方式不改变法球规则。

Channel, evoke and lock Fire, Tide and Growth orbs. Choose red negative lock turns, the old turn counter, or the original value with turns below. These display choices leave orb rules unchanged.

<p align="center"><img src="src/Librarian.Mod/Librarian/images/timeline/v1.0.2-beta2/epoch-03.png" alt="随身藏品：散落书卷中的元素魔法 / Carried Collection: elemental magic from scattered scrolls" width="760"></p>

### Notes, experiments and choices / 笔记、实验与选择

药剂笔记以手与器物讲述故事。显示、声音和语言设置可从主页右上角的 RitsuLib 页面进入；恢复默认设置保留解锁与游玩进度。开始提示的“确定”仅关闭本次，选择“不再显示”才停止当前版本的自动提示。

Potion notes tell their story through hands and objects. Open Librarian's display, audio and language settings from RitsuLib at the top right of the main menu. Restoring defaults preserves unlock and play progress. “OK” closes the current introduction; “Don't show again” disables automatic introductions for that version.

<p align="center"><img src="src/Librarian.Mod/Librarian/images/timeline/v1.0.2-beta2/epoch-04.png" alt="药剂笔记：分离双手试验药水 / Potion Notes: detached hands testing potions" width="760"></p>

## Compatibility / 兼容范围

Current source: **1.2.0-beta11**, for the game's public-beta branch.
[Source fingerprint](source-snapshot.json) records public authored inputs; [release metadata](release-metadata.json) identifies the validated beta11 build and Steam delivery. The version tag resolves to the exact public source commit used for this release.

| Component | Verified baseline |
| --- | --- |
| Slay the Spire 2 | **0.111.0 public-beta**, BuildID 24724944, commit 41cef1ea |
| BaseLib | Not required by beta11; historical releases retain their own manifests |
| [RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib/releases/tag/v0.6.4) | 0.6.4, complete bundle with `compat/0.111.0/` and `shared/` |

Manifest minimum versions are not a promise of compatibility with newer game
or dependency versions. Single-player is the primary supported path. Multiplayer
integration exists, but real multi-client/reconnect testing and long-term balance
validation remain incomplete. Visual/audio feedback is welcome.

## Install / 安装

1. Use the game branch/version required by the chosen release.
2. Beta11 requires **the complete RitsuLib 0.6.4 bundle only**, with `compat/0.111.0/` and `shared/`. Use its official release. Historical packages retain their original manifests.
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

开发与美术制作使用了 AI 辅助，内容由作者审阅并根据玩家反馈调整。
AI tools assisted programming and artwork production. The author reviews the content and revises it with player feedback.
