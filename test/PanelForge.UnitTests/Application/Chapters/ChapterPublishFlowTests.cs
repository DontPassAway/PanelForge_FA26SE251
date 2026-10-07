using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.Chapters.Commands.ApproveChapter;
using PanelForge.Application.Features.Chapters.Commands.PublishChapter;
using PanelForge.Application.Features.Chapters.Commands.ReopenChapter;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Application.Features.Chapters.Queries.GetChapterReviews;
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

        _chapter = Chapter.Create(_series.Id, 1, "Khởi đầu");
        _db.Chapters.Add(_chapter);

        _page = Page.CreateNew(_chapter.Id, 1, default, 1200, 1800, 300);
        _db.Pages.Add(_page);

        _db.SaveChanges();
    }

    private Task<Result<ChapterReviewDto>> Approve() =>
        new ApproveChapterCommandHandler(_db).Handle(new ApproveChapterCommand(_chapter.Id, _producerId), default);

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
        var empty = Chapter.Create(_series.Id, 2);
        _db.Chapters.Add(empty);
        await _db.SaveChangesAsync();

        var result = await new ApproveChapterCommandHandler(_db).Handle(new ApproveChapterCommand(empty.Id, _producerId), default);

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
    public async Task Approve_WhenChangeRequestStillOpen_ShouldConflict()
    {
        _db.ChangeRequests.Add(ChangeRequest.Create(
            Guid.NewGuid(), _chapter.Id, null, Guid.NewGuid(), _producerId, "Sửa lại nét vẽ"));
        await _db.SaveChangesAsync();

        (await Approve()).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
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
        (await new GetPublicChapterQueryHandler(_db).Handle(new GetPublicChapterQuery(_chapter.Id), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await new GetPublicCatalogQueryHandler(_db).Handle(new GetPublicCatalogQuery(), default))
            .Value!.TotalCount.Should().Be(0);
        (await new GetPublicSeriesDetailQueryHandler(_db).Handle(new GetPublicSeriesDetailQuery(_series.Id), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Publish_Now_ShouldLetReaderReadPagesInSeriesReadingDirection()
    {
        await Approve();
        (await Publish()).IsSuccess.Should().BeTrue();

        var read = await new GetPublicChapterQueryHandler(_db).Handle(new GetPublicChapterQuery(_chapter.Id), default);

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
}
