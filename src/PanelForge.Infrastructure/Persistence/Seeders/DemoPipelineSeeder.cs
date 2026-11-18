using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.Series.Common;

namespace PanelForge.Infrastructure.Persistence.Seeders;

/// <summary>Bổ sung Pipeline cho Series demo được seed trực tiếp. Chạy lặp không tạo trùng, chỉ dùng ở Development.</summary>
public static class DemoPipelineSeeder
{
    private static readonly Guid[] DemoSeriesIds = [PaymentDemoSeeder.MacKiemLucSeriesId];

    /// <returns>Số Series vừa được bổ sung Pipeline.</returns>
    public static async Task<int> EnsureAsync(
        PanelForgeDbContext db, CancellationToken ct = default, IReadOnlyCollection<Guid>? seriesIds = null)
    {
        var ids = seriesIds ?? DemoSeriesIds;
        var missing = await db.Series.IgnoreQueryFilters()
            .Where(s => ids.Contains(s.Id) && s.PipelineDefinitionId == null).ToListAsync(ct);
        if (missing.Count == 0) return 0;

        var template = await db.PipelineTemplates.Include(t => t.Stages)
            .FirstOrDefaultAsync(t => t.IsDefault && t.IsActive, ct);
        foreach (var series in missing)
        {
            var pipeline = SeriesPipelineFactory.Create(series.WorkspaceId, series.Id, series.Title,
                (series.Format ?? string.Empty).ToUpperInvariant(), template);
            series.SetPipelineDefinition(pipeline.Id);
            db.PipelineDefinitions.Add(pipeline);
        }
        await db.SaveChangesAsync(ct);
        return missing.Count;
    }
}
