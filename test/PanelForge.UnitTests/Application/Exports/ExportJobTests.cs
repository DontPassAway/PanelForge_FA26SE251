using System.Text.Json;
using FluentAssertions;
using MediatR;
using Moq;
using PanelForge.Application.Features.AiAgents.Queries.GetProvenanceReport;
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

        /// <summary>Ghép giả: nối ảnh các Panel theo thứ tự để test kiểm được đúng những ảnh nào đã được ghép.</summary>
        public readonly Dictionary<int, IReadOnlyList<PagePanelLayer>> Composed = new();

        public ExportPageImage ComposePanels(int pageNumber, int w, int h, IReadOnlyList<PagePanelLayer> panels)
        {
            Composed[pageNumber] = panels;
            return new ExportPageImage(pageNumber, panels.SelectMany(p => p.Content ?? []).ToArray(), "image/png");
        }
    }

    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly MemoryStorage _storage = new();
    private readonly FakeRenderer _renderer = new();
    private readonly FakeLettering _lettering = new();
    private readonly Moq.Mock<PanelForge.Application.Common.Interfaces.Persistence.IPageRepository> _pageRepo = new();
    private readonly Mock<IWorkspaceAuthorizationService> _auth = new();
    private readonly Mock<ISender> _sender = new();
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
        _chapter = Chapter.Create(series.Id, 2, "Bão").InProduction();
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

        // Producer có mọi quyền; worker gọi report qua ISender → handler thật (dùng lại GetProvenanceReportQuery)
        _auth.Setup(a => a.HasWorkspaceRoleAsync(_producerId, It.IsAny<Guid>(), It.IsAny<WorkspaceRole[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _sender.Setup(s => s.Send(It.IsAny<GetProvenanceReportQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetProvenanceReportQuery q, CancellationToken ct) =>
                new GetProvenanceReportQueryHandler(_db, _auth.Object, _pageRepo.Object).Handle(q, ct));
    }

    /// <summary>Snapshot bản duyệt như ApproveChapter tạo (manifest + SHA-256).</summary>
    private ChapterSnapshot AddSnapshot()
    {
        var manifest = $$"""{"chapterId":"{{_chapter.Id}}","pages":[{"pageNumber":1},{"pageNumber":2}]}""";
        var snapshot = ChapterSnapshot.Create(_chapter.Id, 1, _chapter.VersionVector, _producerId, manifest, Sha256(manifest));
        _db.ChapterSnapshots.Add(snapshot);
        _db.SaveChanges();
        return snapshot;
    }

    private static string Sha256(string text) => Sha256(System.Text.Encoding.UTF8.GetBytes(text));

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

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
        new ProcessNextExportJobCommandHandler(_db, _storage, _renderer, _pageRepo.Object, _lettering, _sender.Object,
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
        var download = new DownloadExportFileCommandHandler(_db, _storage, _auth.Object);
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

    // ── CF4 4.6: JSON mở kèm provenance ──────────────────────────────────────

    [Fact]
    public async Task OpenJson_WithoutSnapshot_IsRejected()
    {
        var r = await Create("OPEN_JSON");

        r.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        r.ErrorMessage.Should().Contain("snapshot");
        _db.ExportJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenJson_ExportsSnapshotManifestAndProvenance_VerifiableOutsidePlatform()
    {
        var snapshot = AddSnapshot();
        (await Create("open_json")).IsSuccess.Should().BeTrue();

        (await ProcessNext()).Should().BeTrue();

        var job = _db.ExportJobs.Single();
        job.Status.Should().Be(ExportJobStatus.Completed, job.Error);
        job.ResultContentType.Should().Be("application/json");
        job.ResultFileName.Should().Be("Đèn biển - Chuong 2 - provenance.json");
        var bytes = _storage.Files[job.ResultStorageKey!];
        Sha256(bytes).Should().Be(job.ResultSha256); // SHA-256 của file khớp bản ghi job

        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        root.GetProperty("schema").GetString().Should().Be("panelforge.open-json");
        root.GetProperty("chapter").GetProperty("id").GetGuid().Should().Be(_chapter.Id);
        var snap = root.GetProperty("snapshot");
        snap.GetProperty("id").GetGuid().Should().Be(snapshot.Id);
        // Tính lại manifestHash từ chính manifest trong file
        Sha256(snap.GetProperty("manifestJson").GetString()!).Should().Be(snap.GetProperty("manifestHash").GetString());
        snap.GetProperty("manifest").GetProperty("pages").GetArrayLength().Should().Be(2);
        var provenance = root.GetProperty("provenance");
        provenance.GetProperty("chapterId").GetGuid().Should().Be(_chapter.Id);
        provenance.GetProperty("nodes").EnumerateArray()
            .Should().Contain(n => n.GetProperty("nodeType").GetString() == "Snapshot"); // dựng bằng GetProvenanceReportQuery
    }

    [Fact]
    public async Task OpenJson_TamperedManifest_OrRequesterLostAccess_FailsWithReason()
    {
        var snapshot = AddSnapshot();
        await Create("OPEN_JSON");
        typeof(ChapterSnapshot).GetProperty(nameof(ChapterSnapshot.ManifestJson))!.SetValue(snapshot, """{"pages":[]}""");
        await _db.SaveChangesAsync();

        await ProcessNext();
        _db.ExportJobs.Single().Error.Should().Contain("không khớp hash");

        typeof(ChapterSnapshot).GetProperty(nameof(ChapterSnapshot.ManifestJson))!
            .SetValue(snapshot, $$"""{"chapterId":"{{_chapter.Id}}","pages":[{"pageNumber":1},{"pageNumber":2}]}""");
        await _db.SaveChangesAsync();
        _auth.Reset(); // người yêu cầu không còn là Producer/Editor lúc job chạy (BR-07)
        _db.ExportJobs.Add(PanelForge.Domain.Entities.Publishing.ExportJob.Create(
            _chapter.Id, Guid.NewGuid(), Guid.NewGuid(), "OPEN_JSON", _producerId, _chapter.VersionVector, [Guid.NewGuid()]));
        await _db.SaveChangesAsync();

        await ProcessNext();
        _db.ExportJobs.OrderBy(j => j.CreatedAt).Last().Error.Should().Contain("Provenance Report");
    }

    [Fact]
    public async Task Download_EditorGetsOnlyOpenJson_OutsiderGetsNothing()
    {
        var editor = Guid.NewGuid();
        _auth.Setup(a => a.HasWorkspaceRoleAsync(editor, It.IsAny<Guid>(),
                It.Is<WorkspaceRole[]>(r => r.Contains(WorkspaceRole.Editor)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        AddSnapshot();
        var pdf = (await Create("PDF")).Value!;
        var json = (await Create("OPEN_JSON")).Value!;
        await ProcessNext();
        await ProcessNext();
        var download = new DownloadExportFileCommandHandler(_db, _storage, _auth.Object);

        (await download.Handle(new DownloadExportFileCommand(_chapter.Id, pdf.Id, editor), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        (await download.Handle(new DownloadExportFileCommand(_chapter.Id, json.Id, editor), default))
            .Value!.ContentType.Should().Be("application/json");
        (await download.Handle(new DownloadExportFileCommand(_chapter.Id, json.Id, Guid.NewGuid()), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
    }

    /// <summary>Artwork của một Panel (ảnh tải theo khung).</summary>
    private AssetReference AddPanelArtwork(Page page, Panel panel, PipelineStage stage, int version, byte[] bytes)
    {
        var asset = Asset.Create(_chapter.Id, AssetRulesHash(bytes, panel.Id, stage.Id), bytes.Length, "image/png");
        _db.Assets.Add(asset);
        _storage.Files[asset.StorageKey] = bytes;
        var reference = AssetReference.Create(asset.Id, Guid.NewGuid(), _chapter.Id, page.Id, stage.Id, version, null,
            "panel.png", _producerId, panelId: panel.Id);
        _db.AssetReferences.Add(reference);
        return reference;
    }

    [Fact]
    public async Task PageDrawnPerPanel_IsComposedFromLatestPanelArtwork_NotAnOlderWholePageDraft()
    {
        // Trang 3: ảnh cả trang chỉ có ở Pencil (bản nháp), còn Ink tải theo từng khung → phải ghép từ ảnh khung
        var page3 = Page.CreateNew(_chapter.Id, 3, default, 1200, 1800, 300);
        _db.Pages.Add(page3);
        var pencil = _db.PipelineStages.Local.Single(s => s.Name == "Pencil");
        var panelA = Panel.Create(page3.Id, 1, 1);
        panelA.UpdateBoundingBox("""{"x":60,"y":60,"unit":"px","width":1080,"height":600,"pageWidthPx":1200,"pageHeightPx":1800}""");
        var panelB = Panel.Create(page3.Id, 2, 2);
        panelB.UpdateBoundingBox("""{"x":60,"y":720,"unit":"px","width":528,"height":516,"pageWidthPx":1200,"pageHeightPx":1800}""");
        var deleted = Panel.Create(page3.Id, 3, 3);
        _db.Panels.AddRange(panelA, panelB, deleted);
        AddArtwork(page3, pencil, 1, [5]);
        AddPanelArtwork(page3, panelA, pencil, 1, [6]);
        var aInk = AddPanelArtwork(page3, panelA, _ink, 1, [7]);
        var bInk = AddPanelArtwork(page3, panelB, _ink, 2, [8]);
        AddPanelArtwork(page3, deleted, _ink, 1, [9]);
        _db.SaveChanges();
        _db.Panels.Remove(deleted); // khung đã xoá không còn được ghép
        _db.SaveChanges();

        var r = await Create();

        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        r.Value!.PageCount.Should().Be(3);
        _db.ExportJobs.Single().SnapshotArtworkIds.Should().Contain([aInk.Id, bInk.Id]).And.HaveCount(4);

        (await ProcessNext()).Should().BeTrue();
        _db.ExportJobs.Single().Status.Should().Be(ExportJobStatus.Completed, _db.ExportJobs.Single().Error);
        var layers = _lettering.Composed[3];
        layers.Select(l => l.Content).Should().BeEquivalentTo(new[] { new byte[] { 7 }, new byte[] { 8 } },
            o => o.WithStrictOrdering());
        layers[1].Should().Match<PagePanelLayer>(l => l.X == 60 && l.Y == 720 && l.Width == 528 && l.Height == 516);
        _lettering.Composed.Should().NotContainKeys(1, 2); // trang có ảnh cả trang ở stage cuối giữ nguyên
    }
}
