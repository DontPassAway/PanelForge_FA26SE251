using System.Text.Json;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Models;

public sealed record RunWorkflowRequest(
    Guid ChapterId,
    /// <summary>Bắt buộc với mọi workflow nội dung; bỏ trống với Producer (báo cáo cấp Chapter).</summary>
    Guid? SceneId,
    string Brief,
    ReadingDirection ReadingDirection,
    /// <summary>Chapter.VersionVector mà client đang thấy (cơ chế version nguồn thực tế của Chapter).</summary>
    long ChapterVersion,
    /// <summary>Id ContentRevision script mới nhất client thấy; null nếu Scene chưa có revision.</summary>
    Guid? BaseScriptRevisionId = null,
    /// <summary>Nhiệm vụ cần chạy. Không gửi = FullScene (hành vi của API demo cũ). Quyền KHÔNG lấy từ client: Backend tự xác định.</summary>
    AiWorkflowType WorkflowType = AiWorkflowType.FullScene);

public sealed record RunWorkflowResponse(Guid RunId, AiRunStatus Status, string Provider, string Model);

public sealed record ReviewSuggestionRequest(string? Reason, long? ChapterVersion);

public sealed record ReviewSuggestionResponse(
    Guid SuggestionId, AiSuggestionStatus Status, Guid DecisionId, Guid? ResultRevisionId, long ChapterVersion);

public sealed record AiStepDto(
    Guid Id,
    int StepIndex,
    AiAgentType Agent,
    int Round,
    Guid? ParentStepId,
    AiStepStatus Status,
    string Provider,
    string Model,
    string PromptVersion,
    string PromptRef,
    JsonElement? Parameters,
    JsonElement? Input,
    JsonElement? Output,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    int DurationMs,
    long? PromptTokens,
    long? CompletionTokens,
    int RepairAttempts,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record AiDecisionDto(
    Guid Id,
    AiSuggestionStatus Decision,
    Guid DecidedByUserId,
    string? Reason,
    DateTime DecidedAtUtc,
    long ChapterVersionAtDecision,
    Guid? ResultRevisionId,
    string ModificationLevel);

public sealed record AiSuggestionDto(
    Guid Id,
    Guid RunId,
    Guid StepId,
    Guid SceneId,
    AiSuggestionKind Kind,
    AiSuggestionStatus Status,
    string Summary,
    JsonElement? Payload,
    string PayloadHash,
    long BaseChapterVersion,
    Guid? BaseScriptRevisionId,
    IReadOnlyList<Guid> BibleRevisionIds,
    Guid? ParentSuggestionId,
    bool HasBlockingFindings,
    DateTime CreatedAt,
    IReadOnlyList<AiDecisionDto> Decisions,
    /// <summary>Vai trò Workspace được phép quyết định loại đề xuất này (theo chính sách của Backend).</summary>
    IReadOnlyList<WorkspaceRole> DecisionRoles,
    /// <summary>Người gọi API có thể Accept/Reject/RequestChanges ngay lúc này (đúng vai trò + đề xuất còn Pending).</summary>
    bool CanDecide);

public sealed record ContentRevisionDto(
    Guid Id,
    ContentRevisionKind Kind,
    int VersionNumber,
    string Summary,
    string ContentHash,
    Guid? PreviousRevisionId,
    Guid? SourceSuggestionId,
    JsonElement? Mapping,
    DateTime CreatedAt);

public sealed record AiRunDto(
    Guid Id,
    Guid SeriesId,
    Guid ChapterId,
    Guid? SceneId,
    Guid RequestedByUserId,
    AiRunStatus Status,
    string Provider,
    string Model,
    string Brief,
    ReadingDirection ReadingDirection,
    int RevisionRounds,
    int MaxRevisionRounds,
    long BaseChapterVersion,
    Guid? BaseScriptRevisionId,
    IReadOnlyList<Guid> BibleRevisionIds,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime LastActivityAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<AiStepDto> Steps,
    IReadOnlyList<AiSuggestionDto> Suggestions,
    /// <summary>Loại nhiệm vụ; run tạo trước khi có loại workflow = FullScene.</summary>
    AiWorkflowType WorkflowType);

/// <summary>Một điều kiện còn thiếu để chạy workflow. Kind: Permission | Config | State | Context.</summary>
public sealed record AiMissingConditionDto(string Code, string Kind, string Message);

public sealed record AiWorkflowAvailabilityDto(
    AiWorkflowType WorkflowType,
    string Title,
    string Description,
    IReadOnlyList<string> Agents,
    IReadOnlyList<WorkspaceRole> RequiredRoles,
    /// <summary>Loại kết quả: các AiSuggestionKind có thể áp dụng, hoặc "Report" (báo cáo tham khảo, không Accept/Reject).</summary>
    IReadOnlyList<string> ResultKinds,
    bool CanRun,
    IReadOnlyList<AiMissingConditionDto> Missing);

public sealed record AiWorkflowAvailabilityResponse(
    WorkspaceRole? YourRole,
    Guid ChapterId,
    Guid? SceneId,
    IReadOnlyList<AiWorkflowAvailabilityDto> Workflows);