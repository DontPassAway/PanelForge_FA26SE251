using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Models;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Exports.Common;
using PanelForge.Application.Features.Exports.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Exports.Commands;

// ─── Huỷ job đang chờ ────────────────────────────────────────────────────────

/// <summary>UC-12: Producer huỷ bản xuất còn Queued. Job đang chạy không huỷ giữa chừng (409).</summary>
public sealed record CancelExportJobCommand(Guid ChapterId, Guid JobId, Guid UserId) : IRequest<Result<ExportJobDto>>;

public sealed class CancelExportJobCommandHandler : IRequestHandler<CancelExportJobCommand, Result<ExportJobDto>>
{
    private readonly IPanelForgeDbContext _db;

    public CancelExportJobCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ExportJobDto>> Handle(CancelExportJobCommand cmd, CancellationToken ct)
    {
        var job = await _db.ExportJobs.FirstOrDefaultAsync(j => j.Id == cmd.JobId && j.ChapterId == cmd.ChapterId, ct);
        if (job is null) return Result<ExportJobDto>.Failure("Bản xuất không tồn tại.", ResultErrorCodes.NotFound);

        try { job.Cancel(); }
        catch (InvalidOperationException ex) { return Result<ExportJobDto>.Failure(ex.Message, ResultErrorCodes.Conflict); }

        job.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        var pageCount = (await FinalArtwork.PageCountsAsync(_db, [job.SnapshotArtworkIds], ct))[0];
        return Result<ExportJobDto>.Success(ExportJobDto.From(job, pageCount));
    }
}

// ─── Tải file ────────────────────────────────────────────────────────────────

/// <summary>
/// UC-12 (CF5 bước 10): tải file đã xuất qua API có kiểm quyền Workspace (không trả đường dẫn kho, BR-16). Mỗi lần tải được
/// ghi lại (đếm + thời điểm, có audit) nên là command. Editor chỉ tải được file JSON provenance (CF4 4.6, Q4: Editor xem
/// Provenance Report chỉ đọc); file ảnh/PDF/CBZ chỉ Producer.
/// </summary>
public sealed record DownloadExportFileCommand(Guid ChapterId, Guid JobId, Guid UserId) : IRequest<Result<AssetDownloadDto>>;

public sealed class DownloadExportFileCommandHandler : IRequestHandler<DownloadExportFileCommand, Result<AssetDownloadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IWorkspaceAuthorizationService _auth;

    public DownloadExportFileCommandHandler(IPanelForgeDbContext db, IAssetStorage storage, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _storage = storage;
        _auth = auth;
    }

    public async Task<Result<AssetDownloadDto>> Handle(DownloadExportFileCommand cmd, CancellationToken ct)
    {
        var job = await _db.ExportJobs.FirstOrDefaultAsync(j => j.Id == cmd.JobId && j.ChapterId == cmd.ChapterId, ct);
        if (job is null) return Result<AssetDownloadDto>.Failure("Bản xuất không tồn tại.", ResultErrorCodes.NotFound);
        WorkspaceRole[] allowed = job.Format == OpenJsonExport.Format
            ? [WorkspaceRole.Producer, WorkspaceRole.Editor]
            : [WorkspaceRole.Producer];
        if (!await _auth.HasWorkspaceRoleAsync(cmd.UserId, job.WorkspaceId, allowed, ct))
            return Result<AssetDownloadDto>.Failure("Bạn không có quyền tải bản xuất này.", ResultErrorCodes.Forbidden);
        if (job.Status != ExportJobStatus.Completed)
            return Result<AssetDownloadDto>.Failure($"Bản xuất đang ở trạng thái {job.Status}, chưa có file.", ResultErrorCodes.Conflict);

        var stream = await _storage.OpenReadAsync(job.ResultStorageKey!, ct);
        if (stream is null) return Result<AssetDownloadDto>.Failure("Không tìm thấy file trong kho.", ResultErrorCodes.NotFound);

        job.RecordDownload();
        job.UpdatedBy = cmd.UserId.ToString();
        await _db.SaveChangesAsync(ct);
        return Result<AssetDownloadDto>.Success(new AssetDownloadDto(stream, job.ResultContentType!, job.ResultFileName!));
    }
}
