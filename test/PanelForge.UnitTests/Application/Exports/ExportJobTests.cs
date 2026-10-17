using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Exports.Commands;
using PanelForge.Application.Features.Exports.Queries;
using PanelForge.Domain.Entities.Assets;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Exports;

/// <summary>CF5 UC-12 / UC-14: xuất chương đã duyệt chạy nền, đúng bản đã chụp, lỗi thì Failed kèm lý do, tải có ghi lại.</summary>
public class ExportJobTests
{
    private sealed class MemoryStorage : IAssetStorage
    {
        public readonly Dictionary<string, byte[]> Files = [];

        public Task SaveAsync(string key, byte[] content, AssetObjectInfo? info, CancellationToken ct = default)
        {
            Files[key] = content;
            return Task.CompletedTask;
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);
    }

    /// <summary>Renderer giả: nối byte các trang theo thứ tự, để kiểm thứ tự và nội dung đúng bản đã chụp.</summary>
    private sealed class FakeRenderer : IChapterExportRenderer
    {
        public Exception? Throw;
        public ExportManifest? LastManifest;

        public bool Supports(string format) => format is "CBZ" or "PDF";

        public ExportFile Render(string format, ExportManifest manifest, IReadOnlyList<ExportPageImage> pages)
        {
            if (Throw is not null) throw Throw;
            LastManifest = manifest;
            return new ExportFile(pages.SelectMany(p => p.Content).ToArray(), "application/pdf", "pdf");
        }
    }

    /// <summary>Lớp chữ giả: ghi lại element chữ nhận được cho từng trang, trả nguyên ảnh.</summary>
    private sealed class FakeLettering : IPageLetteringCompositor
    {
        public readonly Dictionary<int, IReadOnlyList<ExportLetteringElement>> Calls = new();

        public ExportPageImage Compose(ExportPageImage page, int w, int h, IReadOnlyList<ExportLetteringElement> elements)
        {
            Calls[page.PageNumber] = elements;
            return page;
        }
    }

    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly MemoryStorage _storage = new();
    private readonly FakeRenderer _renderer = new();
    private readonly FakeLettering _lettering = new();
    private readonly Moq.Mock<PanelForge.Application.Common.Interfaces.Persistence.IPageRepository> _pageRepo = new();
    private readonly Guid _producerId = Guid.NewGuid();
    private readonly Chapter _chapter;
    private readonly Page _page1;
    private readonly Page _page2;
    private readonly PipelineStage _ink;

    public ExportJobTests()
    {
        var studio = StudioWorkspace.Create("Studio", _producerId);
        _db.StudioWorkspaces.Add(studio);
        var series = DomainSeries.Create(studio.Id, "Đèn biển", ReadingDirection.RightToLeft);
        _db.Series.Add(series);
        _chapter = Chapter.Create(series.Id, 2, "Bão");
        _chapter.Approve();
        _db.Chapters.Add(_chapter);
        // Trang 2 tạo trước để kiểm thứ tự theo số trang
        _page2 = Page.CreateNew(_chapter.Id, 2, default, 1200, 1800, 300);
        _page1 = Page.CreateNew(_chapter.Id, 1, default, 1200, 1800, 300);
        _db.Pages.AddRange(_page2, _page1);
        var def = PipelineDefinition.Create(studio.Id, "P", seriesId: series.Id);
        var pencil = PipelineStage.Create(def.Id, "Pencil", "pencil", 1, allowedRole: WorkspaceRole.Artist, isInitial: true);
        _ink = PipelineStage.Create(def.Id, "Ink", "ink", 2, allowedRole: WorkspaceRole.Artist);
        _db.PipelineStages.AddRange(pencil, _ink);
        _db.ExportPresets.AddRange(
            ExportPreset.Create("PDF", "PDF Document", "pdf", """{"resolution":"300dpi"}"""),
            ExportPreset.Create("OPEN_JSON", "Open JSON", "json"));
        AddArtwork(_page1, pencil, 1, [9]);
        AddArtwork(_page1, _ink, 1, [1]);
        AddArtwork(_page2, _ink, 1, [2]);
        _db.SaveChanges();
    }

    private void AddArtwork(Page page, PipelineStage stage, int version, byte[] bytes)
    {
        var asset = Asset.Create(_chapter.Id, AssetRulesHash(bytes, page.Id, stage.Id), bytes.Length, "image/png");
        _db.Assets.Add(asset);
        _storage.Files[asset.StorageKey] = bytes;
        _db.AssetReferences.Add(AssetReference.Create(asset.Id, Guid.NewGuid(), _chapter.Id, page.Id, stage.Id, version, null, "p.png", _producerId));
    }

    private static string AssetRulesHash(byte[] bytes, Guid a, Guid b) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([.. bytes, .. a.ToByteArray(), .. b.ToByteArray()])).ToLowerInvariant();

    private Task<Result<PanelForge.Application.Features.Exports.Models.ExportJobDto>> Create(string code = "PDF") =>
        new CreateExportJobCommandHandler(_db, _renderer).Handle(new CreateExportJobCommand(_chapter.Id, _producerId, code), default);

    private Task<bool> ProcessNext() =>
        new ProcessNextExportJobCommandHandler(_db, _storage, _renderer, _pageRepo.Object, _lettering,
                NullLogger<ProcessNextExportJobCommandHandler>.Instance)
            .Handle(new ProcessNextExportJobCommand(), default);

    [Fact]
    public async Task Create_QueuesJob_WithFinalArtworkInPageOrder()
    {
        var r = await Create("pdf");

        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        r.Value!.Status.Should().Be(ExportJobStatus.Queued);
        r.Value.Format.Should().Be("PDF");
        r.Value.PageCount.Should().Be(2);
        var job = _db.ExportJobs.Single();
        var finalRefs = _db.AssetReferences.Where(x => x.PipelineStageId == _ink.Id).ToList();
        job.SnapshotArtworkIds.Should().Equal(
            finalRefs.Single(x => x.PageId == _page1.Id).Id, finalRefs.Single(x => x.PageId == _page2.Id).Id);
    }

    [Fact]
    public async Task Create_RejectsUnapprovedChapter_UnknownOrUnsupportedPreset_AndPagesWithoutFinalArtwork()
    {
        (await Create("KHONG_CO")).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Create("OPEN_JSON")).ErrorCode.Should().Be(ResultErrorCodes.Validation);

        _db.Pages.Add(Page.CreateNew(_chapter.Id, 3, default, 1200, 1800, 300));
        await _db.SaveChangesAsync();
        var missing = await Create();
        missing.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        missing.ErrorMessage.Should().Contain("Trang 3");

        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(_chapter, ChapterStatus.InProduction);
        await _db.SaveChangesAsync();
        (await Create()).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _db.ExportJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Process_RendersSnapshot_StoresFileWithHash_AndNotifiesRequester()
    {
        await Create();

        (await ProcessNext()).Should().BeTrue();
        (await ProcessNext()).Should().BeFalse();

        var job = _db.ExportJobs.Single();
        job.Status.Should().Be(ExportJobStatus.Completed);
        _storage.Files[job.ResultStorageKey!].Should().Equal(1, 2); // trang 1 (ink, không phải pencil) rồi trang 2
        job.ResultSha256.Should().Be(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2 })).ToLowerInvariant());
        job.ResultFileName.Should().Be("Đèn biển - Chuong 2.pdf");
        _renderer.LastManifest!.ReadingDirection.Should().Be(ReadingDirection.RightToLeft);
        _renderer.LastManifest.ConfigOptionsJson.Should().Contain("300dpi");
        _db.Notifications.Should().ContainSingle(n => n.UserId == _producerId && n.Type == NotificationType.ExportCompleted);
    }

    [Fact]
    public async Task Process_DrawsLetteringLayer_FromPageTextElements_WithSavedBalloonStyle()
    {
        var user = Guid.NewGuid();
        var style = new TextStyle("Mali", 26, Color: "#FFFFFF", StrokeColor: "#000000", StrokeWidth: 2,
            BalloonShape: "none", FillOpacity: 0, BorderWidth: 0);
        _page1.AddElement("SPEECH_BALLOON", 100, 120, 300, 200, 2, user, "Đi thôi!");
        _page1.AddElement("ARTWORK_LAYER", 0, 0, 1200, 1800, 0, user);
        var balloon = _page1.Elements.Values.Single(e => e.ElementType == "SPEECH_BALLOON").ElementId;
        _page1.UpdateElementStyle(balloon, style, user);
        _pageRepo.Setup(r => r.GetAsync(_page1.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_page1);
        await Create();

        (await ProcessNext()).Should().BeTrue();

        _db.ExportJobs.Single().Status.Should().Be(ExportJobStatus.Completed);
        var text = _lettering.Calls[1].Should().ContainSingle().Subject; // chỉ element chữ, không có layer ảnh
        text.Text.Should().Be("Đi thôi!");
        text.Style.Should().Be(style);
        (text.X, text.Y, text.Width, text.Height).Should().Be((100d, 120d, 300d, 200d));
        _lettering.Calls.Should().NotContainKey(2); // trang 2 không có stream Page → giữ nguyên ảnh
    }

    [Fact]
    public async Task Process_RendererError_MarksFailedWithReason_AndNotifies()
    {
        await Create();
        _renderer.Throw = new InvalidOperationException("Ảnh trang 2 hỏng hoặc không đọc được.");

        await ProcessNext();

        var job = _db.ExportJobs.Single();
        job.Status.Should().Be(ExportJobStatus.Failed);
        job.Error.Should().Contain("trang 2");
        _db.Notifications.Should().ContainSingle(n => n.Type == NotificationType.ExportFailed);
    }

    [Fact]
    public async Task Cancel_OnlyQueued_AndDownload_OnlyCompleted_IsRecorded()
    {
        var queued = (await Create()).Value!;
        var download = new DownloadExportFileCommandHandler(_db, _storage);
        (await download.Handle(new DownloadExportFileCommand(_chapter.Id, queued.Id, _producerId), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        var cancel = new CancelExportJobCommandHandler(_db);
        (await cancel.Handle(new CancelExportJobCommand(_chapter.Id, queued.Id, _producerId), default))
            .Value!.Status.Should().Be(ExportJobStatus.Cancelled);

        var second = (await Create()).Value!;
        await ProcessNext();
        (await cancel.Handle(new CancelExportJobCommand(_chapter.Id, second.Id, _producerId), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        var file = await download.Handle(new DownloadExportFileCommand(_chapter.Id, second.Id, _producerId), default);
        file.IsSuccess.Should().BeTrue(file.ErrorMessage);
        file.Value!.ContentType.Should().Be("application/pdf");
        _db.ExportJobs.Single(j => j.Id == second.Id).DownloadCount.Should().Be(1);

        // Job của chương khác không tải được qua route chương này
        (await download.Handle(new DownloadExportFileCommand(Guid.NewGuid(), second.Id, _producerId), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        var list = await new GetExportJobsQueryHandler(_db).Handle(new GetExportJobsQuery(_chapter.Id), default);
        list.Value!.Select(j => j.Status).Should().Equal(ExportJobStatus.Completed, ExportJobStatus.Cancelled);
    }
}
