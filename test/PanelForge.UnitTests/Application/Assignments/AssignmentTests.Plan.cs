using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assignments.Commands.CreateAssignmentPlan;
using PanelForge.Application.Features.Assignments.Commands.StartAssignment;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.Assignments.Queries.GetAssignmentPlanTemplate;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>Giao theo kế hoạch nhiều stage (all-or-nothing, phụ thuộc nối trong kế hoạch, 1 thông báo/người), mẫu từ chương trước, "đến lượt bạn".</summary>
public partial class AssignmentTests
{
    private static Task<Result<AssignmentPlanResultDto>> Plan(Fixture f, bool dryRun = false, params AssignmentPlanStageRequest[] stages) =>
        new CreateAssignmentPlanCommandHandler(f.Db.Object, new PassThroughUnitOfWork()).Handle(
            new CreateAssignmentPlanCommand(f.Chapter.Id, f.ProducerId,
                new CreateAssignmentPlanRequest(stages, "Làm chương 1", DryRun: dryRun)),
            CancellationToken.None);

    private static AssignmentPlanStageRequest[] FullTeam(Fixture f) =>
    [
        new(f.ReviewStage.Id, f.EditorId), // thứ tự gửi lộn xộn: handler tự xếp theo StageOrder
        new(f.ArtistStage.Id, f.ArtistId),
        new(f.InkStage.Id, f.Artist2Id),
    ];

    [Fact]
    public async Task Plan_CreatesAllStages_ChainsDependencies_OneBatch_OneNotificationPerAssignee()
    {
        var f = new Fixture();
        f.AddPage(2);

        var result = await Plan(f, false, FullTeam(f));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.CreatedCount.Should().Be(5); // 2 Pencil + 2 Ink + 1 Review
        result.Value.Stages.Select(s => s.StageName).Should().Equal("Pencil", "Ink", "Review");
        f.Assignments.Should().OnlyContain(a => a.BatchId == result.Value.BatchId);

        var pencil = f.Assignments.Where(a => a.PipelineStageId == f.ArtistStage.Id).ToList();
        var ink = f.Assignments.Where(a => a.PipelineStageId == f.InkStage.Id).ToList();
        var review = f.Assignments.Single(a => a.PipelineStageId == f.ReviewStage.Id);
        ink.Should().OnlyContain(i => i.GetDependsOnAssignmentIds().Single() == pencil.Single(p => p.TargetEntityId == i.TargetEntityId).Id);
        review.GetDependsOnAssignmentIds().Should().BeEquivalentTo(ink.Select(i => i.Id));

        f.Notifications.Select(n => n.UserId).Should().BeEquivalentTo([f.ArtistId, f.Artist2Id, f.EditorId]);
    }

    [Fact]
    public async Task Plan_SecondPageOfSameAssignee_CanStart_WithTheBatchNotification()
    {
        var f = new Fixture();
        f.AddPage(2);
        (await Plan(f, false, new AssignmentPlanStageRequest(f.ArtistStage.Id, f.ArtistId))).IsSuccess.Should().BeTrue();
        var pencil = f.Assignments.Where(a => a.PipelineStageId == f.ArtistStage.Id).ToList();
        pencil.Should().HaveCount(2);
        var notifiedId = f.Notifications.Should().ContainSingle().Subject.AssignmentId;
        var other = pencil.Single(a => a.Id != notifiedId);

        var result = await new StartAssignmentCommandHandler(f.Db.Object)
            .Handle(new StartAssignmentCommand(f.Chapter.Id, other.Id, f.ArtistId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        other.Status.Should().Be(AssignmentStatus.InProgress);
    }

    [Fact]
    public async Task Start_WithoutAnyNotification_IsStillRejected_BR14()
    {
        var f = new Fixture();
        var a = f.Seed();
        f.Notifications.Clear();

        var result = await new StartAssignmentCommandHandler(f.Db.Object)
            .Handle(new StartAssignmentCommand(f.Chapter.Id, a.Id, a.AssigneeUserId), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("BR-14");
    }

    [Fact]
    public async Task Plan_ConflictInLaterStage_SavesNothing()
    {
        var f = new Fixture();
        f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);
        var before = f.Assignments.Count;

        var result = await Plan(f, false, FullTeam(f));

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().StartWith("Stage Ink");
        f.Assignments.Should().HaveCount(before);
        f.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Plan_NoPagesYet_SkipsPageStages_ButAssignsChapterStage()
    {
        var f = new Fixture();
        f.PagesList.Clear();

        var result = await Plan(f, false, FullTeam(f));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.CreatedCount.Should().Be(1);
        result.Value.Stages.Where(s => s.SkippedReason != null).Select(s => s.StageName).Should().BeEquivalentTo(["Pencil", "Ink"]);
        f.Assignments.Should().ContainSingle(a => a.PipelineStageId == f.ReviewStage.Id);
    }

    [Fact]
    public async Task Bulk_AfterSkippedStage_BackfillsDependencyOfLaterChapterWork_OnlyFromNearestAssignedStage()
    {
        var f = new Fixture();
        f.PagesList.Clear();
        (await Plan(f, false, FullTeam(f))).IsSuccess.Should().BeTrue();
        var review = f.Assignments.Single(a => a.PipelineStageId == f.ReviewStage.Id);
        review.GetDependsOnAssignmentIds().Should().BeEmpty();

        f.PagesList.Add(f.Page);
        (await Bulk(f, f.InkStage.Id, assignee: f.Artist2Id)).IsSuccess.Should().BeTrue();
        var ink = f.Assignments.Where(a => a.PipelineStageId == f.InkStage.Id).Select(a => a.Id).ToList();
        review.GetDependsOnAssignmentIds().Should().BeEquivalentTo(ink);

        // Pencil giao sau: Review đã chờ Ink (đứng giữa) nên không nối thêm
        (await Bulk(f, f.ArtistStage.Id)).IsSuccess.Should().BeTrue();
        review.GetDependsOnAssignmentIds().Should().BeEquivalentTo(ink);
    }

    [Fact]
    public async Task Plan_DryRun_PreviewsWithoutSaving_AndWrongRoleFails()
    {
        var f = new Fixture();

        var preview = await Plan(f, true, FullTeam(f));
        preview.IsSuccess.Should().BeTrue(preview.ErrorMessage);
        preview.Value!.Stages.Should().HaveCount(3);
        f.Assignments.Should().BeEmpty();

        var wrongRole = await Plan(f, false, new AssignmentPlanStageRequest(f.ReviewStage.Id, f.ArtistId));
        wrongRole.IsSuccess.Should().BeFalse();
        f.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Template_UsesMostFrequentAssigneePerStage_FromPreviousChapter()
    {
        var f = new Fixture();
        var next = Chapter.Create(f.Series.Id, 2);
        f.Db.Setup(d => d.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { f.Chapter, next }));
        var artist = User.Create("artist@x.test", "Họa sĩ A");
        f.Db.Setup(d => d.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<User> { artist }));
        foreach (var _ in Enumerable.Range(0, 2))
            f.Assignments.Add(Assignment.Create(f.Series.Id, f.Chapter.Id, WorkflowEntityType.Page, Guid.NewGuid(), f.ArtistStage.Id,
                artist.Id, WorkspaceRole.Artist, "Vẽ pencil"));
        f.Assignments.Add(Assignment.Create(f.Series.Id, f.Chapter.Id, WorkflowEntityType.Page, Guid.NewGuid(), f.ArtistStage.Id,
            f.Artist2Id, WorkspaceRole.Artist, "khác"));

        var result = await new GetAssignmentPlanTemplateQueryHandler(f.Db.Object)
            .Handle(new GetAssignmentPlanTemplateQuery(next.Id), CancellationToken.None);

        result.Value!.SourceChapterId.Should().Be(f.Chapter.Id);
        var stage = result.Value.Stages.Should().ContainSingle().Subject;
        (stage.StageName, stage.AssigneeUserId, stage.AssigneeName, stage.Brief).Should().Be(("Pencil", artist.Id, "Họa sĩ A", "Vẽ pencil"));
    }

    [Fact]
    public async Task Approve_NotifiesDependentAssignee_WhenAllPrerequisitesApproved()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);
        var other = f.Seed(); // tiền đề thứ hai chưa xong
        var ready = Assignment.Create(f.Series.Id, f.Chapter.Id, WorkflowEntityType.Page, f.Page.Id, f.InkStage.Id,
            f.Artist2Id, WorkspaceRole.Artist, "ink", dependsOnAssignmentIds: [a.Id]);
        var stillWaiting = Assignment.Create(f.Series.Id, f.Chapter.Id, WorkflowEntityType.Chapter, f.Chapter.Id, f.ReviewStage.Id,
            f.EditorId, WorkspaceRole.Editor, "review", dependsOnAssignmentIds: [a.Id, other.Id]);
        f.Assignments.AddRange([ready, stillWaiting]);
        f.Notifications.Clear();

        (await Approve(f, a, f.EditorId)).IsSuccess.Should().BeTrue();

        var n = f.Notifications.Should().ContainSingle(x => x.Type == NotificationType.AssignmentReady).Subject;
        (n.UserId, n.AssignmentId).Should().Be((f.Artist2Id, ready.Id));
    }
}
