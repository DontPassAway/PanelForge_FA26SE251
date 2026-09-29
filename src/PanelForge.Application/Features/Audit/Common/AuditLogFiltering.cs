using PanelForge.Application.Features.Audit.Models;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Application.Features.Audit.Queries.GetAuditLogs;

namespace PanelForge.Application.Features.Audit.Common;

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
