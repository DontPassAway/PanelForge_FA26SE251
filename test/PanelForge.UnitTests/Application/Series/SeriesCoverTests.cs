using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Application.Features.Series.Queries.GetSeriesCover;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using SeriesEntity = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.SeriesTests;

/// <summary>Ảnh bìa Series: Producer đặt/xoá, lưu theo hash nội dung, DTO trả coverUrl đổi theo nội dung.</summary>
public class SeriesCoverTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static (PanelForgeDbContext db, SeriesEntity series) Seed()
    {
        var db = InMemoryDb.Create();
        var series = SeriesEntity.Create(Guid.NewGuid(), "Thành phố mưa", ReadingDirection.RightToLeft);
        db.Series.Add(series);
        db.SaveChanges();
        return (db, series);
    }

    [Fact]
    public async Task SetCover_StoresByHash_AndReturnsVersionedUrl()
    {
        var (db, series) = Seed();
        var storage = new Mock<IAssetStorage>();

        var result = await new SetSeriesCoverCommandHandler(db, storage.Object)
            .Handle(new SetSeriesCoverCommand(series.Id, Guid.NewGuid(), Png), default);

        series.CoverImageSha256.Should().HaveLength(64);
        result.Value.Should().Be($"/api/series/{series.Id}/cover?v={series.CoverImageSha256![..12]}");
        storage.Verify(s => s.SaveAsync($"covers/{series.Id:N}/{series.CoverImageSha256}", Png,
            It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task SetCover_RejectsNonImage_AndKeepsOldCover()
    {
        var (db, series) = Seed();
        series.SetCover(new string('a', 64));
        var storage = new Mock<IAssetStorage>(MockBehavior.Strict);

        var result = await new SetSeriesCoverCommandHandler(db, storage.Object)
            .Handle(new SetSeriesCoverCommand(series.Id, Guid.NewGuid(), "not an image"u8.ToArray()), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        series.CoverImageSha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task ClearCover_RemovesCover_AndGetReturnsNotFound()
    {
        var (db, series) = Seed();
        series.SetCover(new string('b', 64));
        var storage = new Mock<IAssetStorage>(MockBehavior.Strict);

        var cleared = await new SetSeriesCoverCommandHandler(db, storage.Object)
            .Handle(new SetSeriesCoverCommand(series.Id, Guid.NewGuid(), null), default);
        var get = await new GetSeriesCoverQueryHandler(db, storage.Object)
            .Handle(new GetSeriesCoverQuery(series.Id), default);

        cleared.Value.Should().BeNull();
        series.CoverImageSha256.Should().BeNull();
        get.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetCover_ReadsCurrentHashFromDb()
    {
        var (db, series) = Seed();
        var sha = new string('c', 64);
        series.SetCover(sha);
        db.SaveChanges();
        var storage = new Mock<IAssetStorage>();
        storage.Setup(s => s.OpenReadAsync($"covers/{series.Id:N}/{sha}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Png));

        var result = await new GetSeriesCoverQueryHandler(db, storage.Object)
            .Handle(new GetSeriesCoverQuery(series.Id), default);

        result.Value!.ContentType.Should().Be("image/png");
        result.Value.Content.Should().Equal(Png);
    }
}
