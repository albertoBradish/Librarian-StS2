# Source and release status / 源码与发布状态

2026-10-07 本地（America/Los_Angeles）。本次按用户要求同步尚未进入 GitHub 的自有源码、现用资源、测试及公开文档；开发源树按通道分别导出。

This source synchronization includes authored code, production assets, tests and public documentation through 2026-10-07. Each channel uses its own source tree.

| 通道 / Channel | 当前 Git 源码 / Current source | 已发布 Steam 包 / Published Steam package | 独立 GitHub 下载 / Separate GitHub runtime download |
| --- | --- | --- | --- |
| `main` | stable 0.107.1，V1.1.1 加后续维护 / V1.1.1 plus later maintenance | V1.1.1 | V1.1.0 stable |
| `codex/beta` | public-beta 0.111.0，V1.2.0-beta9 加净格挡预览 / V1.2.0-beta9 plus net Block preview | V1.2.0-beta9 | V1.1.0-beta1 |

## Beta 已发布功能 / Published beta features

- beta7：常用／显示／特效与声音／工具四页设置、示例与声音试听、Downloads 导出、精简提示、二次确认初始化、8 秒限时更新通知及永久锁定说明。
- beta8：未完待续先抽总数再统一选择置底；萌发护壁双语说明更新，既有 6／9 格挡保留。
- beta9：12 步互动教学、说明／实景／状态三栏、进度与操作栏、设置入口，以及教学期间的初始化保护。

- beta7: four settings pages, examples and sound previews, Downloads export, compact tooltips, confirmed initialization, an 8-second update toast, and permanent-lock wording.
- beta8: To Be Continued draws the total before one bottom-placement choice; Sprouting Bulwark receives revised bilingual wording with its existing 6/9 Block.
- beta9: a 12-step interactive tutorial with explanation, native scene and status columns, progress and controls, settings access, and initialization protection during practice.

以上三个 beta 包均已有服务器发布证据；订阅端新版到达仍待确认。当前 Git 源码进一步包含下述开发维护。

All three beta packages have server publication evidence. Subscriber delivery remains unverified. The current Git source also includes the maintenance below.

## 未发布维护 / Unreleased maintenance

| 范围 / Scope | stable | beta |
| --- | --- | --- |
| 法球数值文案、教学／设置、精简提示与初始化 / Orb wording, tutorial/settings, compact tooltips and initialization | 已同步源码，未发布 stable 包 / Source synchronized; not in published stable package | 已含于当前 beta 发布功能 / Included in published beta features |
| 底牌自动打牌选牌等待、釜底抽薪／三相归一 owner 上下文 / Bottom-autoplay choice waiting and Fuel the Fire / Threefold Unity owner context | 已同步本地修复，未发布 stable 包 / Local fixes synchronized; not in published stable package | 保留 beta 自身接口实现 / Retains beta's own API implementation |
| 未完待续顺序及萌发护壁说明 / To Be Continued order and Sprouting Bulwark wording | 已同步源码，未发布 stable 包 / Source synchronized; not in published stable package | beta8 已发布 / Published in beta8 |
| 回合末格挡净变化预览 / End-turn net Block preview | 已同步源码，未发布 / Source synchronized; unreleased | 已同步源码，未发布 / Source synchronized; unreleased |

回合末预览显示当前格挡到玩家回合末结算完成后的净变化，包含支持的原版格挡来源、到期潮涌及回合末伤害，可显示负数、随机范围；未知连锁或多人场景显示问号。stable 的玩法与 API 保持自身通道，不从 beta 整树覆盖。

The preview reports net Block change through the player's end-turn effects, including supported native sources, expiring Tidal Block and end-turn damage. It can show negative values or random ranges; unknown chains and multiplayer cases use a question mark. Stable retains its own gameplay and API behavior.

## 文件身份与验证 / File identity and validation

`source-snapshot.json` 保存当前自有开发输入的逐文件 SHA-256。`release-metadata.json` 的 `baseline_runtime_artifacts` 指向各通道最后已发布 Steam 包，不能用它代表当前分支的新构建。两份元数据明确 `source_matches_baseline_runtime: false`。

`source-snapshot.json` records SHA-256 for the current authored development inputs. `baseline_runtime_artifacts` in `release-metadata.json` identifies each channel's last published Steam package, not a new build of the branch head. Both metadata files state `source_matches_baseline_runtime: false`.

已有本地隔离原生验收分别覆盖教学、设置、卡牌修复及格挡预览；它们对应各自当时的候选。格挡预览每通道完成 137 项检查及 26 个真实回合。公开分支此次运行纯规则测试；CI 仅证明这些规则测试，不能视为新公开副本的菜单／新局／战斗／存读档或真实多客户端验收。实体手柄、用户最终观感与听感、长期平衡及第三方任意组合仍按各项原验证边界保留。

Prior isolated native checks covered the tutorial, settings, card fixes and Block preview with their own candidates. The preview passed 137 checks and 26 real turns per channel. This synchronization runs the public pure-rule suite; CI does not establish a fresh public-clone native menu/new-run/combat/save/reload or multi-client pass. Controller input, final user visual/audio acceptance, long-term balance and arbitrary third-party combinations retain their existing validation limits.

本次不创建运行文件 Release，不替换既有标签或附件。原始设计表格、私有依赖、提取资源与研究历史继续留在本地；公开内容沿用根 LICENSE 的自定义限制性许可。

This synchronization creates no runtime Release and replaces no existing tags or attachments. Original planning spreadsheets, private dependencies, extracted assets and internal research history remain local. Public content retains the custom restrictive LICENSE.
