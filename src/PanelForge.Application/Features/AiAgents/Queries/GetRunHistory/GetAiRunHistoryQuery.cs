using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Queries.GetRunHistory;

/// <summary>
/// Lịch sử tất cả AI runs của một Series, phân trang, lọc theo chapter/scene/status/workflowType.
/// Trả DTO tóm tắt (không kèm steps/suggestions chi tiết — dùng GET /runs/{runId} để xem chi tiết).
/// </summary>
public sealed record GetAiRunHistoryQuery(
    Guid SeriesId,
    Guid UserId,
    Guid? ChapterId = null,
    Guid? SceneId = null,
    AiRunStatus? Status = null,
    AiWorkflowType? WorkflowType = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<Result<AiRunHistoryResponse>>;

// ── DTOs ───────────────────────────────────────────────────────────────

/// <summary>DTO tóm tắt cho danh sách — kèm đếm step/suggestion nhanh.</summary>
public sealed record AiRunSummaryDto(
    Guid Id,
    Guid ChapterId,
    Guid? SceneId,
    Guid RequestedByUserId,
    AiRunStatus Status,
    AiWorkflowType WorkflowType,
    string Provider,
    string Model,
    string Brief,
    int RevisionRounds,
    int MaxRevisionRounds,
    long BaseChapterVersion,
    string? ErrorCode,
    DateTime CreatedAt,
    DateTime LastActivityAtUtc,
    DateTime? CompletedAtUtc,
    int StepCount,
    int SuggestionCount,
    int AcceptedCount,
    int RejectedCount);

public sealed record AiRunHistoryResponse(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<AiRunSummaryDto> Items);

// ── Handler ────────────────────────────────────────────────────────────

public sealed class GetAiRunHistoryQueryHandler
    : IRequestHandler<GetAiRunHistoryQuery, Result<AiRunHistoryResponse>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public GetAiRunHistoryQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<AiRunHistoryResponse>> Handle(GetAiRunHistoryQuery q, CancellationToken ct)
    {
        // ── 1. Kiểm tra quyền: phải là thành viên Workspace chứa Series ──
        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, q.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<AiRunHistoryResponse>("Series không tồn tại.", ResultErrorCodes.NotFound);
        if (!await _auth.IsMemberAsync(q.UserId, workspaceId.Value, ct))
            return AiRunHelpers.Fail<AiRunHistoryResponse>("Bạn không phải thành viên.", ResultErrorCodes.Forbidden);

        // ── 2. Chuẩn hoá paging ──
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, 50);

        // ── 3. Build query với filter ──
        var query = _db.AiWorkflowRuns.AsNoTracking()
            .Where(r => r.SeriesId == q.SeriesId);

        if (q.ChapterId.HasValue)
            query = query.Where(r => r.ChapterId == q.ChapterId.Value);
        if (q.SceneId.HasValue)
            query = query.Where(r => r.SceneId == q.SceneId.Value);
        if (q.Status.HasValue)
            query = query.Where(r => r.Status == q.Status.Value);
        if (q.WorkflowType.HasValue)
            query = query.Where(r => r.WorkflowType == q.WorkflowType.Value);

        // ── 4. Đếm tổng + lấy trang ──
        var total = await query.CountAsync(ct);

        var runs = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // ── 5. Batch đếm steps & suggestions cho mỗi run ──
        var runIds = runs.Select(r => r.Id).ToList();

        var stepCounts = await _db.AiAgentSteps.AsNoTracking()
            .Where(s => runIds.Contains(s.RunId))
            .GroupBy(s => s.RunId)
            .Select(g => new { RunId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RunId, g => g.Count, ct);

        var suggestionStats = await _db.AiSuggestions.AsNoTracking()
            .Where(s => runIds.Contains(s.RunId))
            .GroupBy(s => s.RunId)
            .Select(g => new
            {
                RunId = g.Key,
                Total = g.Count(),
                Accepted = g.Count(s => s.Status == AiSuggestionStatus.Accepted),
                Rejected = g.Count(s => s.Status == AiSuggestionStatus.Rejected)
            })
            .ToDictionaryAsync(g => g.RunId, ct);

        // ── 6. Map sang DTO ──
        var items = runs.Select(r =>
        {
            suggestionStats.TryGetValue(r.Id, out var stats);
            return new AiRunSummaryDto(
                r.Id, r.ChapterId, r.SceneId, r.RequestedByUserId,
                r.Status, r.WorkflowType, r.Provider, r.Model, r.Brief,
                r.RevisionRounds, r.MaxRevisionRounds, r.BaseChapterVersion,
                r.ErrorCode, r.CreatedAt, r.LastActivityAtUtc, r.CompletedAtUtc,
                stepCounts.GetValueOrDefault(r.Id, 0),
                stats?.Total ?? 0,
                stats?.Accepted ?? 0,
                stats?.Rejected ?? 0);
        }).ToList();

        return Result<AiRunHistoryResponse>.Success(
            new AiRunHistoryResponse(total, page, pageSize, items));
    }
}
