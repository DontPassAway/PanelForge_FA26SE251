using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.SeriesPipeline.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

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

    /// <summary>
    /// Lưới V3: đổi cấu trúc pipeline (thứ tự, nhánh, vai trò, thêm stage) khi còn ô đang làm dở sẽ làm sai tiền đề của ô đó.
    /// Ô "đang làm dở" = đã giao, chưa duyệt, chưa huỷ. Trả lý do chặn (409) hoặc null.
    /// </summary>
    public static async Task<string?> StructureLockedReasonAsync(IPanelForgeDbContext db, Guid seriesId, CancellationToken ct)
    {
        var active = await db.StageTasks.CountAsync(t => t.SeriesId == seriesId &&
            t.Status != StageTaskStatus.Unassigned && t.Status != StageTaskStatus.Approved && t.Status != StageTaskStatus.Cancelled, ct);
        return active == 0 ? null
            : $"Còn {active} ô trên lưới giao việc đang làm dở; không đổi thứ tự, nhánh, vai trò hay thêm công đoạn được cho tới khi các ô này được duyệt hoặc bỏ gán.";
    }

    /// <summary>Tắt / xoá một stage chỉ an toàn khi stage đó chưa có ô nào đang được giao (ô chưa giao bị huỷ ở lần sinh ô sau).</summary>
    public static async Task<string?> StageInUseOnGridReasonAsync(IPanelForgeDbContext db, Guid stageId, CancellationToken ct)
    {
        var used = await db.StageTasks.AnyAsync(t => t.PipelineStageId == stageId &&
            t.Status != StageTaskStatus.Unassigned && t.Status != StageTaskStatus.Cancelled, ct);
        return used ? "Công đoạn này đã có ô được giao trên lưới; bỏ gán các ô đó trước khi tắt công đoạn." : null;
    }

    /// <summary>Dựng lại transition theo thứ tự stage mới và đồng bộ sang DbContext.</summary>
    public static void RebuildTransitions(IPanelForgeDbContext db, PipelineDefinition pipeline)
    {
        var (removed, added) = pipeline.RebuildDefaultTransitions();
        foreach (var t in removed) db.StageTransitions.Remove(t);
        foreach (var t in added) db.StageTransitions.Add(t);
    }
}
