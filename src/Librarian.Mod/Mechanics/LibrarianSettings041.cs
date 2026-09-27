using Godot;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
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

            .WithDescription(Text("presentation_hint", "本机显示与声音选项立即生效并自动保存，不影响卡牌规则或队友设置。"))
            .AddSection("language", section => section.WithTitle(Text("language_section", "语言"))
                .AddDynamicChoice("language", Text("language", "模组语言"),
                    ModSettingsBindings.Callback("Librarian", "language", () => LibrarianLanguage.Selected, LibrarianLanguage.Select, () => { }),
                    LibrarianLanguage.Options, Text("language_hint", "首次识别游戏语言，此后保存选择。"), ModSettingsChoicePresentation.Dropdown)
                .AddButton("reload_language", Text("language_reload", "重新加载语言包"), Text("reload", "重新加载"),
                    host => { LibrarianLanguage.Reload(); host.RequestRefresh(); }, description: Text("language_reload_hint", "读取修改后的翻译文件。"))
                .AddButton("language_templates", Text("language_templates", "导出翻译模板"), Text("export_button", "导出"),
                    host => { LibrarianLanguage.ExportTemplates(); host.RequestRefresh(); }, description: Text("language_templates_hint", "导出内置语言包模板。"))
                .AddParagraph("language_path", ModSettingsText.Dynamic(() => Text("language_path", "自定义翻译目录：{Path}").Resolve().Replace("{Path}", LibrarianLanguage.PackDirectory)))
                .AddParagraph("language_status", ModSettingsText.Dynamic(() => LibrarianLanguage.Status)))
            .AddSection("effects", section => section.WithTitle(Text("effects", "卡牌与法球特效"))
                .AddToggle("card_effects", Text("card_effects", "卡牌特效"), LibrarianPreferences050.Bind("card_effects", p => p.CardEffects, (p,v) => p.CardEffects=v, true))
                .AddToggle("orb_effects", Text("orb_effects", "法球结算与变化特效"), LibrarianPreferences050.Bind("orb_effects", p => p.OrbEffects, (p,v) => p.OrbEffects=v, true), Text("orb_effects_hint", "保留法球数值、锁定标记与悬停说明。"))
                .AddToggle("particles", Text("particles", "附加粒子"), LibrarianPreferences050.Bind("particles", p => p.Particles, (p,v) => p.Particles=v, true))
                .AddToggle("reduced_motion", Text("reduced_motion", "减少动态效果"), LibrarianPreferences050.Bind("reduced_motion", p => p.ReducedMotion, (p,v) => p.ReducedMotion=v, false), Text("reduced_motion_hint", "简化新增特效，停止法阵旋转、法球浮动与施法手势。原版动画遵循游戏设置。"))
                .AddToggle("teammates", Text("teammates", "队友附加特效"), LibrarianPreferences050.Bind("teammates", p => p.TeammateEffects, (p,v) => p.TeammateEffects=v, true))
                .AddIntSlider("opacity", Text("opacity", "特效不透明度"), LibrarianPreferences050.Bind("opacity", p => p.EffectOpacity, (p,v) => p.EffectOpacity=v, 80), 10, 100, 10, valueFormatter: v => v+"%")
                .AddIntSlider("limit", Text("limit", "同时显示的新增特效上限"), LibrarianPreferences050.Bind("limit", p => p.EffectLimit, (p,v) => p.EffectLimit=v, 24), 4, 40, 4, description: Text("limit_hint", "低配置可调低；超出上限省略尾迹，数值与结算不变。")))
            .AddSection("display", section => section.WithTitle(Text("display", "战场显示"))
                .AddToggle("circle", Text("circle", "装饰法阵"), LibrarianPreferences050.Bind("circle", p => p.MagicCircle, (p,v) => p.MagicCircle=v, true), Text("circle_hint", "关闭时仍保留三法球与前台指示。"))
                .AddToggle("idle", Text("idle", "法球待机浮动"), LibrarianPreferences050.Bind("idle", p => p.OrbIdle, (p,v) => p.OrbIdle=v, true))
                .AddToggle("tide_block_feedback", Text("tide_block_feedback", "潮涌格挡变化提示"), LibrarianPreferences050.Bind("tide_block_feedback", p => p.TideBlockFeedback, (p,v) => p.TideBlockFeedback=v, false), Text("tide_block_feedback_hint", "显示“潮涌格挡 +X”及到期减少提示，默认关闭。"))
                .AddToggle("wave", Text("wave", "血条上方浪潮条"), LibrarianPreferences050.Bind("wave", p => p.WaveBar, (p,v) => p.WaveBar=v, true), Text("wave_hint", "关闭时仍可在浪潮状态说明中查看数值。")))
            .AddSection("audio", section => section.WithTitle(Text("audio", "模组音效"))
                .AddToggle("orb_sounds", Text("orb_sounds", "法球音效"), LibrarianPreferences050.Bind("orb_sounds", p => p.OrbSounds, (p,v) => p.OrbSounds=v, true))
                .AddIntSlider("volume", Text("volume", "法球音效音量"), LibrarianPreferences050.Bind("volume", p => p.SoundVolume, (p,v) => p.SoundVolume=v, 100), 0, 100, 10, valueFormatter: v => v+"%", description: Text("volume_hint", "同时受游戏主音量与音效音量控制。"))
                .AddButton("reset_presentation", Text("reset_presentation", "恢复显示与声音默认值"), Text("reset", "恢复"), host => { LibrarianPreferences050.Reset(); host.RequestRefresh(); }, description: Text("reset_hint", "仅恢复本页显示与声音选项，不改变解锁进度。"))
                .AddParagraph("save_status", ModSettingsText.DynamicFullRefreshOnly(() => LibrarianPreferences050.Status)))
            .AddSection("progression", section => section
                .WithTitle(Text("progression", "解锁调试"))
                .AddParagraph("current", ModSettingsText.DynamicFullRefreshOnly(ProgressText))
                .AddButton("progressive", Text("progressive", "正常渐进解锁"), Text("apply", "应用"),
                    host => { ApplyProgress(false); host.RequestRefresh(); },
                    description: Text("progressive_hint", "初始化正常解锁流程；不会撤销已获得内容。对新局生效。"))
                .AddButton("all", Text("all", "全部解锁"), Text("apply", "应用"),
                    host => { ApplyProgress(true); host.RequestRefresh(); },
                    description: Text("all_hint", "解锁当前档案的图书管理员全部章节；不会影响其他角色。此操作不提供降级。")))
            .AddSection("diagnostics", section => section
                .WithTitle(Text("diagnostics", "运行诊断"))
                .AddParagraph("export_scope", Text("export_scope", "仅导出游戏运行日志及版本信息，不包含存档或截图，不会自动上传。"))
                .AddButton("export", Text("export", "导出运行日志"), Text("export_button", "导出"),
                    host => TaskHelper.RunSafely(ExportAsync(host)))
                .AddParagraph("export_status", ModSettingsText.DynamicFullRefreshOnly(() => ExportStatus))), PageId);
        _registered = true;
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
                game_version = MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version,
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
