using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Commands.ApproveAssignment;
using PanelForge.Application.Features.Assignments.Commands.CreateAssignment;
using PanelForge.Application.Features.Assignments.Commands.DeleteAssignment;
using PanelForge.Application.Features.Assignments.Commands.OpenAssignmentReview;
using PanelForge.Application.Features.Assignments.Commands.RequestAssignmentChanges;
using PanelForge.Application.Features.Assignments.Commands.StartAssignment;
using PanelForge.Application.Features.Assignments.Commands.SubmitAssignment;
using PanelForge.Application.Features.Assignments.Commands.UpdateAssignment;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.Assignments.Queries.GetMyWorkspaceAssignments;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>CF2 (UC-05): BR-03 containment, BR-07 membership, BR-12 chapter lock, BR-19 dependency, không chu trình.</summary>
public partial class AssignmentTests
{
    private sealed class Fixture
    {
        public readonly Guid ProducerId = Guid.NewGuid();
        public readonly Guid ArtistId = Guid.NewGuid();
        public readonly Guid WriterId = Guid.NewGuid();
        public readonly Series Series;
        public readonly Chapter Chapter;
        public readonly PipelineStage ArtistStage;
        public readonly Page Page;
        public readonly PipelineStage InkStage;
        public readonly List<Assignment> Assignments = [];
        public readonly List<Page> PagesList = [];
        public readonly List<Panel> Panels = [];
        public readonly List<StageProgress> Progress = [];
        public readonly List<Scene> Scenes = [];
        public readonly List<ScriptLine> Lines = [];
        public readonly List<Notification> Notifications = [];
        public readonly List<AssetReference> AssetReferences = [];
        public readonly List<Submission> Submissions = [];
        public readonly List<ChangeRequest> ChangeRequests = [];
        public readonly Guid Artist2Id = Guid.NewGuid();
        public readonly Guid EditorId = Guid.NewGuid();
        public readonly Mock<IPanelForgeDbContext> Db = new();

        public Fixture()
        {
            var workspaceId = Guid.NewGuid();
            Series = Series.Create(workspaceId, "Series", ReadingDirection.RightToLeft);
            Chapter = Chapter.Create(Series.Id, 1);
            var def = PipelineDefinition.Create(workspaceId, "Pipeline", seriesId: Series.Id);
            ArtistStage = PipelineStage.Create(def.Id, "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
            InkStage = PipelineStage.Create(def.Id, "Ink", "ink", 2, allowedRole: WorkspaceRole.Artist);
            Page = Page.CreateNew(Chapter.Id, 1, default, 1200, 1800, 300);

            var members = new List<WorkspaceMember>
            {
                WorkspaceMember.Create(workspaceId, ProducerId, WorkspaceRole.Producer),
                WorkspaceMember.Create(workspaceId, ArtistId, WorkspaceRole.Artist),
                WorkspaceMember.Create(workspaceId, WriterId, WorkspaceRole.Writer),
                WorkspaceMember.Create(workspaceId, Artist2Id, WorkspaceRole.Artist),
                WorkspaceMember.Create(workspaceId, EditorId, WorkspaceRole.Editor),
            };

            Db.Setup(d => d.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { Series }));
            Db.Setup(d => d.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { Chapter }));
            Db.Setup(d => d.PipelineDefinitions).Returns(DbSetMockHelper.CreateDbSetMock(new List<PipelineDefinition> { def }));
            Db.Setup(d => d.PipelineStages).Returns(DbSetMockHelper.CreateDbSetMock(new List<PipelineStage> { ArtistStage, InkStage }));
            Db.Setup(d => d.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(members));
            PagesList.Add(Page);
            Db.Setup(d => d.Pages).Returns(DbSetMockHelper.CreateDbSetMock(PagesList));
            Db.Setup(d => d.Panels).Returns(DbSetMockHelper.CreateDbSetMock(Panels));
            Db.Setup(d => d.StageProgresses).Returns(DbSetMockHelper.CreateDbSetMock(Progress));
            Db.Setup(d => d.Scenes).Returns(DbSetMockHelper.CreateDbSetMock(Scenes));
            Db.Setup(d => d.ScriptLines).Returns(DbSetMockHelper.CreateDbSetMock(Lines));
            Db.Setup(d => d.Notifications).Returns(DbSetMockHelper.CreateDbSetMock(Notifications));
            Db.Setup(d => d.AssetReferences).Returns(DbSetMockHelper.CreateDbSetMock(AssetReferences));
            Db.Setup(d => d.Submissions).Returns(DbSetMockHelper.CreateDbSetMock(Submissions));
            Db.Setup(d => d.ChangeRequests).Returns(DbSetMockHelper.CreateDbSetMock(ChangeRequests));
            Db.Setup(d => d.Assignments).Returns(DbSetMockHelper.CreateDbSetMock(Assignments));
            Db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Assignment Seed(Guid? dependsOn = null)
        {
            var a = Assignment.Create(Series.Id, Chapter.Id, WorkflowEntityType.Page, Guid.NewGuid(), ArtistStage.Id,
                ArtistId, WorkspaceRole.Artist, "brief", dependsOnAssignmentId: dependsOn);
            Assignments.Add(a);
            // BR-14: việc được seed coi như đã được giao qua API nên đã có thông báo
            Notifications.Add(Notification.Create(a.AssigneeUserId, NotificationType.AssignmentAssigned, "t", "m", a.Id, a.ChapterId));
            return a;
        }

        /// <summary>Assignment gán cho một Page/Panel/Chapter cụ thể ở stage cho trước (mặc định Pencil, Artist).</summary>
        public Assignment SeedFor(WorkflowEntityType type, Guid target, PipelineStage? stage = null,
            WorkspaceRole role = WorkspaceRole.Artist)
        {
            var a = Assignment.Create(Series.Id, Chapter.Id, type, target, (stage ?? ArtistStage).Id,
                ArtistId, role, "brief");
            Assignments.Add(a);
            // BR-14: việc được seed coi như đã được giao qua API nên đã có thông báo
            Notifications.Add(Notification.Create(a.AssigneeUserId, NotificationType.AssignmentAssigned, "t", "m", a.Id, a.ChapterId));
            return a;
        }

        public Panel AddPanel(Page page, int number)
        {
            var p = Panel.Create(page.Id, number, number);
            Panels.Add(p);
            return p;
        }

        public Page AddPage(int number)
        {
            var p = Page.CreateNew(Chapter.Id, number, default, 1200, 1800, 300);
            PagesList.Add(p);
            return p;
        }

        /// <summary>CF3 sẽ có lệnh duyệt; tới lúc đó test đặt Approved bằng reflection.</summary>
        public static void Approve(StageProgress row) =>
            typeof(StageProgress).GetProperty(nameof(StageProgress.Status))!.SetValue(row, StageProgressStatus.Approved);

        /// <summary>Mô phỏng Artist đã tải một phiên bản artwork mới lên mọi Page mà việc bao phủ, ở đúng stage của việc.</summary>
        public void UploadArtworkFor(Assignment a)
        {
            var pageIds = a.EntityType switch
            {
                WorkflowEntityType.Page => new[] { a.TargetEntityId },
                WorkflowEntityType.Panel => Panels.Where(p => p.Id == a.TargetEntityId).Select(p => p.PageId).ToArray(),
                _ => []
            };

            foreach (var pageId in pageIds)
            {
                var version = AssetReferences.Count(r => r.PageId == pageId && r.PipelineStageId == a.PipelineStageId) + 1;
                AssetReferences.Add(AssetReference.Create(
                    Guid.NewGuid(), Series.WorkspaceId, Chapter.Id, pageId, a.PipelineStageId, version, null, "art.png", a.AssigneeUserId));
            }
        }

        public void LockChapter() =>
            typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(Chapter, ChapterStatus.Approved);

        public CreateAssignmentCommand CreateCmd(
            Guid? stageId = null, Guid? assignee = null, WorkflowEntityType type = WorkflowEntityType.Page, Guid? target = null) =>
            new(Chapter.Id, ProducerId, type, target ?? Page.Id, stageId ?? ArtistStage.Id,
                assignee ?? ArtistId, "Vẽ pencil trang 1", null, null, AssignmentPriority.High, null);
    }

    private static Task<Result<AssignmentDto>> Create(Fixture f, CreateAssignmentCommand cmd) =>
        new CreateAssignmentCommandHandler(f.Db.Object).Handle(cmd, CancellationToken.None);

    [Fact]
    public async Task Create_ForPageInChapter_TakesRoleFromMembership()
    {
        var f = new Fixture();

        var result = await Create(f, f.CreateCmd());

        result.IsSuccess.Should().BeTrue();
        result.Value!.AssignedRole.Should().Be(WorkspaceRole.Artist);
        result.Value.Status.Should().Be(AssignmentStatus.Assigned);
        f.Assignments.Should().ContainSingle();
        f.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_ForWholeChapter_DefaultsTargetToChapter()
    {
        var f = new Fixture();

        var result = await Create(f, f.CreateCmd(type: WorkflowEntityType.Chapter) with { TargetEntityId = null });

        result.IsSuccess.Should().BeTrue();
        result.Value!.TargetEntityId.Should().Be(f.Chapter.Id);
    }

    [Fact]
    public async Task Create_AssigneeNotWorkspaceMember_IsRejected()
    {
        var f = new Fixture();

        var result = await Create(f, f.CreateCmd(assignee: Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        f.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_AssigneeRoleNotAllowedForStage_IsRejected()
    {
        var f = new Fixture();

        var result = await Create(f, f.CreateCmd(assignee: f.WriterId));

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("Artist");
    }

    [Fact]
    public async Task Create_StageOutsideSeriesPipeline_IsRejected()
    {
        var f = new Fixture();

        var result = await Create(f, f.CreateCmd(stageId: Guid.NewGuid()));

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Create_PageNotInChapter_IsRejected()
    {
        var f = new Fixture();

        var result = await Create(f, f.CreateCmd(target: Guid.NewGuid()));

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Create_ApprovedChapter_IsConflict()
    {
        var f = new Fixture();
        f.LockChapter();

        var result = await Create(f, f.CreateCmd());

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Create_SameTargetAndStageTwice_IsConflict()
    {
        var f = new Fixture();
        (await Create(f, f.CreateCmd())).IsSuccess.Should().BeTrue();

        var second = await Create(f, f.CreateCmd());

        second.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    private static Task<Result<AssignmentDto>> Update(Fixture f, Assignment target, Guid? dependsOn) =>
        new UpdateAssignmentCommandHandler(f.Db.Object).Handle(new UpdateAssignmentCommand(
            f.Chapter.Id, target.Id, f.ProducerId, "brief mới", null, null, AssignmentPriority.Medium,
            target.AssigneeUserId, dependsOn), CancellationToken.None);

    [Fact]
    public async Task Update_DependencyCycle_IsRejected()
    {
        var f = new Fixture();
        var b = f.Seed();
        var a = f.Seed(dependsOn: b.Id);

        var result = await Update(f, b, dependsOn: a.Id); // b → a → b

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("chu trình");
        b.DependsOnAssignmentId.Should().BeNull();
    }

    [Fact]
    public async Task Update_DependsOnItself_IsRejected()
    {
        var f = new Fixture();
        var a = f.Seed();

        var result = await Update(f, a, dependsOn: a.Id);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Update_DependencyOutsideChapter_IsRejected()
    {
        var f = new Fixture();
        var a = f.Seed();

        var result = await Update(f, a, dependsOn: Guid.NewGuid());

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Update_ValidDependency_IsApplied()
    {
        var f = new Fixture();
        var dep = f.Seed();
        var a = f.Seed();

        var result = await Update(f, a, dependsOn: dep.Id);

        result.IsSuccess.Should().BeTrue();
        a.DependsOnAssignmentId.Should().Be(dep.Id);
        a.Brief.Should().Be("brief mới");
    }

    [Fact]
    public async Task Update_ReassignToNonMember_IsRejected()
    {
        var f = new Fixture();
        var a = f.Seed();

        var result = await new UpdateAssignmentCommandHandler(f.Db.Object).Handle(new UpdateAssignmentCommand(
            f.Chapter.Id, a.Id, f.ProducerId, "brief", null, null, AssignmentPriority.Low,
            Guid.NewGuid(), null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        a.AssigneeUserId.Should().Be(f.ArtistId);
    }

    [Fact]
    public async Task Update_SubmittedAssignment_IsConflict()
    {
        var f = new Fixture();
        var a = f.Seed();
        a.SubmitForReview();

        var result = await Update(f, a, dependsOn: null);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    private static Task<Result<AssignmentDto>> Start(Fixture f, Assignment a, Guid userId) =>
        new StartAssignmentCommandHandler(f.Db.Object).Handle(
            new StartAssignmentCommand(f.Chapter.Id, a.Id, userId), CancellationToken.None);

    [Fact]
    public async Task Start_ByAnotherUser_IsForbidden()
    {
        var f = new Fixture();
        var a = f.Seed();

        var result = await Start(f, a, f.ProducerId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        a.Status.Should().Be(AssignmentStatus.Assigned);
    }

    [Fact]
    public async Task Start_WhileDependencyNotApproved_IsConflict_ThenWorksOnceApproved()
    {
        var f = new Fixture();
        var dep = f.Seed();
        var a = f.Seed(dependsOn: dep.Id);

        var blocked = await Start(f, a, f.ArtistId);
        blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        a.Status.Should().Be(AssignmentStatus.Assigned);

        dep.MarkApproved();
        var ok = await Start(f, a, f.ArtistId);

        ok.IsSuccess.Should().BeTrue();
        a.Status.Should().Be(AssignmentStatus.InProgress);
    }

    [Fact]
    public async Task Start_IndependentAssignment_IsNotBlockedByOthers()
    {
        var f = new Fixture();
        f.Seed(); // việc khác chưa xong (BR-19: không ảnh hưởng)
        var a = f.Seed();

        var result = await Start(f, a, f.ArtistId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_ByAssignee_MovesToSubmitted_ByOthersForbidden()
    {
        var f = new Fixture();
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        f.AddPanel(f.Page, 1);
        (await Start(f, a, f.ArtistId)).IsSuccess.Should().BeTrue();
        f.UploadArtworkFor(a);
        var handler = new SubmitAssignmentCommandHandler(f.Db.Object, Mock.Of<PanelForge.Application.Common.Interfaces.Persistence.IPageRepository>());

        var other = await handler.Handle(new SubmitAssignmentCommand(f.Chapter.Id, a.Id, f.ProducerId), CancellationToken.None);
        other.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        var ok = await handler.Handle(new SubmitAssignmentCommand(f.Chapter.Id, a.Id, f.ArtistId), CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();
        a.Status.Should().Be(AssignmentStatus.Submitted);
    }

    [Fact]
    public async Task Submit_AssignmentOfAnotherChapter_IsNotFound()
    {
        var f = new Fixture();
        var a = f.Seed();

        var result = await new SubmitAssignmentCommandHandler(f.Db.Object, Mock.Of<PanelForge.Application.Common.Interfaces.Persistence.IPageRepository>()).Handle(
            new SubmitAssignmentCommand(Guid.NewGuid(), a.Id, f.ArtistId), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    // ── StageProgress (CF2): tiến độ theo Panel, CurrentStageId suy ra, cổng BR-19 ──

    /// <summary>Nộp sau khi Artist đã tải artwork mới (điều kiện nộp). Dùng <see cref="SubmitWithoutArtwork"/> để kiểm cổng.</summary>
    private static Task<Result<AssignmentDto>> Submit(Fixture f, Assignment a)
    {
        f.UploadArtworkFor(a);
        return SubmitWithoutArtwork(f, a);
    }

    private static Task<Result<AssignmentDto>> SubmitWithoutArtwork(Fixture f, Assignment a) =>
        new SubmitAssignmentCommandHandler(f.Db.Object, Mock.Of<PanelForge.Application.Common.Interfaces.Persistence.IPageRepository>()).Handle(
            new SubmitAssignmentCommand(f.Chapter.Id, a.Id, f.ArtistId), CancellationToken.None);

    [Fact]
    public async Task Start_PageAssignment_CreatesInProgressRowsAndDerivesCurrentStage()
    {
        var f = new Fixture();
        var p1 = f.AddPanel(f.Page, 1);
        var p2 = f.AddPanel(f.Page, 2);
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);

        (await Start(f, a, f.ArtistId)).IsSuccess.Should().BeTrue();

        f.Progress.Should().HaveCount(2).And.OnlyContain(r => r.Status == StageProgressStatus.InProgress);
        p1.CurrentStageId.Should().Be(f.ArtistStage.Id);
        p2.CurrentStageId.Should().Be(f.ArtistStage.Id);
        f.Page.CurrentStageId.Should().Be(f.ArtistStage.Id);
    }

    [Fact]
    public async Task Submit_MarksOnlyAssignedPagePanelsSubmitted_OtherPagesUntouched()
    {
        var f = new Fixture();
        var other = f.AddPage(2);
        var mine = f.AddPanel(f.Page, 1);
        var theirs = f.AddPanel(other, 1);
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        await Start(f, a, f.ArtistId);

        var result = await Submit(f, a);

        result.IsSuccess.Should().BeTrue();
        var row = f.Progress.Should().ContainSingle().Subject;
        row.PanelId.Should().Be(mine.Id);
        row.Status.Should().Be(StageProgressStatus.Submitted);
        row.SubmittedByUserId.Should().Be(f.ArtistId);
        theirs.CurrentStageId.Should().BeNull();
        other.CurrentStageId.Should().BeNull();
    }

    [Fact]
    public async Task Submit_WithoutStarting_IsConflict()
    {
        var f = new Fixture();
        f.AddPanel(f.Page, 1);
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);

        var result = await Submit(f, a);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        f.Progress.Should().BeEmpty();
        a.Status.Should().Be(AssignmentStatus.Assigned);
    }

    [Fact]
    public async Task Submit_ArtistPageWithNoPanels_IsConflict()
    {
        var f = new Fixture();
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        await Start(f, a, f.ArtistId);

        var result = await Submit(f, a);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Panel");
        a.Status.Should().Be(AssignmentStatus.InProgress);
    }

    [Fact]
    public async Task Submit_WriterChapterNeedsScriptLines()
    {
        var f = new Fixture();
        var a = f.SeedFor(WorkflowEntityType.Chapter, f.Chapter.Id, role: WorkspaceRole.Writer);
        (await Start(f, a, f.ArtistId)).IsSuccess.Should().BeTrue();

        var empty = await Submit(f, a);
        empty.ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        var scene = Scene.Create(f.Chapter.Id, 1);
        f.Scenes.Add(scene);
        f.Lines.Add(ScriptLine.Create(scene.Id, 1, "Xin chào"));
        var ok = await Submit(f, a);

        ok.IsSuccess.Should().BeTrue();
        a.Status.Should().Be(AssignmentStatus.Submitted);
        f.Progress.Should().BeEmpty(); // việc cấp Chapter không có Panel
    }

    [Fact]
    public async Task Start_NextStage_BlockedUntilPriorStageApprovedForThatPanel()
    {
        var f = new Fixture();
        var panel = f.AddPanel(f.Page, 1);
        var pencil = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        await Start(f, pencil, f.ArtistId);
        await Submit(f, pencil);
        var ink = f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);

        var blocked = await Start(f, ink, f.ArtistId);
        blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        blocked.ErrorMessage.Should().Contain("Pencil");
        ink.Status.Should().Be(AssignmentStatus.Assigned);

        Fixture.Approve(f.Progress.Single(r => r.PanelId == panel.Id));
        var ok = await Start(f, ink, f.ArtistId);

        ok.IsSuccess.Should().BeTrue();
        panel.CurrentStageId.Should().Be(f.InkStage.Id); // stage cao nhất đã chạm tới
        f.Page.CurrentStageId.Should().Be(f.InkStage.Id);
    }

    [Fact]
    public async Task Start_NextStage_OnePanelApprovedAndOneNot_StillBlocked()
    {
        var f = new Fixture();
        var done = f.AddPanel(f.Page, 1);
        var pending = f.AddPanel(f.Page, 2);
        var pencil = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        await Start(f, pencil, f.ArtistId);
        await Submit(f, pencil);
        Fixture.Approve(f.Progress.Single(r => r.PanelId == done.Id));
        var ink = f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);

        var result = await Start(f, ink, f.ArtistId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("1 panel");
        pending.CurrentStageId.Should().Be(f.ArtistStage.Id);
    }

    [Fact]
    public async Task Start_NextStage_AllowedWhenChapterLevelPriorStageApproved()
    {
        var f = new Fixture();
        f.AddPanel(f.Page, 1);
        var script = f.SeedFor(WorkflowEntityType.Chapter, f.Chapter.Id);
        script.MarkApproved();
        var ink = f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);
        // Ink có stage trước là Pencil: việc cấp Chapter ở Pencil đã Approved (mô phỏng stage Script cấp Chapter)

        var result = await Start(f, ink, f.ArtistId);

        result.IsSuccess.Should().BeTrue();
    }

    // ── Thông báo (BR-14, UC-14) ──

    [Fact]
    public async Task Create_NotifiesAssignee_InTheSameSaveAsTheAssignment()
    {
        var f = new Fixture();

        var result = await Create(f, f.CreateCmd());

        result.IsSuccess.Should().BeTrue();
        var n = f.Notifications.Should().ContainSingle().Subject;
        n.UserId.Should().Be(f.ArtistId);
        n.Type.Should().Be(NotificationType.AssignmentAssigned);
        n.AssignmentId.Should().Be(result.Value!.Id);
        n.ReadAt.Should().BeNull();
        f.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Rejected_CreatesNoNotification()
    {
        var f = new Fixture();

        await Create(f, f.CreateCmd(assignee: Guid.NewGuid()));

        f.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_Reassign_NotifiesNewAssigneeAndTellsPreviousOne()
    {
        var f = new Fixture();
        var a = f.Seed();
        f.Notifications.Clear();

        var result = await new UpdateAssignmentCommandHandler(f.Db.Object).Handle(new UpdateAssignmentCommand(
            f.Chapter.Id, a.Id, f.ProducerId, "brief", null, null, AssignmentPriority.Medium,
            f.Artist2Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        f.Notifications.Should().HaveCount(2);
        f.Notifications.Single(n => n.Type == NotificationType.AssignmentAssigned).UserId.Should().Be(f.Artist2Id);
        f.Notifications.Single(n => n.Type == NotificationType.AssignmentUnassigned).UserId.Should().Be(f.ArtistId);
    }

    [Fact]
    public async Task Update_DetailsChanged_NotifiesCurrentAssignee()
    {
        var f = new Fixture();
        var a = f.Seed();
        f.Notifications.Clear();

        await Update(f, a, dependsOn: null); // brief đổi thành "brief mới"

        var n = f.Notifications.Should().ContainSingle().Subject;
        n.Type.Should().Be(NotificationType.AssignmentUpdated);
        n.UserId.Should().Be(f.ArtistId);
    }

    [Fact]
    public async Task Update_NothingChanged_SendsNoNotification()
    {
        var f = new Fixture();
        var a = f.Seed();
        f.Notifications.Clear();

        var result = await new UpdateAssignmentCommandHandler(f.Db.Object).Handle(new UpdateAssignmentCommand(
            f.Chapter.Id, a.Id, f.ProducerId, "brief", null, null, AssignmentPriority.Medium,
            f.ArtistId, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        f.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task Start_WithoutAssignedNotification_IsConflict()
    {
        var f = new Fixture();
        var a = f.Seed();
        f.Notifications.Clear();

        var result = await Start(f, a, f.ArtistId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("BR-14");
        a.Status.Should().Be(AssignmentStatus.Assigned);
    }

    [Fact]
    public async Task Start_NotificationOfPreviousAssigneeDoesNotCount_AfterReassign()
    {
        var f = new Fixture();
        var a = f.Seed();
        f.Notifications.Clear();
        a.Reassign(f.Artist2Id, WorkspaceRole.Artist); // đổi người nhưng chưa có thông báo cho người mới

        var result = await Start(f, a, f.Artist2Id);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    // ── CF3: cổng duyệt (open-review / approve / request-changes) ──

    private static Task<Result<AssignmentDto>> OpenReview(Fixture f, Assignment a, Guid by) =>
        new OpenAssignmentReviewCommandHandler(f.Db.Object).Handle(
            new OpenAssignmentReviewCommand(f.Chapter.Id, a.Id, by), CancellationToken.None);

    private static Task<Result<AssignmentDto>> Approve(Fixture f, Assignment a, Guid by) =>
        new ApproveAssignmentCommandHandler(f.Db.Object).Handle(
            new ApproveAssignmentCommand(f.Chapter.Id, a.Id, by), CancellationToken.None);

    private static Task<Result<AssignmentDto>> RequestChanges(Fixture f, Assignment a, Guid by, string? comment) =>
        new RequestAssignmentChangesCommandHandler(f.Db.Object).Handle(
            new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, by, comment), CancellationToken.None);

    /// <summary>Việc Pencil của Page 1 (một Panel) đã được Start rồi Submit.</summary>
    private static async Task<(Assignment Assignment, Panel Panel)> Submitted(Fixture f)
    {
        var panel = f.AddPanel(f.Page, 1);
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        (await Start(f, a, f.ArtistId)).IsSuccess.Should().BeTrue();
        (await Submit(f, a)).IsSuccess.Should().BeTrue();
        return (a, panel);
    }

    [Fact]
    public async Task Submit_NotifiesEditor_ButNotTheSubmitter()
    {
        var f = new Fixture();
        f.Notifications.Clear();

        var (a, _) = await Submitted(f);

        var n = f.Notifications.Single(x => x.Type == NotificationType.AssignmentSubmitted);
        n.UserId.Should().Be(f.EditorId);
        n.AssignmentId.Should().Be(a.Id);
    }

    [Fact]
    public async Task OpenReview_MovesAssignmentAndRowsToReview()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);

        var result = await OpenReview(f, a, f.EditorId);

        result.IsSuccess.Should().BeTrue();
        a.Status.Should().Be(AssignmentStatus.UnderReview);
        f.Progress.Should().ContainSingle().Which.Status.Should().Be(StageProgressStatus.InReview);
    }

    [Fact]
    public async Task OpenReview_BeforeSubmit_IsConflict()
    {
        var f = new Fixture();
        f.AddPanel(f.Page, 1);
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);

        var result = await OpenReview(f, a, f.EditorId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        a.Status.Should().Be(AssignmentStatus.Assigned);
    }

    [Fact]
    public async Task Approve_ApprovesRows_NotifiesAssignee_AndOpensNextStage()
    {
        var f = new Fixture();
        var (a, panel) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);
        f.Notifications.Clear();

        var approved = await Approve(f, a, f.EditorId);

        approved.IsSuccess.Should().BeTrue();
        a.Status.Should().Be(AssignmentStatus.Approved);
        a.CompletedAt.Should().NotBeNull();
        f.Progress.Should().ContainSingle().Which.Status.Should().Be(StageProgressStatus.Approved);
        var n = f.Notifications.Should().ContainSingle().Subject;
        n.Type.Should().Be(NotificationType.AssignmentApproved);
        n.UserId.Should().Be(f.ArtistId);

        // BR-19 end-to-end: stage Ink của Panel này giờ mở được
        var ink = f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);
        var next = await Start(f, ink, f.ArtistId);
        next.IsSuccess.Should().BeTrue();
        panel.CurrentStageId.Should().Be(f.InkStage.Id);
    }

    [Fact]
    public async Task Approve_ByTheAssignee_IsForbidden_AndNothingChanges()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);

        var result = await Approve(f, a, f.ArtistId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        a.Status.Should().Be(AssignmentStatus.UnderReview);
        f.Progress.Single().Status.Should().Be(StageProgressStatus.InReview);
    }

    [Fact]
    public async Task Approve_WithoutOpeningReview_IsConflict()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);

        var result = await Approve(f, a, f.EditorId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        a.Status.Should().Be(AssignmentStatus.Submitted);
    }

    [Fact]
    public async Task Approve_InApprovedChapter_IsConflict()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);
        f.LockChapter();

        var result = await Approve(f, a, f.EditorId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        a.Status.Should().Be(AssignmentStatus.UnderReview);
    }

    [Fact]
    public async Task Approve_AssignmentOfAnotherChapter_IsNotFound()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);

        var result = await new ApproveAssignmentCommandHandler(f.Db.Object).Handle(
            new ApproveAssignmentCommand(Guid.NewGuid(), a.Id, f.EditorId), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task RequestChanges_WithoutComment_IsRejected(string? comment)
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);

        var result = await RequestChanges(f, a, f.EditorId, comment);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        a.Status.Should().Be(AssignmentStatus.UnderReview);
    }

    [Fact]
    public async Task RequestChanges_ReturnsWork_AndAssigneeCanRestartAndResubmit()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);

        var returned = await RequestChanges(f, a, f.EditorId, "Khung 1 lệch tỉ lệ");

        returned.IsSuccess.Should().BeTrue();
        a.Status.Should().Be(AssignmentStatus.ReworkRequired);
        a.LastFeedbackComment.Should().Be("Khung 1 lệch tỉ lệ");
        f.Progress.Single().Status.Should().Be(StageProgressStatus.ChangesRequested);
        var n = f.Notifications.Single(x => x.Type == NotificationType.AssignmentChangesRequested);
        n.Message.Should().Contain("lệch tỉ lệ");

        (await Start(f, a, f.ArtistId)).IsSuccess.Should().BeTrue();
        f.Progress.Single().Status.Should().Be(StageProgressStatus.InProgress);

        // CF3 bước 9: chưa xử lý yêu cầu sửa thì chưa nộp lại được
        (await Submit(f, a)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await ResolveCr(f, f.ChangeRequests.Single(), f.ArtistId)).IsSuccess.Should().BeTrue();

        (await Submit(f, a)).IsSuccess.Should().BeTrue();
        a.Status.Should().Be(AssignmentStatus.Submitted);
        f.Progress.Single().Status.Should().Be(StageProgressStatus.Submitted);
    }

    [Fact]
    public async Task Approve_ChapterLevelAssignment_ApprovesWithoutPanels_AndOpensPageStage()
    {
        var f = new Fixture();
        f.AddPanel(f.Page, 1);
        var script = f.SeedFor(WorkflowEntityType.Chapter, f.Chapter.Id); // stage Pencil đóng vai stage cấp Chapter
        await Start(f, script, f.ArtistId);
        await Submit(f, script);
        await OpenReview(f, script, f.EditorId);

        (await Approve(f, script, f.EditorId)).IsSuccess.Should().BeTrue();

        script.Status.Should().Be(AssignmentStatus.Approved);
        f.Progress.Should().BeEmpty();
        var ink = f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);
        (await Start(f, ink, f.ArtistId)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void StageProgress_ReviewTransitions_FollowTheStateMachine()
    {
        var row = StageProgress.Begin(Guid.NewGuid(), Guid.NewGuid());
        row.Invoking(r => r.OpenReview()).Should().Throw<InvalidOperationException>(); // chưa nộp
        row.Submit(Guid.NewGuid());
        row.Invoking(r => r.Approve()).Should().Throw<InvalidOperationException>();     // chưa mở review

        row.OpenReview();
        row.Status.Should().Be(StageProgressStatus.InReview);
        row.Approve();

        row.Status.Should().Be(StageProgressStatus.Approved);
        row.Invoking(r => r.RequestChanges()).Should().Throw<InvalidOperationException>(); // đã đóng băng
    }

    [Fact]
    public void StageProgress_StateMachine_RejectsInvalidTransitions()
    {
        var row = StageProgress.Begin(Guid.NewGuid(), Guid.NewGuid());
        row.Resume(); // idempotent khi đang InProgress
        row.Submit(Guid.NewGuid());

        row.Invoking(r => r.Submit(Guid.NewGuid())).Should().Throw<InvalidOperationException>();
        row.Invoking(r => r.Resume()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Delete_Assignment_SuccessfullyDeletes_AndClearsDependencies()
    {
        var f = new Fixture();
        var a = f.Seed();
        var b = f.Seed(dependsOn: a.Id);

        var handler = new DeleteAssignmentCommandHandler(f.Db.Object);
        var result = await handler.Handle(new DeleteAssignmentCommand(f.Chapter.Id, a.Id, f.ProducerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        f.Assignments.Should().NotContain(a);
        b.DependsOnAssignmentId.Should().BeNull();
        f.Notifications.Should().Contain(n => n.Type == NotificationType.AssignmentUnassigned && n.UserId == a.AssigneeUserId);
        f.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_Assignment_FailsIfNotFound()
    {
        var f = new Fixture();
        var handler = new DeleteAssignmentCommandHandler(f.Db.Object);
        var result = await handler.Handle(new DeleteAssignmentCommand(f.Chapter.Id, Guid.NewGuid(), f.ProducerId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Delete_Assignment_FailsIfChapterLocked()
    {
        var f = new Fixture();
        var a = f.Seed();
        f.LockChapter();

        var handler = new DeleteAssignmentCommandHandler(f.Db.Object);
        var result = await handler.Handle(new DeleteAssignmentCommand(f.Chapter.Id, a.Id, f.ProducerId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Delete_Assignment_FailsIfApproved()
    {
        var f = new Fixture();
        var a = f.Seed();
        a.MarkApproved();

        var handler = new DeleteAssignmentCommandHandler(f.Db.Object);
        var result = await handler.Handle(new DeleteAssignmentCommand(f.Chapter.Id, a.Id, f.ProducerId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Approved");
    }

    [Fact]
    public async Task Update_MultipleDependencies_DAGCycle_IsRejected()
    {
        var f = new Fixture();
        var a = f.Seed();
        var b = f.Seed();
        var c = f.Seed();

        // c phụ thuộc đồng thời vào a và b
        var updateCHandler = new UpdateAssignmentCommandHandler(f.Db.Object);
        var resC = await updateCHandler.Handle(new UpdateAssignmentCommand(
            f.Chapter.Id, c.Id, f.ProducerId, c.Brief, null, null, c.Priority, c.AssigneeUserId, null, [a.Id, b.Id]), CancellationToken.None);
        resC.IsSuccess.Should().BeTrue();
        c.GetDependsOnAssignmentIds().Should().Contain([a.Id, b.Id]);

        // Thử tạo chu trình: cho a phụ thuộc vào c (a -> c -> [a, b])
        var updateAHandler = new UpdateAssignmentCommandHandler(f.Db.Object);
        var resA = await updateAHandler.Handle(new UpdateAssignmentCommand(
            f.Chapter.Id, a.Id, f.ProducerId, a.Brief, null, null, a.Priority, a.AssigneeUserId, null, [c.Id]), CancellationToken.None);

        resA.IsSuccess.Should().BeFalse();
        resA.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        resA.ErrorMessage.Should().Contain("chu trình");
    }

    [Fact]
    public async Task Start_MultipleDependencies_RequiresAllToBeApproved()
    {
        var f = new Fixture();
        var a = f.Seed();
        var b = f.Seed();
        var c = f.Seed();
        c.SetDependencies([a.Id, b.Id]);

        // Thử start c khi cả a và b chưa Approved
        var startHandler = new StartAssignmentCommandHandler(f.Db.Object);
        var res1 = await startHandler.Handle(new StartAssignmentCommand(f.Chapter.Id, c.Id, c.AssigneeUserId), CancellationToken.None);
        res1.IsSuccess.Should().BeFalse();
        res1.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        res1.ErrorMessage.Should().Contain("chưa được duyệt");

        // Duyệt a, nhưng b vẫn chưa duyệt -> vẫn bị chặn
        a.MarkApproved();
        var res2 = await startHandler.Handle(new StartAssignmentCommand(f.Chapter.Id, c.Id, c.AssigneeUserId), CancellationToken.None);
        res2.IsSuccess.Should().BeFalse();
        res2.ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        // Duyệt nốt b -> giờ c bắt đầu thành công
        b.MarkApproved();
        var res3 = await startHandler.Handle(new StartAssignmentCommand(f.Chapter.Id, c.Id, c.AssigneeUserId), CancellationToken.None);
        res3.IsSuccess.Should().BeTrue();
        c.Status.Should().Be(AssignmentStatus.InProgress);
    }

    [Fact]
    public async Task Delete_Assignment_ReleasesFromMultipleDependencies()
    {
        var f = new Fixture();
        var a = f.Seed();
        var b = f.Seed();
        var c = f.Seed();
        c.SetDependencies([a.Id, b.Id]);

        var deleteHandler = new DeleteAssignmentCommandHandler(f.Db.Object);
        var delRes = await deleteHandler.Handle(new DeleteAssignmentCommand(f.Chapter.Id, a.Id, f.ProducerId), CancellationToken.None);
        delRes.IsSuccess.Should().BeTrue();

        // c chỉ còn phụ thuộc vào b
        c.GetDependsOnAssignmentIds().Should().Equal([b.Id]);
    }
}
