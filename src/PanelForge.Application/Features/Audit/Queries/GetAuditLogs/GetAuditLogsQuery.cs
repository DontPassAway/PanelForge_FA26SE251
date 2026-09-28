using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Audit.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Audit.Common;

namespace PanelForge.Application.Features.Audit.Queries.GetAuditLogs;

/// <summary>
/// UC-15 bước 2: lọc audit theo workspace, user, entity, action và khoảng ngày.
/// </summary>
/// <param name="UserEmail">Tìm gần đúng (contains, không phân biệt hoa thường).</param>
/// <param name="EntityName">Tên entity chính xác, ví dụ "Series", "BibleEntry".</param>
/// <param name="From">Mốc bắt đầu (UTC, bao gồm).</param>
/// <param name="To">Mốc kết thúc (UTC, không bao gồm).</param>
public sealed record GetAuditLogsQuery(
    Guid? WorkspaceId = null,
    string? Action = null,
    int Page = 1,
    int PageSize = 30,
    Guid? UserId = null,
    string? UserEmail = null,
    string? EntityName = null,
    string? EntityId = null,
    DateTime? From = null,
    DateTime? To = null
) : IRequest<Result<AuditLogsPagedResponse>>;

public sealed class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, Result<AuditLogsPagedResponse>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetAuditLogsQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AuditLogsPagedResponse>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        if (request.From.HasValue && request.To.HasValue && request.From.Value >= request.To.Value)
            return Result<AuditLogsPagedResponse>.Failure("Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn 'to'.");

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _dbContext.AuditLogs.AsNoTracking().ApplyFilter(request);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(AuditLogFiltering.ToDto)
            .ToListAsync(cancellationToken);

        return Result<AuditLogsPagedResponse>.Success(new AuditLogsPagedResponse(
            TotalCount: total,
            Page: page,
            PageSize: pageSize,
            Items: items
        ));
    }
}
