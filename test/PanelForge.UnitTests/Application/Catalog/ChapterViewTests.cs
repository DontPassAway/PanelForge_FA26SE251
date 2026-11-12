using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Catalog;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Catalog;

/// <summary>Lượt xem thật: chỉ chương đã phát hành và đọc được, gộp lượt đọc lại trong 30 phút, catalog đếm theo kỳ.</summary>
public class ChapterViewTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly DomainSeries _series;
    private readonly Chapter _free;
    private readonly Chapter _paid;
    private readonly Chapter _draft;

    public ChapterViewTests()
    {
        var studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        _db.StudioWorkspaces.Add(studio);
        _series = DomainSeries.Create(studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _db.Series.Add(_series);

        _free = Chapter.Create(_series.Id, 1, "Miễn phí");
        _paid = Chapter.Create(_series.Id, 2, "Trả phí");
        _paid.SetPricing(true, 10000m);
        _draft = Chapter.Create(_series.Id, 3, "Nháp");
        Publish(_free);
        Publish(_paid);
        _db.Chapters.AddRange(_free, _paid, _draft);
        _db.SaveChanges();
    }

    private static void Publish(Chapter c)
    {
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(c, ChapterStatus.Published);
        typeof(Chapter).GetProperty(nameof(Chapter.PublishedAt))!.SetValue(c, DateTime.UtcNow.AddDays(-2));
    }

    private Task<Result<bool>> Record(Guid chapterId, Guid? userId = null, string? ip = "10.0.0.1") =>
        new RecordChapterViewCommandHandler(_db, new FakeCurrentUser(userId))
            .Handle(new RecordChapterViewCommand(chapterId, ip), default);

    [Fact]
    public async Task RepeatedReadWithinWindow_CountsOnce_DifferentViewersCountSeparately()
    {
        var reader = Guid.NewGuid();
        (await Record(_free.Id, reader)).Value.Should().BeTrue();
        (await Record(_free.Id, reader)).Value.Should().BeFalse();
        (await Record(_free.Id, null, "10.0.0.2")).Value.Should().BeTrue();

        (await _db.ChapterViews.CountAsync()).Should().Be(2);
        (await _db.ChapterViews.AnyAsync(v => v.ViewerKey.Contains("10.0.0.2"))).Should().BeFalse("không lưu IP thô");
    }

    [Fact]
    public async Task LockedOrUnpublishedChapters_AreNotCounted()
    {
        (await Record(_paid.Id)).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await Record(_draft.Id)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        (await _db.ChapterViews.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Catalog_ReportsTotalAndPeriodViews()
    {
        var now = DateTime.UtcNow;
        _db.ChapterViews.AddRange(
            ChapterView.Create(_free.Id, _series.Id, "u:a", now.AddHours(-2)),
            ChapterView.Create(_free.Id, _series.Id, "u:b", now.AddDays(-3)),
            ChapterView.Create(_free.Id, _series.Id, "u:c", now.AddDays(-20)),
            ChapterView.Create(_free.Id, _series.Id, "u:d", now.AddDays(-60)));
        await _db.SaveChangesAsync();

        var item = (await new GetPublicCatalogQueryHandler(_db).Handle(new GetPublicCatalogQuery(), default))
            .Value!.Items.Single();

        item.ViewCount.Should().Be(4);
        item.ViewsLast1Day.Should().Be(1);
        item.ViewsLast7Days.Should().Be(2);
        item.ViewsLast30Days.Should().Be(3);

        var detail = await new GetPublicSeriesDetailQueryHandler(_db, new FakeCurrentUser())
            .Handle(new GetPublicSeriesDetailQuery(_series.Id), default);
        detail.Value!.ViewCount.Should().Be(4);
    }
}
