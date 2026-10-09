# Source and release status / 源码与发布状态

2026-10-09（America/Los_Angeles）。beta12同步Steam与独立GitHub预发行，仍仅需RitsuLib。

Beta12 is published separately on Steam and GitHub and still requires RitsuLib only.

| 通道 / Channel | 当前源码 / Current source | Steam | GitHub runtime |
| --- | --- | --- | --- |
| `main` | stable 0.107.1，V1.1.1加后续维护 / V1.1.1 plus maintenance | V1.1.1 | V1.1.0 stable |
| `codex/beta` | public-beta 0.111.0，V1.2.0-beta12 | V1.2.0-beta12 | V1.2.0-beta12 prerelease |

## Beta12范围 / Beta12 scope

修复设置内初始化的未知进度保留路径；为最终确认增加5秒等待和红字提醒。调试工具可预览首次教学邀请与精简提示，选项只关闭预览，不写入记录或修改设置。内容解锁新增图书管理员专属A10／A0，并提供难复现场景的调试说明。身份重复、字段冲突或无法匹配的结构仍会拒绝操作。

Fixes unknown-progress preservation during initialization and adds a five-second confirmation with a red warning. Debug tools preview the first-play invitation and compact-tooltip offer without writing receipts or changing settings. Unlock tools add Librarian-only A10/A0 options and debugging notes. Ambiguous identities, conflicting fields and unmatchable structures still reject the operation.

沿用beta11的91张活动卡、100张活动与旧档模型、现有美术、12步教学与完整RitsuLib迁移。本次卡牌规则、数值和美术保持原字节；stable及历史发行保留各自的依赖和文件。

Retains beta11's 91 active cards, 100 active/legacy models, artwork, 12-step tutorial and complete RitsuLib migration. Card rules, values and artwork remain byte-for-byte unchanged. Stable and historical releases retain their own dependencies and files.

## 身份与验证 / Identity and validation

同一三文件包分别在固定游戏0.111.0和RitsuLib0.6.4／0.6.7下完成菜单、新局、战斗、初始化、普通局存读档及独立进程重载。预览选择前后逐文件哈希一致；A10／A0只改两项角色进阶字段。未知字段与不可用记录在主文件、镜像及双方备份中继续保留。Core75/75、纯JSON22/22通过；完整日志由既有门禁检查。

The same three-file package passes menu, new-run, combat, initialization, ordinary save/reload and independent-process restart checks in game 0.111.0 with RitsuLib 0.6.4 and 0.6.7. Preview choices preserve file hashes; A10/A0 alter only the two character Ascension fields. Unknown fields and unavailable records remain in the main save, mirror and both backups. Core75/75 and JSON22/22 pass; complete logs pass the existing quality gate.

`source-snapshot.json`保存公开自有输入；`release-metadata.json`标识已验证包，版本标签对应准确源码提交。私有依赖、研究日志、原始设计和存档不进入公开树。

`source-snapshot.json` records public authored inputs; `release-metadata.json` identifies the verified package, and the tag resolves to matching source. Private dependencies, research logs, original designs and saves are excluded.

用户未提供存档，原日志具体失败字段仍未确认。一次早期原生菜单创建崩溃及加载补丁、文案问题的失败日志保留；修复后的最终完整运行通过。云同步后端、任意第三方组合、实体手柄、真实多人及长期平衡未新增验收。Steam服务端确认不等于本机订阅目录已收到新版。

No user save was supplied, so the exact offending fields remain unidentified. An earlier native menu-creation crash and failed patch/text iterations are retained; final complete runs pass. Cloud backend, arbitrary third-party combinations, physical controllers, true multiplayer and long-term balance remain unverified. Steam server verification does not establish local subscriber arrival.
