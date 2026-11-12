using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.Chapters.Commands.SetChapterCover;
using PanelForge.Application.Features.Chapters.Queries.GetChapterById;
using PanelForge.Application.Features.Chapters.Queries.GetChapterCover;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using SeriesEntity = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>Ảnh bìa chương: cùng luật bìa Series (lưu theo hash, coverUrl đổi theo nội dung, xoá = null).</summary>
public class ChapterCoverTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static (PanelForgeDbContext db, Chapter chapter) Seed()
    {
        var db = InMemoryDb.Create();
        var studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        db.StudioWorkspaces.Add(studio);
        var series = SeriesEntity.Create(studio.Id, "Thành phố mưa", ReadingDirection.RightToLeft);
        var chapter = Chapter.Create(series.Id, 1);
        db.Series.Add(series);
        db.Chapters.Add(chapter);
        db.SaveChanges();
        return (db, chapter);
    }

    [Fact]
    public async Task SetCover_StoresByHash_AndDtoExposesVersionedUrl()
    {
        var (db, chapter) = Seed();
        var storage = new Mock<IAssetStorage>();

        var result = await new SetChapterCoverCommandHandler(db, storage.Object)
            .Handle(new SetChapterCoverCommand(chapter.Id, Guid.NewGuid(), Png), default);
        var detail = await new GetChapterByIdQueryHandler(db).Handle(new GetChapterByIdQuery(chapter.Id), default);

        chapter.CoverImageSha256.Should().HaveLength(64);
        result.Value.Should().Be($"/api/chapters/{chapter.Id}/cover?v={chapter.CoverImageSha256![..12]}");
        detail.Value!.CoverUrl.Should().Be(result.Value);
        storage.Verify(s => s.SaveAsync($"chapter-covers/{chapter.Id:N}/{chapter.CoverImageSha256}", Png,
            It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task SetCover_RejectsNonImage_AndKeepsOldCover()
    {
        var (db, chapter) = Seed();
        chapter.SetCover(new string('a', 64));
        var storage = new Mock<IAssetStorage>(MockBehavior.Strict);

        var result = await new SetChapterCoverCommandHandler(db, storage.Object)
            .Handle(new SetChapterCoverCommand(chapter.Id, Guid.NewGuid(), "not an image"u8.ToArray()), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        chapter.CoverImageSha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task ClearCover_RemovesCover_AndGetReturnsNotFound()
    {
        var (db, chapter) = Seed();
        chapter.SetCover(new string('b', 64));
        var storage = new Mock<IAssetStorage>(MockBehavior.Strict);

        var cleared = await new SetChapterCoverCommandHandler(db, storage.Object)
            .Handle(new SetChapterCoverCommand(chapter.Id, Guid.NewGuid(), null), default);
        var get = await new GetChapterCoverQueryHandler(db, storage.Object)
            .Handle(new GetChapterCoverQuery(chapter.Id), default);

        cleared.Value.Should().BeNull();
        chapter.CoverImageSha256.Should().BeNull();
        get.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetCover_ReadsCurrentHashFromDb()
    {
        var (db, chapter) = Seed();
        var sha = new string('c', 64);
        chapter.SetCover(sha);
        db.SaveChanges();
        var storage = new Mock<IAssetStorage>();
        storage.Setup(s => s.OpenReadAsync($"chapter-covers/{chapter.Id:N}/{sha}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Png));

        var result = await new GetChapterCoverQueryHandler(db, storage.Object)
            .Handle(new GetChapterCoverQuery(chapter.Id), default);

        result.Value!.ContentType.Should().Be("image/png");
        result.Value.Content.Should().Equal(Png);
    }

    private static void Publish(Chapter c, DateTime at)
    {
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(c, ChapterStatus.Published);
        typeof(Chapter).GetProperty(nameof(Chapter.PublishedAt))!.SetValue(c, at);
    }

    [Fact]
    public async Task PublicCover_OnlyForReleasedChapter_AndDtosExposePublicUrl()
    {
        var (db, chapter) = Seed();
        var sha = new string('d', 64);
        chapter.SetCover(sha);
        db.SaveChanges();
        var storage = new Mock<IAssetStorage>();
        storage.Setup(s => s.OpenReadAsync($"chapter-covers/{chapter.Id:N}/{sha}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Png));
        var handler = new GetPublicChapterCoverQueryHandler(db, storage.Object);

        // Chưa phát hành / lên lịch tương lai → không lộ bìa
        (await handler.Handle(new GetPublicChapterCoverQuery(chapter.Id), default)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        Publish(chapter, DateTime.UtcNow.AddDays(1));
        db.SaveChanges();
        (await handler.Handle(new GetPublicChapterCoverQuery(chapter.Id), default)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        Publish(chapter, DateTime.UtcNow.AddMinutes(-1));
        db.SaveChanges();
        var cover = await handler.Handle(new GetPublicChapterCoverQuery(chapter.Id), default);
        var read = await new GetPublicChapterQueryHandler(db, Mock.Of<ICurrentUserService>()).Handle(new GetPublicChapterQuery(chapter.Id), default);
        var detail = await new GetPublicSeriesDetailQueryHandler(db, Mock.Of<ICurrentUserService>()).Handle(new GetPublicSeriesDetailQuery(chapter.SeriesId), default);

        cover.Value!.Content.Should().Equal(Png);
        var url = $"/api/catalog/chapters/{chapter.Id}/cover?v={sha[..12]}";
        read.Value!.CoverUrl.Should().Be(url);
        detail.Value!.Chapters.Single().CoverUrl.Should().Be(url);
    }
}
