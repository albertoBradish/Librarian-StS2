using Godot;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Saves;
using STS2RitsuLib;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Use the shared settings host; do not add buttons to vanilla menu layouts.</summary>
public static class LibrarianSettings041
{
    public const string PageId = "librarian-settings";
    private static bool _registered;
    private static bool _exporting;
    private static string _debugStatusKey = "";
    internal static string ExportStatus { get; private set; } = "";
    private static ModSettingsText Text(string key, string fallback) =>
        ModSettingsText.Dynamic(() => LibrarianLanguage.TryRaw("main_menu_ui", "LIBRARIAN_SETTINGS." + key, out var value) ? value : fallback);

    public const string DisplayPageId = "librarian-display";
    public const string EffectsPageId = "librarian-effects";
    public const string ToolsPageId = "librarian-tools";

    public static void Initialize()
    {
        if (_registered) return;
        LibrarianPreferences050.Load();
        RitsuLibFramework.RegisterModSettings("Librarian", page => page
            .WithTitle(Text("common", "常用设置"))
            .WithModDisplayName(Text("title", "图书管理员"))
            .WithDescription(Text("presentation_hint", "语言、显示和声音设置会自动保存。"))
            .AddSection("language", section => section.WithTitle(Text("language_section", "语言"))
                .AddDynamicChoice("language", Text("language", "模组语言"),
                    ModSettingsBindings.WithDefault(ModSettingsBindings.Callback("Librarian", "language", () => LibrarianLanguage.Selected, LibrarianLanguage.Select, () => { }), () => DefaultLanguage),
                    LibrarianLanguage.Options, Text("language_hint", "首次使用时跟随游戏语言，之后记住你的选择。"), ModSettingsChoicePresentation.Dropdown))
            .AddSection("quick", section => section.WithTitle(Text("quick", "设置与教学"))
                .AddSubpage("display_page", Text("display", "战场显示"), DisplayPageId, Text("open", "打开"), Text("display_summary", "调整法球、格挡预览和悬浮提示，查看显示效果。"))
                .AddSubpage("effects_page", Text("effects_audio", "特效与声音"), EffectsPageId, Text("open", "打开"), Text("effects_summary", "调整动画、粒子、特效强度和法球音量。"))
                .AddSubpage("tools_page", Text("tools", "工具与维护"), ToolsPageId, Text("open", "打开"), Text("tools_summary", "导出日志或翻译模板，管理解锁与重置选项。"))
                .AddButton("character_tutorial", Text("character_tutorial", "角色教学"), Text("start", "开始"),
                    host => LibrarianOnboarding.StartPracticeFromMenu(),
                    description: Text("character_tutorial_hint", "从主菜单开始实战教学，保留普通存档。"))), PageId);

        RitsuLibFramework.RegisterModSettings("Librarian", page => page
            .AsChildOf(PageId).WithTitle(Text("display", "战场显示")).WithDescriptionHidden(true)
            .AddSection("display", section => section.WithTitle(Text("display", "战场显示"))
                .AddToggle("compact_hover_tips", Text("compact_hover_tips", "精简悬浮提示"), LibrarianPreferences050.Bind("compact_hover_tips", p => p.CompactHoverTips, (p,v) => p.CompactHoverTips=v), Text("compact_hover_tips_hint", "省略嵌套的基础说明，保留卡牌直接涉及的提示、核心机制和原版词条。"))
                .AddCustom("compact_hover_tips_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("compact_hover_tips"))
                .AddToggle("circle", Text("circle", "装饰法阵"), LibrarianPreferences050.Bind("circle", p => p.MagicCircle, (p,v) => p.MagicCircle=v), Text("circle_hint", "关闭后仍显示三法球和前台标记。"))
                .AddCustom("circle_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("circle"))
                .AddToggle("idle", Text("idle", "法球待机浮动"), LibrarianPreferences050.Bind("idle", p => p.OrbIdle, (p,v) => p.OrbIdle=v))
                .AddCustom("idle_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("idle"))
                .AddEnumChoice("locked_orb_display", Text("locked_orb_display", "锁定法球显示"),
                    LibrarianPreferences050.Bind("locked_orb_display", p => p.LockedOrbDisplay, (p,v) => p.LockedOrbDisplay=v),
                    mode => mode switch
                    {
                        LibrarianLockedOrbDisplayMode.LegacyTurns => Text("locked_orb_mode_legacy", "正数回合"),
                        LibrarianLockedOrbDisplayMode.ValueAndTurns => Text("locked_orb_mode_value", "法球数值与剩余回合"),
                        _ => Text("locked_orb_mode_negative", "红色负数回合（默认）")
                    }, Text("locked_orb_display_hint", "红色 −1 表示还锁定1回合；显示法球数值时，剩余回合放在球下方。"), ModSettingsChoicePresentation.Dropdown)
                .AddCustom("locked_orb_display_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("locked_orb_display"))
                .AddToggle("tide_block_feedback", Text("tide_block_feedback", "波涛格挡变化提示"), LibrarianPreferences050.Bind("tide_block_feedback", p => p.TideBlockFeedback, (p,v) => p.TideBlockFeedback=v), Text("tide_block_feedback_hint", "显示波涛格挡获得和到期时的文字提示。"))
                .AddCustom("tide_block_feedback_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("tide_block_feedback"))
                .AddToggle("wave", Text("wave", "回合结束格挡预览"), LibrarianPreferences050.Bind("wave", p => p.WaveBar, (p,v) => p.WaveBar=v), Text("wave_hint", "在血条上方显示回合末格挡的净变化，包含覆甲等原版效果。悬浮可查看预计总格挡及不确定因素。"))
                .AddCustom("wave_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("wave"))), DisplayPageId);

        RitsuLibFramework.RegisterModSettings("Librarian", page => page
            .AsChildOf(PageId).WithTitle(Text("effects_audio", "特效与声音")).WithDescriptionHidden(true)
            .AddSection("effects", section => section.WithTitle(Text("effects", "卡牌与法球特效"))
                .AddToggle("card_effects", Text("card_effects", "卡牌特效"), LibrarianPreferences050.Bind("card_effects", p => p.CardEffects, (p,v) => p.CardEffects=v))
                .AddCustom("card_effects_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("card_effects"))
                .AddToggle("orb_effects", Text("orb_effects", "法球特效"), LibrarianPreferences050.Bind("orb_effects", p => p.OrbEffects, (p,v) => p.OrbEffects=v), Text("orb_effects_hint", "关闭后仍显示法球数值、锁定标记和悬浮说明。"))
                .AddCustom("orb_effects_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("orb_effects"))
                .AddToggle("particles", Text("particles", "粒子特效"), LibrarianPreferences050.Bind("particles", p => p.Particles, (p,v) => p.Particles=v))
                .AddCustom("particles_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("particles"))
                .AddToggle("reduced_motion", Text("reduced_motion", "减少动态效果"), LibrarianPreferences050.Bind("reduced_motion", p => p.ReducedMotion, (p,v) => p.ReducedMotion=v), Text("reduced_motion_hint", "停止法阵旋转、法球浮动和施法手势，并简化其他特效。"))
                .AddCustom("reduced_motion_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("reduced_motion"))
                .AddToggle("teammates", Text("teammates", "队友附加特效"), LibrarianPreferences050.Bind("teammates", p => p.TeammateEffects, (p,v) => p.TeammateEffects=v), Text("teammates_hint", "控制本模组在队友身上显示的附加特效。"))
                .AddIntSlider("opacity", Text("opacity", "特效不透明度"), LibrarianPreferences050.Bind("opacity", p => p.EffectOpacity, (p,v) => p.EffectOpacity=v), 10, 100, 10, valueFormatter: v => v+"%")
                .AddCustom("opacity_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("opacity"))
                .AddIntSlider("limit", Text("limit", "同时显示的特效上限"), LibrarianPreferences050.Bind("limit", p => p.EffectLimit, (p,v) => p.EffectLimit=v), 4, 40, 4, description: Text("limit_hint", "调低可减少同时出现的特效；超过上限时省略部分尾迹。"))
                .AddCustom("limit_preview", Text("preview", "效果示例"), _ => LibrarianSettingsPreview.Create("limit")))
            .AddSection("audio", section => section.WithTitle(Text("audio", "模组音效"))
                .AddToggle("orb_sounds", Text("orb_sounds", "法球音效"), LibrarianPreferences050.Bind("orb_sounds", p => p.OrbSounds, (p,v) => p.OrbSounds=v))
                .AddIntSlider("volume", Text("volume", "法球音效音量"), LibrarianPreferences050.Bind("volume", p => p.SoundVolume, (p,v) => p.SoundVolume=v), 0, 100, 10, valueFormatter: v => v+"%", description: Text("volume_hint", "音量也受游戏主音量和音效音量控制。"))
                .AddButton("sound_preview", Text("sound_preview", "试听波涛音效"), Text("listen", "试听"), host => LibrarianOrbAudio.TryPreview(LibrarianOrbAudio.Cue.Tide), description: Text("sound_preview_hint", "使用当前音量试听。关闭法球音效或将音量设为0时静音。"))
                .AddParagraph("save_status", ModSettingsText.DynamicFullRefreshOnly(() => LibrarianPreferences050.Status))), EffectsPageId);

        RitsuLibFramework.RegisterModSettings("Librarian", page => page
            .AsChildOf(PageId).WithTitle(Text("tools", "工具与维护")).WithDescriptionHidden(true)
            .AddSection("diagnostics", section => section.WithTitle(Text("diagnostics", "运行日志"))
                .AddButton("export", Text("export", "导出运行日志"), Text("export_button", "导出"), host => TaskHelper.RunSafely(ExportAsync(host)),
                    description: Text("export_scope", "导出日志和版本信息到下载文件夹，完成后自动打开。包含本机显示设置，不含存档或截图，也不会上传。"))
                .AddParagraph("export_status", ModSettingsText.DynamicFullRefreshOnly(() => PlainText(ExportStatus))))
            .AddSection("translations", section => section.WithTitle(Text("translations", "翻译工具")).Collapsible(true)
                .AddButton("language_templates", Text("language_templates", "导出翻译模板"), Text("export_button", "导出"), host => { LibrarianLanguage.ExportTemplates(); host.RequestRefresh(); },
                    description: Text("language_templates_hint", "导出中英文模板到下载文件夹，完成后自动打开。每次生成独立文件夹，保留已有文件。"))
                .AddButton("reload_language", Text("language_reload", "重新加载语言包"), Text("reload", "重新加载"), host => { LibrarianLanguage.Reload(); host.RequestRefresh(); },
                    description: Text("language_reload_hint", "读取新增或修改的翻译；缺失条目使用内置文本。"))
                .AddParagraph("language_path", ModSettingsText.Dynamic(() => PlainText(Text("language_path", "自定义翻译读取目录：{Path}").Resolve().Replace("{Path}", LibrarianLanguage.PackDirectory))))
                .AddParagraph("language_install", Text("language_install", "完成翻译后，将语言包文件夹复制到上方目录，再重新加载。"))
                .AddParagraph("language_status", ModSettingsText.Dynamic(() => PlainText(LibrarianLanguage.Status))))
            .AddSection("progression", section => section.WithTitle(Text("progression", "内容解锁")).Collapsible(true)
                .AddParagraph("current", ModSettingsText.Dynamic(ProgressText))
                .AddButton("progressive", Text("progressive", "逐步解锁"), Text("apply", "应用"), host => { ApplyProgress(false); host.RequestRefresh(); }, description: Text("progressive_hint", "新局按游戏进度解锁，保留已解锁内容。"))
                .AddButton("all", Text("all", "全部解锁"), Text("apply", "应用"), host => { ApplyProgress(true); host.RequestRefresh(); }, description: Text("all_hint", "解锁当前档案的全部图书管理员内容，之后不会重新锁定。"))
                .AddParagraph("ascension_current", ModSettingsText.Dynamic(() => LibrarianAscensionTools.Current))
                .AddButton("unlock_a10", Text("unlock_a10", "解锁 A10"), Text("apply", "应用"), host => LibrarianAscensionTools.Apply(false, host), description: Text("unlock_a10_hint", "解锁当前档案图书管理员的 A10，不改变通关记录或当前选择。"))
                .AddButton("reset_a0", Text("reset_a0", "重置回 A0"), Text("apply", "应用"), host => LibrarianAscensionTools.Apply(true, host), description: Text("reset_a0_hint", "将图书管理员的进阶上限和当前选择重置为 A0，保留其他解锁与游玩记录。"))
                .AddParagraph("ascension_result", ModSettingsText.Dynamic(() => LibrarianAscensionTools.Status)))
            .AddSection("defaults", section => section.WithTitle(Text("defaults", "恢复默认设置")).Collapsible(true)
                .AddButton("reset_settings", Text("reset_settings", "恢复默认设置"), Text("reset", "恢复"), host => { RestoreDefaults(); host.RequestRefresh(); }, description: Text("reset_hint", "恢复语言、显示和声音的默认值。语言重新跟随游戏，保留解锁、游玩进度和提示记录。")))
            .AddSection("debug", section => section.WithTitle(Text("debug", "调试工具")).Collapsible(true)
                .AddButton("preview_tutorial", Text("preview_tutorial", "预览首次教学邀请"), Text("preview", "预览"), host => PreviewOnboarding(LibrarianTutorialMode.Consent, host), description: Text("preview_tutorial_hint", "仅限主菜单。查看首次教学窗口，不记录选择，也不开始教学局。"))
                .AddButton("preview_compact", Text("preview_compact", "预览精简提示窗口"), Text("preview", "预览"), host => PreviewOnboarding(LibrarianTutorialMode.CompactOffer, host), description: Text("preview_compact_hint", "仅限主菜单。查看通关后的精简提示窗口，不改变设置或提示记录。"))
                .AddButton("replay_welcome", Text("replay_welcome", "查看当前版本介绍"), Text("show", "查看"), host => { _debugStatusKey = LibrarianUpdateNotice051.RequestRedisplay() ? "notice_queued" : "notice_menu_required"; host.RequestRefresh(); }, description: Text("replay_welcome_hint", "清除当前版本的“不再显示”选择，返回主菜单后打开介绍。"))
                .AddButton("preview_review", Text("preview_review", "预览通关好评提示"), Text("preview", "预览"), host => { _debugStatusKey = LibrarianArchitectReview102.RequestPreview() ? "review_queued" : "notice_menu_required"; host.RequestRefresh(); }, description: Text("preview_review_hint", "返回主菜单后预览，不记录通关，也不消耗首次提示。"))
                .AddButton("reload_presentation", Text("reload_presentation", "重新读取显示与声音设置"), Text("reload", "重新加载"), host => { LibrarianPreferences050.Load(); _debugStatusKey = "presentation_reloaded"; host.RequestRefresh(); }, description: Text("reload_presentation_hint", "读取本机保存的选项，不改变解锁进度。"))
                .AddParagraph("debug_status", ModSettingsText.DynamicFullRefreshOnly(() => _debugStatusKey.Length == 0 ? "" : Text(_debugStatusKey, "").Resolve()))
                .AddButton("reset_mod", Text("reset_all", "初始化模组（调试）"), Text("reset_all_button", "初始化"), host => TaskHelper.RunSafely(LibrarianDebugReset.RequestAsync(host)), description: Text("reset_all_hint", "重置当前档案的图书管理员记录和本机设置。仅限主菜单，操作前需要再次确认。"))
                .AddParagraph("reset_mod_status", ModSettingsText.Dynamic(() => LibrarianDebugReset.Status)))
            .AddSection("debug_notes", section => section.WithTitle(Text("debug_notes", "调试说明")).Collapsible(true)
                .AddParagraph("reset_note", Text("debug_reset_note", "初始化失败时先导出日志。若提示无法保留其他进度，请保留原档；不要通过卸载模组或删除记录强行初始化。"))
                .AddParagraph("countdown_note", Text("debug_countdown_note", "确认窗每次打开都会等待5秒。等待期间可取消；按住确认键或连续点击都不能跳过倒计时。"))
                .AddParagraph("preview_note", Text("debug_preview_note", "两个预览窗口里的选项只用于查看，不会开始教学、切换精简设置或记录首次选择。"))
                .AddParagraph("ascension_note", Text("debug_ascension_note", "A10和A0只修改图书管理员的进阶。操作后重新进入角色选择，再重启游戏确认；不会改变进行中的对局。"))), ToolsPageId);
        _registered = true;
    }

    internal static string PlainText(string text)
    {
        var escaped = new System.Text.StringBuilder(text.Length);
        foreach (char character in text)
            escaped.Append(character switch { '[' => "[lb]", ']' => "[rb]", _ => character.ToString() });
        return escaped.ToString();
    }

    internal static string DefaultLanguage => LibrarianLanguage.Detect(LocManager.Instance.Language);
    private static void PreviewOnboarding(LibrarianTutorialMode mode, IModSettingsUiActionHost host)
    {
        if (!LibrarianDebugReset.CanReset || LibrarianDebugReset.IsBusy)
        {
            _debugStatusKey = "notice_menu_required";
            host.RequestRefresh();
            return;
        }
        TaskHelper.RunSafely(LibrarianOnboarding.PreviewAsync(mode, host));
    }
    internal static void RestoreDefaults()
    {
        LibrarianPreferences050.Reset();
        LibrarianLanguage.Select(DefaultLanguage);
    }

    internal static string ProgressText()
    {
        var progress = SaveManager.Instance.Progress;
        int revealed = Enumerable.Range(1, 7).Count(n => progress.Epochs.Any(e =>
            e.Id == LibrarianUnlocks040.Id(n) && e.State == EpochState.Revealed));
        return Text("progress_status", "已解锁章节：{Count}/7").Resolve().Replace("{Count}", revealed.ToString());
    }

    internal static void ApplyProgress(bool all)
    {
        LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, all);
        SaveManager.Instance.SaveProgressFile();
    }

    internal static void EnsureProgressiveProfile()
    {
        if (!SaveManager.Instance.Progress.FtueCompleted.Contains(LibrarianUnlocks040.ChoiceMarker))
            ApplyProgress(false);
    }

    internal static async Task ExportAsync(IModSettingsUiActionHost host)
    {
        if (_exporting) return;
        _exporting = true;
        ExportStatus = Text("export_working", "正在导出……").Resolve();
        host.RequestRefresh();
        try
        {
            string root = ProjectSettings.GlobalizePath("user://");
            string metadata = JsonSerializer.Serialize(new {
                librarian = LibrarianUpdateNotice051.CurrentVersion ?? "unknown", presentation = LibrarianPreferences050.Current, recorded_utc = DateTime.UtcNow,
                engine = Engine.GetVersionInfo()["string"].AsString(),
                game_version = MegaCrit.Sts2.Core.Nodes.NGame.GetGameVersion(),
                game_assembly = typeof(SaveManager).Assembly.GetName().Version?.ToString(),
                mods = ModManager.Mods.Select(m => new { id = m.manifest?.id, version = m.manifest?.version, state = m.state.ToString() }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true });
            string path = await Task.Run(() => LibrarianLogExport041.Export(root, metadata));
            ExportStatus = Text("export_success", "已导出：{Path}").Resolve().Replace("{Path}", path);
            if (!LibrarianExportDestination.TryOpenDirectory(Path.GetDirectoryName(path)!, out string reason))
                ExportStatus += "\n" + Text("folder_open_failed", "文件已导出，但未能自动打开文件夹：{Reason}").Resolve().Replace("{Reason}", reason);
        }
        catch (Exception ex)
        {
            ExportStatus = Text("export_failed", "导出失败：{Reason}").Resolve().Replace("{Reason}", ex.Message);
            Librarian.LibrarianCode.MainFile.Logger.Warn("041 log export failed: " + ex.Message);
        }
        finally { _exporting = false; host.RequestRefresh(); }
    }
}

internal static class LibrarianLogExport041
{
    internal static string Export(string userDirectory, string metadata, string? destinationDirectory = null)
    {
        string logs = Path.Combine(userDirectory, "logs");
        if (!Directory.Exists(logs)) throw new IOException(LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.logs_missing_directory"));
        var files = Directory.EnumerateFiles(logs).Where(p =>
            (p.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) &&
            (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).OrderBy(p => p).ToArray();
        if (files.Length == 0) throw new IOException(LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.logs_missing_files"));
        string directory = destinationDirectory ?? LibrarianExportDestination.DownloadsDirectory;
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "Librarian-logs-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N")[..6] + ".zip");
        string partial = destination + ".partial";
        int copied = 0;
        var skipped = new List<string>();
        try
        {
            using (var output = new FileStream(partial, FileMode.CreateNew))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (string file in files)
                {
                    string content;
                    try
                    {
                        using var input = new FileStream(file, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        content = MegaCrit.Sts2.Core.DevConsole.ConsoleCommands.GetLogsConsoleCmd.ReadTailText(input, 8 * 1024 * 1024);
                    }
                    catch (IOException ex) { skipped.Add(Path.GetFileName(file) + ": " + ex.GetType().Name); continue; }
                    Write(zip, "logs/" + Path.GetFileName(file), MegaCrit.Sts2.Core.Logging.LogSanitizer.Sanitize(content));
                    copied++;
                }
                if (copied == 0) throw new IOException(LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.logs_unreadable"));
                Write(zip, "versions.json", metadata);
                Write(zip, "export-summary.json", JsonSerializer.Serialize(new { copied, skipped, max_bytes_per_log = 8 * 1024 * 1024, saves_included = false }));
            }
            File.Move(partial, destination);
            return destination;
        }
        catch { if (File.Exists(partial)) File.Delete(partial); throw; }
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open());
        writer.Write(content);
    }
}
