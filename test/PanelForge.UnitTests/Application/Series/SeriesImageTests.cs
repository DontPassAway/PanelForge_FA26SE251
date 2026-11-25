using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Series.Commands.SetSeriesImage;
using PanelForge.Application.Features.Series.Queries.GetSeriesImage;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using SeriesEntity = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.SeriesTests;

/// <summary>Avatar & banner Series: độc lập với bìa dọc và với nhau, lưu theo hash trên kho asset, lỗi kho thì giữ ảnh cũ.</summary>
public class SeriesImageTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static (PanelForgeDbContext db, SeriesEntity series) Seed()
    {
        var db = InMemoryDb.Create();
        var series = SeriesEntity.Create(Guid.NewGuid(), "Thành phố mưa", ReadingDirection.RightToLeft);
        series.SetCover(new string('c', 64));
        db.Series.Add(series);
        db.SaveChanges();
        return (db, series);
    }

    [Theory]
    [InlineData(SeriesImageKind.Avatar, "series-avatars", "avatar")]
    [InlineData(SeriesImageKind.Banner, "series-banners", "banner")]
    public async Task SetImage_StoresUnderOwnPrefix_AndLeavesOtherImagesAlone(SeriesImageKind kind, string prefix, string segment)
    {
        var (db, series) = Seed();
        var storage = new Mock<IAssetStorage>();

        var result = await new SetSeriesImageCommandHandler(db, storage.Object)
            .Handle(new SetSeriesImageCommand(series.Id, Guid.NewGuid(), kind, Png), default);

        var sha = kind == SeriesImageKind.Avatar ? series.AvatarImageSha256 : series.BannerImageSha256;
        sha.Should().HaveLength(64);
        result.Value.Should().Be($"/api/series/{series.Id}/{segment}?v={sha![..12]}");
        storage.Verify(s => s.SaveAsync($"{prefix}/{series.Id:N}/{sha}", Png,
            It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()));
        series.CoverImageSha256.Should().Be(new string('c', 64));
        (kind == SeriesImageKind.Avatar ? series.BannerImageSha256 : series.AvatarImageSha256).Should().BeNull();
    }

    [Fact]
    public async Task SetImage_RejectsNonImage_AndKeepsOld()
    {
        var (db, series) = Seed();
        series.SetBanner(new string('a', 64));
        var storage = new Mock<IAssetStorage>(MockBehavior.Strict);

        var result = await new SetSeriesImageCommandHandler(db, storage.Object)
            .Handle(new SetSeriesImageCommand(series.Id, Guid.NewGuid(), SeriesImageKind.Banner, "not an image"u8.ToArray()), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        series.BannerImageSha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task StorageFailure_DoesNotChangeDb()
    {
        var (db, series) = Seed();
        series.SetAvatar(new string('a', 64));
        db.SaveChanges();
        var storage = new Mock<IAssetStorage>();
        storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("R2 down"));

        var act = () => new SetSeriesImageCommandHandler(db, storage.Object)
            .Handle(new SetSeriesImageCommand(series.Id, Guid.NewGuid(), SeriesImageKind.Avatar, Png), default);

        await act.Should().ThrowAsync<IOException>();
        series.AvatarImageSha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task Clear_ThenGet_ReturnsNotFound_AndGetReadsKeyFromDb()
    {
        var (db, series) = Seed();
        var sha = new string('d', 64);
        series.SetAvatar(sha);
        db.SaveChanges();
        var storage = new Mock<IAssetStorage>();
        storage.Setup(s => s.OpenReadAsync($"series-avatars/{series.Id:N}/{sha}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Png));

        var got = await new GetSeriesImageQueryHandler(db, storage.Object)
            .Handle(new GetSeriesImageQuery(series.Id, SeriesImageKind.Avatar), default);
        got.Value!.ContentType.Should().Be("image/png");

        await new SetSeriesImageCommandHandler(db, storage.Object)
            .Handle(new SetSeriesImageCommand(series.Id, Guid.NewGuid(), SeriesImageKind.Avatar, null), default);
        var after = await new GetSeriesImageQueryHandler(db, storage.Object)
            .Handle(new GetSeriesImageQuery(series.Id, SeriesImageKind.Avatar), default);
        after.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }
}
