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

    public static void Initialize()
    {
        if (_registered) return;
        LibrarianPreferences050.Load();
        // Buttons act on native per-profile progression; no duplicate/global copy of unlock data.
        RitsuLibFramework.RegisterModSettings("Librarian", page => page
            .WithTitle(Text("title", "图书管理员"))
            .WithModDisplayName(Text("title", "图书管理员"))

            .WithDescription(Text("presentation_hint", "本机显示与声音设置立即生效并自动保存。"))
            .AddSection("language", section => section.WithTitle(Text("language_section", "语言"))
                .AddDynamicChoice("language", Text("language", "模组语言"),
                    ModSettingsBindings.WithDefault(ModSettingsBindings.Callback("Librarian", "language", () => LibrarianLanguage.Selected, LibrarianLanguage.Select, () => { }), () => DefaultLanguage),
                    LibrarianLanguage.Options, Text("language_hint", "首次启用时使用游戏语言；此后单独保存你的选择。"), ModSettingsChoicePresentation.Dropdown)
                .AddButton("reload_language", Text("language_reload", "重新加载语言包"), Text("reload", "重新加载"),
                    host => { LibrarianLanguage.Reload(); host.RequestRefresh(); }, description: Text("language_reload_hint", "重新读取新增或修改的翻译文件。缺失条目使用内置语言包。"))
                .AddButton("language_templates", Text("language_templates", "导出翻译模板"), Text("export_button", "导出"),
                    host => { LibrarianLanguage.ExportTemplates(); host.RequestRefresh(); }, description: Text("language_templates_hint", "导出内置中英文翻译模板；已有文件保留。"))
                .AddParagraph("language_path", ModSettingsText.Dynamic(() => Text("language_path", "自定义翻译目录：{Path}").Resolve().Replace("{Path}", LibrarianLanguage.PackDirectory)))
                .AddParagraph("language_status", ModSettingsText.Dynamic(() => LibrarianLanguage.Status)))
            .AddSection("defaults", section => section.WithTitle(Text("defaults", "默认设置"))
                .AddButton("reset_settings", Text("reset_settings", "恢复默认设置"), Text("reset", "恢复"),
                    host => { RestoreDefaults(); host.RequestRefresh(); },
                    description: Text("reset_hint", "恢复模组语言、显示与声音设置，语言重新匹配游戏。解锁、游玩进度与弹窗提示记录保留。")))
            .AddSection("effects", section => section.WithTitle(Text("effects", "卡牌与法球特效"))
                .AddToggle("card_effects", Text("card_effects", "卡牌特效"), LibrarianPreferences050.Bind("card_effects", p => p.CardEffects, (p,v) => p.CardEffects=v))
                .AddToggle("orb_effects", Text("orb_effects", "法球特效"), LibrarianPreferences050.Bind("orb_effects", p => p.OrbEffects, (p,v) => p.OrbEffects=v), Text("orb_effects_hint", "关闭后仍显示法球数值、锁定标记与悬停说明。"))
                .AddToggle("particles", Text("particles", "粒子特效"), LibrarianPreferences050.Bind("particles", p => p.Particles, (p,v) => p.Particles=v))
                .AddToggle("reduced_motion", Text("reduced_motion", "减少动态效果"), LibrarianPreferences050.Bind("reduced_motion", p => p.ReducedMotion, (p,v) => p.ReducedMotion=v), Text("reduced_motion_hint", "停止法阵旋转、法球浮动与施法手势，并简化本模组的其他特效。"))
                .AddToggle("teammates", Text("teammates", "队友附加特效"), LibrarianPreferences050.Bind("teammates", p => p.TeammateEffects, (p,v) => p.TeammateEffects=v))
                .AddIntSlider("opacity", Text("opacity", "特效不透明度"), LibrarianPreferences050.Bind("opacity", p => p.EffectOpacity, (p,v) => p.EffectOpacity=v), 10, 100, 10, valueFormatter: v => v+"%")
                .AddIntSlider("limit", Text("limit", "同时显示的特效上限"), LibrarianPreferences050.Bind("limit", p => p.EffectLimit, (p,v) => p.EffectLimit=v), 4, 40, 4, description: Text("limit_hint", "低配置可调低；超出上限时省略部分尾迹。")))
            .AddSection("display", section => section.WithTitle(Text("display", "战场显示"))
                .AddToggle("circle", Text("circle", "装饰法阵"), LibrarianPreferences050.Bind("circle", p => p.MagicCircle, (p,v) => p.MagicCircle=v), Text("circle_hint", "关闭后，三法球与前台标记仍会显示。"))
                .AddToggle("idle", Text("idle", "法球待机浮动"), LibrarianPreferences050.Bind("idle", p => p.OrbIdle, (p,v) => p.OrbIdle=v))
                .AddEnumChoice("locked_orb_display", Text("locked_orb_display", "锁定法球显示"),
                    LibrarianPreferences050.Bind("locked_orb_display", p => p.LockedOrbDisplay, (p,v) => p.LockedOrbDisplay=v),
                    mode => mode switch
                    {
                        LibrarianLockedOrbDisplayMode.LegacyTurns => Text("locked_orb_mode_legacy", "锁定回合（正数）"),
                        LibrarianLockedOrbDisplayMode.ValueAndTurns => Text("locked_orb_mode_value", "法球数值 + 下方回合"),
                        _ => Text("locked_orb_mode_negative", "红色负数回合（默认）")
                    },
                    Text("locked_orb_display_hint", "默认红色 −1 表示还锁定1回合。选择显示法球数值时，锁定回合显示在球下方。"),
                    ModSettingsChoicePresentation.Dropdown)
                .AddToggle("tide_block_feedback", Text("tide_block_feedback", "潮涌格挡变化提示"), LibrarianPreferences050.Bind("tide_block_feedback", p => p.TideBlockFeedback, (p,v) => p.TideBlockFeedback=v), Text("tide_block_feedback_hint", "显示潮涌格挡的获得与到期提示。"))
                .AddToggle("wave", Text("wave", "血条上方浪潮条"), LibrarianPreferences050.Bind("wave", p => p.WaveBar, (p,v) => p.WaveBar=v), Text("wave_hint", "关闭时仍可在浪潮状态说明中查看数值。")))
            .AddSection("audio", section => section.WithTitle(Text("audio", "模组音效"))
                .AddToggle("orb_sounds", Text("orb_sounds", "法球音效"), LibrarianPreferences050.Bind("orb_sounds", p => p.OrbSounds, (p,v) => p.OrbSounds=v))
                .AddIntSlider("volume", Text("volume", "法球音效音量"), LibrarianPreferences050.Bind("volume", p => p.SoundVolume, (p,v) => p.SoundVolume=v), 0, 100, 10, valueFormatter: v => v+"%", description: Text("volume_hint", "同时受游戏主音量与音效音量控制。"))
                .AddParagraph("save_status", ModSettingsText.DynamicFullRefreshOnly(() => LibrarianPreferences050.Status)))
            .AddSection("progression", section => section
                .WithTitle(Text("progression", "内容解锁"))
                .AddParagraph("current", ModSettingsText.DynamicFullRefreshOnly(ProgressText))
                .AddButton("progressive", Text("progressive", "逐步解锁"), Text("apply", "应用"),
                    host => { ApplyProgress(false); host.RequestRefresh(); },
                    description: Text("progressive_hint", "新局按游戏进度解锁；已解锁内容保留。"))
                .AddButton("all", Text("all", "全部解锁"), Text("apply", "应用"),
                    host => { ApplyProgress(true); host.RequestRefresh(); },
                    description: Text("all_hint", "立即解锁当前档案的全部图书管理员内容。已解锁内容不会重新锁定。")))
            .AddSection("diagnostics", section => section
                .WithTitle(Text("diagnostics", "运行诊断"))
                .AddButton("replay_welcome", Text("replay_welcome", "重新显示开始弹窗"), Text("show", "显示"),
                    host => { _debugStatusKey = LibrarianUpdateNotice051.RequestRedisplay() ? "notice_queued" : "notice_menu_required"; host.RequestRefresh(); },
                    description: Text("replay_welcome_hint", "清除当前版本的“不再显示”选择，返回主菜单后重新显示。"))
                .AddButton("preview_review", Text("preview_review", "预览通关好评提示"), Text("preview", "预览"),
                    host => { _debugStatusKey = LibrarianArchitectReview102.RequestPreview() ? "review_queued" : "notice_menu_required"; host.RequestRefresh(); },
                    description: Text("preview_review_hint", "返回主菜单后预览，不记录通关或消耗首次提示。"))
                .AddButton("reload_presentation", Text("reload_presentation", "重新读取显示与声音设置"), Text("reload", "重新加载"),
                    host => { LibrarianPreferences050.Load(); _debugStatusKey = "presentation_reloaded"; host.RequestRefresh(); },
                    description: Text("reload_presentation_hint", "重新读取本机已保存的选项，不改变解锁进度。"))
                .AddParagraph("debug_status", ModSettingsText.DynamicFullRefreshOnly(() => _debugStatusKey.Length == 0 ? "" : Text(_debugStatusKey, "").Resolve()))
                .AddParagraph("export_scope", Text("export_scope", "仅导出游戏运行日志及版本信息，不包含存档或截图，不会自动上传。"))
                .AddButton("export", Text("export", "导出运行日志"), Text("export_button", "导出"),
                    host => TaskHelper.RunSafely(ExportAsync(host)))
                .AddParagraph("export_status", ModSettingsText.DynamicFullRefreshOnly(() => ExportStatus))), PageId);
        _registered = true;
    }

    internal static string DefaultLanguage => LibrarianLanguage.Detect(LocManager.Instance.Language);
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

    private static async Task ExportAsync(IModSettingsUiActionHost host)
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
    internal static string Export(string userDirectory, string metadata)
    {
        string logs = Path.Combine(userDirectory, "logs");
        if (!Directory.Exists(logs)) throw new IOException(LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.logs_missing_directory"));
        var files = Directory.EnumerateFiles(logs).Where(p =>
            (p.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) &&
            (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).OrderBy(p => p).ToArray();
        if (files.Length == 0) throw new IOException(LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.logs_missing_files"));
        string directory = Path.Combine(userDirectory, "Librarian", "diagnostics");
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
