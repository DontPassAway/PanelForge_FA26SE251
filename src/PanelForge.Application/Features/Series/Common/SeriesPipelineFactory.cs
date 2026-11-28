using PanelForge.Domain.Entities.MasterData;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Series.Common;

/// <summary>Dựng PipelineDefinition riêng của Series từ template, dùng chung cho tạo Series và dữ liệu demo.</summary>
public static class SeriesPipelineFactory
{
    public static PipelineDefinition Create(Guid workspaceId, Guid seriesId, string seriesTitle, string formatUpper, PipelineTemplate? template)
    {
        var pipelineName = template != null
            ? $"{seriesTitle} — {template.Name}"
            : $"{seriesTitle} — 7-Stage Standard Pipeline";

        var pipeline = Domain.Entities.Workflow.PipelineDefinition.Create(
            workspaceId: workspaceId,
            name: pipelineName,
            description: template != null ? $"Cloned from template: {template.Code}" : "Default 7-stage production pipeline",
            seriesId: seriesId,
            isDefault: false
        );

        if (template != null && template.Stages.Count > 0)
        {
            // Clone stages từ PipelineTemplate
            foreach (var templateStage in template.Stages.OrderBy(s => s.Order))
            {
                pipeline.AddStage(
                    name: templateStage.Name,
                    slug: templateStage.Code.ToLowerInvariant(),
                    stageOrder: templateStage.Order,
                    colorCode: null,
                    allowedRole: templateStage.RequiredRole,
                    isApprovalGate: templateStage.GateType == "Approval",
                    isInitial: templateStage.Order == template.Stages.Min(s => s.Order),
                    isTerminal: templateStage.Order == template.Stages.Max(s => s.Order)
                );
            }
        }
        else
        {
            // Fallback: công đoạn chuẩn manga trắng đen (F-02 & RF-11: không có Color mặc định).
            // D1 / SCR-12: kịch bản không còn là stage pipeline; vòng đời riêng theo ScriptStatus (D12).
            // Nhóm không dùng công đoạn Thumbnail nữa: bắt đầu từ Pencil.
            pipeline.AddStage("Pencil", "pencil", 1, null, WorkspaceRole.Artist, false, isInitial: true, isTerminal: false);
            // Gate 2 theo trang: duyệt bước cuối mỗi nhánh (trắng đen: Ink; nhánh chữ: Letter)
            pipeline.AddStage("Ink", "ink", 2, null, WorkspaceRole.Artist, isApprovalGate: true, isInitial: false, isTerminal: false);
            pipeline.AddStage("Letter", "letter", 3, null, WorkspaceRole.Letterer, isApprovalGate: true, isInitial: false, isTerminal: false);
            pipeline.AddStage("Review", "review", 4, null, WorkspaceRole.Editor, isApprovalGate: true, isInitial: false, isTerminal: false);
            pipeline.AddStage("Approved", "approved", 5, null, WorkspaceRole.Editor, false, isInitial: false, isTerminal: true);
        }

        // BR-19: sau Pencil, nhánh A (Ink → Color) và nhánh B (Letter) chạy song song; Review chờ cả hai.
        // Template chưa có cột nhánh nên suy theo slug; Producer vẫn đổi được trong cấu hình pipeline.
        foreach (var stage in pipeline.Stages)
            stage.SetTrack(stage.Slug?.ToLowerInvariant() switch
            {
                "ink" or "color" => "A",
                "letter" or "lettering" => "B",
                _ => null
            });

        // F-02 / RF-11: Manga trắng đen → Color tắt mặc định (template chuẩn vẫn có Color); Webtoon/Comic giữ bật.
        // Cổng duyệt của Color chuyển sang Ink để nhánh tranh vẫn qua Editor theo trang (Gate 2).
        // Producer bật lại được trong cấu hình pipeline.
        if (formatUpper.Contains("MANGA"))
            foreach (var stage in pipeline.Stages.Where(s => s.Slug == "color").ToList())
            {
                stage.SetEnabled(false);
                pipeline.MoveGateOffDisabledStage(stage);
            }

        // Spec V2.6 chỉ có Gate 2 (Editor duyệt từng trang ở bước cuối mỗi nhánh) và Gate 3 (Producer chốt chương).
        // Stage "Review" cấp chương bắt Producer duyệt thêm một lần nên tắt mặc định; Producer bật lại được nếu cần.
        foreach (var stage in pipeline.Stages.Where(s => s.Slug == "review" && !s.IsInitial && !s.IsTerminal).ToList())
            stage.SetEnabled(false);

        // Sinh transition mặc định
        pipeline.RebuildDefaultTransitions();

        return pipeline;
    }
}
