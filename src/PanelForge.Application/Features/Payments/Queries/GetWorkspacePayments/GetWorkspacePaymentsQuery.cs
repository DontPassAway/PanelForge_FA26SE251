using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Payments.Queries.GetWorkspacePayments;

public sealed record GetWorkspacePaymentsQuery(
    Guid WorkspaceId,
    string? Status = null,
    Guid? SeriesId = null,
    string? Search = null,
    int PageIndex = 1,
    int PageSize = 20
) : IRequest<Result<WorkspacePaymentsResponseDto>>;

public sealed class GetWorkspacePaymentsQueryHandler : IRequestHandler<GetWorkspacePaymentsQuery, Result<WorkspacePaymentsResponseDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetWorkspacePaymentsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<WorkspacePaymentsResponseDto>> Handle(GetWorkspacePaymentsQuery q, CancellationToken ct)
    {
        var pageIndex = Math.Max(1, q.PageIndex);
        var pageSize = Math.Clamp(q.PageSize, 1, 100);

        // 1. KPI Summary của Studio
        var totalRevenue = await _db.PaymentTransactions
            .Where(t => t.WorkspaceId == q.WorkspaceId && t.Status == PaymentTransactionStatus.Approved && !t.IsDeleted)
            .SumAsync(t => (decimal?)t.AmountVnd, ct) ?? 0m;

        var pendingCount = await _db.PaymentTransactions
            .CountAsync(t => t.WorkspaceId == q.WorkspaceId && t.Status == PaymentTransactionStatus.Pending && !t.IsDeleted, ct);

        var totalUnlocked = await _db.ChapterEntitlements
            .CountAsync(e => e.WorkspaceId == q.WorkspaceId && !e.IsDeleted, ct);

        var paidTxCount = await _db.PaymentTransactions
            .CountAsync(t => t.WorkspaceId == q.WorkspaceId && t.Status == PaymentTransactionStatus.Approved && !t.IsDeleted, ct);

        var summary = new WorkspacePaymentsSummaryDto(
            TotalRevenueVnd: totalRevenue,
            PendingCount: pendingCount,
            TotalUnlockedChapters: totalUnlocked,
            PaidTransactionsCount: paidTxCount
        );

        // 2. Query danh sách giao dịch
        var query = _db.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.WorkspaceId == q.WorkspaceId && !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(q.Status) && !q.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<PaymentTransactionStatus>(q.Status, true, out var parsedStatus))
            {
                query = query.Where(t => t.Status == parsedStatus);
            }
        }

        if (q.SeriesId.HasValue)
        {
            query = query.Where(t => t.SeriesId == q.SeriesId.Value);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim().ToLower();
            query = query.Where(t =>
                t.OrderCode.ToLower().Contains(search) ||
                t.User.FullName.ToLower().Contains(search) ||
                t.User.Email.ToLower().Contains(search));
        }

        var totalItems = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new WorkspacePaymentItemDto(
                t.Id,
                t.OrderCode,
                t.User.FullName,
                t.User.Email,
                t.User.AvatarUrl,
                t.Type.ToString(),
                t.SeriesId,
                t.Series != null ? t.Series.Title : null,
                t.ChapterId,
                t.Chapter != null ? (decimal?)t.Chapter.ChapterNumber : null,
                t.Chapter != null ? t.Chapter.Title : null,
                t.AmountVnd,
                t.Status.ToString(),
                t.QrImageUrl,
                t.CreatedAt,
                t.ConfirmedAt,
                t.ConfirmedByUser != null ? t.ConfirmedByUser.FullName : null,
                t.RejectionReason
            ))
            .ToListAsync(ct);

        var pagedResult = new PagedResult<WorkspacePaymentItemDto>(
            items,
            totalItems,
            pageIndex,
            pageSize,
            (int)Math.Ceiling(totalItems / (double)pageSize)
        );

        return Result<WorkspacePaymentsResponseDto>.Success(new WorkspacePaymentsResponseDto(summary, pagedResult));
    }
}

