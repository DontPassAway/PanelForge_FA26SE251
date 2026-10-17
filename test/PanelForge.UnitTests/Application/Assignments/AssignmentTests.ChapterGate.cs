using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>Việc cấp Chapter ở stage sau (vd 7. Review) không bắt đầu / nộp được khi còn trang chưa xong stage trước.</summary>
public partial class AssignmentTests
{
    [Fact]
    public async Task ChapterLevelLaterStage_BlockedUntilEveryPanelOfChapterApprovedAtPriorStage()
    {
        var f = new Fixture();
        f.PagesList.Add(f.Page);
        var page2 = f.AddPage(2);
        var p1 = f.AddPanel(f.Page, 1);
        var p2 = f.AddPanel(page2, 1);

        // Stage Ink đóng vai "7. Review" cấp Chapter; stage trước là Pencil (theo Panel)
        var done = StageProgress.Begin(p1.Id, f.ArtistStage.Id);
        Fixture.Approve(done);
        f.Progress.Add(done);
        f.Progress.Add(StageProgress.Begin(p2.Id, f.ArtistStage.Id)); // trang 2 còn đang làm

        var review = f.SeedFor(WorkflowEntityType.Chapter, f.Chapter.Id, f.InkStage);

        var blocked = await Start(f, review, f.ArtistId);
        blocked.IsSuccess.Should().BeFalse();
        blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        blocked.ErrorMessage.Should().Contain("Trang 2").And.Contain("Pencil");
        review.Status.Should().NotBe(AssignmentStatus.InProgress);

        Fixture.Approve(f.Progress.Last());
        (await Start(f, review, f.ArtistId)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChapterLevelLaterStage_ChapterWithoutPages_IsBlocked()
    {
        var f = new Fixture();
        var review = f.SeedFor(WorkflowEntityType.Chapter, f.Chapter.Id, f.InkStage);

        var r = await Start(f, review, f.ArtistId);
        r.IsSuccess.Should().BeFalse();
        r.ErrorMessage.Should().Contain("chưa có trang");
    }
}
