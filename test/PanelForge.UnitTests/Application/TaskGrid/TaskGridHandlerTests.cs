using PanelForge.Domain.Entities.Identity;
using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.SeriesPipeline.Common;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Application.Features.TaskGrid.Commands.UpdateStageTaskLot;
using PanelForge.Application.Features.TaskGrid.Commands.WithdrawStageTask;
using PanelForge.Application.Features.TaskGrid.Commands.ClaimStageTaskReview;
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
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Application.Features.TaskGrid.Queries.GetAutoAssignPlan;
using PanelForge.Application.Features.TaskGrid.Queries.GetChapterTaskGrid;
using PanelForge.Application.Features.TaskGrid.Queries.GetStageTaskSubmissions;
using PanelForge.Application.Features.TaskGrid.Queries.GetMyTaskLots;
using PanelForge.Application.Features.TaskGrid.Queries.GetTaskGridReviewQueue;
using PanelForge.Application.Features.TaskGrid.Commands.MigrateAssignmentsToGrid;
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
        public readonly List<Panel> Panels = [];
        public readonly List<StageTask> Tasks = [];
        public readonly List<StageTaskLot> Lots = [];
        public readonly List<StageTaskSubmission> Submissions = [];
        public readonly List<Notification> Notifications = [];
        public readonly List<User> Users = [];
        public readonly List<SeriesMember> SeriesMembers = [];
        public readonly List<AssetReference> Artwork;
        public readonly Mock<IPanelForgeDbContext> Db = new();
        public readonly Mock<IPageRepository> PageRepo = new();
        public readonly List<ScriptVersion> ScriptVersions = [];
        public readonly List<Assignment> Assignments = [];
        public Guid WorkspaceId => _workspaceId;
        private readonly Guid _workspaceId;
        private readonly List<WorkspaceMember> _members;

        public void AddMember(Guid userId, WorkspaceRole role) => _members.Add(WorkspaceMember.Create(_workspaceId, userId, role));
        public void RemoveMember(Guid userId) => _members.RemoveAll(m => m.UserId == userId);
        public Task<Result<AutoAssignPlanDto>> Plan() =>
            new GetAutoAssignPlanQueryHandler(Db.Object).Handle(new GetAutoAssignPlanQuery(Chapter.Id), default);

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

            _workspaceId = workspaceId;
            _members = new List<WorkspaceMember>
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
            Db.Setup(d => d.Users).Returns(DbSetMockHelper.CreateDbSetMock(Users));
            // Mỗi trang một panel và ảnh cả trang ở mọi công đoạn, để điều kiện nộp (CF2) của ô cấp Trang được thoả.
            // CreatedAt ở tương lai: vẫn tính là "bản mới" sau lần bị trả về.
            Panels.AddRange(Pages.Select(p => Panel.Create(p.Id, 1, 1, "{\"x\":0,\"y\":0,\"width\":10,\"height\":10}")));
            Artwork = Pages.SelectMany(p => new[] { Pencil, Ink, Color, Lettering, Review }.Select(s =>
            {
                var r = AssetReference.Create(Guid.NewGuid(), workspaceId, Chapter.Id, p.Id, s.Id, 1, null, "art.png", Guid.NewGuid());
                r.CreatedAt = DateTime.UtcNow.AddYears(1);
                return r;
            })).ToList();
            Db.Setup(d => d.Panels).Returns(DbSetMockHelper.CreateDbSetMock(Panels));
            Db.Setup(d => d.AssetReferences).Returns(DbSetMockHelper.CreateDbSetMock(Artwork));
            Db.Setup(d => d.Assignments).Returns(DbSetMockHelper.CreateDbSetMock(Assignments));
            Db.Setup(d => d.StageProgresses).Returns(DbSetMockHelper.CreateDbSetMock(new List<StageProgress>()));
            Db.Setup(d => d.ChangeRequests).Returns(DbSetMockHelper.CreateDbSetMock(new List<ChangeRequest>()));
            Db.Setup(d => d.ConsistencyFindings).Returns(DbSetMockHelper.CreateDbSetMock(new List<ConsistencyFinding>()));
            Db.Setup(d => d.AiSuggestions).Returns(DbSetMockHelper.CreateDbSetMock(new List<AiSuggestion>()));
            Db.Setup(d => d.ScriptVersions).Returns(DbSetMockHelper.CreateDbSetMock(ScriptVersions));
            PageRepo.Setup(p => p.GetStreamVersionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(7);
            Db.Setup(d => d.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(_members));
            Db.Setup(d => d.SeriesMembers).Returns(DbSetMockHelper.CreateDbSetMock(SeriesMembers));
            Db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace>()));
        }

        public Task<Result<GenerateStageTasksResultDto>> Generate() =>
            new GenerateStageTasksCommandHandler(Db.Object).Handle(new GenerateStageTasksCommand(Chapter.Id, ProducerId), default);

        public Task<Result<AssignStageTaskLotsResultDto>> Assign(bool dryRun, params StageTaskLotRequest[] lots) =>
            new AssignStageTaskLotsCommandHandler(Db.Object).Handle(new AssignStageTaskLotsCommand(Chapter.Id, ProducerId, lots, dryRun), default);

        public Task<Result<StageTaskDto>> Start(StageTask task, Guid userId) =>
            new StartStageTaskCommandHandler(Db.Object).Handle(new StartStageTaskCommand(Chapter.Id, task.Id, userId), default);

        public Task<Result<IReadOnlyList<StageTaskDto>>> Submit(Guid userId, params StageTask[] tasks) =>
            new SubmitStageTasksCommandHandler(Db.Object, PageRepo.Object).Handle(new SubmitStageTasksCommand(Chapter.Id, tasks.Select(t => t.Id).ToList(), userId), default);

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
    public async Task AutoAssignPlan_SplitsContiguousPages_KeepsSameArtistAcrossStages_AndCanBeApplied()
    {
        var f = new Fixture();
        var secondArtist = Guid.NewGuid();
        f.AddMember(secondArtist, WorkspaceRole.Artist);
        f.Pages.Add(Page.CreateNew(f.Chapter.Id, 3, default, 1200, 1800, 300));
        f.Pages.Add(Page.CreateNew(f.Chapter.Id, 4, default, 1200, 1800, 300));
        await f.Generate();

        var plan = (await f.Plan()).Value!;

        var pencil = plan.Lots.Where(l => l.PipelineStageId == f.Pencil.Id).ToList();
        pencil.Select(l => l.PageNumbers).Should().BeEquivalentTo(new[] { new[] { 1, 2 }, new[] { 3, 4 } });
        pencil.Select(l => l.AssigneeUserId).Should().BeEquivalentTo(new[] { f.ArtistId, secondArtist });
        var ink = plan.Lots.Where(l => l.PipelineStageId == f.Ink.Id).ToList();
        ink.Single(l => l.PageNumbers.Contains(1)).AssigneeUserId.Should().Be(pencil.Single(l => l.PageNumbers.Contains(1)).AssigneeUserId);
        plan.Lots.Single(l => l.PipelineStageId == f.Review.Id).AssigneeUserId.Should().Be(f.EditorId);
        plan.Lots.Should().NotContain(l => l.AssigneeUserId == f.PendingArtistId);
        plan.Skipped.Should().BeEmpty();
        f.Lots.Should().BeEmpty("lập phương án không được ghi gì");

        var applied = await f.Assign(false, plan.Lots.Select(l =>
            new StageTaskLotRequest(l.PipelineStageId, l.AssigneeUserId, l.TaskIds)).ToArray());
        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        f.Tasks.Should().OnlyContain(t => t.Status == StageTaskStatus.Assigned);
    }

    [Fact]
    public async Task AutoAssignPlan_PrefersLessLoadedMember_AndFallsBackToArtistForLettering()
    {
        var f = new Fixture();
        var secondArtist = Guid.NewGuid();
        f.AddMember(secondArtist, WorkspaceRole.Artist);
        f.RemoveMember(f.LettererId);
        await f.Generate();
        (await f.Assign(false, f.Lot(f.Color, f.ArtistId, f.Cell(f.Color, 0), f.Cell(f.Color, 1)))).IsSuccess.Should().BeTrue();

        var plan = (await f.Plan()).Value!;

        var pencil = plan.Lots.Single(l => l.PipelineStageId == f.Pencil.Id);
        pencil.AssigneeUserId.Should().Be(secondArtist, "người kia đang giữ 2 ô Color");
        pencil.TaskIds.Should().HaveCount(2);
        pencil.OpenWorkUnits.Should().Be(0);
        plan.Lots.Single(l => l.PipelineStageId == f.Ink.Id).AssigneeUserId.Should().Be(secondArtist, "Ink nối đúng người vẽ Pencil");
        // BR-31: không có Letterer thì Artist tự đặt chữ, Lettering không bị bỏ qua
        plan.Skipped.Should().BeEmpty();
        plan.Lots.Where(l => l.PipelineStageId == f.Lettering.Id).SelectMany(l => l.TaskIds).Should().HaveCount(2);
        plan.Lots.Where(l => l.PipelineStageId == f.Lettering.Id)
            .Select(l => l.AssigneeUserId).Should().OnlyContain(id => id == f.ArtistId || id == secondArtist);
    }

    [Fact]
    public async Task AutoAssignPlan_BalancesByPanelCount()
    {
        var f = new Fixture();
        f.AddMember(Guid.NewGuid(), WorkspaceRole.Artist);
        f.Pages.Add(Page.CreateNew(f.Chapter.Id, 3, default, 1200, 1800, 300));
        f.Pages.Add(Page.CreateNew(f.Chapter.Id, 4, default, 1200, 1800, 300));
        for (var i = 2; i <= 6; i++)
            f.Panels.Add(Panel.Create(f.Pages[0].Id, i, i, "{\"x\":0,\"y\":0,\"width\":10,\"height\":10}"));
        await f.Generate();

        var pencil = (await f.Plan()).Value!.Lots.Where(l => l.PipelineStageId == f.Pencil.Id).ToList();

        pencil.Select(l => l.WorkUnits).Should().BeEquivalentTo(new[] { 6, 3 }, "trang 1 có 6 panel, nặng bằng ba trang còn lại cộng lại");
        pencil.Select(l => l.PageNumbers.Count).Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Fact]
    public async Task AutoAssignPlan_TieGoesToMemberAssignedLongestAgo_NotLowestId()
    {
        var f = new Fixture();
        var secondArtist = Guid.NewGuid();
        f.AddMember(secondArtist, WorkspaceRole.Artist);
        f.Pages.Add(Page.CreateNew(f.Chapter.Id, 3, default, 1200, 1800, 300));
        f.Lots.Add(StageTaskLot.Create(f.Chapter.SeriesId, Guid.NewGuid(), f.Pencil.Id, f.ArtistId, WorkspaceRole.Artist,
            f.ProducerId, null, null));
        await f.Generate();

        var pencil = (await f.Plan()).Value!.Lots.Where(l => l.PipelineStageId == f.Pencil.Id).ToList();

        pencil.Single(l => l.AssigneeUserId == secondArtist).PageNumbers.Should().Equal(1, 2);
        pencil.Single(l => l.AssigneeUserId == f.ArtistId).PageNumbers.Should().Equal(3);
    }

    [Fact]
    public async Task AutoAssignPlan_SuggestsDueDateBackFromReleaseDate()
    {
        var f = new Fixture();
        f.Chapter.UpdateDetails("Chương 1", new DateOnly(2027, 1, 20));
        f.Ink.UpdateDetails("Ink", null, WorkspaceRole.Artist, false, 2);
        f.Color.UpdateDetails("Color", null, WorkspaceRole.Artist, true, 3);
        f.Lettering.UpdateDetails("Lettering", null, WorkspaceRole.Letterer, false, 1);
        f.Review.UpdateDetails("Review", null, WorkspaceRole.Editor, true, 2);
        await f.Generate();

        var plan = (await f.Plan()).Value!;

        plan.Lots.First(l => l.PipelineStageId == f.Pencil.Id).SuggestedDueDate
            .Should().Be(new DateTime(2027, 1, 12, 23, 59, 0, DateTimeKind.Utc), "trừ 2 + 3 + 1 + 2 ngày của các công đoạn sau");
        plan.Lots.Single(l => l.PipelineStageId == f.Review.Id).SuggestedDueDate
            .Should().Be(new DateTime(2027, 1, 20, 23, 59, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ReleaseMemberCells_NotifiesCoordinators_AndPlanProposesReplacement()
    {
        var f = new Fixture();
        var producer = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        f.AddMember(producer, WorkspaceRole.Producer);
        f.AddMember(replacement, WorkspaceRole.Artist);
        await f.AssignPageOne();
        var seriesIds = new[] { f.Chapter.SeriesId };
        f.RemoveMember(f.ArtistId);
        f.Notifications.Clear();

        await TaskGridRules.ReleaseMemberCellsAsync(f.Db.Object, f.ArtistId, f.WorkspaceId, f.ProducerId, seriesIds, default);

        f.Notifications.Where(n => n.Title == "Có 3 ô cần giao lại").Select(n => n.UserId)
            .Should().BeEquivalentTo(new[] { f.ProducerId, producer, f.EditorId });
        var plan = (await f.Plan()).Value!;
        plan.Lots.Where(l => l.PipelineStageId == f.Pencil.Id).Should().OnlyContain(l => l.AssigneeUserId == replacement);
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
    public async Task AssignLots_ArtistCanHoldLettering_BR31()
    {
        var f = new Fixture();
        await f.Generate();

        (await f.Assign(false, f.Lot(f.Lettering, f.ArtistId, f.Cell(f.Lettering, 0)))).IsSuccess.Should().BeTrue();
        f.Lots.Single().AssigneeUserId.Should().Be(f.ArtistId);
    }

    [Fact]
    public async Task AssignLots_WrongRole_PendingMember_OrTaskInTwoLots_IsRejected_AllOrNothing()
    {
        var f = new Fixture();
        await f.Generate();

        // Letterer không được giữ công đoạn vẽ (Pencil); chiều ngược lại Artist được đặt chữ (BR-31)
        (await f.Assign(false,
                f.Lot(f.Lettering, f.ArtistId, f.Cell(f.Lettering, 0)),
                f.Lot(f.Pencil, f.LettererId, f.Cell(f.Pencil, 0))))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        f.Cell(f.Lettering, 0).Status.Should().Be(StageTaskStatus.Unassigned);
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

    /// <summary>Nộp nhiều ô: tất cả hoặc không, nhưng 409 liệt kê đủ mọi trang chưa đạt.</summary>
    [Fact]
    public async Task Submit_ManyCellsWithoutArtwork_ListsEveryBlockedPage()
    {
        var f = new Fixture();
        (await f.Generate()).IsSuccess.Should().BeTrue();
        var p1 = f.Cell(f.Pencil, 0);
        var p2 = f.Cell(f.Pencil, 1);
        (await f.Assign(false, f.Lot(f.Pencil, f.ArtistId, p1, p2))).IsSuccess.Should().BeTrue();
        await f.Start(p1, f.ArtistId);
        await f.Start(p2, f.ArtistId);
        f.Artwork.RemoveAll(r => r.PipelineStageId == f.Pencil.Id);

        var result = await f.Submit(f.ArtistId, p1, p2);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Trang 1").And.Contain("Trang 2");
        p1.Status.Should().Be(StageTaskStatus.InProgress);
        p2.Status.Should().Be(StageTaskStatus.InProgress);
    }

    /// <summary>CF2: ô cấp Trang của Artist không nộp được khi trang chưa có artwork ở công đoạn đó (như V2).</summary>
    [Fact]
    public async Task Submit_ArtistCellWithoutArtwork_IsBlocked_AndCellStaysInProgress()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        var pencil = f.Cell(f.Pencil, 0);
        await f.Start(pencil, f.ArtistId);
        f.Artwork.RemoveAll(r => r.PipelineStageId == f.Pencil.Id);

        var result = await f.Submit(f.ArtistId, pencil);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Trang 1").And.Contain("artwork");
        pencil.Status.Should().Be(StageTaskStatus.InProgress);
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

    /// <summary>Người cứu viện: chuyển ô đang làm sang người khác trong 1 bước (cần cờ reassign); Producer gia hạn lô.</summary>
    [Fact]
    public async Task Reassign_MovesWorkingCellToHelper_AndLotCanBeExtended()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        var pencil = f.Cell(f.Pencil, 0);
        await f.Start(pencil, f.ArtistId);
        var helper = Guid.NewGuid();
        f.AddMember(helper, WorkspaceRole.Artist);
        f.AddMember(f.ProducerId, WorkspaceRole.Producer);
        var handler = new AssignStageTaskLotsCommandHandler(f.Db.Object);
        StageTaskLotRequest Lot() => new(f.Pencil.Id, helper, [pencil.Id]);
        var seen = new Dictionary<Guid, uint> { [pencil.Id] = pencil.RowVersion };

        (await handler.Handle(new(f.Chapter.Id, f.ProducerId, [Lot()], false), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict, "không có cờ reassign thì chỉ giao được ô chưa giao");
        (await handler.Handle(new(f.Chapter.Id, f.EditorId, [Lot()], false, true, true, seen), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden, "Editor không đổi người ô đã giao");
        (await handler.Handle(new(f.Chapter.Id, f.ProducerId, [Lot()], false, Reassign: true), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict, "ô đang làm dở cần xác nhận");
        (await handler.Handle(new(f.Chapter.Id, f.ProducerId, [Lot()], false, true, true,
                new Dictionary<Guid, uint> { [pencil.Id] = pencil.RowVersion + 1 }), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict, "ô đã đổi so với lúc xem");
        pencil.Status.Should().Be(StageTaskStatus.InProgress, "các lần bị từ chối không đổi gì");
        var moved = await handler.Handle(new(f.Chapter.Id, f.ProducerId, [Lot()], false, true, true, seen), default);

        moved.IsSuccess.Should().BeTrue(moved.ErrorMessage);
        pencil.Status.Should().Be(StageTaskStatus.Assigned, "người mới bấm Bắt đầu lại");
        pencil.LotId.Should().Be(moved.Value!.Lots.Single().Id);
        f.Notifications.Should().Contain(n => n.UserId == f.ArtistId && n.Type == NotificationType.AssignmentUnassigned);

        var due = DateTime.UtcNow.AddDays(3);
        var updated = await new UpdateStageTaskLotCommandHandler(f.Db.Object)
            .Handle(new(f.Chapter.Id, pencil.LotId!.Value, f.ProducerId, "Vẽ gấp", due, false), default);
        updated.Value!.DueDate.Should().Be(due);
        updated.Value.Brief.Should().Be("Vẽ gấp");
    }

    /// <summary>Lát cắt 3: rút bài khi chưa ai duyệt, khoá duyệt chống Editor dẫm nhau, lưới trả nhận xét lần sửa gần nhất.</summary>
    [Fact]
    public async Task Withdraw_ClaimReview_AndGridShowsLastDecisionComment()
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
        var withdraw = new WithdrawStageTaskCommandHandler(f.Db.Object);
        var claim = new ClaimStageTaskReviewCommandHandler(f.Db.Object);

        (await withdraw.Handle(new(f.Chapter.Id, color.Id, f.EditorId), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await withdraw.Handle(new(f.Chapter.Id, color.Id, f.ArtistId), default)).IsSuccess.Should().BeTrue();
        color.Status.Should().Be(StageTaskStatus.InProgress);
        f.Submissions.Single(s => s.StageTaskId == color.Id).Status.Should().Be(SubmissionStatus.Superseded);

        await f.Submit(f.ArtistId, color);
        (await claim.Handle(new(f.Chapter.Id, color.Id, f.ArtistId, Open: true), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await claim.Handle(new(f.Chapter.Id, color.Id, f.EditorId, Open: true), default)).IsSuccess.Should().BeTrue();
        color.Status.Should().Be(StageTaskStatus.UnderReview);
        (await withdraw.Handle(new(f.Chapter.Id, color.Id, f.ArtistId), default)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await claim.Handle(new(f.Chapter.Id, color.Id, f.EditorId, Open: false), default)).IsSuccess.Should().BeTrue();
        color.Status.Should().Be(StageTaskStatus.Submitted);

        var changed = await new RequestStageTaskChangesCommandHandler(f.Db.Object).Handle(new(f.Chapter.Id, color.Id, f.EditorId, "Màu da lệch"), default);
        changed.Value!.LastDecisionComment.Should().Be("Màu da lệch"); // DTO trả về đã có sẵn, FE khỏi tải lại lưới
        changed.Value.AttemptCount.Should().Be(2);
        var grid = await new GetChapterTaskGridQueryHandler(f.Db.Object).Handle(new GetChapterTaskGridQuery(f.Chapter.Id), default);
        var cell = grid.Value!.Tasks.Single(t => t.Id == color.Id);
        cell.LastDecisionComment.Should().Be("Màu da lệch");
        cell.AttemptCount.Should().Be(2);
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

    // ---- Lát cắt 3: phần còn lại của hệ thống đọc lưới ----

    /// <summary>Quyền ghi canvas theo ô lưới: chỉ người giữ ô đang làm; người khác, ô chưa bắt đầu, ô đã nộp đều bị chặn.</summary>
    [Fact]
    public async Task WorkScopeGuard_FollowsGridCells()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        var page = f.Pages[0].Id;
        var otherArtist = Guid.NewGuid();
        f.AddMember(otherArtist, WorkspaceRole.Artist);
        Task<string?> Deny(Guid user) => WorkScopeGuard.DenyReasonAsync(f.Db.Object, user, page, null, default);

        (await Deny(f.ArtistId)).Should().Contain("Bắt đầu");          // ô Pencil mới Assigned
        var pencil = f.Cell(f.Pencil, 0);
        await f.Start(pencil, f.ArtistId);
        (await Deny(f.ArtistId)).Should().BeNull();
        (await WorkScopeGuard.DenyReasonAsync(f.Db.Object, f.ArtistId, page, null, default, layout: true))
            .Should().BeNull("Pencil là công đoạn vẽ đầu tiên nên được sửa khung");
        (await Deny(otherArtist)).Should().Contain("người khác");
        (await Deny(f.LettererId)).Should().Contain("Bắt đầu");        // Letterer giữ ô Lettering chưa bắt đầu

        await f.Submit(f.ArtistId, pencil);                                // Pencil không cổng → Approved; Ink còn Assigned
        (await Deny(f.ArtistId)).Should().Contain("Bắt đầu");
        (await WorkScopeGuard.DenyReasonAsync(f.Db.Object, otherArtist, f.Pages[1].Id, null, default))
            .Should().Contain("không có ô việc", "chương đã dùng lưới: trang chưa giao thì không ai ngoài Producer sửa");

        // Ink và Color xong → Pencil/Ink/Color của trang 1 đều Approved: Artist không còn sửa được trang đã duyệt
        var ink = f.Cell(f.Ink, 0);
        await f.Start(ink, f.ArtistId);
        (await Deny(f.ArtistId)).Should().BeNull("Ink vẫn vẽ được lên trang");
        (await WorkScopeGuard.DenyReasonAsync(f.Db.Object, f.ArtistId, page, null, default, layout: true))
            .Should().Contain("Bố cục khung", "Ink không được đổi khung Pencil đã duyệt");
        await f.Submit(f.ArtistId, ink);
        var color = f.Cell(f.Color, 0);
        if (color.Status != StageTaskStatus.Unassigned)
        {
            await f.Start(color, f.ArtistId);
            await f.Submit(f.ArtistId, color);
            await new ApproveStageTaskCommandHandler(f.Db.Object).Handle(new(f.Chapter.Id, color.Id, f.EditorId), default);
            (await Deny(f.ArtistId)).Should().Contain("không có ô việc");
        }
    }

    [Fact]
    public async Task ChapterLockGate_BlocksUnfinishedAndOutdatedGridCells_OnlyWhenGridIsUsed()
    {
        var f = new Fixture();
        (await ChapterLockGate.EvaluateAsync(f.Db.Object, f.Chapter, default))
            .Should().NotContain(i => i.Code == "TASK_GRID_NOT_APPROVED", "chưa sinh ô thì chương chưa dùng lưới");
        await f.Generate();
        (await ChapterLockGate.EvaluateAsync(f.Db.Object, f.Chapter, default))
            .Should().Contain(i => i.Code == "TASK_GRID_NOT_APPROVED" && i.Blocking, "đã sinh ô mà chưa giao thì chưa duyệt chương được");

        var (pencil, _, _) = await PencilDoneInkAndLetteringStarted(f);
        await new ReopenStageTaskCommandHandler(f.Db.Object).Handle(new(f.Chapter.Id, pencil.Id, f.ProducerId, "Sửa"), default);

        var gate = await ChapterLockGate.EvaluateAsync(f.Db.Object, f.Chapter, default);
        gate.Should().Contain(i => i.Code == "TASK_GRID_NOT_APPROVED" && i.Blocking);
        gate.Should().Contain(i => i.Code == "IMPACT_FLAGS" && i.Count == 2);
        gate.Should().NotContain(i => i.Code == "PAGES_WITHOUT_WORK" && i.Count == 2);
    }

    // ---- Hoàn thiện lưới trước Clean Cutover ----

    [Fact]
    public async Task Submit_GatedCell_NotifiesEditor_AndSnapshotsCanvasLayoutAndScriptVersion()
    {
        var f = new Fixture();
        var script = ScriptVersion.Create(f.Chapter.Id, 3, "v3", f.ProducerId, "hash", "[]", 0);
        f.ScriptVersions.Add(script);
        await f.AssignPageOne();
        foreach (var stage in new[] { f.Pencil, f.Ink })
        {
            await f.Start(f.Cell(stage, 0), f.ArtistId);
            await f.Submit(f.ArtistId, f.Cell(stage, 0));
        }
        f.Notifications.Should().NotContain(n => n.Type == NotificationType.AssignmentSubmitted, "Pencil/Ink không có cổng duyệt");

        var color = f.Cell(f.Color, 0);
        await f.Start(color, f.ArtistId);
        await f.Submit(f.ArtistId, color);

        f.Notifications.Should().ContainSingle(n => n.Type == NotificationType.AssignmentSubmitted).Which.UserId.Should().Be(f.EditorId);
        var submission = f.Submissions.Single(s => s.StageTaskId == color.Id);
        submission.ScriptVersionId.Should().Be(script.Id);
        submission.CanvasVersionsJson.Should().Contain(f.Pages[0].Id.ToString()).And.Contain("7");
        submission.CanvasVersionsJson.Should().NotContain(f.Pages[1].Id.ToString());
    }

    [Fact]
    public async Task Submit_PinsFrozenScriptVersion_NotNewerDraft()
    {
        var f = new Fixture();
        var frozen = ScriptVersion.Create(f.Chapter.Id, 3, "v3 đóng băng", f.ProducerId, "h3", "[]", 0);
        var draft = ScriptVersion.Create(f.Chapter.Id, 4, "v4 nháp sau khi mở băng", f.ProducerId, "h4", "[]", 0);
        f.ScriptVersions.AddRange([frozen, draft]);
        typeof(Chapter).GetProperty(nameof(Chapter.FrozenScriptVersionId))!.SetValue(f.Chapter, frozen.Id);
        await f.AssignPageOne();

        var pencil = f.Cell(f.Pencil, 0);
        await f.Start(pencil, f.ArtistId);
        await f.Submit(f.ArtistId, pencil);

        // BR-01/BR-08: người làm vẽ theo bản đã đóng băng, không phải nháp Writer đang sửa
        f.Submissions.Single(s => s.StageTaskId == pencil.Id).ScriptVersionId.Should().Be(frozen.Id);
    }

    [Fact]
    public async Task Generate_CancelsUnassignedCellsOfDisabledStage_AndRevivesThemWhenReEnabled()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        f.Ink.SetEnabled(false);

        var off = await f.Generate();
        off.Value!.Cancelled.Should().Be(1, "chỉ ô Ink trang 2 chưa giao; ô Ink trang 1 đã giao thì giữ");
        f.Cell(f.Ink, 1).Status.Should().Be(StageTaskStatus.Cancelled);
        f.Cell(f.Ink, 0).Status.Should().Be(StageTaskStatus.Assigned);

        f.Ink.SetEnabled(true);
        (await f.Generate()).Value!.Created.Should().Be(1);
        f.Cell(f.Ink, 1).Status.Should().Be(StageTaskStatus.Unassigned);
    }

    [Fact]
    public async Task PipelineEdits_AreBlockedWhileGridHasWorkInFlight()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        await f.Start(f.Cell(f.Pencil, 0), f.ArtistId);

        (await SeriesPipelineStore.StructureLockedReasonAsync(f.Db.Object, f.Chapter.SeriesId, default)).Should().Contain("đang làm dở");
        (await SeriesPipelineStore.StageInUseOnGridReasonAsync(f.Db.Object, f.Pencil.Id, default)).Should().NotBeNull();
        (await SeriesPipelineStore.StageInUseOnGridReasonAsync(f.Db.Object, f.Review.Id, default))
            .Should().BeNull("Review chưa có ô được giao nên tắt được");
    }

    // ─── Lát cắt 4: Clean Cutover ─────────────────────────────────────────────

    private static Assignment Legacy(Fixture f, WorkflowEntityType type, Guid target, PipelineStage stage, Guid user, WorkspaceRole role) =>
        Assignment.Create(f.Chapter.SeriesId, f.Chapter.Id, type, target, stage.Id, user, role, "việc cũ");

    private static Task<Result<MigrateAssignmentsResultDto>> Migrate(Fixture f)
    {
        var sender = new Mock<MediatR.ISender>();
        sender.Setup(s => s.Send(It.IsAny<GenerateStageTasksCommand>(), It.IsAny<CancellationToken>()))
            .Returns((GenerateStageTasksCommand c, CancellationToken ct) => new GenerateStageTasksCommandHandler(f.Db.Object).Handle(c, ct));
        return new MigrateAssignmentsToGridCommandHandler(f.Db.Object, sender.Object)
            .Handle(new MigrateAssignmentsToGridCommand(f.Chapter.SeriesId, f.ProducerId), default);
    }

    [Fact]
    public async Task MigrateAssignments_MapsLegacyWorkToLots_SkipsDisabledStage_AndBlocksNewLegacyAssignments()
    {
        var f = new Fixture();
        var pencil = Legacy(f, WorkflowEntityType.Page, f.Pages[0].Id, f.Pencil, f.ArtistId, WorkspaceRole.Artist);
        pencil.StartWork();
        pencil.SubmitForReview();
        var lettering = Legacy(f, WorkflowEntityType.Chapter, f.Chapter.Id, f.Lettering, f.LettererId, WorkspaceRole.Letterer);
        var review = Legacy(f, WorkflowEntityType.Chapter, f.Chapter.Id, f.Review, f.EditorId, WorkspaceRole.Editor);
        var ink = Legacy(f, WorkflowEntityType.Page, f.Pages[0].Id, f.Ink, f.ArtistId, WorkspaceRole.Artist);
        f.Assignments.AddRange([pencil, lettering, review, ink]);
        f.Ink.SetEnabled(false);

        var result = await Migrate(f);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Migrated.Should().Be(3);
        result.Value.CellsAssigned.Should().Be(4, "Pencil trang 1 + Lettering 2 trang + Review");
        result.Value.Skipped.Should().ContainSingle(s => s.AssignmentId == ink.Id);
        f.Cell(f.Pencil, 0).Status.Should().Be(StageTaskStatus.InProgress, "Submitted V2 về InProgress để nộp lại có ảnh chụp");
        f.Cell(f.Pencil, 1).Status.Should().Be(StageTaskStatus.Unassigned);
        f.Cell(f.Lettering, 0).LotId.Should().NotBeNull().And.Be(f.Cell(f.Lettering, 1).LotId, "việc cả chương phủ mọi trang trong một lô");
        f.Tasks.Single(t => t.PipelineStageId == f.Review.Id).Status.Should().Be(StageTaskStatus.Assigned);
        f.Assignments.Should().ContainSingle().Which.Should().Be(ink, "việc đã chuyển bị xoá mềm, việc bỏ qua giữ nguyên");
        pencil.CancelReason.Should().Be(MigrateAssignmentsToGridCommandHandler.CancelReason);

        (await Migrate(f)).Value!.Migrated.Should().Be(0, "gọi lại an toàn");
        (await AssignmentRules.GridActiveReasonAsync(f.Db.Object, f.Chapter.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task MyTaskLots_ListsCallerLotsWithStartableCells()
    {
        var f = new Fixture();
        await f.AssignPageOne();

        var artist = await new GetMyTaskLotsQueryHandler(f.Db.Object).Handle(new GetMyTaskLotsQuery(f.WorkspaceId, f.ArtistId), default);
        var letterer = await new GetMyTaskLotsQueryHandler(f.Db.Object).Handle(new GetMyTaskLotsQuery(f.WorkspaceId, f.LettererId), default);

        artist.Value!.Select(l => l.StageName).Should().Equal("Pencil", "Ink", "Color");
        artist.Value![0].Tasks.Should().ContainSingle().Which.IsStartable.Should().BeTrue();
        artist.Value![1].Tasks.Single().IsStartable.Should().BeFalse("Ink chờ Pencil");
        letterer.Value!.Should().ContainSingle().Which.Tasks.Single().PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task ReviewQueue_ListsSubmittedGatedCells()
    {
        var f = new Fixture();
        await f.AssignPageOne();
        foreach (var stage in new[] { f.Pencil, f.Ink, f.Color })
        {
            (await f.Start(f.Cell(stage, 0), f.ArtistId)).IsSuccess.Should().BeTrue();
            (await f.Submit(f.ArtistId, f.Cell(stage, 0))).IsSuccess.Should().BeTrue();
        }

        var queue = await new GetTaskGridReviewQueueQueryHandler(f.Db.Object).Handle(new GetTaskGridReviewQueueQuery(f.WorkspaceId), default);

        var item = queue.Value!.Should().ContainSingle().Subject;
        item.StageName.Should().Be("Color");
        item.AssigneeUserId.Should().Be(f.ArtistId);
        item.AttemptNumber.Should().Be(1);

        // Đang có người duyệt: hàng đợi trả tên người giữ khoá, FE khỏi gọi danh sách thành viên
        var editor = User.Create("ed@test.com", "Biên tập A");
        f.Users.Add(editor);
        f.Tasks.Single(t => t.Id == item.TaskId).OpenReview(editor.Id);
        var locked = (await new GetTaskGridReviewQueueQueryHandler(f.Db.Object).Handle(new GetTaskGridReviewQueueQuery(f.WorkspaceId), default)).Value!.Single();
        locked.ReviewerName.Should().Be("Biên tập A");
        locked.IsInputOutdated.Should().BeFalse();

        // Công đoạn bị tắt thì ô của nó không còn trong hàng đợi
        f.Color.SetEnabled(false);
        (await new GetTaskGridReviewQueueQueryHandler(f.Db.Object).Handle(new GetTaskGridReviewQueueQuery(f.WorkspaceId), default)).Value!.Should().BeEmpty();
    }
}
