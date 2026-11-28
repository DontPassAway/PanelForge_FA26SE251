using FluentAssertions;
using PanelForge.Application.Features.ScriptLines.Commands.RestoreScriptVersion;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.ScriptLines;

/// <summary>SCR-23: ảnh chụp ScriptVersion → cây lưu kịch bản (phần lưu dùng lại ChapterScriptSaver đã có test riêng).</summary>
public class RestoreScriptVersionTests
{
    private static readonly Guid LivePanel = Guid.NewGuid();
    private static readonly Guid DeletedPanel = Guid.NewGuid();
    private static readonly Guid LiveLine = Guid.NewGuid();
    private static readonly Guid DeletedLine = Guid.NewGuid();
    private static readonly Guid Hero = Guid.NewGuid();
    private static readonly Guid HeroRevision = Guid.NewGuid();

    private static IReadOnlyList<ScriptVersionRow> Snapshot() =>
    [
        new("page:1", "page", 1, 0, null, ""),
        new("panel:1.1", "panel", 1, 1, null, "Toàn cảnh phố", CastRevisions: [new(Hero, HeroRevision)], EntityId: LivePanel),
        new($"line:{LiveLine}", "Dialogue", 1, 1, "AN", "Chào", Refs: [new(Hero, "An")], EntityId: LiveLine),
        new($"line:{DeletedLine}", "Action", 1, 1, null, "An chạy", EntityId: DeletedLine),
        new("panel:1.2", "panel", 1, 2, null, "", EntityId: DeletedPanel),
        new("page:2", "page", 2, 0, null, ""),
    ];

    private static IReadOnlyList<ScriptTreePage> Tree(IReadOnlyList<ScriptVersionRow> rows, List<string> warnings) =>
        RestoreScriptVersionCommandHandler.ToTree(rows, (panelId, page) => panelId == LivePanel && page == 1,
            new HashSet<Guid> { LiveLine }, warnings);

    [Fact]
    public void ToTree_KeepsLiveIds_AndRecreatesDeletedPanelsAndLines()
    {
        var tree = Tree(Snapshot(), []);

        tree.Select(p => p.Number).Should().Equal(1, 2);
        var panels = tree[0].Panels!;
        panels.Select(p => p.Number).Should().Equal(1, 2);

        panels[0].Id.Should().Be(LivePanel);
        panels[0].Direction.Should().Be("Toàn cảnh phố");
        panels[0].CastRevisions.Should().ContainSingle(c => c.EntryId == Hero && c.RevisionId == HeroRevision);
        panels[0].Lines!.Select(l => l.Id).Should().Equal(LiveLine, null); // dòng đã xoá mềm → tạo mới
        panels[0].Lines![0].Kind.Should().Be(ScriptLineKind.Dialogue);
        panels[0].Lines![0].SpeakerLabel.Should().Be("AN");
        panels[0].Lines![0].Refs.Should().ContainSingle(r => r.BibleEntryId == Hero && r.SurfaceText == "An");

        panels[1].Id.Should().BeNull(); // panel đã xoá → tạo mới
        panels[1].Direction.Should().BeNull();
        panels[1].CastRevisions.Should().BeEmpty(); // bản cũ không có cast → gỡ hết, không giữ cast hiện tại
        tree[1].Panels.Should().BeEmpty();
    }

    [Fact]
    public void ToTree_UnplacedLegacyLines_AreSkippedWithWarning()
    {
        var warnings = new List<string>();
        var rows = Snapshot().Append(new ScriptVersionRow($"line:{Guid.NewGuid()}", "Action", 0, 0, null, "Dòng cũ")).ToList();

        var tree = Tree(rows, warnings);

        tree.SelectMany(p => p.Panels!).SelectMany(p => p.Lines!).Should().HaveCount(2);
        warnings.Should().ContainSingle().Which.Should().Contain("1 dòng");
    }

    [Fact]
    public void ToTree_UnknownLineKind_Throws()
    {
        var rows = Snapshot().Append(new ScriptVersionRow($"line:{Guid.NewGuid()}", "Hologram", 1, 1, null, "?")).ToList();

        var act = () => Tree(rows, []);

        act.Should().Throw<FormatException>();
    }
}
