using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Domain.Entities.Publishing;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Exports.Commands;

/// <summary>
/// UC-14 / NFR-05 (CF5 bước 9): worker lấy job Queued cũ nhất, đọc đúng artwork đã chụp, dựng file, lưu vào kho rồi báo người
/// yêu cầu. Lỗi bất kỳ → job Failed kèm lý do (Producer tạo job mới để thử lại). Trả false khi không còn job chờ.
/// </summary>
public sealed record ProcessNextExportJobCommand : IRequest<bool>;

public sealed class ProcessNextExportJobCommandHandler : IRequestHandler<ProcessNextExportJobCommand, bool>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IChapterExportRenderer _renderer;
    private readonly ILogger<ProcessNextExportJobCommandHandler> _logger;

    public ProcessNextExportJobCommandHandler(
        IPanelForgeDbContext db, IAssetStorage storage, IChapterExportRenderer renderer,
        ILogger<ProcessNextExportJobCommandHandler> logger)
    {
        _db = db;
        _storage = storage;
        _renderer = renderer;
        _logger = logger;
    }

    public async Task<bool> Handle(ProcessNextExportJobCommand request, CancellationToken ct)
    {
        // shortcut: một worker, không khoá hàng; chạy nhiều instance API thì cần SELECT ... FOR UPDATE SKIP LOCKED
        var job = await _db.ExportJobs.Where(j => j.Status == ExportJobStatus.Queued)
            .OrderBy(j => j.CreatedAt).FirstOrDefaultAsync(ct);
        if (job is null) return false;

        job.Start();
        await _db.SaveChangesAsync(ct);

        try
        {
            var (fileName, file) = await RenderAsync(job, ct);
            var key = $"exports/{job.Id}.{file.Extension}";
            await _storage.SaveAsync(key, file.Content, new AssetObjectInfo(file.ContentType,
                new Dictionary<string, string> { ["export-job"] = job.Id.ToString(), ["format"] = job.Format }), ct);

            job.Complete(key, fileName, file.ContentType, file.Content.LongLength, AssetRules.ComputeSha256(file.Content));
            _db.Notifications.Add(Notification.Create(job.RequestedByUserId, NotificationType.ExportCompleted,
                "Bản xuất chương đã xong", $"{fileName} sẵn sàng để tải.", chapterId: job.ChapterId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Xuất chương lỗi (job {JobId}, {Format}).", job.Id, job.Format);
            job.Fail(ex is InvalidOperationException ? ex.Message : "Lỗi khi dựng hoặc lưu file xuất, hãy thử lại.");
            _db.Notifications.Add(Notification.Create(job.RequestedByUserId, NotificationType.ExportFailed,
                "Bản xuất chương bị lỗi", job.Error!, chapterId: job.ChapterId));
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<(string FileName, ExportFile File)> RenderAsync(ExportJob job, CancellationToken ct)
    {
        var info = await _db.Chapters.AsNoTracking().Where(c => c.Id == job.ChapterId)
            .Select(c => new { c.ChapterNumber, c.Title, SeriesTitle = c.Series.Title, c.Series.ReadingDirection })
            .FirstAsync(ct);
        var config = await _db.ExportPresets.AsNoTracking().Where(p => p.Id == job.ExportPresetId)
            .Select(p => p.ConfigOptionsJson).FirstAsync(ct);

        var refs = await (
            from r in _db.AssetReferences.AsNoTracking()
            where job.SnapshotArtworkIds.Contains(r.Id)
            join a in _db.Assets.AsNoTracking() on r.AssetId equals a.Id
            join p in _db.Pages.IgnoreQueryFilters().AsNoTracking() on r.PageId equals p.Id
            select new { r.Id, p.PageNumber, a.StorageKey, a.ContentType }).ToListAsync(ct);

        var pages = new List<ExportPageImage>();
        foreach (var id in job.SnapshotArtworkIds)
        {
            var r = refs.FirstOrDefault(x => x.Id == id)
                    ?? throw new InvalidOperationException("Artwork của bản đã duyệt không còn trong hệ thống.");
            await using var stream = await _storage.OpenReadAsync(r.StorageKey, ct)
                                     ?? throw new InvalidOperationException($"Không đọc được ảnh trang {r.PageNumber} trong kho.");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            pages.Add(new ExportPageImage(r.PageNumber, buffer.ToArray(), r.ContentType));
        }

        var file = _renderer.Render(job.Format,
            new ExportManifest(info.SeriesTitle, info.ChapterNumber, info.Title, info.ReadingDirection, config), pages);
        return ($"{SafeName($"{info.SeriesTitle} - Chuong {info.ChapterNumber}")}.{file.Extension}", file);
    }

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
