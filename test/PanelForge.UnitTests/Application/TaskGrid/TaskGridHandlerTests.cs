using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.TaskGrid.Commands.ApproveStageTask;
using PanelForge.Application.Features.TaskGrid.Commands.AssignStageTaskLots;
using PanelForge.Application.Features.TaskGrid.Commands.GenerateStageTasks;
using PanelForge.Application.Features.TaskGrid.Commands.ReopenStageTask;
using PanelForge.Application.Features.TaskGrid.Commands.RequestStageTaskChanges;
using PanelForge.Application.Features.TaskGrid.Commands.ResolveStageTaskImpact;
using PanelForge.Application.Features.TaskGrid.Commands.StartStageTask;
using PanelForge.Application.Features.TaskGrid.Commands.UnassignStageTasks;
using PanelForge.Application.Features.TaskGrid.Commands.SubmitStageTasks;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Application.Features.TaskGrid.Queries.GetChapterTaskGrid;
using PanelForge.Application.Features.TaskGrid.Queries.GetStageTaskSubmissions;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.TaskGrid;

/// <summary>Lưới giao việc V3 – Lát cắt 1: sinh ô, giao lô, start/submit/approve theo BR-19 và BR-06.</summary>
public class TaskGridHandlerTests
{
    private sealed class Fixture
    {
        public readonly Guid ProducerId = Guid.NewGuid();
        public readonly Guid ArtistId = Guid.NewGuid();
        public readonly Guid LettererId = Guid.NewGuid();
        public readonly Guid EditorId = Guid.NewGuid();
        public readonly Guid PendingArtistId = Guid.NewGuid();
        public readonly Chapter Chapter;
        public readonly PipelineStage Pencil, Ink, Color, Lettering, Review;
        public readonly List<Page> Pages = [];
        public readonly List<StageTask> Tasks = [];
        public readonly List<StageTaskLot> Lots = [];
        public readonly List<StageTaskSubmission> Submissions = [];
        public readonly List<Notification> Notifications = [];
        public readonly Mock<IPanelForgeDbContext> Db = new();

        public Fixture()
        {
            var workspaceId = Guid.NewGuid();
            var series = Series.Create(workspaceId, "Series", ReadingDirection.RightToLeft);
            Chapter = Chapter.Create(series.Id, 1);
            var def = PipelineDefinition.Create(workspaceId, "Pipeline", seriesId: series.Id);

            // Pencil → (A: Ink → Color) ∥ (B: Lettering) → Review (cấp Chương)
            Pencil = PipelineStage.Create(def.Id, "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
            Ink = PipelineStage.Create(def.Id, "Ink", "ink", 2, allowedRole: WorkspaceRole.Artist);
            Ink.SetTrack("A");
            Color = PipelineStage.Create(def.Id, "Color", "color", 3, allowedRole: WorkspaceRole.Artist, isApprovalGate: true);
            Color.SetTrack("A");
            Lettering = PipelineStage.Create(def.Id, "Lettering", "lettering", 4, allowedRole: WorkspaceRole.Letterer);
            Lettering.SetTrack("B");
            Review = PipelineStage.Create(def.Id, "Review", "review", 5, allowedRole: WorkspaceRole.Editor,
                isApprovalGate: true, isTerminal: true);

            Pages.Add(Page.CreateNew(Chapter.Id, 1, default, 1200, 1800, 300));
            Pages.Add(Page.CreateNew(Chapter.Id, 2, default, 1200, 1800, 300));

            var members = new List<WorkspaceMember>
            {
                WorkspaceMember.Create(workspaceId, ArtistId, WorkspaceRole.Artist),
                WorkspaceMember.Create(workspaceId, LettererId, WorkspaceRole.Letterer),
                WorkspaceMember.Create(workspaceId, EditorId, WorkspaceRole.Editor),
                WorkspaceMember.Create(workspaceId, PendingArtistId, WorkspaceRole.Artist, WorkspaceMemberStatus.Pending)
            };

            Db.Setup(d => d.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { Chapter }));
            Db.Setup(d => d.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { series }));
            Db.Setup(d => d.PipelineDefinitions).Returns(DbSetMockHelper.CreateDbSetMock(new List<PipelineDefinition> { def }));
            Db.Setup(d => d.PipelineStages).Returns(DbSetMockHelper.CreateDbSetMock(
                new List<PipelineStage> { Pencil, Ink, Color, Lettering, Review }));
            Db.Setup(d => d.Pages).Returns(DbSetMockHelper.CreateDbSetMock(Pages));
            Db.Setup(d => d.StageTasks).Returns(DbSetMockHelper.CreateDbSetMock(Tasks));
            Db.Setup(d => d.StageTaskLots).Returns(DbSetMockHelper.CreateDbSetMock(Lots));
            Db.Setup(d => d.StageTaskSubmissions).Returns(DbSetMockHelper.CreateDbSetMock(Submissions));
            Db.Setup(d => d.Notifications).Returns(DbSetMockHelper.CreateDbSetMock(Notifications));
            Db.Setup(d => d.Panels).Returns(DbSetMockHelper.CreateDbSetMock(new List<Panel>()));
            Db.Setup(d => d.AssetReferences).Returns(DbSetMockHelper.CreateDbSetMock(new List<AssetReference>()));
            Db.Setup(d => d.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(members));
            Db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Task<Result<GenerateStageTasksResultDto>> Generate() =>
            new GenerateStageTasksCommandHandler(Db.Object).Handle(new GenerateStageTasksCommand(Chapter.Id, ProducerId), default);

        public Task<Result<AssignStageTaskLotsResultDto>> Assign(bool dryRun, params StageTaskLotRequest[] lots) =>
            new AssignStageTaskLotsCommandHandler(Db.Object).Handle(new AssignStageTaskLotsCommand(Chapter.Id, ProducerId, lots, dryRun), default);

        public Task<Result<StageTaskDto>> Start(StageTask task, Guid userId) =>
            new StartStageTaskCommandHandler(Db.Object).Handle(new StartStageTaskCommand(Chapter.Id, task.Id, userId), default);

        public Task<Result<IReadOnlyList<StageTaskDto>>> Submit(Guid userId, params StageTask[] tasks) =>
            new SubmitStageTasksCommandHandler(Db.Object).Handle(new SubmitStageTasksCommand(Chapter.Id, tasks.Select(t => t.Id).ToList(), userId), default);

        public Task<Result<StageTaskDto>> Approve(StageTask task, Guid userId) =>
            new ApproveStageTaskCommandHandler(Db.Object).Handle(new ApproveStageTaskCommand(Chapter.Id, task.Id, userId), default);

        public StageTask Cell(PipelineStage stage, int pageIndex) =>
            Tasks.Single(t => t.PipelineStageId == stage.Id && t.PageId == Pages[pageIndex].Id);

        public StageTaskLotRequest Lot(PipelineStage stage, Guid assignee, params StageTask[] tasks) =>
            new(stage.Id, assignee, tasks.Select(t => t.Id).ToList(), "brief");

        /// <summary>Sinh ô và giao Trang 1 cho Artist (Pencil, Ink, Color) và Letterer (Lettering).</summary>
        public async Task AssignPageOne()
        {
            (await Generate()).IsSuccess.Should().BeTrue();
            var result = await Assign(false,
                Lot(Pencil, ArtistId, Cell(Pencil, 0)), Lot(Ink, ArtistId, Cell(Ink, 0)),
                Lot(Color, ArtistId, Cell(Color, 0)), Lot(Lettering, LettererId, Cell(Lettering, 0)));
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }
    }

    [Fact]
    public async Task Generate_CreatesPageCellsAndOneChapterCell_AndIsIdempotent()
    {
        var f = new Fixture();

        var first = await f.Generate();
        first.Value!.Created.Should().Be(4 * 2 + 1);
        f.Tasks.Count(t => t.PipelineStageId == f.Review.Id && t.PageId == null).Should().Be(1);

        var second = await f.Generate();
        second.Value!.Created.Should().Be(0);
        f.Tasks.Should().HaveCount(9);
    }

    [Fact]
    public async Task Generate_CancelsCellsOfDeletedPage()
    {
        var f = new Fixture();
        await f.Generate();
        var removed = f.Pages[1];
        f.Pages.Remove(removed);

        var result = await f.Generate();

        result.Value!.Cancelled.Should().Be(4);
        f.Tasks.Where(t => t.PageId == removed.Id).Should().OnlyContain(t => t.Status == StageTaskStatus.Cancelled);
    }

    [Fact]
    public async Task AssignLots_DryRun_ValidatesWithoutSaving()
    {
        var f = new Fixture();
        await f.Generate();

        var result = await f.Assign(true, f.Lot(f.Pencil, f.ArtistId, f.Cell(f.Pencil, 0), f.Cell(f.Pencil, 1)));

        result.IsSuccess.Should().BeTrue();
        result.Value!.TaskCount.Should().Be(2);
        f.Lots.Should().BeEmpty();
        f.Tasks.Should().OnlyContain(t => t.Status == StageTaskStatus.Unassigned);
    }

    [Fact]
    public async Task AssignLots_WrongRole_PendingMember_OrTaskInTwoLots_IsRejected_AllOrNothing()
    {
        var f = new Fixture();
        await f.Generate();

        (await f.Assign(false,
                f.Lot(f.Pencil, f.ArtistId, f.Cell(f.Pencil, 0)),
                f.Lot(f.Lettering, f.ArtistId, f.Cell(f.Lettering, 0))))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        f.Lots.Should().BeEmpty();
        f.Cell(f.Pencil, 0).Status.Should().Be(StageTaskStatus.Unassigned);

        (await f.Assign(false, f.Lot(f.Pencil, f.PendingArtistId, f.Cell(f.Pencil, 0))))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        (await f.Assign(false,
                f.Lot(f.Pencil, f.ArtistId, f.Cell(f.Pencil, 0)),
                f.Lot(f.Pencil, f.ArtistId, f.Cell(f.Pencil, 0))))
            .IsSuccess.Should().BeFalse();
    }

    /// <summary>BR-19: Pencil xong mở cùng lúc Ink và Lettering; Color chờ Ink.</summary>
    [Fact]
    public async Task Flow_PencilApproved_OpensInkAndLetteringInParallel_ColorWaitsForInk()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        var pencil = f.Cell(f.Pencil, 0);
        var ink = f.Cell(f.Ink, 0);
        var color = f.Cell(f.Color, 0);
        var lettering = f.Cell(f.Lettering, 0);

        (await f.Start(ink, f.ArtistId)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await f.Start(lettering, f.LettererId)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        (await f.Start(pencil, f.ArtistId)).IsSuccess.Should().BeTrue();
        (await f.Submit(f.ArtistId, pencil)).IsSuccess.Should().BeTrue();
        pencil.Status.Should().Be(StageTaskStatus.Approved, "Pencil không có cổng duyệt nên nộp là tự duyệt");

        (await f.Start(ink, f.ArtistId)).IsSuccess.Should().BeTrue();
        (await f.Start(lettering, f.LettererId)).IsSuccess.Should().BeTrue();
        (await f.Start(color, f.ArtistId)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        (await f.Submit(f.ArtistId, ink)).IsSuccess.Should().BeTrue();
        (await f.Start(color, f.ArtistId)).IsSuccess.Should().BeTrue();

        var grid = await new GetChapterTaskGridQueryHandler(f.Db.Object).Handle(new GetChapterTaskGridQuery(f.Chapter.Id), default);
        grid.Value!.Stages.Single(s => s.Id == f.Review.Id).PrerequisiteStageIds
            .Should().BeEquivalentTo([f.Color.Id, f.Lettering.Id]);
        grid.Value.Lots.Single(l => l.PipelineStageId == f.Pencil.Id).Status.Should().Be(StageTaskLotStatus.Completed);
    }

    [Fact]
    public async Task Start_And_Submit_ByOtherUser_IsForbidden()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        var pencil = f.Cell(f.Pencil, 0);

        (await f.Start(pencil, f.LettererId)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        await f.Start(pencil, f.ArtistId);
        (await f.Submit(f.LettererId, pencil)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    /// <summary>BR-06: người làm không tự duyệt; Editor duyệt được và lô hoàn tất.</summary>
    [Fact]
    public async Task Approve_GatedStage_SelfApprovalForbidden_EditorApproves_LotCompletes()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        foreach (var stage in new[] { f.Pencil, f.Ink })
        {
            await f.Start(f.Cell(stage, 0), f.ArtistId);
            await f.Submit(f.ArtistId, f.Cell(stage, 0));
        }
        var color = f.Cell(f.Color, 0);
        await f.Start(color, f.ArtistId);
        (await f.Submit(f.ArtistId, color)).IsSuccess.Should().BeTrue();
        color.Status.Should().Be(StageTaskStatus.Submitted);

        (await f.Approve(color, f.ArtistId)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await f.Approve(color, f.EditorId)).IsSuccess.Should().BeTrue();

        color.Status.Should().Be(StageTaskStatus.Approved);
        f.Lots.Single(l => l.PipelineStageId == f.Color.Id).Status.Should().Be(StageTaskLotStatus.Completed);
    }

    // ---- Lát cắt 2: yêu cầu sửa, mở lại + Impact Review, bỏ gán ----

    private static async Task<(StageTask Pencil, StageTask Ink, StageTask Lettering)> PencilDoneInkAndLetteringStarted(Fixture f)
    {
        await f.AssignPageOne();
        var pencil = f.Cell(f.Pencil, 0);
        await f.Start(pencil, f.ArtistId);
        await f.Submit(f.ArtistId, pencil);
        var ink = f.Cell(f.Ink, 0);
        var lettering = f.Cell(f.Lettering, 0);
        (await f.Start(ink, f.ArtistId)).IsSuccess.Should().BeTrue();
        (await f.Start(lettering, f.LettererId)).IsSuccess.Should().BeTrue();
        return (pencil, ink, lettering);
    }

    [Fact]
    public async Task RequestChanges_BySelfForbidden_ByEditorReturnsToRework_ThenRestartable()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        foreach (var stage in new[] { f.Pencil, f.Ink })
        {
            await f.Start(f.Cell(stage, 0), f.ArtistId);
            await f.Submit(f.ArtistId, f.Cell(stage, 0));
        }
        var color = f.Cell(f.Color, 0);
        await f.Start(color, f.ArtistId);
        await f.Submit(f.ArtistId, color);

        var handler = new RequestStageTaskChangesCommandHandler(f.Db.Object);
        (await handler.Handle(new(f.Chapter.Id, color.Id, f.ArtistId, "x"), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await handler.Handle(new(f.Chapter.Id, color.Id, f.EditorId, "Màu da lệch"), default)).IsSuccess.Should().BeTrue();

        color.Status.Should().Be(StageTaskStatus.ReworkRequired);
        (await f.Start(color, f.ArtistId)).IsSuccess.Should().BeTrue();

        // Lịch sử: lần nộp 1 bị từ chối kèm nhận xét; người làm được báo
        var history = f.Submissions.Where(s => s.StageTaskId == color.Id).ToList();
        history.Should().ContainSingle().Which.Should().Match<StageTaskSubmission>(s =>
            s.AttemptNumber == 1 && s.Status == SubmissionStatus.Rejected && s.DecisionComment == "Màu da lệch");
        f.Notifications.Should().Contain(n => n.UserId == f.ArtistId && n.Type == NotificationType.AssignmentChangesRequested);
        (await handler.Handle(new(f.Chapter.Id, color.Id, f.EditorId, " "), default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    /// <summary>Mở lại Pencil: Ink và Lettering (đã bắt đầu, cả hai nhánh) bị gắn cờ; Color chưa bắt đầu thì không.</summary>
    [Fact]
    public async Task Reopen_MarksStartedDownstreamOnBothTracks_ImpactReviewNeedsReapprovedPrerequisite()
    {
        var f = new Fixture();
        var (pencil, ink, lettering) = await PencilDoneInkAndLetteringStarted(f);
        var otherPageInk = f.Cell(f.Ink, 1);

        var reopen = await new ReopenStageTaskCommandHandler(f.Db.Object)
            .Handle(new(f.Chapter.Id, pencil.Id, f.ProducerId, "Sai bố cục"), default);

        reopen.IsSuccess.Should().BeTrue(reopen.ErrorMessage);
        reopen.Value!.OutdatedTaskIds.Should().BeEquivalentTo([ink.Id, lettering.Id]);
        pencil.Status.Should().Be(StageTaskStatus.ReworkRequired);
        ink.IsInputOutdated.Should().BeTrue();
        ink.OutdatedReason.Should().Contain("Pencil").And.Contain("Sai bố cục");
        f.Cell(f.Color, 0).IsInputOutdated.Should().BeFalse();
        otherPageInk.IsInputOutdated.Should().BeFalse();
        (await f.Submit(f.ArtistId, ink)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        var resolve = new ResolveStageTaskImpactCommandHandler(f.Db.Object);
        (await resolve.Handle(new(f.Chapter.Id, ink.Id, f.ProducerId, Keep: true), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        await f.Start(pencil, f.ArtistId);
        await f.Submit(f.ArtistId, pencil);

        (await resolve.Handle(new(f.Chapter.Id, ink.Id, f.ProducerId, Keep: true), default)).IsSuccess.Should().BeTrue();
        ink.Status.Should().Be(StageTaskStatus.InProgress);
        (await resolve.Handle(new(f.Chapter.Id, lettering.Id, f.ProducerId, Keep: false), default)).IsSuccess.Should().BeTrue();
        lettering.Status.Should().Be(StageTaskStatus.ReworkRequired);
        lettering.IsInputOutdated.Should().BeFalse();
    }

    [Fact]
    public async Task Reopen_WithoutReason_IsValidationError()
    {
        var f = new Fixture();
        var (pencil, _, _) = await PencilDoneInkAndLetteringStarted(f);
        (await new ReopenStageTaskCommandHandler(f.Db.Object).Handle(new(f.Chapter.Id, pencil.Id, f.ProducerId, " "), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Unassign_ReturnsCellsToUnassigned_ClosesEmptyLot_AndRejectsApprovedCell()
    {
        var f = new Fixture();
        var (pencil, ink, _) = await PencilDoneInkAndLetteringStarted(f);
        var handler = new UnassignStageTasksCommandHandler(f.Db.Object);

        (await handler.Handle(new(f.Chapter.Id, [ink.Id, pencil.Id], f.ProducerId), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        ink.LotId.Should().NotBeNull();

        (await handler.Handle(new(f.Chapter.Id, [ink.Id], f.ProducerId), default)).IsSuccess.Should().BeTrue();
        ink.Status.Should().Be(StageTaskStatus.Unassigned);
        f.Lots.Single(l => l.PipelineStageId == f.Ink.Id).Status.Should().Be(StageTaskLotStatus.Closed);
        (await f.Assign(false, f.Lot(f.Ink, f.ArtistId, ink))).IsSuccess.Should().BeTrue();
    }

    /// <summary>BR-01 + BR-14: mỗi lần nộp ghi một bản; công đoạn không cổng tự duyệt; duyệt báo người làm và người kế tiếp.</summary>
    [Fact]
    public async Task Submissions_AreRecordedPerAttempt_AndNotificationsReachAssignees()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        f.Notifications.Where(n => n.Type == NotificationType.AssignmentAssigned).Select(n => n.UserId)
            .Should().BeEquivalentTo([f.ArtistId, f.ArtistId, f.ArtistId, f.LettererId]);

        var pencil = f.Cell(f.Pencil, 0);
        await f.Start(pencil, f.ArtistId);
        await f.Submit(f.ArtistId, pencil);

        // Pencil không có cổng: lần nộp tự duyệt, Ink (Artist) và Lettering (Letterer) được báo đến lượt
        var first = f.Submissions.Single(s => s.StageTaskId == pencil.Id);
        first.Status.Should().Be(SubmissionStatus.Approved);
        pencil.CurrentSubmissionId.Should().Be(first.Id);
        f.Notifications.Where(n => n.Type == NotificationType.AssignmentReady).Select(n => n.UserId)
            .Should().BeEquivalentTo([f.ArtistId, f.LettererId]);

        var ink = f.Cell(f.Ink, 0);
        await f.Start(ink, f.ArtistId);
        await f.Submit(f.ArtistId, ink);
        var color = f.Cell(f.Color, 0);
        await f.Start(color, f.ArtistId);
        await f.Submit(f.ArtistId, color);
        await new RequestStageTaskChangesCommandHandler(f.Db.Object).Handle(new(f.Chapter.Id, color.Id, f.EditorId, "Sửa"), default);
        await f.Start(color, f.ArtistId);
        await f.Submit(f.ArtistId, color);
        (await f.Approve(color, f.EditorId)).IsSuccess.Should().BeTrue();

        var history = await new GetStageTaskSubmissionsQueryHandler(f.Db.Object).Handle(new(f.Chapter.Id, color.Id), default);
        history.Value!.Select(s => (s.AttemptNumber, s.Status)).Should().Equal((2, "Approved"), (1, "Rejected"));
        f.Notifications.Should().Contain(n => n.UserId == f.ArtistId && n.Type == NotificationType.AssignmentApproved);
    }

    [Fact]
    public async Task Reopen_And_Unassign_NotifyAffectedAssignees()
    {
        var f = new Fixture();
        var (pencil, ink, _) = await PencilDoneInkAndLetteringStarted(f);
        f.Notifications.Clear();

        await new ReopenStageTaskCommandHandler(f.Db.Object).Handle(new(f.Chapter.Id, pencil.Id, f.ProducerId, "Sai bố cục"), default);
        f.Notifications.Where(n => n.Type == NotificationType.AssignmentUpdated).Select(n => n.UserId)
            .Should().BeEquivalentTo([f.ArtistId, f.LettererId]);
        f.Notifications.Should().Contain(n => n.UserId == f.ArtistId && n.Type == NotificationType.AssignmentChangesRequested);

        await f.Start(pencil, f.ArtistId);
        await f.Submit(f.ArtistId, pencil);
        await new ResolveStageTaskImpactCommandHandler(f.Db.Object).Handle(new(f.Chapter.Id, ink.Id, f.ProducerId, Keep: true), default);
        await new UnassignStageTasksCommandHandler(f.Db.Object).Handle(new(f.Chapter.Id, [ink.Id], f.ProducerId), default);
        f.Notifications.Should().Contain(n => n.UserId == f.ArtistId && n.Type == NotificationType.AssignmentUnassigned);
    }
}
