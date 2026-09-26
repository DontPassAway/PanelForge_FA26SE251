using System.Globalization;
using System.Text;
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

    // ─── UC-03: Producer cấu hình stage của pipeline Series ────────────────────

    // Method (không phải property) để EF không map nhầm thành navigation
    public IReadOnlyList<PipelineStage> GetOrderedStages() => Stages.OrderBy(s => s.StageOrder).ToList();

    /// <summary>
    /// Chèn stage mới tại vị trí <paramref name="position"/> (1-based, null = cuối pipeline),
    /// sau đó chuẩn hóa thứ tự 1..n và cờ Initial/Terminal.
    /// </summary>
    public PipelineStage InsertStage(
        string name,
        string? slug,
        int? position,
        string? colorCode,
        WorkspaceRole? allowedRole,
        bool isApprovalGate,
        int? estimatedDurationDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalizedSlug = NormalizeSlug(string.IsNullOrWhiteSpace(slug) ? name : slug);
        if (normalizedSlug.Length == 0)
            throw new ArgumentException("Slug của stage không hợp lệ.");
        if (Stages.Any(s => s.Slug == normalizedSlug))
            throw new ArgumentException($"Slug '{normalizedSlug}' đã tồn tại trong pipeline.");

        var ordered = GetOrderedStages().ToList();
        var index = position.HasValue ? Math.Clamp(position.Value, 1, ordered.Count + 1) - 1 : ordered.Count;

        var stage = PipelineStage.Create(
            Id, name, normalizedSlug, ordered.Count + 1, colorCode, allowedRole, isApprovalGate,
            estimatedDurationDays: estimatedDurationDays);
        Stages.Add(stage);

        ordered.Insert(index, stage);
        ApplyOrder(ordered);
        return stage;
    }

    /// <summary>Sắp xếp lại toàn bộ stage. <paramref name="orderedStageIds"/> phải chứa đủ và đúng mọi stage.</summary>
    public void ReorderStages(IReadOnlyList<Guid> orderedStageIds)
    {
        ArgumentNullException.ThrowIfNull(orderedStageIds);
        var byId = Stages.ToDictionary(s => s.Id);
        if (orderedStageIds.Count != byId.Count
            || orderedStageIds.Distinct().Count() != orderedStageIds.Count
            || orderedStageIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new ArgumentException("Danh sách stageIds phải chứa đầy đủ và không trùng lặp các stage hiện có của pipeline.");
        }

        ApplyOrder(orderedStageIds.Select(id => byId[id]).ToList());
    }

    /// <summary>Gỡ stage khỏi pipeline. Pipeline luôn phải còn ít nhất 2 stage (điểm đầu và điểm kết thúc).</summary>
    public PipelineStage RemoveStage(Guid stageId)
    {
        var stage = Stages.FirstOrDefault(s => s.Id == stageId)
            ?? throw new ArgumentException("Stage không thuộc pipeline này.");
        if (Stages.Count <= 2)
            throw new InvalidOperationException("Pipeline phải có ít nhất 2 stage.");

        Stages.Remove(stage);
        ApplyOrder(GetOrderedStages().ToList());
        return stage;
    }

    /// <summary>
    /// Dựng lại bộ transition mặc định theo thứ tự stage hiện tại:
    /// - Chuyển tiếp i → i+1, yêu cầu vai trò của stage nguồn.
    /// - Mỗi stage là cổng duyệt (IsApprovalGate) được trả về (reject) mọi stage phía trước, bắt buộc comment.
    /// Trả về các transition cũ bị gỡ và transition mới để tầng persistence cập nhật.
    /// </summary>
    public (IReadOnlyList<StageTransition> Removed, IReadOnlyList<StageTransition> Added) RebuildDefaultTransitions()
    {
        var removed = Transitions.ToList();
        foreach (var t in removed) Transitions.Remove(t);

        var ordered = GetOrderedStages();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var from = ordered[i];
            var to = ordered[i + 1];
            AddTransition(from.Id, to.Id, TransitionLabel($"{from.Name} → {to.Name}"), from.AllowedRole);
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            var gate = ordered[i];
            if (!gate.IsApprovalGate) continue;

            for (var j = 0; j < i; j++)
            {
                var target = ordered[j];
                AddTransition(gate.Id, target.Id, TransitionLabel($"Reject: trả về {target.Name}"), gate.AllowedRole,
                    requiresComment: true, isBackwardTransition: true);
            }
        }

        return (removed, Transitions.ToList());
    }

    private void ApplyOrder(IReadOnlyList<PipelineStage> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetPosition(i + 1, isInitial: i == 0, isTerminal: i == ordered.Count - 1);
        }
        UpdatedAt = DateTime.UtcNow;
    }

    // Cột transition_name là varchar(150)
    private static string TransitionLabel(string value) => value.Length <= 150 ? value : value[..150];

    private static string NormalizeSlug(string value)
    {
        // Bỏ dấu tiếng Việt: "Tô màu" → "to-mau"
        var decomposed = value.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var chars = decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-')
            .ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 50 ? slug[..50].TrimEnd('-') : slug;
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
    [Obsolete("Replaced by MasterData PipelineTemplate cloning in CreateSeriesCommand (NFR-08).")]
    public static PipelineDefinition CreateDefaultMangaPipeline(Guid workspaceId, Guid? seriesId = null)
    {
        var pipeline = Create(workspaceId, "Standard Manga Pipeline", "Canonical 8-stage manga production pipeline with guarded review gates", seriesId, true);

        // 1. Khởi tạo 8 Stages
        var sScript = pipeline.AddStage("Script", "script", 1, "#6B7280", WorkspaceRole.Writer, isApprovalGate: false, isInitial: true);
        var sThumbnail = pipeline.AddStage("Thumbnail", "thumbnail", 2, "#8B5CF6", WorkspaceRole.Artist);
        var sPencil = pipeline.AddStage("Pencil", "pencil", 3, "#3B82F6", WorkspaceRole.Artist);
        var sInk = pipeline.AddStage("Ink", "ink", 4, "#10B981", WorkspaceRole.Artist);
        var sColor = pipeline.AddStage("Color", "color", 5, "#F59E0B", WorkspaceRole.Artist);
        var sLetter = pipeline.AddStage("Letter", "letter", 6, "#EC4899", WorkspaceRole.Letterer);
        var sReview = pipeline.AddStage("Review", "review", 7, "#EF4444", WorkspaceRole.Editor, isApprovalGate: true);
        var sApproved = pipeline.AddStage("Approved", "approved", 8, "#059669", WorkspaceRole.Editor, isApprovalGate: false, isInitial: false, isTerminal: true);

        // 2. Thiết lập các bước chuyển tiếp hợp lệ (Forward Transitions)
        pipeline.AddTransition(sScript.Id, sThumbnail.Id, "Script Completed", WorkspaceRole.Writer);
        pipeline.AddTransition(sThumbnail.Id, sPencil.Id, "Layout Confirmed", WorkspaceRole.Artist);
        pipeline.AddTransition(sPencil.Id, sInk.Id, "Pencils Finished", WorkspaceRole.Artist);
        pipeline.AddTransition(sInk.Id, sColor.Id, "Inks Finished", WorkspaceRole.Artist);
        pipeline.AddTransition(sColor.Id, sLetter.Id, "Flats & Colors Finished", WorkspaceRole.Artist);
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
