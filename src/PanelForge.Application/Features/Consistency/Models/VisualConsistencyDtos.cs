using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Consistency.Models;

/// <summary>
/// Chi tiết một độ lệch nhất quán hình ảnh so với Character Sheet khóa (V1.2 / CF3).
/// </summary>
public sealed record VisualDeviationDto(
    Guid FindingId,
    Guid ChapterId,
    Guid CharacterId,
    string CharacterName,
    int? CharacterSheetVersion,
    string? CharacterSheetImageUrl,
    Guid? PageId,
    int? PageNumber,
    Guid? PanelId,
    int? PanelNumber,
    Guid? AssetReferenceId,
    string? ArtworkUrl,
    decimal SimilarityScore,
    decimal Threshold,
    bool IsDeviation,
    ConsistencyFindingSeverity Severity,
    string Message,
    ConsistencyFindingStatus Status,
    string? TriageNote,
    Guid? TriagedByUserId,
    string? TriagedByUserName,
    DateTime? TriagedAt,
    DateTime DetectedAt
);

/// <summary>
/// Báo cáo nhất quán hình ảnh tổng thể của một Chapter dành cho Editor (CF3, V1.2).
/// </summary>
public sealed record VisualConsistencyReportDto(
    Guid ChapterId,
    decimal ChapterNumber,
    string ChapterTitle,
    DateTime CheckedAt,
    decimal Threshold,
    int TotalEvaluated,
    int PassedCount,
    int DeviationCount,
    int OpenDeviationCount,
    int BlockingDeviationCount,
    IReadOnlyList<VisualDeviationDto> Deviations
);

/// <summary>
/// Yêu cầu chạy kiểm tra nhất quán hình ảnh (cho phép ghi đè ngưỡng nếu cần, mặc định 0.75).
/// </summary>
public sealed record RunVisualConsistencyCheckRequest(
    decimal? Threshold = null
);

/// <summary>
/// Yêu cầu triage độ lệch hình ảnh từ Editor.
/// </summary>
public sealed record TriageVisualDeviationRequest(
    ConsistencyFindingStatus Decision,
    string? Note = null
);
