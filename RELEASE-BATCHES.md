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
