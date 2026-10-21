using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Workspaces.Commands.SetWorkspaceCover;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceCover;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Workspaces;

/// <summary>Ảnh bìa Studio: lưu theo hash nội dung trên IAssetStorage, chỉ cập nhật DB sau khi upload thành công.</summary>
public class WorkspaceCoverTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static (PanelForgeDbContext db, StudioWorkspace ws) Seed()
    {
        var db = InMemoryDb.Create();
        var ws = StudioWorkspace.Create("Studio Mưa", Guid.NewGuid());
        db.StudioWorkspaces.Add(ws);
        db.SaveChanges();
        return (db, ws);
    }

    [Fact]
    public async Task SetCover_StoresByHash_AndReturnsVersionedUrl()
    {
        var (db, ws) = Seed();
        var storage = new Mock<IAssetStorage>();

        var result = await new SetWorkspaceCoverCommandHandler(db, storage.Object)
            .Handle(new SetWorkspaceCoverCommand(ws.Id, Guid.NewGuid(), Png), default);

        ws.CoverImageSha256.Should().HaveLength(64);
        result.Value.Should().Be($"/api/workspaces/{ws.Id}/cover?v={ws.CoverImageSha256![..12]}");
        storage.Verify(s => s.SaveAsync($"covers/workspaces/{ws.Id:N}/{ws.CoverImageSha256}", Png,
            It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task SetCover_RejectsNonImage_AndKeepsOldCover()
    {
        var (db, ws) = Seed();
        ws.SetCover(new string('a', 64));
        var storage = new Mock<IAssetStorage>(MockBehavior.Strict);

        var result = await new SetWorkspaceCoverCommandHandler(db, storage.Object)
            .Handle(new SetWorkspaceCoverCommand(ws.Id, Guid.NewGuid(), "not an image"u8.ToArray()), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        ws.CoverImageSha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task SetCover_UploadFails_KeepsOldCover()
    {
        var (db, ws) = Seed();
        ws.SetCover(new string('a', 64));
        var storage = new Mock<IAssetStorage>();
        storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("R2 down"));

        var act = () => new SetWorkspaceCoverCommandHandler(db, storage.Object)
            .Handle(new SetWorkspaceCoverCommand(ws.Id, Guid.NewGuid(), Png), default);

        await act.Should().ThrowAsync<IOException>();
        ws.CoverImageSha256.Should().Be(new string('a', 64));
    }

    [Fact]
    public async Task ClearCover_RemovesCover_AndGetReturnsNotFound()
    {
        var (db, ws) = Seed();
        ws.SetCover(new string('b', 64));
        var storage = new Mock<IAssetStorage>(MockBehavior.Strict);

        var cleared = await new SetWorkspaceCoverCommandHandler(db, storage.Object)
            .Handle(new SetWorkspaceCoverCommand(ws.Id, Guid.NewGuid(), null), default);
        var get = await new GetWorkspaceCoverQueryHandler(db, storage.Object)
            .Handle(new GetWorkspaceCoverQuery(ws.Id), default);

        cleared.Value.Should().BeNull();
        ws.CoverImageSha256.Should().BeNull();
        get.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }
}
