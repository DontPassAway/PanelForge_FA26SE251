using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Content;

/// <summary>
/// Một lỗi nhất quán do kiểm tra bằng code tìm ra trên script của một Chapter (CF3 bước 4–5, BR-21).
/// <see cref="Fingerprint"/> định danh "cùng một lỗi" qua các lần chạy: lỗi đã Ignored/Rejected không bị báo lại,
/// trừ khi nguồn đổi (fingerprint đổi). Không xoá: trạng thái chuyển theo triage và các lần kiểm tra lại.
/// </summary>
public class ConsistencyFinding : BaseEntity
{
    public Guid ChapterId { get; private set; }
    /// <summary>Mã luật, vd SPEAKER_MISSING, ENTRY_NOT_YET_EFFECTIVE, RULE_PATTERN.</summary>
    public string RuleCode { get; private set; } = default!;
    /// <summary>ConsistencyRule do Producer cấu hình đã sinh ra finding này (null = luật dựng sẵn).</summary>
    public Guid? ConsistencyRuleId { get; private set; }
    public ConsistencyFindingSeverity Severity { get; private set; }
    public string Message { get; private set; } = default!;
    /// <summary>Trích nguyên văn phần vi phạm (dòng script / mục Bible) làm bằng chứng.</summary>
    public string? Evidence { get; private set; }
    public Guid? SceneId { get; private set; }
    public Guid? ScriptLineId { get; private set; }
    public Guid? BibleEntryId { get; private set; }
    public string Fingerprint { get; private set; } = default!;
    public ConsistencyFindingStatus Status { get; private set; }
    public string? TriageNote { get; private set; }
    public Guid? TriagedByUserId { get; private set; }
    public DateTime? TriagedAt { get; private set; }
    /// <summary>Lần kiểm tra gần nhất còn thấy lỗi này.</summary>
    public DateTime LastDetectedAt { get; private set; }

    private ConsistencyFinding() { }

    public static ConsistencyFinding Create(
        Guid chapterId, string ruleCode, ConsistencyFindingSeverity severity, string message, string fingerprint,
        string? evidence = null, Guid? sceneId = null, Guid? scriptLineId = null, Guid? bibleEntryId = null,
        Guid? consistencyRuleId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        var now = DateTime.UtcNow;
        return new ConsistencyFinding
        {
            ChapterId = chapterId,
            RuleCode = ruleCode,
            ConsistencyRuleId = consistencyRuleId,
            Severity = severity,
            Message = message,
            Evidence = evidence,
            SceneId = sceneId,
            ScriptLineId = scriptLineId,
            BibleEntryId = bibleEntryId,
            Fingerprint = fingerprint,
            Status = ConsistencyFindingStatus.Open,
            LastDetectedAt = now,
            CreatedAt = now
        };
    }

    /// <summary>Lỗi còn mở: chưa được kết luận là không phải lỗi / đã sửa.</summary>
    public bool IsOpen => Status is ConsistencyFindingStatus.Open or ConsistencyFindingStatus.Confirmed
        or ConsistencyFindingStatus.NeedsClarification;

    /// <summary>
    /// Editor triage (BR-21): Confirmed / Rejected / Ignored / NeedsClarification. Ignored và NeedsClarification bắt buộc ghi lý do / câu hỏi.
    /// Chỉ triage được lỗi đang mở.
    /// </summary>
    public void Triage(ConsistencyFindingStatus decision, string? note, Guid byUserId)
    {
        if (decision is not (ConsistencyFindingStatus.Confirmed or ConsistencyFindingStatus.Rejected
            or ConsistencyFindingStatus.Ignored or ConsistencyFindingStatus.NeedsClarification))
            throw new ArgumentException("Chỉ chọn được Confirmed, Rejected, Ignored hoặc NeedsClarification.", nameof(decision));
        if (decision is ConsistencyFindingStatus.Ignored or ConsistencyFindingStatus.NeedsClarification
            && string.IsNullOrWhiteSpace(note))
            throw new ArgumentException(decision == ConsistencyFindingStatus.Ignored
                ? "Bỏ qua lỗi bắt buộc ghi lý do (BR-21)."
                : "Cần làm rõ bắt buộc ghi câu hỏi cụ thể.", nameof(note));
        if (!IsOpen)
            throw new InvalidOperationException($"Finding đang ở trạng thái {Status}, không triage được.");

        Status = decision;
        TriageNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        TriagedByUserId = byUserId;
        TriagedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Lần kiểm tra lại vẫn thấy lỗi: lỗi đã Resolved/Stale mở lại thành Open; Ignored/Rejected giữ nguyên (không báo lại).</summary>
    public void Redetected(DateTime at)
    {
        LastDetectedAt = at;
        if (Status is ConsistencyFindingStatus.Resolved or ConsistencyFindingStatus.Stale)
        {
            Status = ConsistencyFindingStatus.Open;
            TriageNote = null;
            TriagedByUserId = null;
            TriagedAt = null;
            UpdatedAt = at;
        }
    }

    /// <summary>Lần kiểm tra lại không còn thấy lỗi: Confirmed → Resolved (đã sửa); Open/NeedsClarification → Stale (nguồn đã đổi).</summary>
    public void NoLongerDetected(DateTime at)
    {
        if (Status == ConsistencyFindingStatus.Confirmed) Status = ConsistencyFindingStatus.Resolved;
        else if (Status is ConsistencyFindingStatus.Open or ConsistencyFindingStatus.NeedsClarification)
            Status = ConsistencyFindingStatus.Stale;
        else return;
        UpdatedAt = at;
    }
}
