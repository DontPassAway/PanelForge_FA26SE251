using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Queries;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Catalog;

/// <summary>BR-23 / BR-24: public catalog chỉ lộ Series có chương Published và chỉ các chương đó.</summary>
public class PublicCatalogQueryTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly DomainSeries _published;
    private readonly DomainSeries _inProduction;
    private readonly Chapter _publishedChapter;

    public PublicCatalogQueryTests()
    {
        var studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        _db.StudioWorkspaces.Add(studio);

        _published = DomainSeries.Create(studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _inProduction = DomainSeries.Create(studio.Id, "Secret Project", ReadingDirection.LeftToRight, genre: "Fantasy");
        _db.Series.AddRange(_published, _inProduction);

        _publishedChapter = Chapter.Create(_published.Id, 1, "Khởi đầu");
        SetPublished(_publishedChapter, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        _db.Chapters.AddRange(
            _publishedChapter,
            Chapter.Create(_published.Id, 2, "Chưa phát hành"),
            Chapter.Create(_inProduction.Id, 1, "Bản nháp"));

        _db.SaveChanges();
    }

    // Luồng Approve → Lock → Publish thuộc CF5; test đặt trạng thái trực tiếp
    private static void SetPublished(Chapter chapter, DateTime publishedAt)
    {
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(chapter, ChapterStatus.Published);
        typeof(Chapter).GetProperty(nameof(Chapter.PublishedAt))!.SetValue(chapter, publishedAt);
    }

    [Fact]
    public async Task List_ShouldOnlyContainSeriesWithPublishedChapters()
    {
        var result = await new GetPublicCatalogQueryHandler(_db).Handle(new GetPublicCatalogQuery(), default);

        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(_published.Id);
        item.StudioName.Should().Be("Studio Moon");
        item.PublishedChapterCount.Should().Be(1);
        item.LatestPublishedAt.Should().Be(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task List_ShouldFilterBySearchAndGenre()
    {
        var handler = new GetPublicCatalogQueryHandler(_db);

        (await handler.Handle(new GetPublicCatalogQuery(Search: "moon"), default)).Value!.TotalCount.Should().Be(1);
        (await handler.Handle(new GetPublicCatalogQuery(Search: "secret"), default)).Value!.TotalCount.Should().Be(0);
        (await handler.Handle(new GetPublicCatalogQuery(Genre: "Romance"), default)).Value!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Detail_ShouldListOnlyPublishedChapters()
    {
        var result = await new GetPublicSeriesDetailQueryHandler(_db).Handle(new GetPublicSeriesDetailQuery(_published.Id), default);

        result.Value!.Chapters.Should().ContainSingle().Which.Id.Should().Be(_publishedChapter.Id);
    }

    [Fact]
    public async Task Detail_OfSeriesWithoutPublishedChapters_ShouldBeNotFound()
    {
        var result = await new GetPublicSeriesDetailQueryHandler(_db).Handle(new GetPublicSeriesDetailQuery(_inProduction.Id), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task PipelineTemplates_ActiveOnly_ShouldHideInactiveTemplates()
    {
        var active = PipelineTemplate.Create("MANGA", "Manga Standard", isDefault: true);
        var inactive = PipelineTemplate.Create("OLD", "Old template");
        inactive.Update(inactive.Name, inactive.Description, false, isActive: false);
        _db.PipelineTemplates.AddRange(active, inactive);
        await _db.SaveChangesAsync();

        var handler = new GetPipelineTemplatesQueryHandler(_db);

        (await handler.Handle(new GetPipelineTemplatesQuery(ActiveOnly: true), default)).Value!
            .Select(t => t.Code).Should().Equal("MANGA");
        (await handler.Handle(new GetPipelineTemplatesQuery(), default)).Value!.Should().HaveCount(2);
    }
}
