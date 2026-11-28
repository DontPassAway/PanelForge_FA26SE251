using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Models;

namespace PanelForge.Application.Features.Quotas.Commands.ReconcileStorage;

/// <summary>
/// POST /api/admin/storage/reconcile — Admin đối soát kho file với DB (OPS-22).
/// Apply = false chỉ báo cáo; Apply = true ghi lại UsedStorageBytes = dung lượng file còn được dùng (audit tự ghi, BR-18).
/// Lệnh này không xoá file; xoá file mồ côi là lệnh riêng <c>DeleteOrphanFilesCommand</c>.
/// </summary>
public sealed record ReconcileStorageCommand(bool Apply) : IRequest<Result<StorageReconcileReportDto>>;

public sealed class ReconcileStorageCommandHandler(IPanelForgeDbContext db, IAssetStorage storage)
    : IRequestHandler<ReconcileStorageCommand, Result<StorageReconcileReportDto>>
{
    public const int MaxOrphanSamples = 200;
    public const string ConcurrentMessage = "Có Studio vừa tải file lên trong lúc đối soát. Chạy lại để lấy số mới.";

    public async Task<Result<StorageReconcileReportDto>> Handle(ReconcileStorageCommand command, CancellationToken ct)
    {
        var scan = await StorageScan.RunAsync(db, storage, ct);
        var recordedBefore = scan.Workspaces.ToDictionary(w => w.Id, w => w.UsedStorageBytes);

        if (command.Apply)
        {
            foreach (var w in scan.Workspaces.Where(w => w.UsedStorageBytes != scan.Rows[w.Id].Computed))
                w.UpdateUsedStorage(scan.Rows[w.Id].Computed);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<StorageReconcileReportDto>.Failure(ConcurrentMessage, ResultErrorCodes.Conflict);
            }
        }

        return Result<StorageReconcileReportDto>.Success(ToReport(scan, command.Apply, recordedBefore));
    }

    /// <param name="recordedBefore">Số đang ghi lúc quét, trước khi Apply.</param>
    internal static StorageReconcileReportDto ToReport(
        StorageScan.Result scan, bool applied, IReadOnlyDictionary<Guid, long> recordedBefore)
    {
        var rows = scan.Workspaces.Select(w =>
        {
            var t = scan.Rows[w.Id];
            var recorded = recordedBefore.GetValueOrDefault(w.Id, w.UsedStorageBytes);
            return new StorageReconcileWorkspaceDto(
                w.Id, w.Name, recorded, t.Computed, t.OrphanFiles, t.OrphanBytes, t.Derived, t.Missing);
        }).ToList();
        var samples = scan.Orphans.Take(MaxOrphanSamples)
            .Select(o => new StorageOrphanFileDto(o.Key, o.SizeBytes, o.Owner, o.LastModifiedUtc))
            .ToList();
        return new StorageReconcileReportDto(
            DateTime.UtcNow, applied, scan.ScannedFiles, scan.ScannedBytes, rows,
            scan.UnattributedFiles, scan.UnattributedBytes, samples, scan.Orphans.Count > samples.Count);
    }
}
