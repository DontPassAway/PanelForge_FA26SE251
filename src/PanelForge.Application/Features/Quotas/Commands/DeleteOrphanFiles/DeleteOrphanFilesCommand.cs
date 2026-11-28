using MediatR;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.Quotas.Commands.DeleteOrphanFiles;

/// <summary>
/// POST /api/admin/storage/orphans/delete — Admin xoá file mồ côi sau khi xem báo cáo đối soát (OPS-22, C6).
/// Không khôi phục được, nên chỉ xoá file:
/// - thuộc Studio đang hoạt động (file không rõ chủ có thể của môi trường khác dùng chung bucket, hoặc của Studio đã xoá mềm);
/// - cũ hơn <see cref="MinAgeDays"/> ngày (file mới có thể là upload đang chờ ghi DB);
/// - vẫn không có bản ghi nào trỏ tới khi đọc lại DB ngay trước lúc xoá.
/// Chỉ xoá file, không đụng bản ghi lịch sử. Mỗi Studio có một dòng audit (BR-18).
/// </summary>
public sealed record DeleteOrphanFilesCommand(Guid AdminId) : IRequest<Result<DeleteOrphanFilesResultDto>>;

public sealed class DeleteOrphanFilesCommandHandler(IPanelForgeDbContext db, IAssetStorage storage)
    : IRequestHandler<DeleteOrphanFilesCommand, Result<DeleteOrphanFilesResultDto>>
{
    public const int MinAgeDays = 7;

    public async Task<Result<DeleteOrphanFilesResultDto>> Handle(DeleteOrphanFilesCommand command, CancellationToken ct)
    {
        var scan = await StorageScan.RunAsync(db, storage, ct);
        var cutoff = DateTime.UtcNow.AddDays(-MinAgeDays);

        var owned = scan.Orphans.Where(o => o.Owner is not null).ToList();
        var old = owned.Where(o => o.LastModifiedUtc <= cutoff).ToList();
        var stillReferenced = await StorageScan.StillReferencedAsync(db, old.Select(o => o.Key), ct);

        int deleted = 0, failed = 0;
        long deletedBytes = 0;
        var perWorkspace = new Dictionary<Guid, (int Files, long Bytes)>();
        foreach (var orphan in old.Where(o => !stillReferenced.Contains(o.Key)))
        {
            try
            {
                await storage.DeleteAsync(orphan.Key, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Một file lỗi không chặn các file còn lại; số lỗi trả về cho Admin để chạy lại.
                failed++;
                continue;
            }
            deleted++;
            deletedBytes += orphan.SizeBytes;
            var current = perWorkspace.GetValueOrDefault(orphan.Owner!.Value);
            perWorkspace[orphan.Owner.Value] = (current.Files + 1, current.Bytes + orphan.SizeBytes);
        }

        foreach (var (workspaceId, (files, bytes)) in perWorkspace)
            db.AuditLogs.Add(AuditLog.Create(
                action: "DELETE_ORPHAN_FILES",
                entityName: nameof(StudioWorkspace),
                entityId: workspaceId.ToString(),
                workspaceId: workspaceId,
                userId: command.AdminId,
                details: $"Admin xoá {files} file mồ côi ({bytes} byte) khỏi kho (OPS-22)."));
        if (perWorkspace.Count > 0) await db.SaveChangesAsync(ct);

        return Result<DeleteOrphanFilesResultDto>.Success(new DeleteOrphanFilesResultDto(
            deleted,
            deletedBytes,
            SkippedRecentFiles: owned.Count - old.Count,
            SkippedUnattributedFiles: scan.Orphans.Count - owned.Count,
            SkippedReferencedFiles: stillReferenced.Count,
            FailedFiles: failed,
            MinAgeDays));
    }
}
