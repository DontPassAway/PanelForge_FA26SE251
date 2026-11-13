using PanelForge.Application.Common.Interfaces.Services;
using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.Chapters.Commands.ApproveChapter;
using PanelForge.Application.Features.Chapters.Commands.PublishChapter;
using PanelForge.Application.Features.Chapters.Commands.ReopenChapter;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Queries.GetChapterReviews;
using PanelForge.Application.Features.Chapters.Queries.GetChapterSnapshot;
using PanelForge.Application.Features.Chapters.Queries.GetChapterSnapshots;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>
/// Họp tuần 5, core flow đoạn cuối: Producer duyệt chương → Publisher duyệt và lên lịch (hoặc từ chối) → Reader đọc theo lịch.
/// </summary>
public class ChapterPublishFlowTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    // Marten giả: GetAsync trả null nên snapshot ghi CanvasStreamVersion = -1
    private readonly Mock<IPageRepository> _pages = new();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly Guid _publisherId = Guid.NewGuid();
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Page _page;

    public ChapterPublishFlowTests()
    {
        var studio = StudioWorkspace.Create("Studio Moon", _producerId);
        _db.StudioWorkspaces.Add(studio);
        _db.WorkspaceMembers.AddRange(
            WorkspaceMember.Create(studio.Id, _producerId, WorkspaceRole.Producer),
            WorkspaceMember.Create(studio.Id, _publisherId, WorkspaceRole.Publisher));

        _series = DomainSeries.Create(studio.Id, "Moon Blade", ReadingDirection.RightToLeft, genre: "Fantasy");
        _db.Series.Add(_series);

        _chapter = Chapter.Create(_series.Id, 1, "Khởi đầu").InProduction();
        _db.Chapters.Add(_chapter);

        _page = Page.CreateNew(_chapter.Id, 1, default, 1200, 1800, 300);
        _db.Pages.Add(_page);

        _db.SaveChanges();
    }

    private Task<Result<ChapterReviewDto>> Approve() =>
        new ApproveChapterCommandHandler(_db, _pages.Object).Handle(new ApproveChapterCommand(_chapter.Id, _producerId), default);

    private Task<Result<ChapterReviewDto>> Publish(DateTime? at = null) =>
        new PublishChapterCommandHandler(_db).Handle(new PublishChapterCommand(_chapter.Id, _publisherId, at), default);

    private Task<Result<ChapterReviewDto>> Reopen(Guid userId, string? comment) =>
        new ReopenChapterCommandHandler(_db).Handle(new ReopenChapterCommand(_chapter.Id, userId, comment), default);

    [Fact]
    public async Task Approve_ShouldMoveToApproved_RecordHistory_AndNotifyPublisher()
    {
        var result = await Approve();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(ChapterReviewAction.ProducerApproved);
        _chapter.Status.Should().Be(ChapterStatus.Approved);
        _db.Notifications.Should().ContainSingle(n =>
            n.UserId == _publisherId && n.Type == NotificationType.ChapterReadyForPublication && n.ChapterId == _chapter.Id);
    }

    [Fact]
    public async Task Approve_WhenChapterHasNoPage_ShouldConflict()
    {
        var empty = Chapter.Create(_series.Id, 2).InProduction();
        _db.Chapters.Add(empty);
        await _db.SaveChangesAsync();

        var result = await new ApproveChapterCommandHandler(_db, _pages.Object).Handle(new ApproveChapterCommand(empty.Id, _producerId), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        empty.Status.Should().Be(ChapterStatus.InProduction);
    }

    [Fact]
    public async Task Approve_WhenAssignmentNotApproved_ShouldConflict()
    {
        _db.Assignments.Add(Assignment.Create(
            _series.Id, _chapter.Id, WorkflowEntityType.Chapter, _chapter.Id, Guid.NewGuid(),
            Guid.NewGuid(), WorkspaceRole.Writer, "Viết kịch bản"));
        await _db.SaveChangesAsync();

        var result = await Approve();

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _chapter.Status.Should().Be(ChapterStatus.InProduction);
        _db.ChapterReviews.Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_WhenApprovedWorkHasUnresolvedImpactFlag_ShouldConflict()
    {
        var work = Assignment.Create(
            _series.Id, _chapter.Id, WorkflowEntityType.Page, _page.Id, Guid.NewGuid(),
            Guid.NewGuid(), WorkspaceRole.Artist, "Vẽ trang 1");
        work.StartWork();
        work.SubmitForReview();
        work.OpenReview();
        work.MarkApproved();
        work.FlagImpactReview("Kịch bản trang 1 đã đổi");
        _db.Assignments.Add(work);
        await _db.SaveChangesAsync();

        var gate = await new PanelForge.Application.Features.Chapters.Queries.GetChapterLockGate.GetChapterLockGateQueryHandler(_db)
            .Handle(new(_chapter.Id), default);

        gate.Value!.Items.Should().Contain(i => i.Code == "IMPACT_FLAGS" && i.Blocking && i.Count == 1);
        (await Approve()).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _chapter.Status.Should().Be(ChapterStatus.InProduction);
    }

    [Fact]
    public async Task LockGate_PageWithoutProductionWork_IsWarningOnly()
    {
        var gate = await new PanelForge.Application.Features.Chapters.Queries.GetChapterLockGate.GetChapterLockGateQueryHandler(_db)
            .Handle(new(_chapter.Id), default);

        gate.Value!.CanApprove.Should().BeTrue();
        gate.Value.Items.Should().Contain(i => i.Code == "PAGES_WITHOUT_WORK" && !i.Blocking && i.Count == 1);
    }

    [Fact]
    public async Task Approve_WhenChangeRequestStillOpen_ShouldConflict()
    {
        _db.ChangeRequests.Add(ChangeRequest.Create(
            Guid.NewGuid(), _chapter.Id, null, Guid.NewGuid(), _producerId, "Sửa lại nét vẽ"));
        await _db.SaveChangesAsync();

        (await Approve()).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Fact]
    public async Task Approve_WhenBlockingConsistencyFindingOpen_ShouldConflict_UntilTriaged()
    {
        var blocking = ConsistencyFinding.Create(_chapter.Id, "SPEAKER_NOT_CHARACTER", ConsistencyFindingSeverity.Blocking,
            "Cảnh 1, dòng 1: người nói không phải nhân vật.", "fp-1");
        _db.ConsistencyFindings.AddRange(blocking, ConsistencyFinding.Create(
            _chapter.Id, "SPEAKER_MISSING", ConsistencyFindingSeverity.Warning, "Cảnh 1, dòng 2: thiếu người nói.", "fp-2"));
        await _db.SaveChangesAsync();

        var blocked = await Approve();
        blocked.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        blocked.ErrorMessage.Should().Contain("1 lỗi nhất quán");
        _chapter.Status.Should().Be(ChapterStatus.InProduction);

        // Editor bỏ qua có lý do (BR-21) → cổng mở; lỗi Warning còn mở không chặn
        blocking.Triage(ConsistencyFindingStatus.Ignored, "Cảnh trong mơ", Guid.NewGuid());
        await _db.SaveChangesAsync();
        (await Approve()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_BeforeProducerApproval_ShouldConflict()
    {
        (await Publish()).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _chapter.Status.Should().Be(ChapterStatus.InProduction);
    }

    [Fact]
    public async Task Publish_InThePast_ShouldFailValidation()
    {
        await Approve();

        var result = await Publish(DateTime.UtcNow.AddDays(-1));

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _chapter.Status.Should().Be(ChapterStatus.Approved);
    }

    [Fact]
    public async Task Publish_Scheduled_ShouldHideFromReaderUntilDue()
    {
        await Approve();
        var due = DateTime.UtcNow.AddDays(3);

        var result = await Publish(due);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(ChapterReviewAction.PublisherPublished);
        result.Value.ScheduledAt.Should().BeCloseTo(due, TimeSpan.FromSeconds(1));
        _chapter.Status.Should().Be(ChapterStatus.Published);
        _db.Notifications.Should().Contain(n => n.UserId == _producerId && n.Type == NotificationType.ChapterPublished);

        // Chưa đến giờ: Reader không thấy chương, không thấy Series trong catalog
        (await new GetPublicChapterQueryHandler(_db, Mock.Of<ICurrentUserService>()).Handle(new GetPublicChapterQuery(_chapter.Id), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await new GetPublicCatalogQueryHandler(_db).Handle(new GetPublicCatalogQuery(), default))
            .Value!.TotalCount.Should().Be(0);
        (await new GetPublicSeriesDetailQueryHandler(_db, Mock.Of<ICurrentUserService>()).Handle(new GetPublicSeriesDetailQuery(_series.Id), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Publish_Now_ShouldLetReaderReadPagesInSeriesReadingDirection()
    {
        await Approve();
        (await Publish()).IsSuccess.Should().BeTrue();

        var read = await new GetPublicChapterQueryHandler(_db, Mock.Of<ICurrentUserService>()).Handle(new GetPublicChapterQuery(_chapter.Id), default);

        read.IsSuccess.Should().BeTrue();
        read.Value!.ReadingDirection.Should().Be(ReadingDirection.RightToLeft);
        read.Value.Pages.Should().ContainSingle().Which.PageNumber.Should().Be(1);
        read.Value.Pages[0].ImageUrl.Should().BeNull("trang chưa có artwork");

        (await new GetPublicCatalogQueryHandler(_db).Handle(new GetPublicCatalogQuery(), default))
            .Value!.Items.Should().ContainSingle().Which.Id.Should().Be(_series.Id);
    }

    [Fact]
    public async Task Reopen_WithoutReason_ShouldFailValidation()
    {
        await Approve();

        (await Reopen(_publisherId, "  ")).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _chapter.Status.Should().Be(ChapterStatus.Approved);
    }

    [Fact]
    public async Task Reopen_ByPublisher_ShouldRejectBackToProduction_AndNotifyProducer()
    {
        await Approve();

        var result = await Reopen(_publisherId, "Trang 3 thiếu chữ");

        result.Value!.Action.Should().Be(ChapterReviewAction.PublisherRejected);
        result.Value.Comment.Should().Be("Trang 3 thiếu chữ");
        _chapter.Status.Should().Be(ChapterStatus.InProduction);
        _db.Notifications.Should().Contain(n =>
            n.UserId == _producerId && n.Type == NotificationType.ChapterReopened && n.Message.Contains("Trang 3 thiếu chữ"));
    }

    [Fact]
    public async Task Reopen_ByProducer_ShouldRecordProducerReopened()
    {
        await Approve();

        (await Reopen(_producerId, "Cần sửa lại cảnh mở đầu")).Value!.Action.Should().Be(ChapterReviewAction.ProducerReopened);
    }

    [Fact]
    public async Task Reopen_AfterPublished_ShouldConflict()
    {
        await Approve();
        await Publish();

        (await Reopen(_producerId, "Muốn sửa")).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _chapter.Status.Should().Be(ChapterStatus.Published);
    }

    [Fact]
    public async Task History_ShouldKeepEveryDecisionInOrder()
    {
        await Approve();
        await Reopen(_publisherId, "Chưa đạt");
        await Approve();
        await Publish();

        var history = await new GetChapterReviewsQueryHandler(_db).Handle(new GetChapterReviewsQuery(_chapter.Id), default);

        history.Value!.Select(r => r.Action).Should().Equal(
            ChapterReviewAction.ProducerApproved,
            ChapterReviewAction.PublisherRejected,
            ChapterReviewAction.ProducerApproved,
            ChapterReviewAction.PublisherPublished);
    }

    [Fact]
    public async Task Approve_ShouldCreateSnapshot_AndReapproveAppendsNewOne_KeepingOldUnchanged()
    {
        (await Approve()).IsSuccess.Should().BeTrue();

        var first = _db.ChapterSnapshots.Single();
        first.SnapshotNumber.Should().Be(1);
        first.CreatedByUserId.Should().Be(_producerId);
        first.ManifestJson.Should().Contain(_page.Id.ToString());
        var firstHash = first.ManifestHash;

        (await Reopen(_producerId, "Sửa lại trang 1")).IsSuccess.Should().BeTrue();
        (await Approve()).IsSuccess.Should().BeTrue();

        var all = _db.ChapterSnapshots.Where(s => s.ChapterId == _chapter.Id).OrderBy(s => s.SnapshotNumber).ToList();
        all.Select(s => s.SnapshotNumber).Should().Equal(1, 2);
        all[0].ManifestHash.Should().Be(firstHash, "snapshot cũ không bị sửa (BR-01)");
    }

    [Fact]
    public async Task Approve_WhenBlocked_ShouldNotCreateSnapshot()
    {
        var empty = Chapter.Create(_series.Id, 2).InProduction();
        _db.Chapters.Add(empty);
        await _db.SaveChangesAsync();

        (await new ApproveChapterCommandHandler(_db, _pages.Object)
            .Handle(new ApproveChapterCommand(empty.Id, _producerId), default)).IsSuccess.Should().BeFalse();

        _db.ChapterSnapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSnapshots_ShouldListNewestFirst_AndDetailVerifiesHash()
    {
        await Approve();
        await Reopen(_producerId, "Sửa lại");
        await Approve();

        var list = await new GetChapterSnapshotsQueryHandler(_db).Handle(new GetChapterSnapshotsQuery(_chapter.Id), default);
        list.IsSuccess.Should().BeTrue();
        list.Value!.Select(s => (s.SnapshotNumber, s.IsLatest)).Should().Equal((2, true), (1, false));

        var old = list.Value![1];
        var detail = await new GetChapterSnapshotQueryHandler(_db).Handle(new GetChapterSnapshotQuery(_chapter.Id, old.Id), default);
        detail.IsSuccess.Should().BeTrue();
        detail.Value!.HashVerified.Should().BeTrue();
        detail.Value.Snapshot.IsLatest.Should().BeFalse();
        detail.Value.Manifest.GetProperty("pages").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetSnapshot_FromAnotherChapter_ShouldBeNotFound()
    {
        await Approve();
        var snapshot = _db.ChapterSnapshots.Single();
        var other = Chapter.Create(_series.Id, 2).InProduction();
        _db.Chapters.Add(other);
        await _db.SaveChangesAsync();

        var result = await new GetChapterSnapshotQueryHandler(_db).Handle(new GetChapterSnapshotQuery(other.Id, snapshot.Id), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }
}
