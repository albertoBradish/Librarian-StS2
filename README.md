# 图书管理员 · Librarian

**当前开发版 V1.3.0：RitsuLib 0.6.4 单前置，单包适配游戏 0.107.1 / 0.111.0。发布与两版验收状态分别记录，尚不能据此称新版已上线。历史发行按各自原清单安装。**

**V1.3.0 development uses RitsuLib 0.6.4 only and targets game 0.107.1 / 0.111.0 in one bundle. This source update does not announce a published release or completed native acceptance. Historical releases retain their original dependencies.**

<p align="center">
  <img src="assets/librarian-avatar.png" alt="图书管理员 Q版头像" width="240" height="240">
</p>

<p align="center">浮空藏书馆 · 三元素法球 · Slay the Spire 2 原创角色</p>

当前统一开发源码为 **V1.3.5**，在 `main` 维护，由引导层自动选择两个固定游戏目标的内容 DLL。包含后续烈焰表现、首次选角字体缓存及 Spine 动画修订。Steam 当前已发布包为 **V1.3.0**；V1.3.5 尚未发布运行包。本次分批同步仅更新自有源码与生产资源，旧 `codex/beta` 分支、标签及独立发行附件保留。完整同步身份见 `source-snapshot.json`。

The current **V1.3.5 development source** uses `main` and one loader selecting content for two fixed game APIs. It includes fire effects, first-selection font caching and Spine animation revisions. **V1.3.0 is published on Steam**; V1.3.5 has no published runtime package. This batched source sync preserves `codex/beta`, existing tags and runtime attachments. `source-snapshot.json` identifies the complete synchronized inputs.

An original playable character mod for **Slay the Spire 2**: a floating magical
archive with three elemental orbs, 91 active cards, 9 relics and 3 potions.
Includes Simplified Chinese and English, independent language settings, and
custom translation packs.

图书管理员通过烈焰、波涛、翠叶三种法球的注魔、结算与锁定组织战斗。
本仓库用于源码维护、独立版本下载和玩家反馈。

- [Releases / 独立下载](https://github.com/albertoBradish/Librarian-StS2/releases)
- [Published historical beta package / 已发布历史 beta 包 1.2.0-beta12](https://github.com/albertoBradish/Librarian-StS2/releases/tag/v1.2.0-beta12)。此历史版下载同一发行的三份运行文件与许可；V1.3.0 采用六文件结构。This historical release has three runtime files; V1.3.0 uses six.
- [Source and release history / 源码与发布历史](RELEASE-BATCHES.md)。历史发行状态保留；当前统一包身份另见[构建元数据](release-metadata.json)。Historical publication records remain separate from this unified source snapshot.
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

Current development source: **1.3.5**, with exact game-version selection.
[Release metadata](release-metadata.json) distinguishes current development source
from the published V1.3.0 Steam package and its artifact hashes. Steam server
verification does not establish subscriber download delivery. This source update
does not create a GitHub runtime release.

| Component | Verified baseline |
| --- | --- |
| Slay the Spire 2 | **0.107.1 public / 59260271** and **0.111.0 public-beta / 41cef1ea**; other versions stop at the loader |
| BaseLib | Not required by unified V1.3.0; historical releases retain their own manifests |
| [RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib/releases/tag/v0.6.4) | 0.6.4 compile baseline; complete bundle with both target `compat/` directories and `shared/` |

Manifest minimum versions are not a promise of compatibility with newer game
or dependency versions. Single-player is the primary supported path. Multiplayer
integration exists, but real multi-client/reconnect testing and long-term balance
validation remain incomplete. Visual/audio feedback is welcome.

## Install / 安装

1. Use a game branch/version supported by the chosen release. Unified V1.3.0 supports only the two exact versions above.
2. V1.3.0 requires **the complete RitsuLib bundle with baseline 0.6.4**, installed separately. Historical packages retain their original dependencies.
3. For an available unified GitHub release, download all six runtime assets and follow `INSTALL-MAP.json`. Place root `Librarian.dll`, `Librarian.json`, `Librarian.pck` and `librarian-variants.manifest` in `mods/Librarian/`. Rename `compat-0.107.1-Librarian.dll` and `compat-0.111.0-Librarian.dll` to `Librarian.dll` in their respective `compat/<version>/` subdirectories. Keep notices and verify `SHA256SUMS.txt`. Historical three-file releases use their own instructions.
4. For Workshop migration, subscribe to [3801958367](https://steamcommunity.com/sharedfiles/filedetails/?id=3801958367), quit the game, unsubscribe from old 3811677053, wait for the download, and restart with Librarian enabled. Keep saves. The old item remains public with a final migration notice after the new release succeeds; duplicate ModIds cannot guarantee that the new copy wins.
5. Avoid loading both Workshop and manual copies of Librarian. Exit the game
   before changing mod files, back up saves, and use a spare profile/new run when
   testing a beta. Follow the game's mod activation/restart prompts.

The automatically generated GitHub **Source code** archives are source snapshots,
not installable mod packages. Download the complete runtime attachment set for
the selected release. No full ZIP is produced by default.
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
