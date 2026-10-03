# Release batches / 发布批次

本轮只同步 `codex/beta` 的 V1.2.0-beta5 源码。已发布 beta4 的32卡修订与 r3 先古卡图作为基线；本批更新97个法球文案条目（中文52、英文45），保留动作简称，数值增减与清空改为直接写法球数值，并移除“强化”玩家提示。规则、数值与美术沿用 beta4。

This update synchronizes V1.2.0-beta5 source on `codex/beta`. Published beta4's 32-card revision and approved r3 Ancient artwork form its baseline. The batch updates 97 Orb localization entries (52 Chinese, 45 English), retains action keywords, writes stored-value changes explicitly, and removes the Strengthen player tooltip. Rules, values and artwork retain the beta4 baseline.

| Beta version | Scope / 范围 | Status / 状态 |
| --- | --- | --- |
| V1.2.0-beta5 | 法球文案 / Orb value wording | 已发布，服务器核验通过 / Published; server verified |
| V1.2.0-beta6 | 角色教学与精简提示 / Character practice and compact tooltips | 未发布，须独立验收 / Unpublished; separate release acceptance required |
| V1.2.0-beta7 | 调试初始化 / Confirmed debug initialization | 未发布，须独立验收 / Unpublished; separate release acceptance required |
| V1.2.0-beta8 | 遗物与药水修订 / Relic and potion revision | 未发布，须独立验收 / Unpublished; separate release acceptance required |
| V1.2.0-beta9 | 蓝条修订 / Blue-bar revision | 未发布，须独立验收 / Unpublished; separate release acceptance required |

后续批次不包含在 beta5 中。版本顺序是当前计划，各批仍需分别确认范围和验收；本地实现或测试完成不等于已经发布。

Later batches are excluded from beta5. Their order is the current plan; each needs its own scope and acceptance. Local implementation or testing does not establish publication.

`main` 保持已发布 stable V1.1.1 源码。本轮不公开 stable 尚未发布的教学、初始化或底牌自动打牌选牌修复；选牌修复等待独立 stable 维护版本。

`main` retains published stable V1.1.1 source. This update excludes unpublished stable practice, initialization and bottom-autoplay selection fixes; the selection fix awaits a separate stable maintenance release.

GitHub 源码同步与独立 Release 分别记录。独立下载仍为 beta V1.1.0-beta1 和 stable V1.1.0；本轮不新增 Release，也不替换历史标签或附件。

GitHub source synchronization is separate from runtime Releases. Separate downloads remain beta V1.1.0-beta1 and stable V1.1.0. This batch creates no Release and replaces no historical tag or attachment.
