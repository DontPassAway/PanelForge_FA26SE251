using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Quotas.Queries.GetAdminResources;

/// <summary>
/// GET /api/admin/resources — mỗi Studio một dòng: dung lượng, token và ảnh AI đã dùng so với mức cấp,
/// số yêu cầu tăng hạn mức đang chờ (OPS-23). Chỉ số liệu vận hành, không đọc nội dung sản xuất (BR-07).
/// </summary>
public sealed record GetAdminResourcesQuery : IRequest<Result<IReadOnlyList<AdminWorkspaceResourceDto>>>;

public sealed class GetAdminResourcesQueryHandler(IPanelForgeDbContext db)
    : IRequestHandler<GetAdminResourcesQuery, Result<IReadOnlyList<AdminWorkspaceResourceDto>>>
{
    public async Task<Result<IReadOnlyList<AdminWorkspaceResourceDto>>> Handle(GetAdminResourcesQuery q, CancellationToken ct)
        => Result<IReadOnlyList<AdminWorkspaceResourceDto>>.Success(await LoadAsync(db, null, ct));

    internal static async Task<List<AdminWorkspaceResourceDto>> LoadAsync(IPanelForgeDbContext db, Guid? workspaceId, CancellationToken ct)
    {
        var workspaces = db.StudioWorkspaces.AsNoTracking();
        if (workspaceId is { } id) workspaces = workspaces.Where(w => w.Id == id);

        var rows = await workspaces
            .OrderBy(w => w.Name)
            .Select(w => new
            {
                w.Id,
                w.Name,
                OwnerName = w.Owner.FullName,
                OwnerEmail = w.Owner.Email,
                w.StorageQuotaBytes,
                w.UsedStorageBytes,
                Ai = db.WorkspaceAiConfigs.Where(c => c.WorkspaceId == w.Id)
                    .Select(c => new { c.IsEnabled, c.MonthlyTokenQuota, c.UsedTokensCurrentMonth, c.MonthlyImageQuota, c.UsedImagesCurrentMonth })
                    .FirstOrDefault(),
                Pending = db.QuotaIncreaseRequests.Count(r => r.WorkspaceId == w.Id && r.Status == QuotaIncreaseRequestStatus.Pending)
            })
            .ToListAsync(ct);

        return rows.Select(r => new AdminWorkspaceResourceDto(
            r.Id,
            r.Name,
            r.OwnerName,
            r.OwnerEmail,
            r.StorageQuotaBytes,
            r.UsedStorageBytes,
            HasAiConfig: r.Ai is not null,
            AiEnabled: r.Ai?.IsEnabled ?? false,
            MonthlyTokenQuota: r.Ai?.MonthlyTokenQuota ?? 0,
            UsedTokensCurrentMonth: r.Ai?.UsedTokensCurrentMonth ?? 0,
            MonthlyImageQuota: r.Ai?.MonthlyImageQuota ?? 0,
            UsedImagesCurrentMonth: r.Ai?.UsedImagesCurrentMonth ?? 0,
            PendingRequestCount: r.Pending)).ToList();
    }
}
