using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Scenes.Commands.CreateScene;
using PanelForge.Application.Features.Scenes.Commands.DeleteScene;
using PanelForge.Application.Features.Scenes.Commands.ReorderScenes;
using PanelForge.Application.Features.Scenes.Commands.UpdateScene;
using PanelForge.Application.Features.Scenes.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Scenes;

/// <summary>Scene dùng cùng cổng giao việc với ScriptLine: chương đã giao Writer thì chỉ người được giao mới sửa cấu trúc cảnh.</summary>
public class SceneAssignmentGateTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Guid _producer = Guid.NewGuid();
    private readonly Guid _assignedWriter = Guid.NewGuid();
    private readonly Guid _otherWriter = Guid.NewGuid();
    private readonly Chapter _chapter;
    private readonly Scene _scene;
    private readonly PipelineStage _scriptStage;

    public SceneAssignmentGateTests()
    {
        var studio = StudioWorkspace.Create("Studio", _producer);
        _db.StudioWorkspaces.Add(studio);
        var series = DomainSeries.Create(studio.Id, "Lantern", ReadingDirection.RightToLeft);
        _db.Series.Add(series);
        _chapter = Chapter.Create(series.Id, 1, "Lá thư");
        _db.Chapters.Add(_chapter);
        _scene = Scene.Create(_chapter.Id, 1, "Bến cảng");
        _db.Scenes.Add(_scene);
        var def = PipelineDefinition.Create(studio.Id, "Pipeline", seriesId: series.Id);
        _db.PipelineDefinitions.Add(def);
        _scriptStage = PipelineStage.Create(def.Id, "Script", "script", 1, allowedRole: WorkspaceRole.Writer);
        _db.PipelineStages.Add(_scriptStage);
        _db.SaveChanges();

        _auth.Setup(a => a.GetMemberRoleAsync(_producer, studio.Id, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceRole.Producer);
        _auth.Setup(a => a.GetMemberRoleAsync(_assignedWriter, studio.Id, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceRole.Writer);
        _auth.Setup(a => a.GetMemberRoleAsync(_otherWriter, studio.Id, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceRole.Writer);
    }

    private void AssignWriter()
    {
        _db.Assignments.Add(Assignment.Create(_chapter.SeriesId, _chapter.Id, WorkflowEntityType.Chapter, _chapter.Id,
            _scriptStage.Id, _assignedWriter, WorkspaceRole.Writer, "Viết kịch bản chương 1"));
        _db.SaveChanges();
    }

    private Task<Result<SceneDto>> CreateAs(Guid user) =>
        new CreateSceneCommandHandler(_db, _auth.Object).Handle(new CreateSceneCommand(_chapter.Id, user), default);

    [Fact]
    public async Task Writer_CanCreateAndUpdateScenes_IndependentlyOfAssignment()
    {
        AssignWriter();

        (await CreateAs(_otherWriter)).IsSuccess.Should().BeTrue();
        (await new UpdateSceneCommandHandler(_db, _auth.Object).Handle(
            new UpdateSceneCommand(_scene.Id, _otherWriter, "x", null), default)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Writer_IsBlocked_OnAllSceneCommands_WhenScriptIsFrozen()
    {
        _chapter.FreezeScript(_producer);
        await _db.SaveChangesAsync();

        (await CreateAs(_otherWriter)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await new UpdateSceneCommandHandler(_db, _auth.Object).Handle(
            new UpdateSceneCommand(_scene.Id, _otherWriter, "x", null), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await new ReorderScenesCommandHandler(_db, _auth.Object).Handle(
            new ReorderScenesCommand(_chapter.Id, _otherWriter, [new ReorderSceneItem(_scene.Id, 1)]), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await new DeleteSceneCommandHandler(_db, _auth.Object).Handle(
            new DeleteSceneCommand(_scene.Id, _otherWriter), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task AssignedWriter_AndProducer_CanCreate()
    {
        AssignWriter();

        (await CreateAs(_assignedWriter)).IsSuccess.Should().BeTrue();
        (await CreateAs(_producer)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AnyWriter_CanCreate_WhenChapterHasNoWriterAssignment() =>
        (await CreateAs(_otherWriter)).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task DeleteScene_SoftDeletesItsLines_AndUnlinksPanels()
    {
        var page = Page.CreateNew(_chapter.Id, 1, default, 1200, 1800, 300);
        _db.Pages.Add(page);
        var panel = Panel.Create(page.Id, 1, 1);
        panel.AssignToScene(_scene.Id);
        _db.Panels.Add(panel);
        _db.ScriptLines.AddRange(ScriptLine.Create(_scene.Id, 1, "a"), ScriptLine.Create(_scene.Id, 2, "b"));
        _db.SaveChanges();

        (await new DeleteSceneCommandHandler(_db, _auth.Object).Handle(
            new DeleteSceneCommand(_scene.Id, _producer), default)).IsSuccess.Should().BeTrue();

        _db.Scenes.Should().BeEmpty();
        _db.ScriptLines.Should().BeEmpty();
        _db.ScriptLines.IgnoreQueryFilters().Should().HaveCount(2).And.OnlyContain(l => l.IsDeleted);
        _db.Panels.Should().ContainSingle(p => p.Id == panel.Id && p.SceneId == null);
    }
}
