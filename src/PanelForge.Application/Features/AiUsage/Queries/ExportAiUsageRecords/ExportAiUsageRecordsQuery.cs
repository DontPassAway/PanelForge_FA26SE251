using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiUsage.Queries.GetAiUsageRecords;
using PanelForge.Application.Features.AiUsage.Common;

namespace PanelForge.Application.Features.AiUsage.Queries.ExportAiUsageRecords;

public sealed record ExportAiUsageRecordsQuery(GetAiUsageRecordsQuery Filter) : IRequest<Result<CsvExport>>
{
    public const int MaxRows = 10_000;
}

public sealed class ExportAiUsageRecordsQueryHandler : IRequestHandler<ExportAiUsageRecordsQuery, Result<CsvExport>>
{
    private readonly IPanelForgeDbContext _dbContext;
    public ExportAiUsageRecordsQueryHandler(IPanelForgeDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<CsvExport>> Handle(ExportAiUsageRecordsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        if (f.From.HasValue && f.To.HasValue && f.From.Value >= f.To.Value)
            return Result<CsvExport>.Failure("Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn 'to'.");

        var rows = await _dbContext.AiUsageRecords.AsNoTracking()
            .ApplyFilter(f)
            .OrderByDescending(r => r.OccurredAtUtc)
            .Take(ExportAiUsageRecordsQuery.MaxRows + 1)
            .Select(AiUsageFiltering.ToDto)
            .ToListAsync(ct);

        var truncated = rows.Count > ExportAiUsageRecordsQuery.MaxRows;
        if (truncated) rows.RemoveAt(rows.Count - 1);

        var content = CsvWriter.Write(
            ["OccurredAtUtc", "WorkspaceId", "UserId", "Feature", "Provider", "Model", "Status",
             "PromptTokens", "CompletionTokens", "TotalTokens", "CostUsd", "DurationMs", "ErrorMessage"],
            rows.Select(r => new object?[]
            {
                r.OccurredAtUtc, r.WorkspaceId, r.UserId, r.Feature, r.Provider, r.Model, r.Status,
                r.PromptTokens, r.CompletionTokens, r.TotalTokens, r.CostUsd, r.DurationMs, r.ErrorMessage
            }));

        return Result<CsvExport>.Success(new CsvExport(
            $"ai-usage-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv", content, rows.Count, truncated));
    }
}
