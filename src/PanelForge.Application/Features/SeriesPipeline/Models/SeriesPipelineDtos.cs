using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.SeriesPipeline.Models;

public sealed record SeriesPipelineDto(
    Guid Id,
    Guid SeriesId,
    string Name,
    string? Description,
    IReadOnlyList<PipelineStageDto> Stages,
    IReadOnlyList<StageTransitionDto> Transitions);

public sealed record PipelineStageDto(
    Guid Id,
    string Name,
    string Slug,
    int StageOrder,
    string? ColorCode,
    WorkspaceRole? AllowedRole,
    bool IsApprovalGate,
    bool IsInitial,
    bool IsTerminal,
    int? EstimatedDurationDays);

public sealed record StageTransitionDto(
    Guid Id,
    Guid FromStageId,
    Guid ToStageId,
    string TransitionName,
    WorkspaceRole? RequiredRole,
    bool RequiresComment,
    bool IsBackwardTransition);

// ─── Requests ─────────────────────────────────────────────────────────────────

/// <param name="Slug">Tùy chọn; bỏ trống thì sinh từ Name ("Tô màu" → "to-mau").</param>
/// <param name="Position">Vị trí 1-based để chèn; null = cuối pipeline.</param>
public sealed record AddPipelineStageRequest(
    string Name,
    string? Slug = null,
    int? Position = null,
    string? ColorCode = null,
    WorkspaceRole? AllowedRole = null,
    bool IsApprovalGate = false,
    int? EstimatedDurationDays = null);

public sealed record UpdatePipelineStageRequest(
    string Name,
    string? ColorCode = null,
    WorkspaceRole? AllowedRole = null,
    bool IsApprovalGate = false,
    int? EstimatedDurationDays = null);

/// <param name="StageIds">Toàn bộ stage id của pipeline theo thứ tự mới.</param>
public sealed record ReorderPipelineStagesRequest(IReadOnlyList<Guid> StageIds);

internal static class SeriesPipelineMappings
{
    public static SeriesPipelineDto ToDto(this PipelineDefinition pipeline, Guid seriesId)
    {
        var order = pipeline.Stages.ToDictionary(s => s.Id, s => s.StageOrder);

        return new SeriesPipelineDto(
            Id: pipeline.Id,
            SeriesId: seriesId,
            Name: pipeline.Name,
            Description: pipeline.Description,
            Stages: pipeline.GetOrderedStages()
                .Select(s => new PipelineStageDto(
                    s.Id, s.Name, s.Slug, s.StageOrder, s.ColorCode, s.AllowedRole,
                    s.IsApprovalGate, s.IsInitial, s.IsTerminal, s.EstimatedDurationDays))
                .ToList(),
            Transitions: pipeline.Transitions
                .OrderBy(t => order.GetValueOrDefault(t.FromStageId))
                .ThenBy(t => t.IsBackwardTransition)
                .ThenByDescending(t => order.GetValueOrDefault(t.ToStageId))
                .Select(t => new StageTransitionDto(
                    t.Id, t.FromStageId, t.ToStageId, t.TransitionName, t.RequiredRole,
                    t.RequiresComment, t.IsBackwardTransition))
                .ToList());
    }
}
