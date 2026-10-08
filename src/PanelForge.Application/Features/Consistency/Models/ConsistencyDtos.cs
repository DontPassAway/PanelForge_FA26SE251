using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Consistency.Models;

public sealed record ConsistencyFindingDto(
    Guid Id,
    Guid ChapterId,
    string RuleCode,
    Guid? ConsistencyRuleId,
    ConsistencyFindingSeverity Severity,
    string Message,
    string? Evidence,
    Guid? SceneId,
    Guid? ScriptLineId,
    Guid? BibleEntryId,
    ConsistencyFindingStatus Status,
    string? TriageNote,
    Guid? TriagedByUserId,
    DateTime? TriagedAt,
    DateTime LastDetectedAt,
    DateTime CreatedAt)
{
    public static ConsistencyFindingDto From(ConsistencyFinding f) => new(
        f.Id, f.ChapterId, f.RuleCode, f.ConsistencyRuleId, f.Severity, f.Message, f.Evidence, f.SceneId, f.ScriptLineId,
        f.BibleEntryId, f.Status, f.TriageNote, f.TriagedByUserId, f.TriagedAt, f.LastDetectedAt, f.CreatedAt);
}

/// <summary>Kết quả một lượt kiểm tra: số lỗi mới, còn mở, đã hết (Resolved/Stale) và toàn bộ finding của chương.</summary>
public sealed record ConsistencyCheckResultDto(
    Guid ChapterId,
    DateTime CheckedAt,
    int NewCount,
    int OpenCount,
    int BlockingOpenCount,
    int ClearedCount,
    IReadOnlyList<ConsistencyFindingDto> Findings);

/// <summary>Body triage: decision = Confirmed | Rejected | Ignored | NeedsClarification; note bắt buộc với Ignored / NeedsClarification.</summary>
public sealed record TriageConsistencyFindingRequest(ConsistencyFindingStatus Decision, string? Note);
