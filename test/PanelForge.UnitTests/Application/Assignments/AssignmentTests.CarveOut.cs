using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assignments.Commands.CreateAssignment;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>Giao cả chương cho Artist/Letterer rồi tách trang/khung cho người khác: phạm vi thực tế của việc cả chương co lại.</summary>
public partial class AssignmentTests
{
    private static CreateAssignmentCommand WholeChapter(Fixture f) =>
        f.CreateCmd(type: WorkflowEntityType.Chapter) with { TargetEntityId = null };

    /// <summary>Chương 2 trang, mỗi trang 1 khung; A giữ cả chương ở Pencil, trang 2 tách cho Artist2.</summary>
    private static async Task<(Assignment Whole, Panel P1, Panel P2)> ChapterWithPage2CarvedOut(Fixture f)
    {
        var page2 = f.AddPage(2);
        var p1 = f.AddPanel(f.Page, 1);
        var p2 = f.AddPanel(page2, 1);
        (await Create(f, WholeChapter(f))).IsSuccess.Should().BeTrue();
        var whole = f.Assignments.Single();
        (await Create(f, f.CreateCmd(assignee: f.Artist2Id, target: page2.Id))).IsSuccess.Should().BeTrue();
        return (whole, p1, p2);
    }

    [Fact]
    public async Task Create_ChapterScope_OnArtistStage_IsAllowed()
    {
        var f = new Fixture();

        var result = await Create(f, WholeChapter(f));

        result.IsSuccess.Should().BeTrue();
        result.Value!.EntityType.Should().Be(WorkflowEntityType.Chapter);
    }

    [Fact]
    public async Task CarveOut_PageFromWholeChapter_IsAllowed_NotifiesHolder_AndShrinksScope()
    {
        var f = new Fixture();
        var (whole, p1, _) = await ChapterWithPage2CarvedOut(f);

        f.Notifications.Should().Contain(n =>
            n.UserId == f.ArtistId && n.Type == NotificationType.AssignmentUnassigned && n.AssignmentId == whole.Id);

        (await Start(f, whole, f.ArtistId)).IsSuccess.Should().BeTrue();
        f.Progress.Should().ContainSingle().Which.PanelId.Should().Be(p1.Id);
    }

    [Fact]
    public async Task CarveOut_WhenWholeChapterAlreadySubmitted_IsConflict()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        (await Create(f, WholeChapter(f))).IsSuccess.Should().BeTrue();
        typeof(Assignment).GetProperty(nameof(Assignment.Status))!.SetValue(f.Assignments.Single(), AssignmentStatus.Submitted);

        var result = await Create(f, f.CreateCmd(assignee: f.Artist2Id, target: page2.Id));

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("còn đang làm");
    }

    [Fact]
    public async Task WholeChapter_SubmitAndApprove_CoverOnlyItsScope_AndUnlockNextStagePerPage()
    {
        var f = new Fixture();
        var (whole, p1, p2) = await ChapterWithPage2CarvedOut(f);
        (await Start(f, whole, f.ArtistId)).IsSuccess.Should().BeTrue();

        (await SubmitWithoutArtwork(f, whole)).ErrorMessage.Should().Contain("artwork");

        f.AssetReferences.Add(AssetReference.Create(
            Guid.NewGuid(), f.Series.WorkspaceId, f.Chapter.Id, f.Page.Id, f.ArtistStage.Id, 1, null, "a.png", f.ArtistId));
        (await SubmitWithoutArtwork(f, whole)).IsSuccess.Should().BeTrue();
        (await OpenReview(f, whole, f.EditorId)).IsSuccess.Should().BeTrue();
        (await Approve(f, whole, f.EditorId)).IsSuccess.Should().BeTrue();

        f.Progress.Single(r => r.PanelId == p1.Id).Status.Should().Be(StageProgressStatus.Approved);
        f.Progress.Should().NotContain(r => r.PanelId == p2.Id);

        // Ink trang 1 bắt đầu được dù trang 2 (người khác) chưa xong Pencil
        var ink = f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);
        (await Start(f, ink, f.ArtistId)).IsSuccess.Should().BeTrue();
    }
}
