using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;

namespace PanelForge.Application.Features.SeriesPipeline.Common;

// UC-03 bước 3: Producer cấu hình pipeline stage và vai trò của từng stage cho Series.
// Pipeline của Series là bản sao độc lập của PipelineTemplate (CreateSeriesCommand), nên sửa ở đây
// không ảnh hưởng template của Admin hay Series khác. Quyền Producer được kiểm tra ở RequireWorkspaceRole.

internal static class SeriesPipelineStore
{
    public static async Task<PipelineDefinition?> LoadAsync(IPanelForgeDbContext db, Guid seriesId, CancellationToken ct)
    {
        var pipelineId = await db.Series
            .Where(s => s.Id == seriesId)
            .Select(s => s.PipelineDefinitionId)
            .FirstOrDefaultAsync(ct);

        if (pipelineId is null) return null;

        return await db.PipelineDefinitions
            .Include(p => p.Stages)
            .Include(p => p.Transitions)
            .FirstOrDefaultAsync(p => p.Id == pipelineId.Value, ct);
    }

    public static Result<SeriesPipelineDto> NotFound(Guid seriesId)
        => Result<SeriesPipelineDto>.Failure($"Series {seriesId} không tồn tại hoặc chưa có pipeline.", ResultErrorCodes.NotFound);

    /// <summary>Dựng lại transition theo thứ tự stage mới và đồng bộ sang DbContext.</summary>
    public static void RebuildTransitions(IPanelForgeDbContext db, PipelineDefinition pipeline)
    {
        var (removed, added) = pipeline.RebuildDefaultTransitions();
        foreach (var t in removed) db.StageTransitions.Remove(t);
        foreach (var t in added) db.StageTransitions.Add(t);
    }
}
