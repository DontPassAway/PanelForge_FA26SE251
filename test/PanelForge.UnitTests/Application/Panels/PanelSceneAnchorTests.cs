using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.Panels.Commands.AssignPanelScene;
using PanelForge.Application.Features.Panels.Commands.AssignPanelScriptLines;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Panels;

/// <summary>V1.2: Panel thuộc tối đa một Scene, ScriptLine neo tối đa một Panel; Scene không chứa Page.</summary>
public class PanelSceneAnchorTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _chapterId = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    private async Task<(Panel Panel, Scene Scene, ScriptLine Line)> SeedAsync(Guid? sceneChapter = null)
    {
        var page = Page.CreateNew(_chapterId, 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _db.Pages.Add(page);
        var panel = Panel.Create(page.Id, 1, 1);
        var scene = Scene.Create(sceneChapter ?? _chapterId, 1);
        var line = ScriptLine.Create(scene.Id, 1, "Xin chào");
        _db.Panels.Add(panel);
        _db.Scenes.Add(scene);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();
        return (panel, scene, line);
    }

    [Fact]
    public async Task AssignScene_SetsAndClears_ButRejectsOtherChapter()
    {
        var (panel, scene, _) = await SeedAsync();
        var handler = new AssignPanelSceneCommandHandler(_db);

        (await handler.Handle(new AssignPanelSceneCommand(panel.Id, scene.Id, _user), default)).Value!.SceneId.Should().Be(scene.Id);
        (await handler.Handle(new AssignPanelSceneCommand(panel.Id, null, _user), default)).Value!.SceneId.Should().BeNull();

        var foreign = Scene.Create(Guid.NewGuid(), 1);
        _db.Scenes.Add(foreign);
        await _db.SaveChangesAsync();
        (await handler.Handle(new AssignPanelSceneCommand(panel.Id, foreign.Id, _user), default)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task AssignScriptLines_ReplacesSet_AndMovesLineBetweenPanels()
    {
        var (panel, scene, line) = await SeedAsync();
        var page2 = Page.CreateNew(_chapterId, 2, LayoutFormat.StandardPage, 1000, 1500, 300);
        _db.Pages.Add(page2);
        var panel2 = Panel.Create(page2.Id, 1, 1);
        _db.Panels.Add(panel2);
        await _db.SaveChangesAsync();
        var handler = new AssignPanelScriptLinesCommandHandler(_db);

        (await handler.Handle(new AssignPanelScriptLinesCommand(panel.Id, [line.Id], _user), default)).IsSuccess.Should().BeTrue();
        (await _db.ScriptLines.SingleAsync()).PanelId.Should().Be(panel.Id);

        // Phân trang lại: dòng chuyển sang panel khác, nội dung giữ nguyên
        await handler.Handle(new AssignPanelScriptLinesCommand(panel2.Id, [line.Id], _user), default);
        (await _db.ScriptLines.SingleAsync()).PanelId.Should().Be(panel2.Id);

        // Tập rỗng nhả dòng
        await handler.Handle(new AssignPanelScriptLinesCommand(panel2.Id, [], _user), default);
        (await _db.ScriptLines.SingleAsync()).PanelId.Should().BeNull();
    }

    [Fact]
    public async Task AssignScriptLines_RejectsLineFromOtherChapter()
    {
        var (panel, _, _) = await SeedAsync();
        var foreignScene = Scene.Create(Guid.NewGuid(), 1);
        var foreignLine = ScriptLine.Create(foreignScene.Id, 1, "Khác chapter");
        _db.Scenes.Add(foreignScene);
        _db.ScriptLines.Add(foreignLine);
        await _db.SaveChangesAsync();

        var result = await new AssignPanelScriptLinesCommandHandler(_db)
            .Handle(new AssignPanelScriptLinesCommand(panel.Id, [foreignLine.Id], _user), default);

        result.IsSuccess.Should().BeFalse();
    }
}
