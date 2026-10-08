# Source and release status / 源码与发布状态

2026-10-07 本地（America/Los_Angeles）。beta10 为独立 GitHub 预发行；Steam 与 stable 发布身份分别保留。

| 通道 / Channel | 当前源码 / Current source | Steam | GitHub runtime |
| --- | --- | --- | --- |
| `main` | stable 0.107.1，V1.1.1 加后续维护 / V1.1.1 plus maintenance | V1.1.1 | V1.1.0 stable |
| `codex/beta` | public-beta 0.111.0，V1.2.0-beta10 | V1.2.0-beta9 | V1.2.0-beta10 prerelease |

## Beta10 范围 / Beta10 scope

91 张活动卡和 9 张退役存档兼容模型改用 RitsuLib 模板与显式注册，原 ID、机制、数值、文案和美术保留。角色、三个池、31 个能力模型、九件遗物、三种药水、两个全局挂钩及动画／音频辅助代码仍依赖 BaseLib。必须同时启用 BaseLib 3.4.5 与完整 RitsuLib 0.6.4。

All 91 active cards and nine retired save models use RitsuLib templates and explicit registration while retaining IDs, rules, values, text and artwork. Character, three pools, 31 power models, nine relics, three potions, two singleton hooks and animation/audio helpers still depend on BaseLib. Enable both BaseLib 3.4.5 and the complete RitsuLib 0.6.4 bundle.

新版还包含此前通过验证的回合末格挡净变化预览，以及 beta7 的四页设置、试听、Downloads 导出、精简提示与初始化，beta8 的未完待续顺序／萌发护壁文案修复，beta9 的 12 步互动教学。预览支持已有原版格挡来源、到期潮涌及回合末伤害；未知连锁与多人场景显示问号。stable 的后续维护仍只在自身源码内，未随本次发包。

The release also includes the previously verified end-turn net Block preview, beta7's four settings pages, sound previews, Downloads export, compact tooltips and initialization, beta8's draw-order/text fixes, and beta9's 12-step tutorial. Block previews include supported native sources, expiring Tidal Block and end-turn damage; unresolved chains and multiplayer cases show a question mark. Stable maintenance stays in its own source channel and is not released here.

## 身份与验证 / Identity and validation

`source-snapshot.json` 保存公开自有输入逐文件 SHA256；`release-metadata.json` 分别标识本次 GitHub beta10 的已验证三文件与 Steam beta9 原包。标签指向匹配源码提交，不覆盖历史标签／附件，不打包私有依赖、研究日志、原始设计或存档。

`source-snapshot.json` records each public authored input's SHA256. `release-metadata.json` distinguishes the validated GitHub beta10 runtime from the original Steam beta9 package. The tag resolves to matching source. Existing tags/assets remain intact; private dependencies, research logs, original designs and saves are excluded.

逐卡简测覆盖基础／升级 182 个场景、原生手动出牌与选择、真实回合、364 双语模型阶段、九张退役兼容模型及保存／重载，另补测三相归一三法球六路径与 BaseLib 旧档。测试使用加速内部计时；不代表正常速度的完整视觉验收。真实多客户端、长期平衡、实体手柄、最终视觉／听感与任意第三方组合仍待验收。纯规则 CI 不替代原生测试。

Simple native checks cover 182 base/upgraded cases, manual actions and choices, real turns, 364 bilingual model stages, nine legacy models and save/reload, plus six Threefold Unity paths and an old BaseLib save. Internal test timing is accelerated and does not establish full visual acceptance at normal speed. Real multi-client, long-term balance, physical controller, final visual/audio and arbitrary third-party combinations remain pending. Pure-rule CI does not replace native tests.
