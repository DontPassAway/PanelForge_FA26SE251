using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assignments.Commands.ReturnWorkFromReview;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>Màn duyệt cả chương (7. Review): Editor trả việc của đúng trang/khung về người làm.</summary>
public class ReturnWorkFromReviewTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _editor = Guid.NewGuid();
    private readonly Chapter _chapter;
    private readonly Panel _panel;
    private readonly Panel _otherPagePanel;
    private readonly Assignment _letter;   // việc trang đã duyệt
    private readonly Assignment _review;   // việc cấp Chapter của Editor
    private readonly StageProgress _letterRow;

    public ReturnWorkFromReviewTests()
    {
        var producer = Guid.NewGuid();
        var studio = StudioWorkspace.Create("Studio", producer);
        _db.StudioWorkspaces.Add(studio);
        var series = DomainSeries.Create(studio.Id, "Test", ReadingDirection.RightToLeft);
        _db.Series.Add(series);
        _chapter = Chapter.Create(series.Id, 1, "Chương 1");
        _db.Chapters.Add(_chapter);
        var page1 = Page.CreateNew(_chapter.Id, 1, default, 1200, 1800, 300);
        var page2 = Page.CreateNew(_chapter.Id, 2, default, 1200, 1800, 300);
        _db.Pages.AddRange(page1, page2);
        _panel = Panel.Create(page1.Id, 1, 1);
        _otherPagePanel = Panel.Create(page2.Id, 1, 1);
        _db.Panels.AddRange(_panel, _otherPagePanel);

        var def = PipelineDefinition.Create(studio.Id, "P", seriesId: series.Id);
        var letterStage = PipelineStage.Create(def.Id, "Letter", "letter", 1, allowedRole: WorkspaceRole.Letterer, isInitial: true);
        var reviewStage = PipelineStage.Create(def.Id, "Review", "review", 2, allowedRole: WorkspaceRole.Editor);
        _db.PipelineStages.AddRange(letterStage, reviewStage);

        _letter = Assignment.Create(series.Id, _chapter.Id, WorkflowEntityType.Page, page1.Id, letterStage.Id,
            Guid.NewGuid(), WorkspaceRole.Letterer, "Chèn chữ trang 1");
        _letter.MarkApproved();
        _letterRow = StageProgress.Begin(_panel.Id, letterStage.Id);
        typeof(StageProgress).GetProperty(nameof(StageProgress.Status))!.SetValue(_letterRow, StageProgressStatus.Approved);
        _review = Assignment.Create(series.Id, _chapter.Id, WorkflowEntityType.Chapter, _chapter.Id, reviewStage.Id,
            _editor, WorkspaceRole.Editor, "Duyệt cả chương");
        _db.Assignments.AddRange(_letter, _review);
        _db.StageProgresses.Add(_letterRow);
        _db.SaveChanges();
    }

    private Task<Result<PanelForge.Application.Features.Assignments.Models.ChangeRequestDto>> Return(
        Guid target, string description = "Bóng thoại khung 1 che mặt Minh", Guid? panelId = null, Guid? review = null) =>
        new ReturnWorkFromReviewCommandHandler(_db).Handle(new ReturnWorkFromReviewCommand(
            _chapter.Id, review ?? _review.Id, target, _editor, description,
            "{\"x\":0.1,\"y\":0.1,\"w\":0.3,\"h\":0.2}", panelId), default);

    [Fact]
    public async Task ApprovedPageWork_IsSentBackWithChangeRequestAndNotification()
    {
        var r = await Return(_letter.Id, panelId: _panel.Id);

        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        _letter.Status.Should().Be(AssignmentStatus.ReworkRequired);
        _letterRow.Status.Should().Be(StageProgressStatus.ChangesRequested);
        var cr = _db.ChangeRequests.Single();
        cr.AssignmentId.Should().Be(_letter.Id);
        cr.ResponsibleUserId.Should().Be(_letter.AssigneeUserId);
        cr.PanelId.Should().Be(_panel.Id);
        cr.CreatedByUserId.Should().Be(_editor);
        _db.Notifications.Should().Contain(n =>
            n.UserId == _letter.AssigneeUserId && n.Type == NotificationType.AssignmentChangesRequested);
    }

    [Fact]
    public async Task ReviewerWhoHoldsTheWork_CannotSendItBack_FourEyes()
    {
        _letter.Reassign(_editor, WorkspaceRole.Letterer);
        await _db.SaveChangesAsync();

        (await Return(_letter.Id, panelId: _panel.Id)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        _letter.Status.Should().Be(AssignmentStatus.Approved);
        _db.ChangeRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidRequests_AreRejectedWithoutChangingWork()
    {
        (await Return(_letter.Id, description: " ")).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Return(_review.Id)).ErrorCode.Should().Be(ResultErrorCodes.Validation);                       // không trả việc cấp Chapter
        (await Return(_letter.Id, review: _letter.Id)).ErrorCode.Should().Be(ResultErrorCodes.Validation);   // gọi từ việc không phải Review
        (await Return(_letter.Id, panelId: _otherPagePanel.Id)).ErrorCode.Should().Be(ResultErrorCodes.Validation); // khung trang khác
        (await Return(Guid.NewGuid())).ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        _letter.Status.Should().Be(AssignmentStatus.Approved);
        _db.ChangeRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkUnderReview_MustGoThroughItsOwnGate()
    {
        typeof(Assignment).GetProperty(nameof(Assignment.Status))!.SetValue(_letter, AssignmentStatus.UnderReview);
        (await Return(_letter.Id)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task ReviewAlreadyApproved_IsConflict()
    {
        _review.MarkApproved();
        _db.SaveChanges();
        (await Return(_letter.Id)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }
}
