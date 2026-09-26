using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Audit.Queries;

/// <summary>UC-15 bước 4: export view audit đã lọc ra CSV (tối đa <see cref="MaxRows"/> dòng mới nhất).</summary>
public sealed record ExportAuditLogsQuery(GetAuditLogsQuery Filter) : IRequest<Result<CsvExport>>
{
    public const int MaxRows = 10_000;
}

public sealed class ExportAuditLogsQueryHandler : IRequestHandler<ExportAuditLogsQuery, Result<CsvExport>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public ExportAuditLogsQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<CsvExport>> Handle(ExportAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var f = request.Filter;
        if (f.From.HasValue && f.To.HasValue && f.From.Value >= f.To.Value)
            return Result<CsvExport>.Failure("Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn 'to'.");

        var rows = await _dbContext.AuditLogs.AsNoTracking()
            .ApplyFilter(f)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(ExportAuditLogsQuery.MaxRows + 1)
            .Select(AuditLogFiltering.ToDto)
            .ToListAsync(cancellationToken);

        var truncated = rows.Count > ExportAuditLogsQuery.MaxRows;
        if (truncated) rows.RemoveAt(rows.Count - 1);

        var content = CsvWriter.Write(
            ["TimestampUtc", "Action", "EntityName", "EntityId", "WorkspaceId", "UserId", "UserEmail", "IpAddress", "Details", "ChangesJson"],
            rows.Select(r => new object?[]
            {
                r.TimestampUtc, r.Action, r.EntityName, r.EntityId, r.WorkspaceId, r.UserId,
                r.UserEmail, r.IpAddress, r.Details, r.ChangesJson
            }));

        return Result<CsvExport>.Success(new CsvExport(
            FileName: $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            Content: content,
            RowCount: rows.Count,
            Truncated: truncated));
    }
}
