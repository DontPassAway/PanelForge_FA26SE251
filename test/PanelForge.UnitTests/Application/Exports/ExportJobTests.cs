using FluentAssertions;
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

    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly MemoryStorage _storage = new();
    private readonly FakeRenderer _renderer = new();
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
        new ProcessNextExportJobCommandHandler(_db, _storage, _renderer, NullLogger<ProcessNextExportJobCommandHandler>.Instance)
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
