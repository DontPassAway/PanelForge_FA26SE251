using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Audit.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Audit.Queries;

public sealed record GetAuditLogsQuery(
    Guid? WorkspaceId = null,
    string? Action = null,
    int Page = 1,
    int PageSize = 30
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
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (request.WorkspaceId.HasValue)
        {
            query = query.Where(a => a.WorkspaceId == request.WorkspaceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var actionLower = request.Action.Trim().ToLower();
            query = query.Where(a => a.Action.ToLower().Contains(actionLower));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(
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
                a.Details
            ))
            .ToListAsync(cancellationToken);

        return Result<AuditLogsPagedResponse>.Success(new AuditLogsPagedResponse(
            TotalCount: total,
            Page: page,
            PageSize: pageSize,
            Items: items
        ));
    }
}
