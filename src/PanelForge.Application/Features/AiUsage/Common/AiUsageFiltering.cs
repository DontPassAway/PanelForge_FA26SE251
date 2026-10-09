using PanelForge.Domain.Entities.Ai;
using PanelForge.Application.Features.AiUsage.Models;
using PanelForge.Application.Features.AiUsage.Queries.GetAiUsageRecords;

namespace PanelForge.Application.Features.AiUsage.Common;

internal static class AiUsageFiltering
{
    public static readonly System.Linq.Expressions.Expression<Func<AiUsageRecord, AiUsageRecordDto>> ToDto = r => new AiUsageRecordDto(
        r.Id, r.WorkspaceId, r.UserId, r.Feature, r.Provider, r.Model, r.PromptTokens, r.CompletionTokens,
        r.TotalTokens, r.CostUsd, r.Status, r.ErrorMessage, r.DurationMs, r.OccurredAtUtc);

    public static IQueryable<AiUsageRecord> ApplyFilter(this IQueryable<AiUsageRecord> query, GetAiUsageRecordsQuery f)
    {
        if (f.WorkspaceId.HasValue) query = query.Where(r => r.WorkspaceId == f.WorkspaceId.Value);
        if (f.UserId.HasValue) query = query.Where(r => r.UserId == f.UserId.Value);
        if (!string.IsNullOrWhiteSpace(f.Feature))
        {
            var feature = f.Feature.Trim();
            query = query.Where(r => r.Feature == feature);
        }
        if (f.Status.HasValue) query = query.Where(r => r.Status == f.Status.Value);
        if (f.From.HasValue)
        {
            var from = DateTime.SpecifyKind(f.From.Value, DateTimeKind.Utc);
            query = query.Where(r => r.OccurredAtUtc >= from);
        }
        if (f.To.HasValue)
        {
            var to = DateTime.SpecifyKind(f.To.Value, DateTimeKind.Utc);
            query = query.Where(r => r.OccurredAtUtc < to);
        }
        return query;
    }

    /// <summary>Mặc định kỳ báo cáo = tháng hiện tại (khớp với hạn mức theo tháng).</summary>
    public static (DateTime From, DateTime To, string? Error) ResolvePeriod(DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;
        var resolvedFrom = from.HasValue
            ? DateTime.SpecifyKind(from.Value, DateTimeKind.Utc)
            : new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var resolvedTo = to.HasValue ? DateTime.SpecifyKind(to.Value, DateTimeKind.Utc) : now.AddSeconds(1);

        return resolvedFrom >= resolvedTo
            ? (resolvedFrom, resolvedTo, "Khoảng thời gian không hợp lệ: 'from' phải nhỏ hơn 'to'.")
            : (resolvedFrom, resolvedTo, null);
    }
}
