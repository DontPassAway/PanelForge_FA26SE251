using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Audit.Common;
using PanelForge.Application.Features.Quotas.Common;
using PanelForge.Application.Features.Quotas.Queries.GetAdminResources;
using PanelForge.Application.Features.Quotas.Queries.GetQuotaIncreaseRequests;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Admin.Queries.GetAdminWorkspaceDetail;

/// <summary>
/// GET /api/admin/workspaces/{id} — trang chi tiết Studio cho Admin (OPS-31). Số tài nguyên lấy cùng nguồn với màn
/// Tài nguyên để hai nơi không lệch nhau. Chỉ siêu dữ liệu vận hành, không đọc nội dung sản xuất (BR-07).
/// </summary>
public sealed record GetAdminWorkspaceDetailQuery(Guid WorkspaceId, int ActivityLimit = 20)
    : IRequest<Result<AdminWorkspaceDetailDto>>;

public sealed class GetAdminWorkspaceDetailQueryHandler(IPanelForgeDbContext db, WorkspaceDefaultsSettings defaults)
    : IRequestHandler<GetAdminWorkspaceDetailQuery, Result<AdminWorkspaceDetailDto>>
{
    public async Task<Result<AdminWorkspaceDetailDto>> Handle(GetAdminWorkspaceDetailQuery q, CancellationToken ct)
    {
        var ws = await db.StudioWorkspaces.AsNoTracking()
            .Where(w => w.Id == q.WorkspaceId)
            .Select(w => new { w.Id, w.OwnerId, w.SessionLifetimeMinutes, w.SessionIdleTimeoutMinutes, w.MaxConcurrentSessions })
            .FirstOrDefaultAsync(ct);
        if (ws is null)
            return Result<AdminWorkspaceDetailDto>.Failure("Không tìm thấy Studio.", ResultErrorCodes.NotFound);

        var policy = await QuotaPolicies.GetEffectiveAsync(db, defaults, ct);
        var resources = (await GetAdminResourcesQueryHandler.LoadAsync(db, ws.Id, policy.WarningThresholdPercent, ct)).Single();

        var aiProvider = await db.WorkspaceAiConfigs.AsNoTracking()
            .Where(c => c.WorkspaceId == ws.Id)
            .Select(c => c.Provider)
            .FirstOrDefaultAsync(ct);

        var members = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == ws.Id)
            .OrderBy(m => m.Role).ThenBy(m => m.User.FullName)
            .Select(m => new AdminWorkspaceMemberDto(
                m.UserId, m.User.FullName, m.User.Email, m.Role, m.Status, m.User.IsActive, m.JoinedAt))
            .ToListAsync(ct);

        var series = await db.Series.AsNoTracking()
            .Where(s => s.WorkspaceId == ws.Id)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new AdminWorkspaceSeriesDto(
                s.Id,
                s.Title,
                s.IsCompleted,
                db.Chapters.Count(c => c.SeriesId == s.Id),
                db.Chapters.Count(c => c.SeriesId == s.Id && c.Status == ChapterStatus.HandedOffAndPublished),
                s.CreatedAt))
            .ToListAsync(ct);

        var requests = await GetQuotaIncreaseRequestsQueryHandler
            .ToDtos(db, db.QuotaIncreaseRequests.AsNoTracking()
                .Where(r => r.WorkspaceId == ws.Id)
                .OrderByDescending(r => r.CreatedAt)
                .Take(10))
            .ToListAsync(ct);

        var since = DateTime.UtcNow.AddDays(-30);
        var usage = db.AiUsageRecords.AsNoTracking().Where(r => r.WorkspaceId == ws.Id && r.OccurredAtUtc >= since);
        var ai = new AdminWorkspaceAiActivityDto(
            Tokens: await usage.SumAsync(r => (long?)r.TotalTokens, ct) ?? 0,
            Calls: await usage.CountAsync(ct),
            FailedCalls: await usage.CountAsync(r => r.Status != AiUsageStatus.Succeeded, ct),
            FailedWorkflowRuns: await db.AiWorkflowRuns.AsNoTracking()
                .CountAsync(r => r.WorkspaceId == ws.Id && r.CreatedAt >= since
                    && (r.Status == AiRunStatus.Failed || r.Status == AiRunStatus.Interrupted), ct));

        var activity = await db.AuditLogs.AsNoTracking()
            .Where(a => a.WorkspaceId == ws.Id)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(q.ActivityLimit, 1, 100))
            .Select(AuditLogFiltering.ToDto)
            .ToListAsync(ct);

        return Result<AdminWorkspaceDetailDto>.Success(new AdminWorkspaceDetailDto(
            resources, ws.OwnerId, aiProvider,
            ws.SessionLifetimeMinutes, ws.SessionIdleTimeoutMinutes, ws.MaxConcurrentSessions,
            members, series, requests, ai, activity));
    }
}
