# Release batches / 发布批次

## 2026-10-04 source maintenance / 源码维护

`main` and `codex/beta` now include a maintenance change to the update notice: the
Librarian uses RitsuLib's timed toast (8 seconds) instead of automatically opening
the details dialog. Clicking the toast opens the existing dialog. Explicit
per-version suppression and manual redisplay remain available.

`main` 与 `codex/beta` 新增更新通知维护：复用 RitsuLib 的 8 秒限时提示，玩家点击后
才打开原有详情；保留当前版本“不再显示”和设置主动重显。中文提示为
“图书管理员已更新至【版本号】，点此查看更多”。

The implementation was checked on each channel's fixed native game with bilingual
mouse input, both 1280×720 and 1920×1080 windows, timeout behavior, process restarts,
and a new run / combat / save / reload. Each channel passed 67 notice checks.
The public source receives this focused change; other unpublished development
batches retain their existing status.

两通道分别通过 67 项通知检查、双语鼠标操作、双窗口尺寸、超时及独立进程重启，
并核查新局／战斗／保存重载。用户最终观感及实体手柄对提示的直接操作尚未验收。

This is source maintenance pending a future release. Steam packages, version
numbers, release-metadata.json runtime hashes, and existing GitHub release tags
and attachments remain their published baselines. No new binary release is
created by this source push.

本次是待后续发版的源码维护；Steam 安装包、版本号、release-metadata.json 中的
运行文件哈希及既有 GitHub 标签／附件保留已发布基线，本次源码推送不创建新发行。

本轮同步 `codex/beta` 的 V1.2.0-beta6 源码。以已发布 beta5 为基础，本批包含四项遗物与两项药水修订，以及波涛与浪潮在回合结束时带来的潮涌格挡预览。beta5 的法球文案改动与此前卡牌内容继续沿用；教学、精简提示和调试初始化不在本版。

This update synchronizes V1.2.0-beta6 source on `codex/beta`, building on published beta5. It includes four relic and two potion revisions, plus an end-turn preview of Tidal Block from Tide and Waves. Earlier beta5 Orb wording and card content remain in the source; character practice, compact hovers and debug initialization are excluded.

| Beta version | Scope / 范围 | Status / 状态 |
| --- | --- | --- |
| V1.2.0-beta5 | 法球文案 / Orb value wording | 已发布，服务器核验通过 / Published; server verified |
| V1.2.0-beta6 | 四项遗物与两项药水修订；回合结束潮涌格挡预览 / Four relic and two potion revisions; end-turn Tidal Block preview | 已发布，服务器核验通过 / Published; server verified |
| V1.2.0-beta7 | 角色教学、精简提示、入口修复、金色描边与21项教学文案 / Character practice, compact hovers, entry fixes, gold outline and 21 teaching text updates | 未发布，须独立验收 / Unpublished; separate release acceptance required |
| V1.2.0-beta8 | 二次确认调试初始化、设置刷新 / Confirmed debug initialization and settings refresh | 未发布，须独立验收 / Unpublished; separate release acceptance required |

后续批次不包含在 beta6 中。各批仍需分别确认范围和验收；本地实现或测试完成不等于已经发布。

Later batches are excluded from beta6. Each needs its own scope and acceptance. Local implementation or testing does not establish publication.

`main` 保持已发布 stable V1.1.1 源码。本轮不公开 stable 尚未发布的教学、初始化或底牌自动打牌选牌修复；选牌修复等待独立 stable 维护版本。

`main` retains published stable V1.1.1 source. This update excludes unpublished stable practice, initialization and bottom-autoplay selection fixes; the selection fix awaits a separate stable maintenance release.

GitHub 源码同步与独立 Release 分别记录。独立下载仍为 beta V1.1.0-beta1 和 stable V1.1.0；本轮只同步 beta 源码，不新增 Release，也不替换历史标签或附件。

GitHub source synchronization is separate from runtime Releases. Separate downloads remain beta V1.1.0-beta1 and stable V1.1.0. This batch synchronizes beta source only; it creates no Release and replaces no historical tag or attachment.
