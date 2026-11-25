using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Panels.Commands.UpdatePanelCoordinates;
using PanelForge.Application.Features.ScriptLines.Commands.DeleteScriptLine;
using PanelForge.Application.Features.ScriptLines.Commands.UpdateScriptLine;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>
/// CF4 (bảng lan truyền, BR-05, BR-06, BR-18): nguồn của việc đã duyệt đổi sau khi duyệt thì việc đó phải làm lại —
/// Writer sửa/xoá thoại đã đặt chữ (Lettering), đổi khung Panel đã có artwork duyệt (Artwork + Lettering). Chỉ đúng Panel liên quan.
/// </summary>
public class SourceChangeReworkTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Mock<IPageRepository> _pages = new();
    private readonly Guid _writer = Guid.NewGuid();
    private readonly Chapter _chapter;
    private readonly PipelineStage _ink;
    private readonly PipelineStage _letter;
    private readonly Panel _p1;
    private readonly Panel _p2;
    private readonly Panel _p3;
    private readonly ScriptLine _line;
    private readonly ScriptLine _otherLine;

    public SourceChangeReworkTests()
    {
        var studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
        var series = DomainSeries.Create(studio.Id, "Moon", ReadingDirection.RightToLeft);
        _chapter = Chapter.Create(series.Id, 1);
        var scene = Scene.Create(_chapter.Id, 1);
        var def = PipelineDefinition.Create(studio.Id, "P", seriesId: series.Id);
        _ink = PipelineStage.Create(def.Id, "Ink", "ink", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
        _letter = PipelineStage.Create(def.Id, "Letter", "letter", 2, allowedRole: WorkspaceRole.Letterer);
        _db.AddRange(studio, series, _chapter, scene, def, _ink, _letter);

        // Trang 1: p1 (bóng gắn _line, có PanelId) và p2 (bóng gắn dòng khác); trang 2: p3
        var page1 = Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        var page2 = Page.CreateNew(_chapter.Id, 2, LayoutFormat.StandardPage, 1000, 1500, 300);
        _p1 = Panel.Create(page1.Id, 1, 1, "{\"x\":0}");
        _p2 = Panel.Create(page1.Id, 2, 2, "{\"x\":500}");
        _p3 = Panel.Create(page2.Id, 1, 1, "{\"x\":0}");
        _line = ScriptLine.Create(scene.Id, 1, "Xin chào");
        _otherLine = ScriptLine.Create(scene.Id, 2, "Tạm biệt");
        _db.AddRange(page1, page2, _p1, _p2, _p3, _line, _otherLine);
        _db.SaveChanges();

        page1.AddElement("SPEECH_BALLOON", 10, 10, 100, 50, 1, _writer, "Xin chào", scriptLineId: _line.Id,
            sourceLineText: "Xin chào", panelId: _p1.Id);
        page1.AddElement("SPEECH_BALLOON", 600, 10, 100, 50, 1, _writer, "Tạm biệt", scriptLineId: _otherLine.Id,
            sourceLineText: "Tạm biệt", panelId: _p2.Id);
        _pages.Setup(r => r.GetAsync(page1.Id, It.IsAny<CancellationToken>())).ReturnsAsync(page1);
        _pages.Setup(r => r.GetAsync(page2.Id, It.IsAny<CancellationToken>())).ReturnsAsync(page2);
        _auth.Setup(a => a.GetMemberRoleAsync(_writer, studio.Id, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceRole.Writer);
    }

    private (Assignment Work, StageProgress Row) Approved(PipelineStage stage, Panel panel, WorkspaceRole role)
    {
        var work = Assignment.Create(_chapter.SeriesId, _chapter.Id, WorkflowEntityType.Panel, panel.Id,
            stage.Id, Guid.NewGuid(), role, $"{stage.Name} panel {panel.PanelNumber}");
        work.MarkApproved();
        var row = StageProgress.Begin(panel.Id, stage.Id);
        typeof(StageProgress).GetProperty(nameof(StageProgress.Status))!.SetValue(row, StageProgressStatus.Approved);
        _db.Assignments.Add(work);
        _db.StageProgresses.Add(row);
        _db.SaveChanges();
        return (work, row);
    }

    private Task<PanelForge.Application.Common.Result<PanelForge.Application.Features.ScriptLines.Models.ScriptLineDto>> UpdateLine(
        string dialogue) =>
        new UpdateScriptLineCommandHandler(_db, _auth.Object, _pages.Object).Handle(
            new UpdateScriptLineCommand(_chapter.Id, _line.Id, _writer, dialogue, null, null, null), default);

    [Fact]
    public async Task DialogueEdited_ApprovedLetteringOfThatPanel_GoesBackToRework_OtherPanelsUntouched()
    {
        var (p1Letter, p1Row) = Approved(_letter, _p1, WorkspaceRole.Letterer);
        var (p2Letter, p2Row) = Approved(_letter, _p2, WorkspaceRole.Letterer);
        var (p1Ink, _) = Approved(_ink, _p1, WorkspaceRole.Artist);

        var r = await UpdateLine("Xin chào cậu!");

        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        p1Letter.Status.Should().Be(AssignmentStatus.ReworkRequired);
        p1Letter.LastFeedbackComment.Should().Contain("sửa thoại");
        p1Row.Status.Should().Be(StageProgressStatus.ChangesRequested);
        _db.Notifications.Should().ContainSingle(n => n.UserId == p1Letter.AssigneeUserId && n.AssignmentId == p1Letter.Id);
        p2Letter.Status.Should().Be(AssignmentStatus.Approved);
        p2Row.Status.Should().Be(StageProgressStatus.Approved);
        p1Ink.Status.Should().Be(AssignmentStatus.Approved); // sửa thoại không đụng artwork
    }

    [Fact]
    public async Task DialogueSavedUnchanged_DoesNotReworkAnything()
    {
        var (p1Letter, _) = Approved(_letter, _p1, WorkspaceRole.Letterer);

        (await UpdateLine("Xin chào")).IsSuccess.Should().BeTrue();

        p1Letter.Status.Should().Be(AssignmentStatus.Approved);
        _db.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task DialogueLineDeleted_ApprovedLetteringOfThatPanel_GoesBackToRework()
    {
        var (p1Letter, _) = Approved(_letter, _p1, WorkspaceRole.Letterer);
        var (p2Letter, _) = Approved(_letter, _p2, WorkspaceRole.Letterer);

        var r = await new DeleteScriptLineCommandHandler(_db, _auth.Object, _pages.Object).Handle(
            new DeleteScriptLineCommand(_chapter.Id, _line.Id, _writer, null), default);

        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        p1Letter.Status.Should().Be(AssignmentStatus.ReworkRequired);
        p2Letter.Status.Should().Be(AssignmentStatus.Approved);
    }

    private Task<PanelForge.Application.Common.Result<PanelForge.Application.Features.Panels.Models.PanelDto>> Move(
        Panel panel, string box, int? readingOrder = null) =>
        new UpdatePanelCoordinatesCommandHandler(_db).Handle(
            new UpdatePanelCoordinatesCommand(panel.Id, Guid.NewGuid(), box, readingOrder), default);

    [Fact]
    public async Task PanelFrameChanged_ApprovedArtworkAndLetteringOfThatPanel_GoBackToRework_OtherPanelsUntouched()
    {
        var (p1Ink, p1InkRow) = Approved(_ink, _p1, WorkspaceRole.Artist);
        var (p1Letter, _) = Approved(_letter, _p1, WorkspaceRole.Letterer);
        var (p2Ink, _) = Approved(_ink, _p2, WorkspaceRole.Artist);
        var (p3Ink, _) = Approved(_ink, _p3, WorkspaceRole.Artist);

        var r = await Move(_p1, "{\"x\":40}");

        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        p1Ink.Status.Should().Be(AssignmentStatus.ReworkRequired);
        p1InkRow.Status.Should().Be(StageProgressStatus.ChangesRequested);
        p1Letter.Status.Should().Be(AssignmentStatus.ReworkRequired);
        p1Ink.LastFeedbackComment.Should().Contain("Khung Panel 1");
        _db.Notifications.Should().HaveCount(2);
        p2Ink.Status.Should().Be(AssignmentStatus.Approved);
        p3Ink.Status.Should().Be(AssignmentStatus.Approved);
    }

    [Fact]
    public async Task PanelSavedWithSameFrame_OrOnlyReadingOrder_DoesNotRework()
    {
        var (p1Ink, _) = Approved(_ink, _p1, WorkspaceRole.Artist);

        (await Move(_p1, "{\"x\":0}")).IsSuccess.Should().BeTrue();
        (await Move(_p1, "{\"x\":0}", readingOrder: 3)).IsSuccess.Should().BeTrue();

        p1Ink.Status.Should().Be(AssignmentStatus.Approved);
        _db.Notifications.Should().BeEmpty();
    }
}
