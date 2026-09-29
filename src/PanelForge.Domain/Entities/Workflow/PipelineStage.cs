using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Workflow;

/// <summary>
/// Đại diện cho một bước/giai đoạn trong quy trình sản xuất (Script, Thumbnail, Pencil, Ink, Color, Letter, Review, Approved).
/// </summary>
public class PipelineStage : BaseEntity
{
    public Guid PipelineDefinitionId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public int StageOrder { get; private set; }
    public string? ColorCode { get; private set; }
    public WorkspaceRole? AllowedRole { get; private set; }
    public bool IsApprovalGate { get; private set; }
    public bool IsInitial { get; private set; }
    public bool IsTerminal { get; private set; }
    public int? EstimatedDurationDays { get; private set; }

    public PipelineDefinition PipelineDefinition { get; private set; } = default!;
    public ICollection<StageTransition> OutgoingTransitions { get; private set; } = [];
    public ICollection<StageTransition> IncomingTransitions { get; private set; } = [];
    public ICollection<Assignment> Assignments { get; private set; } = [];

    private PipelineStage() { }

    public static PipelineStage Create(
        Guid pipelineDefinitionId,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateLengths(name, colorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stageOrder);

        return new PipelineStage
        {
            PipelineDefinitionId = pipelineDefinitionId,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            StageOrder = stageOrder,
            ColorCode = colorCode?.Trim(),
            AllowedRole = allowedRole,
            IsApprovalGate = isApprovalGate,
            IsInitial = isInitial,
            IsTerminal = isTerminal,
            EstimatedDurationDays = estimatedDurationDays
        };
    }

    public void UpdateDetails(
        string name,
        string? colorCode,
        WorkspaceRole? allowedRole,
        bool isApprovalGate,
        int? estimatedDurationDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateLengths(name, colorCode);
        Name = name.Trim();
        ColorCode = colorCode?.Trim();
        AllowedRole = allowedRole;
        IsApprovalGate = isApprovalGate;
        EstimatedDurationDays = estimatedDurationDays;
        UpdatedAt = DateTime.UtcNow;
    }

    // Khớp schema: name varchar(100), color_code varchar(20)
    private static void ValidateLengths(string name, string? colorCode)
    {
        if (name.Trim().Length > 100)
            throw new ArgumentException("Tên stage tối đa 100 ký tự.");
        if (colorCode?.Trim().Length > 20)
            throw new ArgumentException("Mã màu tối đa 20 ký tự.");
    }

    /// <summary>Chỉ PipelineDefinition được đổi vị trí / cờ biên để giữ thứ tự 1..n liên tục.</summary>
    internal void SetPosition(int stageOrder, bool isInitial, bool isTerminal)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stageOrder);
        if (StageOrder == stageOrder && IsInitial == isInitial && IsTerminal == isTerminal) return;

        StageOrder = stageOrder;
        IsInitial = isInitial;
        IsTerminal = isTerminal;
        UpdatedAt = DateTime.UtcNow;
    }
}
