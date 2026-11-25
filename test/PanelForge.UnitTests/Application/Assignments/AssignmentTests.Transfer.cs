using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using PanelForge.API.Common;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Assignments.Commands.RejectChangeRequest;
using PanelForge.Application.Features.Assignments.Commands.RequestAssignmentChanges;
using PanelForge.Application.Features.Assignments.Commands.ResolveChangeRequest;
using PanelForge.Application.Features.Assignments.Commands.ResolveImpactReview;
using PanelForge.Application.Features.Assignments.Commands.SubmitAssignment;
using PanelForge.Application.Features.Assignments.Commands.TransferAssignmentScope;
using PanelForge.Application.Features.Assignments.Commands.UpdateAssignment;
using PanelForge.Application.Features.Assignments.Common;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>
/// Giao cả chương, chuyển giao trang/khung, đổi người khi trả sửa, kiểm tra ảnh hưởng khi đầu vào đổi, snapshot lần nộp,
/// khoá ghi theo phạm vi và tranh chấp đồng thời.
/// </summary>
public partial class AssignmentTests
{
    private const string TransferReason = "Cảnh tình cảm chưa đạt";

    private static Task<Result<IReadOnlyList<AssignmentDto>>> Transfer(
        Fixture f, Assignment whole, Guid to, WorkflowEntityType type, params Guid[] ids) =>
        new TransferAssignmentScopeCommandHandler(f.Db.Object).Handle(
            new TransferAssignmentScopeCommand(f.Chapter.Id, whole.Id, f.ProducerId, type, ids, to, TransferReason),
            CancellationToken.None);

    private static Task<Result<AssignmentDto>> SubmitAs(Fixture f, Assignment a, Guid userId) =>
        new SubmitAssignmentCommandHandler(f.Db.Object, Mock.Of<IPageRepository>()).Handle(
            new SubmitAssignmentCommand(f.Chapter.Id, a.Id, userId), CancellationToken.None);

    private static Task<Result<AssignmentDto>> Reassign(Fixture f, Assignment a, Guid to) =>
        new UpdateAssignmentCommandHandler(f.Db.Object).Handle(
            new UpdateAssignmentCommand(f.Chapter.Id, a.Id, f.ProducerId, a.Brief, a.ReferenceNotes, a.DueDate, a.Priority, to, null),
            CancellationToken.None);

    private static Task<Result<AssignmentDto>> DecideImpact(Fixture f, Assignment a, Guid by, ImpactReviewDecision d, string reason) =>
        new ResolveImpactReviewCommandHandler(f.Db.Object).Handle(
            new ResolveImpactReviewCommand(f.Chapter.Id, a.Id, by, d, reason), CancellationToken.None);

    private static void UploadPage(Fixture f, Page page, Guid by) =>
        f.AssetReferences.Add(AssetReference.Create(Guid.NewGuid(), f.Series.WorkspaceId, f.Chapter.Id, page.Id, f.ArtistStage.Id,
            f.AssetReferences.Count(r => r.PageId == page.Id) + 1, null, "a.png", by));

    /// <summary>
    /// Chương 6 trang (mỗi trang 1 khung), A giữ cả chương ở Pencil, đã nộp và bị Editor trả sửa: hai yêu cầu gắn khung trang 5, 6
    /// và một yêu cầu chung.
    /// </summary>
    private static async Task<(Assignment Whole, List<Page> Pages, List<Panel> Panels)> WholeChapterReturnedForPages5And6(Fixture f)
    {
        var pages = new List<Page> { f.Page };
        for (var n = 2; n <= 6; n++) pages.Add(f.AddPage(n));
        var panels = pages.Select(p => f.AddPanel(p, 1)).ToList();

        (await Create(f, WholeChapter(f))).IsSuccess.Should().BeTrue();
        var whole = f.Assignments.Single();
        (await Start(f, whole, f.ArtistId)).IsSuccess.Should().BeTrue();
        foreach (var p in pages) UploadPage(f, p, f.ArtistId);
        (await SubmitAs(f, whole, f.ArtistId)).IsSuccess.Should().BeTrue();
        (await OpenReview(f, whole, f.EditorId)).IsSuccess.Should().BeTrue();
        (await new RequestAssignmentChangesCommandHandler(f.Db.Object).Handle(new RequestAssignmentChangesCommand(
            f.Chapter.Id, whole.Id, f.EditorId, TransferReason,
            [new ChangeRequestItemRequest("Sửa trang 5", PanelId: panels[4].Id),
             new ChangeRequestItemRequest("Sửa trang 6", PanelId: panels[5].Id),
             new ChangeRequestItemRequest("Chỉnh tông chung")]), CancellationToken.None)).IsSuccess.Should().BeTrue();
        whole.Status.Should().Be(AssignmentStatus.ReworkRequired);
        return (whole, pages, panels);
    }

    // ── Giao cả chương: phân công mặc định, trang tạo thêm kế thừa, phần giao riêng không bị ghi đè ──

    [Fact]
    public async Task WholeChapter_PagesAddedLater_InheritTheDefaultAssignee_ButTransferredPagesStayWithTheNewOne()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        f.AddPanel(f.Page, 1);
        f.AddPanel(page2, 1);
        (await Create(f, WholeChapter(f))).IsSuccess.Should().BeTrue();
        var whole = f.Assignments.Single();
        (await Transfer(f, whole, f.Artist2Id, WorkflowEntityType.Page, page2.Id)).IsSuccess.Should().BeTrue();

        // Trang 3 tạo sau khi giao: thuộc phạm vi mặc định của A, không cần giao lại
        var page3 = f.AddPage(3);
        var p3 = f.AddPanel(page3, 1);
        // Khung thêm vào trang 2 (đã giao riêng cho B): vẫn thuộc B, phân công mặc định không ghi đè
        var p2b = f.AddPanel(page2, 2);

        var wholeScope = (await StageProgressRules.ResolvePanelsAsync(f.Db.Object, whole, CancellationToken.None)).Select(p => p.Id).ToList();
        wholeScope.Should().Contain(p3.Id).And.NotContain(p2b.Id);
        var part = f.Assignments.Single(a => a.AssigneeUserId == f.Artist2Id);
        (await StageProgressRules.ResolvePanelsAsync(f.Db.Object, part, CancellationToken.None)).Select(p => p.Id).Should().Contain(p2b.Id);
    }

    // ── Tình huống bắt buộc: Editor trả sửa Trang 5–6, Producer chuyển riêng cho B ──

    [Fact]
    public async Task Transfer_Pages5And6_ToB_MovesTheirOpenChangeRequests_AndEverythingElseStaysWithA()
    {
        var f = new Fixture();
        var (whole, pages, panels) = await WholeChapterReturnedForPages5And6(f);
        var firstAttempt = f.Submissions.Single();

        var result = await Transfer(f, whole, f.Artist2Id, WorkflowEntityType.Page, pages[4].Id, pages[5].Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var parts = f.Assignments.Where(a => a.AssigneeUserId == f.Artist2Id).ToList();
        parts.Should().HaveCount(2).And.OnlyContain(a => a.Status == AssignmentStatus.ReworkRequired && a.LastFeedbackComment == TransferReason);

        // A giữ trang 1–4, mất trang 5–6
        var wholeScope = (await StageProgressRules.ResolvePanelsAsync(f.Db.Object, whole, CancellationToken.None)).Select(p => p.Id).ToList();
        wholeScope.Should().BeEquivalentTo(panels.Take(4).Select(p => p.Id));

        // Yêu cầu sửa của trang 5–6 đi theo B; yêu cầu chung ở lại A; mô tả và người tạo giữ nguyên
        var cr5 = f.ChangeRequests.Single(c => c.PanelId == panels[4].Id);
        cr5.ResponsibleUserId.Should().Be(f.Artist2Id);
        cr5.CreatedByUserId.Should().Be(f.EditorId);
        parts.Single(p => p.TargetEntityId == pages[4].Id).Id.Should().Be(cr5.AssignmentId);
        f.ChangeRequests.Single(c => c.PanelId == panels[5].Id).ResponsibleUserId.Should().Be(f.Artist2Id);
        var general = f.ChangeRequests.Single(c => c.PanelId == null);
        general.AssignmentId.Should().Be(whole.Id);
        general.ResponsibleUserId.Should().Be(f.ArtistId);

        // Lịch sử nộp của A không đổi; A được báo
        f.Submissions.Should().ContainSingle().Which.Should().BeSameAs(firstAttempt);
        firstAttempt.SubmittedByUserId.Should().Be(f.ArtistId);
        f.Notifications.Should().Contain(n => n.UserId == f.ArtistId && n.AssignmentId == whole.Id &&
                                              n.Type == NotificationType.AssignmentUnassigned);
        f.Notifications.Should().Contain(n => n.UserId == f.Artist2Id && n.Type == NotificationType.AssignmentAssigned);
    }

    [Fact]
    public async Task AfterTransfer_A_CannotWriteOrResolveTheTransferredPages_B_Can_AndA_ResubmitsOnlyTheRest()
    {
        var f = new Fixture();
        var (whole, pages, panels) = await WholeChapterReturnedForPages5And6(f);
        (await Transfer(f, whole, f.Artist2Id, WorkflowEntityType.Page, pages[4].Id, pages[5].Id)).IsSuccess.Should().BeTrue();
        var crPage5 = f.ChangeRequests.Single(c => c.PanelId == panels[4].Id);

        // Ghi canvas / bố cục: A bị chặn trên trang 5 (khung lẫn cả trang), B được; A vẫn sửa trang 1
        (await WorkScopeGuard.DenyReasonAsync(f.Db.Object, f.ArtistId, pages[4].Id, panels[4].Id, default)).Should().NotBeNull();
        (await WorkScopeGuard.DenyReasonAsync(f.Db.Object, f.ArtistId, pages[4].Id, null, default)).Should().NotBeNull();
        (await WorkScopeGuard.DenyReasonAsync(f.Db.Object, f.Artist2Id, pages[4].Id, panels[4].Id, default)).Should().BeNull();
        (await WorkScopeGuard.DenyReasonAsync(f.Db.Object, f.ArtistId, pages[0].Id, panels[0].Id, default)).Should().BeNull();
        // B không được đụng phần còn lại của A
        (await WorkScopeGuard.DenyReasonAsync(f.Db.Object, f.Artist2Id, pages[0].Id, panels[0].Id, default)).Should().NotBeNull();

        // Yêu cầu sửa của trang 5: chỉ B báo đã sửa được
        var resolve = new ResolveChangeRequestCommandHandler(f.Db.Object);
        (await resolve.Handle(new(f.Chapter.Id, crPage5.Id, f.ArtistId, "xong"), default)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await resolve.Handle(new(f.Chapter.Id, crPage5.Id, f.Artist2Id, "xong"), default)).IsSuccess.Should().BeTrue();

        // B bắt đầu phần của mình (tiến độ khung trang 5–6 tiếp tục từ ChangesRequested)
        var part5 = f.Assignments.Single(a => a.TargetEntityId == pages[4].Id);
        (await Start(f, part5, f.Artist2Id)).IsSuccess.Should().BeTrue();

        // A làm lại và nộp lại: chỉ trang 1–4 nằm trong lần nộp
        (await Start(f, whole, f.ArtistId)).IsSuccess.Should().BeTrue();
        var general = f.ChangeRequests.Single(c => c.PanelId == null);
        (await resolve.Handle(new(f.Chapter.Id, general.Id, f.ArtistId, "xong"), default)).IsSuccess.Should().BeTrue();
        foreach (var p in pages.Take(4)) UploadPage(f, p, f.ArtistId);
        (await SubmitAs(f, whole, f.ArtistId)).IsSuccess.Should().BeTrue();

        var second = f.Submissions.Single(s => s.AssignmentId == whole.Id && s.AttemptNumber == 2);
        var page5And6Art = f.AssetReferences.Where(r => r.PageId == pages[4].Id || r.PageId == pages[5].Id).Select(r => r.Id);
        second.ArtworkReferenceIds.Should().HaveCount(4).And.NotContain(page5And6Art);
        f.Progress.Where(r => r.PanelId == panels[4].Id).Should().OnlyContain(r => r.Status == StageProgressStatus.InProgress);
    }

    [Fact]
    public async Task Transfer_Validations_ReasonTargetAndAssignee()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        f.AddPanel(page2, 1);
        (await Create(f, WholeChapter(f))).IsSuccess.Should().BeTrue();
        var whole = f.Assignments.Single();

        (await new TransferAssignmentScopeCommandHandler(f.Db.Object).Handle(new TransferAssignmentScopeCommand(
            f.Chapter.Id, whole.Id, f.ProducerId, WorkflowEntityType.Page, [page2.Id], f.Artist2Id, " "), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Transfer(f, whole, f.ArtistId, WorkflowEntityType.Page, page2.Id)).ErrorMessage.Should().Contain("trùng");
        (await Transfer(f, whole, f.WriterId, WorkflowEntityType.Page, page2.Id)).ErrorMessage.Should().Contain("dành cho vai trò");
        (await Transfer(f, whole, f.PendingArtistId, WorkflowEntityType.Page, page2.Id)).ErrorMessage.Should().Contain("chưa chấp nhận");
        (await Transfer(f, whole, f.Artist2Id, WorkflowEntityType.Page, Guid.NewGuid())).ErrorMessage.Should().Contain("không thuộc");

        (await Transfer(f, whole, f.Artist2Id, WorkflowEntityType.Page, page2.Id)).IsSuccess.Should().BeTrue();
        (await Transfer(f, whole, f.Artist2Id, WorkflowEntityType.Page, page2.Id)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Transfer_FromApprovedWork_IsConflict_MustBeReopenedFirst()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        (await Create(f, WholeChapter(f))).IsSuccess.Should().BeTrue();
        var whole = f.Assignments.Single();
        whole.MarkApproved();

        var result = await Transfer(f, whole, f.Artist2Id, WorkflowEntityType.Page, page2.Id);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("mở lại");
    }

    // ── Đổi người theo trạng thái ──

    [Fact]
    public async Task Reassign_WhenReworkRequired_OpenChangeRequestsFollowTheNewAssignee_ClosedOnesAndHistoryStay()
    {
        var f = new Fixture();
        var (a, open) = await Returned(f, "Khung 1 lệch tỉ lệ");
        var closed = ChangeRequest.Create(a.Id, f.Chapter.Id, null, f.ArtistId, f.EditorId, "Đổi màu viền");
        f.ChangeRequests.Add(closed);
        (await new RejectChangeRequestCommandHandler(f.Db.Object).Handle(
            new RejectChangeRequestCommand(f.Chapter.Id, closed.Id, f.EditorId, "Không cần"), default)).IsSuccess.Should().BeTrue();

        var result = await Reassign(f, a, f.Artist2Id);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        a.Status.Should().Be(AssignmentStatus.ReworkRequired);
        open.ResponsibleUserId.Should().Be(f.Artist2Id);
        open.AssignmentId.Should().Be(a.Id);
        closed.ResponsibleUserId.Should().Be(f.ArtistId);
        f.Submissions.Should().ContainSingle().Which.SubmittedByUserId.Should().Be(f.ArtistId);

        // Người cũ không còn báo sửa được; người mới bắt đầu được
        (await new ResolveChangeRequestCommandHandler(f.Db.Object).Handle(new(f.Chapter.Id, open.Id, f.ArtistId, null), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await Start(f, a, f.ArtistId)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await Start(f, a, f.Artist2Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Reassign_WhileSubmissionPending_OrApproved_IsConflict_SoTheOldResultIsNeverApprovedAsTheNewPersons()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);

        var pending = await Reassign(f, a, f.Artist2Id);
        pending.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        pending.ErrorMessage.Should().Contain("chờ duyệt");

        (await OpenReview(f, a, f.EditorId)).IsSuccess.Should().BeTrue();
        (await Reassign(f, a, f.Artist2Id)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        (await Approve(f, a, f.EditorId)).IsSuccess.Should().BeTrue();
        var approved = await Reassign(f, a, f.Artist2Id);
        approved.ErrorMessage.Should().Contain("mở lại");
        a.AssigneeUserId.Should().Be(f.ArtistId);
    }

    [Fact]
    public async Task Reviewer_WhoSubmittedThisWorkBefore_CannotDecideIt()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        // Người duyệt từng là người nộp (vd từng giữ việc trước khi đổi người)
        f.Submissions.Add(Submission.Create(a.Id, f.Chapter.Id, f.EditorId, 9));
        (await OpenReview(f, a, f.ProducerId)).IsSuccess.Should().BeTrue();

        var result = await Approve(f, a, f.EditorId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await Approve(f, a, f.ProducerId)).IsSuccess.Should().BeTrue();
    }

    // ── ChangeRequest: yêu cầu đã đóng không làm kẹt nộp lại ──

    [Fact]
    public async Task VerifiedChangeRequest_DoesNotBlockResubmit()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        cr.Resolve("đã sửa");
        cr.Verify(f.EditorId);
        (await Start(f, a, f.ArtistId)).IsSuccess.Should().BeTrue();

        (await Submit(f, a)).IsSuccess.Should().BeTrue();
    }

    // ── Đầu vào đổi trong lúc làm / chờ duyệt ──

    [Fact]
    public async Task InputChangedWhileWorking_BlocksSubmit_UntilAReviewerKeepsTheResult_WithReason()
    {
        var f = new Fixture();
        f.AddPanel(f.Page, 1);
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        (await Start(f, a, f.ArtistId)).IsSuccess.Should().BeTrue();
        a.FlagImpactReview("Đầu vào 'Script' của việc này bị mở lại: sửa thoại");

        var blocked = await Submit(f, a);
        blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        blocked.ErrorMessage.Should().Contain("kiểm tra ảnh hưởng");

        (await DecideImpact(f, a, f.ArtistId, ImpactReviewDecision.Keep, "ổn")).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await DecideImpact(f, a, f.EditorId, ImpactReviewDecision.Keep, " ")).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await DecideImpact(f, a, f.EditorId, ImpactReviewDecision.Keep, "Thoại đổi không ảnh hưởng nét vẽ")).IsSuccess.Should().BeTrue();
        a.ImpactReviewReason.Should().BeNull();

        (await Submit(f, a)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InputChangedWhileUnderReview_BlocksApprove()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        (await OpenReview(f, a, f.EditorId)).IsSuccess.Should().BeTrue();
        a.FlagImpactReview("Đầu vào bị mở lại");

        (await Approve(f, a, f.EditorId)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        a.Status.Should().Be(AssignmentStatus.UnderReview);
    }

    [Fact]
    public async Task DependencyReopenedAfterStart_BlocksSubmit()
    {
        var f = new Fixture();
        f.AddPanel(f.Page, 1);
        var dep = f.Seed();
        dep.MarkApproved();
        var a = f.SeedFor(WorkflowEntityType.Page, f.Page.Id);
        a.SetDependency(dep.Id);
        (await Start(f, a, f.ArtistId)).IsSuccess.Should().BeTrue();

        dep.RequestRework("Mở lại");

        var result = await Submit(f, a);
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("tiền đề");
    }

    [Fact]
    public async Task ImpactReview_Rework_OnApprovedWork_ReopensItWithAChangeRequest_KeepsTheOldDecisionInHistory()
    {
        var f = new Fixture();
        var (a, panel) = await Submitted(f);
        (await OpenReview(f, a, f.EditorId)).IsSuccess.Should().BeTrue();
        (await Approve(f, a, f.EditorId)).IsSuccess.Should().BeTrue();
        a.FlagImpactReview("Đầu vào 'Layout' bị mở lại");

        var result = await DecideImpact(f, a, f.EditorId, ImpactReviewDecision.Rework, "Bố cục khung đổi, phải vẽ lại");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        a.Status.Should().Be(AssignmentStatus.ReworkRequired);
        a.ImpactReviewReason.Should().BeNull();
        f.Progress.Single(r => r.PanelId == panel.Id).Status.Should().Be(StageProgressStatus.ChangesRequested);
        f.ChangeRequests.Should().ContainSingle(c => c.Status == ChangeRequestStatus.Open && c.ResponsibleUserId == f.ArtistId);
        f.Submissions.Should().ContainSingle().Which.Status.Should().Be(SubmissionStatus.Approved);
    }

    [Fact]
    public async Task FlaggedPriorStage_BlocksTheNextStageFromStarting()
    {
        var f = new Fixture();
        var (pencil, _) = await Submitted(f);
        (await OpenReview(f, pencil, f.EditorId)).IsSuccess.Should().BeTrue();
        (await Approve(f, pencil, f.EditorId)).IsSuccess.Should().BeTrue();
        pencil.FlagImpactReview("Đầu vào bị mở lại");
        var ink = f.SeedFor(WorkflowEntityType.Page, f.Page.Id, f.InkStage);

        (await Start(f, ink, f.ArtistId)).ErrorMessage.Should().Contain("kiểm tra ảnh hưởng");
    }

    // ── Khoá chương ──

    [Fact]
    public async Task LockedChapter_BlocksTransfer_AndImpactDecisions()
    {
        var f = new Fixture();
        var page2 = f.AddPage(2);
        (await Create(f, WholeChapter(f))).IsSuccess.Should().BeTrue();
        var whole = f.Assignments.Single();
        whole.FlagImpactReview("x");
        f.LockChapter();

        (await Transfer(f, whole, f.Artist2Id, WorkflowEntityType.Page, page2.Id)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await DecideImpact(f, whole, f.EditorId, ImpactReviewDecision.Keep, "ok")).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        whole.ImpactReviewReason.Should().Be("x");
    }

    // ── Snapshot lần nộp ──

    [Fact]
    public async Task Submission_SnapshotsTheScriptRevisionItWasBasedOn_LaterScriptEditsDoNotChangeIt()
    {
        var f = new Fixture();
        var scene = Scene.Create(f.Chapter.Id, 1);
        f.Scenes.Add(scene);
        var rev1 = ContentRevision.Create(f.Chapter.Id, scene.Id, ContentRevisionKind.Script, 1, "v1", "[]", "h1", null, null);
        f.Revisions.Add(rev1);

        var (a, _) = await Submitted(f);
        var rev2 = ContentRevision.Create(f.Chapter.Id, scene.Id, ContentRevisionKind.Script, 2, "v2", "[1]", "h2", rev1.Id, null);
        f.Revisions.Add(rev2);

        var submission = f.Submissions.Single(s => s.AssignmentId == a.Id);
        submission.ScriptRevisionIds.Should().Equal(rev1.Id);
        SubmissionDto.From(submission).ScriptRevisionIds.Should().Equal(rev1.Id);
    }

    // ── Đồng thời: chuyển giao / lưu / duyệt cùng lúc trên một việc ──

    [Fact]
    public async Task ConcurrentChangesToTheSameAssignment_SecondSaveIsRejected_AndMapsTo409()
    {
        var dbName = Guid.NewGuid().ToString();
        var a = Assignment.Create(Guid.NewGuid(), Guid.NewGuid(), WorkflowEntityType.Chapter, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), WorkspaceRole.Artist, "brief");
        await using (var seed = InMemoryDb.Create(databaseName: dbName))
        {
            seed.Assignments.Add(a);
            await seed.SaveChangesAsync();
        }

        await using var reviewer = InMemoryDb.Create(databaseName: dbName);
        await using var producer = InMemoryDb.Create(databaseName: dbName);
        var seenByReviewer = await reviewer.Assignments.SingleAsync(x => x.Id == a.Id);
        var seenByProducer = await producer.Assignments.SingleAsync(x => x.Id == a.Id);

        seenByReviewer.RequestRework("Sửa");
        await reviewer.SaveChangesAsync();
        seenByProducer.Reassign(Guid.NewGuid(), WorkspaceRole.Artist);

        var act = () => producer.SaveChangesAsync();
        var ex = (await act.Should().ThrowAsync<DbUpdateConcurrencyException>()).Which;

        var context = new ExceptionContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), []) { Exception = ex };
        new ConcurrencyConflictFilter().OnException(context);
        context.Result.Should().BeOfType<ConflictObjectResult>();
        context.ExceptionHandled.Should().BeTrue();
    }
}
