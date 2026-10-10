using System.Security.Cryptography;
using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Commands.DownloadAsset;
using PanelForge.Application.Features.Assets.Commands.IssueArtworkAccessLink;
using PanelForge.Application.Features.Assets.Commands.UploadArtwork;
using PanelForge.Application.Features.Assets.Models;
using PanelForge.Application.Features.Assets.Queries.GetPageArtwork;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Assets;

/// <summary>CF2 bước 10: upload artwork. BR-04 hash do server tính, BR-13 dùng lại Asset, BR-16 link ngắn hạn, quota storage.</summary>
public class ArtworkUploadTests
{
    private sealed class Fixture
    {
        public readonly Guid ArtistId = Guid.NewGuid();
        public readonly Guid OtherId = Guid.NewGuid();
        public readonly StudioWorkspace Workspace;
        public readonly Chapter Chapter;
        public readonly Page Page;
        public readonly PipelineStage Stage;
        public readonly Assignment Assignment;
        public readonly List<Asset> Assets = [];
        public readonly List<AssetReference> References = [];
        public readonly List<AssetAccessLog> AccessLogs = [];
        public readonly List<Panel> Panels = [];
        public readonly Mock<IPanelForgeDbContext> Db = new();
        public readonly Mock<IAssetStorage> Storage = new();
        public readonly Mock<IAssetLinkSigner> Signer = new();
        public readonly Mock<IWorkspaceAuthorizationService> Auth = new();

        public Fixture(long quota = 0)
        {
            Workspace = StudioWorkspace.Create("Studio", Guid.NewGuid(), quota);
            var series = Series.Create(Workspace.Id, "Series", ReadingDirection.RightToLeft);
            Chapter = Chapter.Create(series.Id, 1);
            var def = PipelineDefinition.Create(Workspace.Id, "Pipeline", seriesId: series.Id);
            Stage = PipelineStage.Create(def.Id, "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
            Page = Page.CreateNew(Chapter.Id, 1, default, 1200, 1800, 300);

            Assignment = Assignment.Create(series.Id, Chapter.Id, WorkflowEntityType.Page, Page.Id, Stage.Id,
                ArtistId, WorkspaceRole.Artist, "Vẽ pencil");
            Assignment.StartWork();

            Db.Setup(d => d.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { series }));
            Db.Setup(d => d.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { Chapter }));
            Db.Setup(d => d.Pages).Returns(DbSetMockHelper.CreateDbSetMock(new List<Page> { Page }));
            Db.Setup(d => d.Panels).Returns(DbSetMockHelper.CreateDbSetMock(Panels));
            Db.Setup(d => d.PipelineDefinitions).Returns(DbSetMockHelper.CreateDbSetMock(new List<PipelineDefinition> { def }));
            Db.Setup(d => d.PipelineStages).Returns(DbSetMockHelper.CreateDbSetMock(new List<PipelineStage> { Stage }));
            Db.Setup(d => d.Assignments).Returns(DbSetMockHelper.CreateDbSetMock(new List<Assignment> { Assignment }));
            Db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { Workspace }));
            Db.Setup(d => d.ElementTypes).Returns(DbSetMockHelper.CreateDbSetMock(
                new List<ElementTypeMasterData> { ElementTypeMasterData.Create("ARTWORK_LAYER", "Artwork Layer") }));
            Db.Setup(d => d.Assets).Returns(DbSetMockHelper.CreateDbSetMock(Assets));
            Db.Setup(d => d.AssetReferences).Returns(DbSetMockHelper.CreateDbSetMock(References));
            Db.Setup(d => d.AssetAccessLogs).Returns(DbSetMockHelper.CreateDbSetMock(AccessLogs));
            Db.Setup(d => d.Users).Returns(DbSetMockHelper.CreateDbSetMock(new List<PanelForge.Domain.Entities.Identity.User>()));
            Db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Task<Result<AssetReferenceDto>> Upload(
            byte[] content, string? clientHash = null, Guid? userId = null, Guid? stageId = null, Guid? pageId = null,
            string? layerType = null, string? fileName = "page1.png", Guid? panelId = null) =>
            new UploadArtworkCommandHandler(Db.Object, Storage.Object).Handle(new UploadArtworkCommand(
                Chapter.Id, pageId ?? Page.Id, stageId ?? Stage.Id, userId ?? ArtistId, content, fileName,
                clientHash ?? Hash(content), layerType, panelId), CancellationToken.None);
    }

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>PNG tối thiểu hợp lệ về chữ ký, đệm thêm byte để mỗi <paramref name="seed"/> cho nội dung khác nhau.</summary>
    private static byte[] Png(int seed = 0, int size = 64)
    {
        var bytes = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        bytes[^1] = (byte)seed;
        bytes[^2] = (byte)(seed >> 8);
        return bytes;
    }

    // ── BR-04: hash do server tính ──

    [Fact]
    public async Task Upload_ValidFile_StoresOnce_ReservesQuota_AndCreatesVersionOne()
    {
        var f = new Fixture(quota: 1000);
        var png = Png();

        var result = await f.Upload(png, layerType: "ARTWORK_LAYER");

        result.IsSuccess.Should().BeTrue();
        result.Value!.VersionNumber.Should().Be(1);
        result.Value.Reused.Should().BeFalse();
        result.Value.Sha256.Should().Be(Hash(png));
        result.Value.ContentType.Should().Be("image/png");
        result.Value.LayerType.Should().Be("ARTWORK_LAYER");
        f.Assets.Should().ContainSingle();
        f.References.Should().ContainSingle().Which.UploadedByUserId.Should().Be(f.ArtistId);
        f.Workspace.UsedStorageBytes.Should().Be(png.Length);
        f.Storage.Verify(s => s.SaveAsync(f.Assets[0].StorageKey, png, It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_NewFile_PassesReadableLabelsToStorage()
    {
        var f = new Fixture(quota: 1000);
        AssetObjectInfo? info = null;
        f.Storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], AssetObjectInfo?, CancellationToken>((_, _, i, _) => info = i)
            .Returns(Task.CompletedTask);

        await f.Upload(Png(), layerType: "ARTWORK_LAYER", fileName: "v3Ch1_Trang1.png");

        info.Should().NotBeNull();
        info!.ContentType.Should().Be("image/png");
        info.Metadata.Should().Contain(new Dictionary<string, string>
        {
            ["workspace"] = "Studio", ["series"] = "Series", ["chapter"] = "1", ["page"] = "1",
            ["panel"] = "ca trang", ["stage"] = "Pencil", ["layer"] = "ARTWORK_LAYER", ["file-name"] = "v3Ch1_Trang1.png"
        });
    }

    [Fact]
    public async Task Upload_ClientHashUppercase_IsAccepted()
    {
        var f = new Fixture();
        var png = Png();

        var result = await f.Upload(png, clientHash: Hash(png).ToUpperInvariant());

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_HashMismatch_IsRejected_NothingStoredOrCharged()
    {
        var f = new Fixture(quota: 1000);

        var result = await f.Upload(Png(), clientHash: new string('a', 64));

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("BR-04");
        f.Assets.Should().BeEmpty();
        f.References.Should().BeEmpty();
        f.Workspace.UsedStorageBytes.Should().Be(0);
        f.Storage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task Upload_MissingOrMalformedClientHash_IsRejected(string? clientHash)
    {
        var f = new Fixture();
        var cmd = new UploadArtworkCommand(f.Chapter.Id, f.Page.Id, f.Stage.Id, f.ArtistId, Png(), "a.png", clientHash, null);

        var result = await new UploadArtworkCommandHandler(f.Db.Object, f.Storage.Object).Handle(cmd, CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        f.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_NotAnImage_IsRejected_EvenWithMatchingHash()
    {
        var f = new Fixture();

        var result = await f.Upload("<script>alert(1)</script>"u8.ToArray());

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("PNG");
    }

    [Fact]
    public async Task Upload_JpegAndWebp_AreDetectedByContent()
    {
        var f = new Fixture();
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        var webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();

        (await f.Upload(jpeg)).Value!.ContentType.Should().Be("image/jpeg");
        (await f.Upload(webp)).Value!.ContentType.Should().Be("image/webp");
    }

    [Fact]
    public async Task Upload_EmptyOrTooLarge_IsRejected()
    {
        var f = new Fixture();

        (await f.Upload([])).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await f.Upload(Png(size: 25 * 1024 * 1024 + 1))).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        f.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_FileNameIsSanitized_NoPathLeaks()
    {
        var f = new Fixture();

        var result = await f.Upload(Png(), fileName: @"..\..\secret\page1.png");

        result.Value!.FileName.Should().Be("page1.png");
    }

    // ── BR-13: dùng lại Asset ──

    [Fact]
    public async Task Upload_SameContentTwice_ReusesAsset_NoExtraBytes_NewVersion()
    {
        var f = new Fixture(quota: 1000);
        var png = Png();
        await f.Upload(png);

        var second = await f.Upload(png);

        second.IsSuccess.Should().BeTrue();
        second.Value!.Reused.Should().BeTrue();
        second.Value.VersionNumber.Should().Be(2);
        f.Assets.Should().ContainSingle();
        f.References.Should().HaveCount(2);
        f.Workspace.UsedStorageBytes.Should().Be(png.Length); // không tính lại
        f.Storage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_DifferentContent_GetsNewAssetAndIncrementingVersion()
    {
        var f = new Fixture();

        await f.Upload(Png(1));
        var second = await f.Upload(Png(2));

        second.Value!.VersionNumber.Should().Be(2);
        second.Value.Reused.Should().BeFalse();
        f.Assets.Should().HaveCount(2);
    }

    // ── Quota storage ──

    [Fact]
    public async Task Upload_BeyondQuota_IsConflict_NothingStoredOrSaved()
    {
        var f = new Fixture(quota: 10);

        var result = await f.Upload(Png());

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("quota");
        f.Workspace.UsedStorageBytes.Should().Be(0);
        f.Assets.Should().BeEmpty();
        f.References.Should().BeEmpty();
        f.Storage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<AssetObjectInfo?>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Db.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_AtQuotaButContentAlreadyStored_StillWorks()
    {
        var f = new Fixture(quota: 64);
        var png = Png();
        await f.Upload(png);                       // chiếm đủ quota

        var again = await f.Upload(png);           // dùng lại Asset, không cần thêm dung lượng

        again.IsSuccess.Should().BeTrue();
    }

    // ── Quyền và ngữ cảnh ──

    [Fact]
    public async Task Upload_ByUserWhoDoesNotHoldTheWork_IsForbidden()
    {
        var f = new Fixture();

        var result = await f.Upload(Png(), userId: f.OtherId);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        f.Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_WhenAssignmentNotInProgress_IsForbidden()
    {
        var f = new Fixture();
        f.Assignment.SubmitForReview();

        var result = await f.Upload(Png());

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Upload_ToAPageOfAnotherChapter_IsNotFound()
    {
        var f = new Fixture();

        var result = await f.Upload(Png(), pageId: Guid.NewGuid());

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Upload_StageOutsideSeriesPipeline_IsRejected()
    {
        var f = new Fixture();

        var result = await f.Upload(Png(), stageId: Guid.NewGuid());

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Upload_UnknownLayerType_IsRejected()
    {
        var f = new Fixture();

        var result = await f.Upload(Png(), layerType: "NOT_A_TYPE");

        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Upload_ToApprovedChapter_IsConflict()
    {
        var f = new Fixture();
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(f.Chapter, ChapterStatus.Approved);

        var result = await f.Upload(Png());

        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    // ── Danh sách phiên bản ──

    [Fact]
    public async Task GetPageArtwork_ListsVersionsInOrder_AndRejectsForeignPage()
    {
        var f = new Fixture();
        await f.Upload(Png(1));
        await f.Upload(Png(2));
        var handler = new GetPageArtworkQueryHandler(f.Db.Object);

        var list = await handler.Handle(new GetPageArtworkQuery(f.Chapter.Id, f.Page.Id), CancellationToken.None);
        var foreign = await handler.Handle(new GetPageArtworkQuery(f.Chapter.Id, Guid.NewGuid()), CancellationToken.None);

        list.Value!.Select(r => r.VersionNumber).Should().Equal(1, 2);
        foreign.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    // ── BR-16: link ngắn hạn + nhật ký truy cập ──

    [Fact]
    public async Task IssueLink_ForReferenceInChapter_ReturnsSignedShortLivedUrl()
    {
        var f = new Fixture();
        var up = (await f.Upload(Png())).Value!;
        f.Signer.Setup(s => s.Create(up.Id, f.ArtistId, IssueArtworkAccessLinkCommandHandler.LinkLifetime)).Returns("tok+/=");

        var result = await new IssueArtworkAccessLinkCommandHandler(f.Db.Object, f.Signer.Object).Handle(
            new IssueArtworkAccessLinkCommand(f.Chapter.Id, up.Id, f.ArtistId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Url.Should().Be("/api/assets/download?token=" + Uri.EscapeDataString("tok+/="));
        result.Value.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IssueLink_ForReferenceOfAnotherChapter_IsNotFound()
    {
        var f = new Fixture();
        var up = (await f.Upload(Png())).Value!;

        var result = await new IssueArtworkAccessLinkCommandHandler(f.Db.Object, f.Signer.Object).Handle(
            new IssueArtworkAccessLinkCommand(Guid.NewGuid(), up.Id, f.ArtistId), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        f.Signer.Verify(s => s.Create(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    private static async Task<(Fixture F, AssetReferenceDto Up, DownloadAssetCommandHandler Handler)> ReadyToDownload()
    {
        var f = new Fixture();
        var up = (await f.Upload(Png())).Value!;
        f.Storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
        f.Auth.Setup(a => a.IsMemberAsync(f.ArtistId, f.Workspace.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var handler = new DownloadAssetCommandHandler(f.Db.Object, f.Storage.Object, f.Signer.Object, f.Auth.Object);
        return (f, up, handler);
    }

    [Fact]
    public async Task Download_WithValidToken_StreamsFile_AndLogsTheAccess()
    {
        var (f, up, handler) = await ReadyToDownload();
        f.Signer.Setup(s => s.Validate("good")).Returns(new AssetLinkClaims(up.Id, f.ArtistId));

        var result = await handler.Handle(new DownloadAssetCommand("good", "203.0.113.5"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/png");
        result.Value.FileName.Should().Be("page1.png");
        var log = f.AccessLogs.Should().ContainSingle().Subject;
        log.AssetReferenceId.Should().Be(up.Id);
        log.UserId.Should().Be(f.ArtistId);
        log.IpAddress.Should().Be("203.0.113.5");
        result.Value.Content.Dispose();
    }

    [Fact]
    public async Task Download_WithInvalidOrExpiredToken_IsUnauthorized_AndNotLogged()
    {
        var (f, _, handler) = await ReadyToDownload();
        f.Signer.Setup(s => s.Validate(It.IsAny<string>())).Returns((AssetLinkClaims?)null);

        var result = await handler.Handle(new DownloadAssetCommand("expired", null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Unauthorized);
        f.AccessLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_AfterMemberRemoved_IsForbidden_EvenWithValidToken()
    {
        var (f, up, handler) = await ReadyToDownload();
        f.Signer.Setup(s => s.Validate("good")).Returns(new AssetLinkClaims(up.Id, f.ArtistId));
        f.Auth.Setup(a => a.IsMemberAsync(f.ArtistId, f.Workspace.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await handler.Handle(new DownloadAssetCommand("good", null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        f.AccessLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Download_WhenStoredContentIsMissing_IsNotFound()
    {
        var (f, up, handler) = await ReadyToDownload();
        f.Signer.Setup(s => s.Validate("good")).Returns(new AssetLinkClaims(up.Id, f.ArtistId));
        f.Storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Stream?)null);

        var result = await handler.Handle(new DownloadAssetCommand("good", null), CancellationToken.None);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        f.AccessLogs.Should().BeEmpty();
    }

    // ── Domain ──

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    public void Asset_RequiresLowercaseHex64(string sha) =>
        FluentActions.Invoking(() => Asset.Create(Guid.NewGuid(), sha, 10, "image/png"))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Asset_StorageKey_IsContentAddressedPerWorkspace()
    {
        var ws = Guid.NewGuid();
        var sha = new string('c', 64);

        var asset = Asset.Create(ws, sha, 10, "image/png");

        asset.StorageKey.Should().Be($"{ws:N}/cc/{sha}");
    }

    // ── Upload theo Panel ──

    [Fact]
    public async Task Upload_ForPanel_VersionsSeparatelyFromWholePage()
    {
        var f = new Fixture();
        var panel = Panel.Create(f.Page.Id, 1, 1);
        f.Panels.Add(panel);

        var page = await f.Upload(Png(1));
        var first = await f.Upload(Png(2), panelId: panel.Id);
        var second = await f.Upload(Png(3), panelId: panel.Id);

        page.Value!.PanelId.Should().BeNull();
        page.Value.VersionNumber.Should().Be(1);
        first.Value!.PanelId.Should().Be(panel.Id);
        first.Value.VersionNumber.Should().Be(1);
        second.Value!.VersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task Upload_ForPanelOfAnotherPage_IsNotFound()
    {
        var f = new Fixture();
        f.Panels.Add(Panel.Create(Guid.NewGuid(), 1, 1));

        var result = await f.Upload(Png(), panelId: f.Panels[0].Id);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        f.References.Should().BeEmpty();
    }
}
