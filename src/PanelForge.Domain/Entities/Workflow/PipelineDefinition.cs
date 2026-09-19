using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Aggregate Root cho Quy trình sản xuất (Workflow / Pipeline Definition) và Finite State Machine (FSM).
/// Cấu hình danh sách các Stage và các bước chuyển đổi có điều kiện (Guarded Transitions).
/// </summary>
public class PipelineDefinition : BaseEntity
{
    public Guid WorkspaceId { get; private set; }
    public Guid? SeriesId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public StudioWorkspace Workspace { get; private set; } = default!;
    public Series? Series { get; private set; }
    public ICollection<PipelineStage> Stages { get; private set; } = [];
    public ICollection<StageTransition> Transitions { get; private set; } = [];

    private PipelineDefinition() { }

    public static PipelineDefinition Create(
        Guid workspaceId,
        string name,
        string? description = null,
        Guid? seriesId = null,
        bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new PipelineDefinition
        {
            WorkspaceId = workspaceId,
            Name = name.Trim(),
            Description = description?.Trim(),
            SeriesId = seriesId,
            IsDefault = isDefault,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public PipelineStage AddStage(
        string name,
        string slug,
        int stageOrder,
        string? colorCode = null,
        WorkspaceRole? allowedRole = null,
        bool isApprovalGate = false,
        bool isInitial = false,
        bool isTerminal = false,
        int? estimatedDurationDays = null)
    {
        var stage = PipelineStage.Create(
            Id,
            name,
            slug,
            stageOrder,
            colorCode,
            allowedRole,
            isApprovalGate,
            isInitial,
            isTerminal,
            estimatedDurationDays);

        Stages.Add(stage);
        UpdatedAt = DateTime.UtcNow;
        return stage;
    }

    public StageTransition AddTransition(
        Guid fromStageId,
        Guid toStageId,
        string transitionName,
        WorkspaceRole? requiredRole = null,
        bool requiresComment = false,
        bool isBackwardTransition = false)
    {
        var transition = StageTransition.Create(
            Id,
            fromStageId,
            toStageId,
            transitionName,
            requiredRole,
            requiresComment,
            isBackwardTransition);

        Transitions.Add(transition);
        UpdatedAt = DateTime.UtcNow;
        return transition;
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ của bước chuyển trạng thái (FSM Guard Evaluation).
    /// </summary>
    public bool CanTransition(Guid fromStageId, Guid toStageId, WorkspaceRole? userRole)
    {
        var transition = Transitions.FirstOrDefault(t => t.FromStageId == fromStageId && t.ToStageId == toStageId);
        if (transition == null)
            return false;

        // Producer luôn có quyền override nếu cần
        if (userRole == WorkspaceRole.Producer)
            return true;

        if (transition.RequiredRole.HasValue && transition.RequiredRole != userRole)
            return false;

        return true;
    }

    /// <summary>
    /// Factory method khởi tạo cấu hình quy trình Manga chuẩn 8 công đoạn theo đặc tả Capstone.
    /// Script → Thumbnail → Pencil → Ink → Color → Letter → Review → Approved
    /// </summary>
    public static PipelineDefinition CreateDefaultMangaPipeline(Guid workspaceId, Guid? seriesId = null)
    {
        var pipeline = Create(workspaceId, "Standard Manga Pipeline", "Canonical 8-stage manga production pipeline with guarded review gates", seriesId, true);

        // 1. Khởi tạo 8 Stages
        var sScript = pipeline.AddStage("Script", "script", 1, "#6B7280", WorkspaceRole.Writer, isApprovalGate: false, isInitial: true);
        var sThumbnail = pipeline.AddStage("Thumbnail", "thumbnail", 2, "#8B5CF6", WorkspaceRole.Penciler);
        var sPencil = pipeline.AddStage("Pencil", "pencil", 3, "#3B82F6", WorkspaceRole.Penciler);
        var sInk = pipeline.AddStage("Ink", "ink", 4, "#10B981", WorkspaceRole.Inker);
        var sColor = pipeline.AddStage("Color", "color", 5, "#F59E0B", WorkspaceRole.Colorist);
        var sLetter = pipeline.AddStage("Letter", "letter", 6, "#EC4899", WorkspaceRole.Letterer);
        var sReview = pipeline.AddStage("Review", "review", 7, "#EF4444", WorkspaceRole.Reviewer, isApprovalGate: true);
        var sApproved = pipeline.AddStage("Approved", "approved", 8, "#059669", WorkspaceRole.Editor, isApprovalGate: false, isInitial: false, isTerminal: true);

        // 2. Thiết lập các bước chuyển tiếp hợp lệ (Forward Transitions)
        pipeline.AddTransition(sScript.Id, sThumbnail.Id, "Script Completed", WorkspaceRole.Writer);
        pipeline.AddTransition(sThumbnail.Id, sPencil.Id, "Layout Confirmed", WorkspaceRole.Penciler);
        pipeline.AddTransition(sPencil.Id, sInk.Id, "Pencils Finished", WorkspaceRole.Penciler);
        pipeline.AddTransition(sInk.Id, sColor.Id, "Inks Finished", WorkspaceRole.Inker);
        pipeline.AddTransition(sColor.Id, sLetter.Id, "Flats & Colors Finished", WorkspaceRole.Colorist);
        pipeline.AddTransition(sLetter.Id, sReview.Id, "Lettering Complete, Submit for Review", WorkspaceRole.Letterer);

        // 3. Thiết lập Guarded Approval Transition (Chỉ Editor hoặc Producer mới được duyệt)
        pipeline.AddTransition(sReview.Id, sApproved.Id, "Approve Page Submission", WorkspaceRole.Editor);

        // 4. Thiết lập Backward Transitions (Từ Review từ chối về làm lại kèm comment)
        pipeline.AddTransition(sReview.Id, sPencil.Id, "Reject: Redraw Pencil", WorkspaceRole.Editor, requiresComment: true, isBackwardTransition: true);
        pipeline.AddTransition(sReview.Id, sInk.Id, "Reject: Touch Up Inks", WorkspaceRole.Editor, requiresComment: true, isBackwardTransition: true);
        pipeline.AddTransition(sReview.Id, sLetter.Id, "Reject: Adjust Balloon Layout", WorkspaceRole.Editor, requiresComment: true, isBackwardTransition: true);

        return pipeline;
    }
}
