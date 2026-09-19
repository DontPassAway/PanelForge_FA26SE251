using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Lưu vết toàn bộ lịch sử chuyển trạng thái (Append-Only Workflow Audit Trail) cho từng Trang truyện hoặc Khung tranh.
/// Giúp producer giám sát chính xác điểm nghẽn sản xuất (bottleneck) ở cấp độ panel thay vì chỉ theo dõi file tổng quan.
/// </summary>
public class WorkflowTransitionLog : BaseEntity
{
    public Guid EntityId { get; private set; }
    public WorkflowEntityType EntityType { get; private set; }
    public Guid? FromStageId { get; private set; }
    public Guid ToStageId { get; private set; }
    public Guid TriggeredByUserId { get; private set; }
    public string? Comment { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public PipelineStage? FromStage { get; private set; }
    public PipelineStage ToStage { get; private set; } = default!;

    private WorkflowTransitionLog() { }

    public static WorkflowTransitionLog Create(
        Guid entityId,
        WorkflowEntityType entityType,
        Guid? fromStageId,
        Guid toStageId,
        Guid triggeredByUserId,
        string? comment = null)
    {
        return new WorkflowTransitionLog
        {
            EntityId = entityId,
            EntityType = entityType,
            FromStageId = fromStageId,
            ToStageId = toStageId,
            TriggeredByUserId = triggeredByUserId,
            Comment = comment?.Trim(),
            CreatedAt = DateTime.UtcNow
        };
    }
}
