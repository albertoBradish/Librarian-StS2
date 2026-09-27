> Licensing: personal use follows LICENSE. Obtain prior written permission before publishing or redistributing adapted translations.
> 授权范围：个人使用遵守 LICENSE；发布或分发改编翻译包前须取得作者书面许可。

# 图书管理员：语言设置与翻译包

## 玩家切换

游戏主菜单 → 模组配置（RitsuLib）→ 图书管理员 / Librarian → 语言 / Language → 模组语言。

内置简体中文和英文，无需订阅补丁包。第一次启用时检测当前游戏语言并保存：简体/繁体中文采用简体中文，英文采用英文，其他未提供翻译的语言采用英文。若已安装与游戏语言代码相同的有效自定义包，会优先选择该包。

之后以模组内手动选择为准；修改游戏语言不会再次覆盖选择。恢复显示与声音默认值也不重置语言。语言只保存在本机，不改变队友的语言或玩法规则。

卡牌、描述、状态、遗物、药水、角色对白、解锁、选择提示和自有设置随模组选择切换。图书管理员卡牌上的原版类型/关键词也随之切换。原版菜单、敌人、回合按钮及RitsuLib自有的“开启/关闭”等控件文字仍使用游戏语言。

## 制作翻译

1. 点击“导出翻译模板 / Export translation templates”。设置页会显示导出路径。按模组版本分目录，含 `eng` 与 `zhs` 两套JSON；已有同版本模板不会被覆盖。需要重新导出时先将自己改过的模板另存，再移走对应旧模板文件。
2. 将模板复制到设置页显示的“自定义翻译目录 / Custom translations”下。每种语言占一个文件夹，例如 `fra`。目录使用2–32位小写字母、数字、下划线或短横线，首位必须是字母。
3. 编辑 `pack.json` 和需要翻译的表。文件采用UTF-8 JSON，元数据字段名为大写开头的 `Name`、`Culture`：

```json
{
  "Name": "Français",
  "Culture": "fr-FR"
}
```

最小示例 `fra/cards.json`：

```json
{
  "LIBRARIAN-SPARK.title": "Étincelle"
}
```

4. 点击“重新加载语言包 / Reload language packs”，从语言下拉选择该包。修改文件后再点重新加载即可。

可用表：`cards`、`powers`、`relics`、`potions`、`static_hover_tips`、`characters`、`ancients`、`events`、`epochs`、`card_selection`、`main_menu_ui`、`librarian_runtime`，扩展名均为 `.json`。

新增语言允许只提供部分表和条目，缺失项回退内置英文。使用 `zhs` 或 `eng` 文件夹可以覆盖对应内置语言，未提供的条目分别回退内置中文或英文。模组更新不会改写这些外部文件。

## 保留格式与语义

- 保留JSON键、`{Damage:diff()}` 等动态参数及大小写。允许调整它们在句子中的顺序，不能删除或增加参数名。
- `diff()`、`plural`、`show` 是原版格式表达式。例如 `{Turns} {Turns:plural:turn|turns}` 根据此包的 `Culture` 显示单复数，不依赖游戏语言。不要在 `{` 后添加空格。
- 保留配对的 `[gold]...[/gold]`、`[blue]...[/blue]`、`[img]...[/img]` 及资源路径。解锁普通名称不额外上色，罕见为blue，稀有为gold。
- `librarian_runtime.json` 中的 `LIBRARIAN_KEYWORD_*` 是悬停词条识别别名；与正文的金色关键词保持一致。多个词形用 `|` 分隔，例如 `Settle|Settles|Settling`。
- 中文“注魔”在英文中使用 **Activate**；“结算”使用 **Settle**。这两项是本模组机制，不套用原版法球的Channel/Evoke效果。
- 状态只说明剩余效果和触发条件，不必重复卡牌打出时已经完成的动作。保留持续时间、剩余次数和多人目标范围。

无法解析的包、未知键或参数集合不匹配会被拒绝，设置页显示对应包和原因。新语言失效时回退英文；同名内置语言仍可使用。若外部单条文案通过解析但无法用实际变量格式化，该条回退内置文案并显示来源键。修正后重新加载即可。

配置保存在 `user://Librarian/language.json`，外部包在 `user://Librarian/languages`。`user://` 是当前游戏的用户数据目录；以设置页显示的绝对路径为准，不应把文件放进Steam安装目录或修改PCK。

## English quick guide

Open **Mod Settings → Librarian → Language**. The mod bundles English and Simplified Chinese and detects the game language once on first use. Later choices are saved independently.

Choose **Export translation templates**, then copy a template into a language folder under the **Custom translations** path shown in settings. Each folder needs a `pack.json` with `Name` and a valid .NET `Culture` such as `fr-FR`. Use a lowercase language code such as `fra`. Translate the values in the JSON tables, keeping their keys, variables, markup and resource paths intact. Update the keyword aliases in `librarian_runtime.json` to match your translated gold terms.

Click **Reload language packs** and select the new language. Partial new translations fall back to bundled English; `eng` and `zhs` folders override their respective bundled languages. Invalid packs are reported in settings. No separate Workshop patch is required, and only this mod's text changes.
