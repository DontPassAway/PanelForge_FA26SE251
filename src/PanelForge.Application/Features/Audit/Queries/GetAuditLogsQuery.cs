using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Audit.Models;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;

namespace PanelForge.Application.Features.Audit.Queries;

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

internal static class AuditLogFiltering
{
    public static readonly System.Linq.Expressions.Expression<Func<AuditLog, AuditLogDto>> ToDto = a => new AuditLogDto(
        a.Id,
        a.WorkspaceId,
        a.UserId,
        a.UserEmail,
        a.Action,
        a.EntityName,
        a.EntityId,
        a.ChangesJson,
        a.TimestampUtc,
        a.IpAddress,
        a.Details);

    public static IQueryable<AuditLog> ApplyFilter(this IQueryable<AuditLog> query, GetAuditLogsQuery f)
    {
        if (f.WorkspaceId.HasValue)
            query = query.Where(a => a.WorkspaceId == f.WorkspaceId.Value);

        if (f.UserId.HasValue)
            query = query.Where(a => a.UserId == f.UserId.Value);

        if (!string.IsNullOrWhiteSpace(f.UserEmail))
        {
            var email = f.UserEmail.Trim().ToLower();
            query = query.Where(a => a.UserEmail != null && a.UserEmail.ToLower().Contains(email));
        }

        if (!string.IsNullOrWhiteSpace(f.EntityName))
        {
            var entityName = f.EntityName.Trim();
            query = query.Where(a => a.EntityName == entityName);
        }

        if (!string.IsNullOrWhiteSpace(f.EntityId))
        {
            var entityId = f.EntityId.Trim();
            query = query.Where(a => a.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(f.Action))
        {
            var actionLower = f.Action.Trim().ToLower();
            query = query.Where(a => a.Action.ToLower().Contains(actionLower));
        }

        if (f.From.HasValue)
        {
            var from = DateTime.SpecifyKind(f.From.Value, DateTimeKind.Utc);
            query = query.Where(a => a.TimestampUtc >= from);
        }

        if (f.To.HasValue)
        {
            var to = DateTime.SpecifyKind(f.To.Value, DateTimeKind.Utc);
            query = query.Where(a => a.TimestampUtc < to);
        }

        return query;
    }
}
