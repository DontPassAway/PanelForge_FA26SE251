using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.Panels.Commands.AutoAssignScriptLines;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Panels;

/// <summary>Tự phân bổ: cụm = dòng chỉ dẫn + các dòng thoại theo sau, cụm i → panel i theo thứ tự đọc.</summary>
public class AutoAssignScriptLinesTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _chapterId = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    private async Task<(Page Page, List<Panel> Panels, Scene Scene, List<ScriptLine> Lines)> SeedAsync(int panelCount)
    {
        var page = Page.CreateNew(_chapterId, 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        var scene = Scene.Create(_chapterId, 1);
        // Thứ tự đọc đảo so với thứ tự tạo để chắc handler sắp theo ReadingOrder
        var panels = Enumerable.Range(1, panelCount).Select(i => Panel.Create(page.Id, i, panelCount - i + 1)).ToList();
        var lines = new List<ScriptLine>
        {
            ScriptLine.Create(scene.Id, 1, stageDirection: "An bước vào phòng."),
            ScriptLine.Create(scene.Id, 2, "Có ai không?"),
            ScriptLine.Create(scene.Id, 3, "Ở đây.", "Bà Lan quay lại."),
            ScriptLine.Create(scene.Id, 4, stageDirection: "Cận mặt An."),
            ScriptLine.Create(scene.Id, 5, stageDirection: "Toàn cảnh căn phòng."),
            ScriptLine.Create(scene.Id, 6, "Lạ thật."),
        };
        _db.Pages.Add(page);
        _db.Scenes.Add(scene);
        _db.Panels.AddRange(panels);
        _db.ScriptLines.AddRange(lines);
        await _db.SaveChangesAsync();
        return (page, panels.OrderBy(p => p.ReadingOrder).ToList(), scene, lines);
    }

    [Fact]
    public async Task GroupsActionWithFollowingDialogue_AndAssignsInReadingOrder()
    {
        var (page, panels, scene, lines) = await SeedAsync(3);

        var result = await new AutoAssignScriptLinesCommandHandler(_db)
            .Handle(new AutoAssignScriptLinesCommand(page.Id, scene.Id, false, _user), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Applied.Should().BeTrue();
        result.Value.Assignments.Select(a => a.PanelId).Should().Equal(panels.Select(p => p.Id));
        result.Value.Assignments[0].ScriptLineIds.Should().Equal(lines[0].Id, lines[1].Id, lines[2].Id);
        result.Value.Assignments[1].ScriptLineIds.Should().Equal(lines[3].Id);
        result.Value.Assignments[2].ScriptLineIds.Should().Equal(lines[4].Id, lines[5].Id);
        result.Value.MissingPanels.Should().Be(0);

        (await _db.ScriptLines.SingleAsync(l => l.Id == lines[5].Id)).PanelId.Should().Be(panels[2].Id);
        (await _db.Panels.Where(p => p.PageId == page.Id).Select(p => p.SceneId).ToListAsync())
            .Should().OnlyContain(id => id == scene.Id);
    }

    [Fact]
    public async Task NotEnoughPanels_ReportsMissing_AndDryRunWritesNothing()
    {
        var (page, _, scene, lines) = await SeedAsync(2);

        var result = await new AutoAssignScriptLinesCommandHandler(_db)
            .Handle(new AutoAssignScriptLinesCommand(page.Id, scene.Id, true, _user), default);

        result.Value!.Applied.Should().BeFalse();
        result.Value.MissingPanels.Should().Be(1);
        result.Value.UnassignedLineIds.Should().Equal(lines[4].Id, lines[5].Id);
        (await _db.ScriptLines.AnyAsync(l => l.PanelId != null)).Should().BeFalse();
    }

    [Fact]
    public async Task LeavesLinesPlacedOnOtherPagesAlone()
    {
        var (page, panels, scene, lines) = await SeedAsync(3);
        var otherPage = Page.CreateNew(_chapterId, 2, LayoutFormat.StandardPage, 1000, 1500, 300);
        var otherPanel = Panel.Create(otherPage.Id, 1, 1);
        _db.Pages.Add(otherPage);
        _db.Panels.Add(otherPanel);
        lines[0].AssignToPanel(otherPanel.Id);
        await _db.SaveChangesAsync();

        var result = await new AutoAssignScriptLinesCommandHandler(_db)
            .Handle(new AutoAssignScriptLinesCommand(page.Id, scene.Id, false, _user), default);

        result.Value!.Assignments.SelectMany(a => a.ScriptLineIds).Should().NotContain(lines[0].Id);
        (await _db.ScriptLines.SingleAsync(l => l.Id == lines[0].Id)).PanelId.Should().Be(otherPanel.Id);
        result.Value.Assignments[0].ScriptLineIds.Should().Equal(lines[1].Id, lines[2].Id);
        result.Value.Assignments[0].PanelId.Should().Be(panels[0].Id);
    }

    [Fact]
    public async Task RejectsSceneFromOtherChapter()
    {
        var (page, _, _, _) = await SeedAsync(1);
        var foreign = Scene.Create(Guid.NewGuid(), 1);
        _db.Scenes.Add(foreign);
        await _db.SaveChangesAsync();

        var result = await new AutoAssignScriptLinesCommandHandler(_db)
            .Handle(new AutoAssignScriptLinesCommand(page.Id, foreign.Id, false, _user), default);

        result.IsSuccess.Should().BeFalse();
    }
}
