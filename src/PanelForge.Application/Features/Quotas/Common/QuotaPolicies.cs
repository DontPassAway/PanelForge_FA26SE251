using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Workspaces.Models;

namespace PanelForge.Application.Features.Quotas.Common;

/// <summary>Chính sách đang dùng: bản ghi <c>quota_policies</c> mới nhất, chưa có thì lấy cấu hình server (OPS-20).</summary>
public static class QuotaPolicies
{
    public static async Task<QuotaPolicyDto> GetEffectiveAsync(
        IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults, CancellationToken ct)
    {
        var latest = await db.QuotaPolicies.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.StorageQuotaBytes,
                p.MonthlyTokenQuota,
                p.MonthlyImageQuota,
                p.CreatedAt,
                UpdatedByName = db.Users.Where(u => u.Id == p.ChangedByUserId).Select(u => u.FullName).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        return latest is null
            ? new QuotaPolicyDto(defaults.StorageQuotaBytes, defaults.MonthlyTokenQuota, defaults.MonthlyImageQuota, true, null, null)
            : new QuotaPolicyDto(latest.StorageQuotaBytes, latest.MonthlyTokenQuota, latest.MonthlyImageQuota, false,
                latest.CreatedAt, latest.UpdatedByName);
    }
}
