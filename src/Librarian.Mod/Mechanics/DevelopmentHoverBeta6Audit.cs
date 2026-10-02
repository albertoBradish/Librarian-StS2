using System.IO;
using System.Text.Json;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace Librarian.Mechanics;

/// <summary>Opt-in beta checks for live orb text and the native title-icon renderer.</summary>
internal static class DevelopmentHoverBeta6Audit
{
    internal static async Task<int> Run(string output, Action<bool, string> check)
    {
        int cases = 0, screenshots = 0, checks = 0;
        void Check(bool ok, string label)
        {
            check(ok, "beta6 hover " + label);
            checks++;
        }
        Check(Path.IsPathFullyQualified(output), "absolute evidence output");
        Check(DisplayServer.GetName() != "headless", "native renderer");
        Directory.CreateDirectory(output);
        string originalLanguage = LibrarianLanguage.Selected;
        var originalSize = NGame.Instance!.GetWindow().Size;
        var evidence = new List<object>();
        var layer = new CanvasLayer { Layer = 120 }; NGame.Instance.AddChild(layer);
        var panel = new Control(); layer.AddChild(panel);
        var background = new ColorRect { Color = new Color("20272e") }; panel.AddChild(background);
        Vector2? nativeIconSize = null;
        async Task Wait() => await NGame.Instance.ToSignal(NGame.Instance.GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
        bool Has(IEnumerable<IHoverTip> tips, string key) => tips.Any(t => t.Id == LibrarianHoverTips.Tip(key).Id);
        IHoverTip[] Roots(OrbView orb)
        {
            var roots = new List<IHoverTip> { LibrarianOrbDisplay.BuildHoverTip(orb) };
            if (orb.IsLocked) roots.Add(LibrarianHoverTips.Tip("LOCK"));
            return roots.ToArray();
        }
        async Task Render(IEnumerable<IHoverTip> tips, string name)
        {
            var expected = IHoverTip.RemoveDupes(tips).OfType<HoverTip>().ToArray();
            var size = NGame.Instance.GetViewport().GetVisibleRect().Size;
            var owner = new Control { Position = new(24, 36), Size = new(20, 20) }; panel.AddChild(owner);
            var set = NHoverTipSet.CreateAndShow(owner, expected.Cast<IHoverTip>(), HoverTipAlignment.Right);
            Check(set is not null, "native hover set " + name);
            set!.GetParent().RemoveChild(set); panel.AddChild(set);
            try
            {
                await Wait();
                var rows = set.GetNode<VFlowContainer>("textHoverTipContainer").GetChildren().OfType<Control>().ToArray();
                Check(rows.Length == expected.Length, "native row count " + name);
                for (int i = 0; i < rows.Length; i++)
                {
                    var icon = rows[i].GetNode<TextureRect>("%Icon");
                    Check(rows[i].GetNode<Label>("%Title").Text == expected[i].Title, "native title binding " + name + " " + i);
                    Check(rows[i].GetNode<RichTextLabel>("%Description").Text == expected[i].Description, "native description binding " + name + " " + i);
                    Check(icon.Texture?.ResourcePath == expected[i].Icon?.ResourcePath, "native icon binding " + name + " " + i);
                    if (expected[i].Icon is null) continue;
                    if (expected[i].Id == ModelDb.Orb<LightningOrb>().DumbHoverTip.Id)
                    {
                        nativeIconSize = icon.Size;
                        Check(icon.Size.X > 0 && icon.Size.Y > 0, "native Lightning icon slot");
                    }
                    else
                    {
                        Check(nativeIconSize is { } reference && icon.Size.IsEqualApprox(reference), "same title icon size as Lightning " + name + " " + i);
                        Check(icon.IsVisibleInTree(), "visible native orb icon " + name + " " + i);
                        var rect = icon.GetGlobalRect();
                        Check(rect.Position.X >= -.5 && rect.Position.Y >= -.5 && rect.End.X <= size.X + .5 && rect.End.Y <= size.Y + .5,
                            "orb title icon fits viewport " + name + " " + i);
                    }
                }
                await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
                Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "capture " + name);
                screenshots++;
            }
            finally { NHoverTipSet.Remove(owner); owner.QueueFree(); }
        }
        try
        {
            foreach (string lang in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(lang);
                foreach (var kind in Enum.GetValues<OrbKind>())
                {
                    string expectedTitle = lang == "zhs" ? kind switch
                    {
                        OrbKind.Fire => "烈焰", OrbKind.Tide => "波涛", OrbKind.Growth => "翠叶", _ => throw new InvalidOperationException()
                    } : kind.ToString();
                    var gainTip = LibrarianHoverTips.Tip(kind.ToString().ToUpperInvariant());
                    Check(gainTip.Icon?.ResourcePath == LibrarianOrbVfx.TexturePath(kind), "gain-term existing artwork " + lang + " " + kind);
                    foreach (bool front in new[] { true, false })
                    foreach (bool active in new[] { true, false })
                    foreach (int lockedTurns in new[] { 0, 2, OrbView.PermanentLock })
                    {
                        var orb = new OrbView(kind, 107, active, front, lockedTurns);
                        var tip = LibrarianOrbDisplay.BuildHoverTip(orb);
                        string label = $"{lang} {kind} front={front} active={active} lock={lockedTurns}";
                        string stateLine = LibrarianLanguage.Format(front ? "ORB_FRONT" : "ORB_BACK");
                        string otherLine = LibrarianLanguage.Format(front ? "ORB_BACK" : "ORB_FRONT");
                        Check(tip.Title == expectedTitle, "short orb title " + label);
                        Check(tip.Icon?.ResourcePath == LibrarianOrbVfx.TexturePath(kind), "live orb existing artwork " + label);
                        Check(!tip.Description.Contains('{') && !tip.Description.Contains('}'), "formatted live orb " + label);
                        Check(tip.Description.StartsWith(lang == "zhs" ? "法球：" : "Orb:", StringComparison.Ordinal), "orb effect prefix " + label);
                        Check(tip.Description.Contains("107", StringComparison.Ordinal), "unhalved stored value in effect " + label);
                        Check(tip.Description.Contains(stateLine, StringComparison.Ordinal) && !tip.Description.Contains(otherLine, StringComparison.Ordinal), "position selects exactly one explanation " + label);
                        Check(tip.Description == LibrarianOrbDisplay.BuildHoverTip(orb with { IsActivated = !active }).Description, "position text independent of activation " + label);
                        if (!orb.IsLocked) Check(tip.Description.Split('\n').Length == 2, "two description lines without lock " + label);
                        else Check(tip.Description.Contains(LibrarianLanguage.Format(orb.IsPermanentlyLocked ? "LOCK_PERMANENT" : "LOCK_TURNS", ("Turns", lockedTurns)), StringComparison.Ordinal), "original lock detail retained " + label);
                        var expanded = LibrarianHoverTips.Expand(Roots(orb)).ToArray();
                        Check(IHoverTip.RemoveDupes(expanded).SequenceEqual(expanded), "native dedupe " + label);
                        Check(LibrarianHoverTips.ForText(tip.Description).All(t => expanded.Any(actual => actual.Id == t.Id)), "live orb reference closure " + label);
                        Check(Has(expanded, "LOCK") == orb.IsLocked, "lock reference matches live state " + label);
                        Check(!Has(expanded, "FIRE") && !Has(expanded, "TIDE") && !Has(expanded, "GROWTH"), "orb noun is not gain action " + label);
                        if (kind == OrbKind.Tide) Check(expanded.Any(t => t.Id == LibrarianLanguage.NativeTip(() => HoverTipFactory.Static(StaticHoverTip.Block)).Id)
                            && Has(expanded, "WAVES"), "Tide Block and Waves references " + label);
                        if (kind == OrbKind.Growth) Check(Has(expanded, "STRENGTHEN"), "Growth Strengthen reference " + label);
                        evidence.Add(new { language = lang, kind = kind.ToString(), front, active, lockedTurns, tip.Title, tip.Description,
                            icon = tip.Icon?.ResourcePath, references = expanded.Select(t => t.Id).ToArray() });
                        cases++;
                    }
                }
                foreach (string key in new[] { "ACTIVATE", "STRENGTHEN", "SETTLE", "WAVES", "LOCK", "FOREGROUND", "BACKGROUND" })
                    Check(LibrarianHoverTips.Tip(key).Icon is null, "unrelated term icon unchanged " + lang + " " + key);
            }
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution); await Wait();
                background.Size = NGame.Instance.GetViewport().GetVisibleRect().Size;
                foreach (string lang in new[] { "zhs", "eng" })
                {
                    LibrarianLanguage.Select(lang);
                    nativeIconSize = null;
                    IHoverTip[] actions = [ModelDb.Orb<LightningOrb>().DumbHoverTip, LibrarianHoverTips.Tip("FIRE"), LibrarianHoverTips.Tip("TIDE"), LibrarianHoverTips.Tip("GROWTH")];
                    await Render(LibrarianHoverTips.Expand(actions), $"hover-gain-icons-{lang}-{resolution.X}");
                    foreach (var front in Enum.GetValues<OrbKind>())
                    {
                        var roots = Enum.GetValues<OrbKind>().SelectMany(kind => Roots(new OrbView(kind, 107, false, kind == front)));
                        await Render(LibrarianHoverTips.Expand(roots), $"hover-orbs-{front.ToString().ToLowerInvariant()}-front-{lang}-{resolution.X}");
                    }
                    var locked = Enum.GetValues<OrbKind>().SelectMany(kind => Roots(new OrbView(kind, 107, false, kind == OrbKind.Tide,
                        kind == OrbKind.Growth ? OrbView.PermanentLock : 2)));
                    await Render(LibrarianHoverTips.Expand(locked), $"hover-orbs-locked-{lang}-{resolution.X}");
                }
            }
            Check(cases == 72, "all bilingual kind/position/activation/lock combinations");
            Check(screenshots == 20, "20 bilingual native icon screenshots");
            File.WriteAllText(Path.Combine(output, "beta6-hover-audit.json"), JsonSerializer.Serialize(new
            {
                cases, checks, screenshots, native = true, reusedExistingArtwork = true, rulesChanged = false, evidence
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            layer.QueueFree(); LibrarianLanguage.Select(originalLanguage);
            DisplayServer.WindowSetSize(originalSize); await Wait();
        }
        MainFile.Logger.Info($"V110_BETA6_HOVER_AUDIT_PASS cases={cases} screenshots={screenshots} checks={checks} native=True rulesChanged=False");
        return screenshots;
    }
}
