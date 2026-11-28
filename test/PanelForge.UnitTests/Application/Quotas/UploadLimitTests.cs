using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Bible.Commands.UploadBibleReferenceImage;
using PanelForge.Application.Features.Quotas.Commands.UpdateUploadLimits;
using PanelForge.Application.Features.Quotas.Queries.GetUploadLimits;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Application.Features.Workspaces.Commands.SetWorkspaceCover;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using SeriesEntity = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Quotas;

/// <summary>Giới hạn kích thước file do Admin đặt: mặc định, append-only, áp ngay cho mọi luồng nhận file.</summary>
public class UploadLimitTests
{
    private const long MiB = UploadLimitPolicy.MiB;

    /// <summary>PNG hợp lệ (đủ chữ ký) với kích thước tuỳ ý.</summary>
    private static byte[] Png(long size)
    {
        var bytes = new byte[size];
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        sig.CopyTo(bytes, 0);
        return bytes;
    }

    private static (PanelForgeDbContext db, StudioWorkspace studio, SeriesEntity series) Seed()
    {
        var db = InMemoryDb.Create();
        var studio = StudioWorkspace.Create("Studio Mưa", Guid.NewGuid());
        db.StudioWorkspaces.Add(studio);
        var series = SeriesEntity.Create(studio.Id, "Thành phố mưa", ReadingDirection.RightToLeft);
        db.Series.Add(series);
        db.SaveChanges();
        return (db, studio, series);
    }

    [Fact]
    public async Task GetLimits_WithoutPolicy_ReturnsDefaults()
    {
        var (db, _, _) = Seed();

        var limits = (await new GetUploadLimitsQueryHandler(db).Handle(new GetUploadLimitsQuery(), default)).Value!;

        limits.IsDefault.Should().BeTrue();
        limits.ArtworkMaxBytes.Should().Be(25 * MiB);
        limits.ReferenceImageMaxBytes.Should().Be(10 * MiB);
        limits.CoverImageMaxBytes.Should().Be(5 * MiB);
        limits.ArtworkCeilingBytes.Should().Be(UploadLimitPolicy.ArtworkCeilingBytes);
    }

    [Fact]
    public async Task UpdateLimits_AppendsRecord_AndLatestWins()
    {
        var (db, _, _) = Seed();
        var handler = new UpdateUploadLimitsCommandHandler(db);

        await handler.Handle(new UpdateUploadLimitsCommand(Guid.NewGuid(), 30 * MiB, 8 * MiB, 3 * MiB), default);
        await Task.Delay(5);
        var result = await handler.Handle(new UpdateUploadLimitsCommand(Guid.NewGuid(), 40 * MiB, 12 * MiB, 2 * MiB), default);

        db.UploadLimitPolicies.Should().HaveCount(2);
        result.Value!.IsDefault.Should().BeFalse();
        result.Value.ArtworkMaxBytes.Should().Be(40 * MiB);
        result.Value.CoverImageMaxBytes.Should().Be(2 * MiB);
    }

    [Theory]
    [InlineData(0, 10, 5)]
    [InlineData(51, 10, 5)]
    [InlineData(25, 21, 5)]
    [InlineData(25, 10, -1)]
    public async Task UpdateLimits_OutsideZeroToCeiling_IsValidation_AndNothingSaved(long artMb, long refMb, long coverMb)
    {
        var (db, _, _) = Seed();

        var result = await new UpdateUploadLimitsCommandHandler(db)
            .Handle(new UpdateUploadLimitsCommand(Guid.NewGuid(), artMb * MiB, refMb * MiB, coverMb * MiB), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        db.UploadLimitPolicies.Should().BeEmpty();
    }

    [Fact]
    public async Task SeriesCover_OverDefaultLimit_IsFileTooLarge_WithoutWritingOrReserving()
    {
        var (db, studio, series) = Seed();
        var storage = new Mock<IAssetStorage>();

        var result = await new SetSeriesCoverCommandHandler(db, storage.Object)
            .Handle(new SetSeriesCoverCommand(series.Id, Guid.NewGuid(), Png(5 * MiB + 1)), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.FileTooLarge);
        storage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()), Times.Never);
        studio.UsedStorageBytes.Should().Be(0);
    }

    [Fact]
    public async Task WorkspaceCover_AdminRaisedLimit_AcceptsLargerFile()
    {
        var (db, studio, _) = Seed();
        await new UpdateUploadLimitsCommandHandler(db)
            .Handle(new UpdateUploadLimitsCommand(Guid.NewGuid(), 25 * MiB, 10 * MiB, 8 * MiB), default);

        var result = await new SetWorkspaceCoverCommandHandler(db, Mock.Of<IAssetStorage>())
            .Handle(new SetWorkspaceCoverCommand(studio.Id, studio.OwnerId, Png(6 * MiB)), default);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task BibleReference_AdminLoweredLimit_RejectsFileThatWasAllowedBefore()
    {
        var (db, _, series) = Seed();
        var png = Png(3 * MiB);
        await new UpdateUploadLimitsCommandHandler(db)
            .Handle(new UpdateUploadLimitsCommand(Guid.NewGuid(), 25 * MiB, 2 * MiB, 5 * MiB), default);

        var result = await new UploadBibleReferenceImageCommandHandler(db, Mock.Of<IAssetStorage>())
            .Handle(new UploadBibleReferenceImageCommand(series.Id, png), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.FileTooLarge);
    }
}
