using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Assignments.Commands.RejectChangeRequest;
using PanelForge.Application.Features.Assignments.Commands.ReopenChangeRequest;
using PanelForge.Application.Features.Assignments.Commands.RequestAssignmentChanges;
using PanelForge.Application.Features.Assignments.Commands.ResolveChangeRequest;
using PanelForge.Application.Features.Assignments.Commands.VerifyChangeRequest;
using PanelForge.Application.Features.Assignments.Models;
using PanelForge.Application.Features.Assignments.Queries.GetAssignmentSubmissions;
using PanelForge.Application.Features.Assignments.Queries.GetChangeRequestsByChapter;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.Application.Assignments;

/// <summary>CF3: Submission (mỗi lần nộp một bản, lịch sử giữ nguyên) và ChangeRequest (Open → Resolved → Verified).</summary>
public partial class AssignmentTests
{
    private static Task<Result<ChangeRequestDto>> ResolveCr(Fixture f, ChangeRequest cr, Guid by, string? note = null) =>
        new ResolveChangeRequestCommandHandler(f.Db.Object).Handle(
            new ResolveChangeRequestCommand(f.Chapter.Id, cr.Id, by, note), CancellationToken.None);

    private static Task<Result<ChangeRequestDto>> VerifyCr(Fixture f, ChangeRequest cr, Guid by) =>
        new VerifyChangeRequestCommandHandler(f.Db.Object).Handle(
            new VerifyChangeRequestCommand(f.Chapter.Id, cr.Id, by), CancellationToken.None);

    private static Task<Result<ChangeRequestDto>> ReopenCr(Fixture f, ChangeRequest cr, Guid by, string? comment) =>
        new ReopenChangeRequestCommandHandler(f.Db.Object).Handle(
            new ReopenChangeRequestCommand(f.Chapter.Id, cr.Id, by, comment), CancellationToken.None);

    private static Task<Result<ChangeRequestDto>> RejectCr(Fixture f, ChangeRequest cr, Guid by, string? reason) =>
        new RejectChangeRequestCommandHandler(f.Db.Object).Handle(
            new RejectChangeRequestCommand(f.Chapter.Id, cr.Id, by, reason), CancellationToken.None);

    /// <summary>Việc đã nộp, được mở review rồi bị Editor trả về với một comment (sinh đúng một ChangeRequest từ comment).</summary>
    private static async Task<(Assignment Assignment, ChangeRequest Cr)> Returned(Fixture f, string comment = "Sửa khung 1")
    {
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);
        (await RequestChanges(f, a, f.EditorId, comment)).IsSuccess.Should().BeTrue();
        return (a, f.ChangeRequests.Single());
    }

    // ── Submission ──

    [Fact]
    public async Task Submit_RecordsPendingSubmission_AttemptOne()
    {
        var f = new Fixture();

        var (a, _) = await Submitted(f);

        var s = f.Submissions.Should().ContainSingle().Subject;
        s.AssignmentId.Should().Be(a.Id);
        s.AttemptNumber.Should().Be(1);
        s.Status.Should().Be(SubmissionStatus.Pending);
        s.SubmittedByUserId.Should().Be(f.ArtistId);
    }

    [Fact]
    public async Task RequestChanges_RejectsThePendingSubmission_AndLinksTheChangeRequest()
    {
        var f = new Fixture();

        var (_, cr) = await Returned(f, "Khung 1 lệch tỉ lệ");

        var s = f.Submissions.Single();
        s.Status.Should().Be(SubmissionStatus.Rejected);
        s.DecidedByUserId.Should().Be(f.EditorId);
        s.DecisionComment.Should().Be("Khung 1 lệch tỉ lệ");
        cr.SubmissionId.Should().Be(s.Id);
        cr.ResponsibleUserId.Should().Be(f.ArtistId);
        cr.CreatedByUserId.Should().Be(f.EditorId);
        cr.Status.Should().Be(ChangeRequestStatus.Open);
        cr.Description.Should().Be("Khung 1 lệch tỉ lệ");
    }

    [Fact]
    public async Task Resubmit_CreatesNewAttempt_AndKeepsTheRejectedOneAsHistory()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        await Start(f, a, f.ArtistId);
        await ResolveCr(f, cr, f.ArtistId);

        (await Submit(f, a)).IsSuccess.Should().BeTrue();

        f.Submissions.Select(s => (s.AttemptNumber, s.Status)).Should().Equal(
            (1, SubmissionStatus.Rejected), (2, SubmissionStatus.Pending));
    }

    [Fact]
    public async Task Approve_ApprovesTheLatestSubmission()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        await Start(f, a, f.ArtistId);
        await ResolveCr(f, cr, f.ArtistId);
        await Submit(f, a);
        await VerifyCr(f, cr, f.EditorId);
        await OpenReview(f, a, f.EditorId);

        var result = await Approve(f, a, f.EditorId);

        result.IsSuccess.Should().BeTrue();
        var last = f.Submissions.OrderBy(s => s.AttemptNumber).Last();
        last.Status.Should().Be(SubmissionStatus.Approved);
        last.DecidedByUserId.Should().Be(f.EditorId);
        f.Submissions.First(s => s.AttemptNumber == 1).Status.Should().Be(SubmissionStatus.Rejected);
    }

    [Fact]
    public void Submission_StateMachine_OnlyDecidesWhilePending()
    {
        var s = Submission.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        s.Invoking(x => x.Reject(Guid.NewGuid(), " ")).Should().Throw<ArgumentException>();

        s.Reject(Guid.NewGuid(), "lý do");

        s.Invoking(x => x.Approve(Guid.NewGuid())).Should().Throw<InvalidOperationException>();
        s.Invoking(x => x.Supersede()).Should().Throw<InvalidOperationException>();
    }

    // ── Tạo ChangeRequest khi trả về ──

    [Fact]
    public async Task RequestChanges_WithItems_CreatesOneChangeRequestEach_WithRegionAndDue()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);
        var due = DateTime.UtcNow.AddDays(2);
        var items = new List<ChangeRequestItemRequest>
        {
            new("Khung 1 lệch", """{"x":0.1,"y":0.2,"w":0.3,"h":0.4}""", due),
            new("Thiếu hiệu ứng"),
        };

        var result = await new RequestAssignmentChangesCommandHandler(f.Db.Object).Handle(
            new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, f.EditorId, "Cần sửa 2 chỗ", items), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        f.ChangeRequests.Should().HaveCount(2);
        f.ChangeRequests[0].RegionJson.Should().Contain("0.3");
        f.ChangeRequests[0].DueDate.Should().Be(due);
        f.ChangeRequests[1].RegionJson.Should().BeNull();
        f.ChangeRequests.Should().OnlyContain(c => c.SubmissionId == f.Submissions.Single().Id);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"x":0.9,"y":0,"w":0.2,"h":0.1}""")]   // vượt khỏi ảnh
    [InlineData("""{"x":0,"y":0,"w":0,"h":0.1}""")]       // w = 0
    [InlineData("""{"x":"a","y":0,"w":0.1,"h":0.1}""")]   // không phải số
    [InlineData("""{"x":0.1,"y":0.1,"w":0.1}""")]         // thiếu h
    [InlineData("[]")]
    public async Task RequestChanges_WithInvalidRegion_IsRejected_AndNothingChanges(string region)
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);

        var result = await new RequestAssignmentChangesCommandHandler(f.Db.Object).Handle(
            new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, f.EditorId, "c",
                [new ChangeRequestItemRequest("Sửa", region)]), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        f.ChangeRequests.Should().BeEmpty();
        a.Status.Should().Be(AssignmentStatus.UnderReview);
        f.Submissions.Single().Status.Should().Be(SubmissionStatus.Pending);
    }

    [Fact]
    public async Task RequestChanges_RegionOnArtworkVersion_StoresVersion_AndRejectsForeignVersion()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);
        f.UploadArtworkFor(a);
        var artwork = f.AssetReferences.Last();
        var handler = new RequestAssignmentChangesCommandHandler(f.Db.Object);
        const string region = """{"x":0.1,"y":0.1,"w":0.2,"h":0.2}""";

        var foreign = await handler.Handle(new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, f.EditorId, "c",
            [new ChangeRequestItemRequest("Sửa", region, null, Guid.NewGuid())]), CancellationToken.None);
        foreign.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        f.ChangeRequests.Should().BeEmpty();

        var ok = await handler.Handle(new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, f.EditorId, "c",
            [new ChangeRequestItemRequest("Mặt Mira lệch", region, null, artwork.Id)]), CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();
        f.ChangeRequests.Single().AssetReferenceId.Should().Be(artwork.Id);
    }

    [Fact]
    public async Task RequestChanges_RegionOnPanel_StoresPanel_AndRejectsPanelOfAnotherChapter()
    {
        var f = new Fixture();
        var panel = f.AddPanel(f.Page, 1);
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);
        var handler = new RequestAssignmentChangesCommandHandler(f.Db.Object);

        var foreign = await handler.Handle(new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, f.EditorId, "c",
            [new ChangeRequestItemRequest("Sửa", PanelId: Guid.NewGuid())]), CancellationToken.None);
        foreign.ErrorCode.Should().Be(ResultErrorCodes.Validation);

        var ok = await handler.Handle(new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, f.EditorId, "c",
            [new ChangeRequestItemRequest("Khung 1 lệch", PanelId: panel.Id)]), CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();
        f.ChangeRequests.Single().PanelId.Should().Be(panel.Id);
    }

    [Fact]
    public async Task Decisions_WithStaleExpectedSubmission_AreConflict_AndCurrentOneWorks()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);
        var pendingId = f.Submissions.Single().Id;

        var staleReturn = await new RequestAssignmentChangesCommandHandler(f.Db.Object).Handle(
            new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, f.EditorId, "c", null, Guid.NewGuid()), CancellationToken.None);
        staleReturn.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        f.ChangeRequests.Should().BeEmpty();

        var approve = new PanelForge.Application.Features.Assignments.Commands.ApproveAssignment.ApproveAssignmentCommandHandler(f.Db.Object);
        (await approve.Handle(new(f.Chapter.Id, a.Id, f.EditorId, Guid.NewGuid()), CancellationToken.None))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        a.Status.Should().Be(AssignmentStatus.UnderReview);

        (await approve.Handle(new(f.Chapter.Id, a.Id, f.EditorId, pendingId), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_WithComment_KeepsTheReviewerComment()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        await Start(f, a, f.ArtistId);
        await ResolveCr(f, cr, f.ArtistId, "Đã chỉnh tỉ lệ");

        var result = await new VerifyChangeRequestCommandHandler(f.Db.Object).Handle(
            new VerifyChangeRequestCommand(f.Chapter.Id, cr.Id, f.EditorId, "  Đạt, tỉ lệ đúng  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(ChangeRequestStatus.Verified);
        result.Value.ReviewerComment.Should().Be("Đạt, tỉ lệ đúng");
    }

    [Fact]
    public async Task RequestChanges_WithBlankItemDescription_IsRejected()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);

        var result = await new RequestAssignmentChangesCommandHandler(f.Db.Object).Handle(
            new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, f.EditorId, "c", [new ChangeRequestItemRequest("  ")]),
            CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        f.ChangeRequests.Should().BeEmpty();
    }

    // ── Cổng: yêu cầu sửa chặn nộp lại / duyệt ──

    [Fact]
    public async Task Resubmit_WhileChangeRequestOpen_IsBlocked_UntilResolved()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        await Start(f, a, f.ArtistId);

        var blocked = await Submit(f, a);
        blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        blocked.ErrorMessage.Should().Contain("1 yêu cầu");
        a.Status.Should().Be(AssignmentStatus.InProgress);

        await ResolveCr(f, cr, f.ArtistId);
        (await Submit(f, a)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Approve_WhileChangeRequestResolvedButNotVerified_IsBlocked_ThenWorksAfterVerify()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        await Start(f, a, f.ArtistId);
        await ResolveCr(f, cr, f.ArtistId);
        await Submit(f, a);
        await OpenReview(f, a, f.EditorId);

        var blocked = await Approve(f, a, f.EditorId);
        blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        a.Status.Should().Be(AssignmentStatus.UnderReview);

        (await VerifyCr(f, cr, f.EditorId)).IsSuccess.Should().BeTrue();
        (await Approve(f, a, f.EditorId)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RejectedChangeRequest_CountsAsClosed_SoWorkCanBeResubmitted()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        await Start(f, a, f.ArtistId);

        var rejected = await RejectCr(f, cr, f.EditorId, "Phản đối hợp lý, giữ nguyên");

        rejected.IsSuccess.Should().BeTrue();
        cr.Status.Should().Be(ChangeRequestStatus.Rejected);
        cr.ClosedByUserId.Should().Be(f.EditorId);
        (await Submit(f, a)).IsSuccess.Should().BeTrue();
    }

    // ── Vòng đời ChangeRequest ──

    [Fact]
    public async Task Resolve_ByResponsible_MovesToResolved_AndNotifiesCreator()
    {
        var f = new Fixture();
        var (_, cr) = await Returned(f);
        f.Notifications.Clear();

        var result = await ResolveCr(f, cr, f.ArtistId, "Đã chỉnh tỉ lệ");

        result.IsSuccess.Should().BeTrue();
        cr.Status.Should().Be(ChangeRequestStatus.Resolved);
        cr.ResolutionNote.Should().Be("Đã chỉnh tỉ lệ");
        cr.ResolvedAt.Should().NotBeNull();
        var n = f.Notifications.Should().ContainSingle().Subject;
        n.Type.Should().Be(NotificationType.ChangeRequestResolved);
        n.UserId.Should().Be(f.EditorId);
    }

    [Fact]
    public async Task Resolve_ByAnotherUser_IsForbidden()
    {
        var f = new Fixture();
        var (_, cr) = await Returned(f);

        var result = await ResolveCr(f, cr, f.EditorId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        cr.Status.Should().Be(ChangeRequestStatus.Open);
    }

    [Fact]
    public async Task Verify_ByResponsible_IsForbidden_AndOpenOneCannotBeVerified()
    {
        var f = new Fixture();
        var (_, cr) = await Returned(f);

        (await VerifyCr(f, cr, f.EditorId)).ErrorCode.Should().Be(ResultErrorCodes.Conflict); // còn Open, chưa Resolved

        await ResolveCr(f, cr, f.ArtistId);
        (await VerifyCr(f, cr, f.ArtistId)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        cr.Status.Should().Be(ChangeRequestStatus.Resolved);

        var ok = await VerifyCr(f, cr, f.EditorId);
        ok.IsSuccess.Should().BeTrue();
        cr.Status.Should().Be(ChangeRequestStatus.Verified);
        cr.IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task Reopen_NeedsComment_AndSendsItBackToOpen_NotifyingResponsible()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        await Start(f, a, f.ArtistId);
        await ResolveCr(f, cr, f.ArtistId);
        f.Notifications.Clear();

        (await ReopenCr(f, cr, f.EditorId, " ")).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        cr.Status.Should().Be(ChangeRequestStatus.Resolved);

        var result = await ReopenCr(f, cr, f.EditorId, "Vẫn lệch");

        result.IsSuccess.Should().BeTrue();
        cr.Status.Should().Be(ChangeRequestStatus.Open);
        cr.ReviewerComment.Should().Be("Vẫn lệch");
        cr.ResolvedAt.Should().BeNull();
        var n = f.Notifications.Should().ContainSingle().Subject;
        n.Type.Should().Be(NotificationType.ChangeRequestReopened);
        n.UserId.Should().Be(f.ArtistId);
        (await Submit(f, a)).ErrorCode.Should().Be(ResultErrorCodes.Conflict); // lại chặn nộp
    }

    [Fact]
    public async Task Reject_NeedsReason_AndOnlyAppliesToOpen()
    {
        var f = new Fixture();
        var (_, cr) = await Returned(f);

        (await RejectCr(f, cr, f.EditorId, null)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await RejectCr(f, cr, f.ArtistId, "tự đóng")).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);

        await ResolveCr(f, cr, f.ArtistId);
        (await RejectCr(f, cr, f.EditorId, "muộn")).ErrorCode.Should().Be(ResultErrorCodes.Conflict); // Resolved không Reject được
    }

    [Fact]
    public async Task ChangeRequestOfAnotherChapter_IsNotFound_AndLockedChapterIsConflict()
    {
        var f = new Fixture();
        var (_, cr) = await Returned(f);

        var other = await new ResolveChangeRequestCommandHandler(f.Db.Object).Handle(
            new ResolveChangeRequestCommand(Guid.NewGuid(), cr.Id, f.ArtistId, null), CancellationToken.None);
        other.ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        f.LockChapter();
        (await ResolveCr(f, cr, f.ArtistId)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        cr.Status.Should().Be(ChangeRequestStatus.Open);
    }

    // ── Truy vấn ──

    [Fact]
    public async Task Submissions_ListsAttemptsInOrder_AndHidesOtherChapters()
    {
        var f = new Fixture();
        var (a, cr) = await Returned(f);
        await Start(f, a, f.ArtistId);
        await ResolveCr(f, cr, f.ArtistId);
        await Submit(f, a);
        var handler = new GetAssignmentSubmissionsQueryHandler(f.Db.Object);

        var list = await handler.Handle(new GetAssignmentSubmissionsQuery(f.Chapter.Id, a.Id), CancellationToken.None);
        var elsewhere = await handler.Handle(new GetAssignmentSubmissionsQuery(Guid.NewGuid(), a.Id), CancellationToken.None);

        list.Value!.Select(s => s.AttemptNumber).Should().Equal(1, 2);
        list.Value![0].Status.Should().Be(SubmissionStatus.Rejected);
        elsewhere.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task ChangeRequests_AreFilteredByStatusAndResponsible()
    {
        var f = new Fixture();
        var (a, _) = await Submitted(f);
        await OpenReview(f, a, f.EditorId);
        await new RequestAssignmentChangesCommandHandler(f.Db.Object).Handle(
            new RequestAssignmentChangesCommand(f.Chapter.Id, a.Id, f.EditorId, "c",
                [new ChangeRequestItemRequest("A"), new ChangeRequestItemRequest("B")]), CancellationToken.None);
        await ResolveCr(f, f.ChangeRequests[0], f.ArtistId);
        var handler = new GetChangeRequestsByChapterQueryHandler(f.Db.Object);

        var all = await handler.Handle(new GetChangeRequestsByChapterQuery(f.Chapter.Id), CancellationToken.None);
        var resolved = await handler.Handle(
            new GetChangeRequestsByChapterQuery(f.Chapter.Id, Status: ChangeRequestStatus.Resolved), CancellationToken.None);
        var notMine = await handler.Handle(
            new GetChangeRequestsByChapterQuery(f.Chapter.Id, ResponsibleUserId: f.WriterId), CancellationToken.None);

        all.Value!.Should().HaveCount(2);
        resolved.Value!.Should().ContainSingle().Which.Description.Should().Be("A");
        notMine.Value!.Should().BeEmpty();
    }

    // ── Domain ──

    [Fact]
    public void ChangeRequest_StateMachine_FollowsTheLifecycle()
    {
        var cr = ChangeRequest.Create(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "Sửa");
        cr.Invoking(c => c.Verify(Guid.NewGuid())).Should().Throw<InvalidOperationException>(); // Open chưa Verify được
        cr.Invoking(c => c.Reopen("x")).Should().Throw<InvalidOperationException>();

        cr.Resolve(null);
        cr.Invoking(c => c.Resolve(null)).Should().Throw<InvalidOperationException>();
        cr.Invoking(c => c.Reject(Guid.NewGuid(), "x")).Should().Throw<InvalidOperationException>();

        cr.Reopen("chưa đạt");
        cr.Status.Should().Be(ChangeRequestStatus.Open);
        cr.Resolve("xong");
        cr.Verify(Guid.NewGuid());

        cr.Status.Should().Be(ChangeRequestStatus.Verified);
        cr.Invoking(c => c.Reopen("x")).Should().Throw<InvalidOperationException>(); // đã đóng
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("""{"x":0,"y":0,"w":1,"h":1}""", true)]
    [InlineData("""{"x":0.25,"y":0.5,"w":0.5,"h":0.5}""", true)]
    [InlineData("""{"x":-0.1,"y":0,"w":0.5,"h":0.5}""", false)]
    [InlineData("""{"x":0.5,"y":0.5,"w":0.6,"h":0.5}""", false)]
    public void ChangeRequest_ValidateRegion_AcceptsOnlyNormalizedRegions(string? json, bool valid) =>
        (ChangeRequest.ValidateRegion(json) is null).Should().Be(valid);
}
