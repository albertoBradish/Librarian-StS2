# Source and release status / 源码与发布状态

2026-10-08（America/Los_Angeles）。beta11 同步 Steam 与独立 GitHub 预发行，单前置 RitsuLib 0.6.4。

Beta11 is published separately on Steam and GitHub. It requires RitsuLib 0.6.4 only.

| 通道 / Channel | 当前源码 / Current source | Steam | GitHub runtime |
| --- | --- | --- | --- |
| `main` | stable 0.107.1，V1.1.1 加后续维护 / V1.1.1 plus maintenance | V1.1.1 | V1.1.0 stable |
| `codex/beta` | public-beta 0.111.0，V1.2.0-beta11 | V1.2.0-beta11 | V1.2.0-beta11 prerelease |

## Beta11 范围 / Beta11 scope

角色、三个池、100 张活动与旧档模型、能力、九件遗物、三种药水、全局挂钩与辅助路径已迁移至 RitsuLib，149 个原 ID 保留。BaseLib 已从构建、运行清单及 beta 工坊必需项移除；只需 RitsuLib 0.6.4。历史发行的依赖与文件保持原快照。

Character, three pools, 100 active/legacy card models, powers, nine relics, three potions, hooks and helpers use RitsuLib, retaining 149 published IDs. BaseLib is removed from the build, runtime manifest and beta Workshop prerequisites. Only RitsuLib 0.6.4 is required. Historical releases retain their original files and dependencies.

深海屏障回合末新增浪潮参与当回合格挡结算，正常衰减／保留保持。余烬清算、蚀卷书虫、溃堤、焚书显示原生计算后的伤害，含锋利附魔及战斗修正；蚀卷书虫的力量损失仍按翠叶数值。蓝字预览回合末格挡净变化，支持已验收的原版来源及回合末伤害。继承 91 张活动卡、设置与 12 步实战教学，现有美术保留。

Deep Sea Barrier's newly gained end-turn Waves contribute to that turn's Block settlement with normal decay/retention. Ember Reckoning, Bookworm, Dam Break and Book Burning show calculated damage including Sharp and combat modifiers; Bookworm's Strength loss still follows Growth value. The blue value previews the net Block change at turn end, including supported native sources and end-turn damage. The release retains 91 active cards, settings, the 12-step tutorial and existing artwork.

## 身份与验证 / Identity and validation

`source-snapshot.json` 保存公开自有输入逐文件 SHA256；`release-metadata.json` 标识本版已验证三文件。准确版本标签指向匹配源码提交。私有依赖、原始设计、研究日志与存档不进入公开树或附件。

`source-snapshot.json` records public authored inputs; `release-metadata.json` identifies the validated runtime. The version tag resolves to matching source. Private dependencies, original designs, research logs and saves are excluded.

本版 Core75/75。专项验证覆盖 192 项攻击预览／384 双语文本、32 实际伤害组合、27 真实回合、48 张附魔牌存读档及继续战斗。Ritsu-only 整体验证 1621 检查，完整库存 200 卡／9 遗物／3 药水恢复并继续战斗；三份最终完整日志门禁通过。迁移另有逐卡基础／升级场景、原 BaseLib 旧档及退役模型证据。

Core75/75 passes. Focused checks cover 192 attack previews/384 bilingual texts, 32 actual-damage combinations, 27 real turns and 48 enchanted cards through save/reload and continued combat. Ritsu-only regression covers 1621 checks and a restored inventory of 200 cards/9 relics/3 potions with continued combat; three final complete logs pass. Separate migration evidence covers base/upgraded cards, old BaseLib saves and retired models.

真实多客户端／重连、长期平衡、实体手柄、完整正常速度视觉听感及任意第三方组合仍待验收。纯规则 CI 不替代原生运行；Steam 服务器确认不等于订阅端已收到新版。

Real multi-client/reconnect, long-term balance, physical controller, full normal-speed visual/audio acceptance and arbitrary third-party combinations remain pending. Pure-rule CI does not replace native validation; Steam server verification does not establish subscriber arrival.
