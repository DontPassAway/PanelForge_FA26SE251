using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Đại diện cho một bước chuyển đổi có điều kiện (Guarded Transition) trong Finite State Machine (FSM).
/// Ràng buộc vai trò được phép chuyển, yêu cầu comment khi từ chối, và phân biệt hướng tiến/lùi (rework).
/// </summary>
public class StageTransition : BaseEntity
{
    public Guid PipelineDefinitionId { get; private set; }
    public Guid FromStageId { get; private set; }
    public Guid ToStageId { get; private set; }
    public string TransitionName { get; private set; } = default!;
    public WorkspaceRole? RequiredRole { get; private set; }
    public bool RequiresComment { get; private set; }
    public bool IsBackwardTransition { get; private set; }

    public PipelineDefinition PipelineDefinition { get; private set; } = default!;
    public PipelineStage FromStage { get; private set; } = default!;
    public PipelineStage ToStage { get; private set; } = default!;

    private StageTransition() { }

    public static StageTransition Create(
        Guid pipelineDefinitionId,
        Guid fromStageId,
        Guid toStageId,
        string transitionName,
        WorkspaceRole? requiredRole = null,
        bool requiresComment = false,
        bool isBackwardTransition = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transitionName);

        if (fromStageId == toStageId)
            throw new ArgumentException("Trạng thái nguồn (FromStageId) và đích (ToStageId) không được trùng nhau.");

        return new StageTransition
        {
            PipelineDefinitionId = pipelineDefinitionId,
            FromStageId = fromStageId,
            ToStageId = toStageId,
            TransitionName = transitionName.Trim(),
            RequiredRole = requiredRole,
            RequiresComment = requiresComment,
            IsBackwardTransition = isBackwardTransition
        };
    }
}
