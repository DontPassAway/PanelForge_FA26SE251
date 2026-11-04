using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Ai;

/// <summary>Quyết định của người đối với một đề xuất AI (BR-10 provenance). Append-only.</summary>
public class AiSuggestionDecision : BaseEntity
{
    public Guid SuggestionId { get; private set; }
    public Guid DecidedByUserId { get; private set; }
    public AiSuggestionStatus Decision { get; private set; }
    public string? Reason { get; private set; }
    public DateTime DecidedAtUtc { get; private set; }
    /// <summary>Chapter.VersionVector mà người quyết định đã nhìn thấy / áp dụng lên.</summary>
    public long ChapterVersionAtDecision { get; private set; }
    /// <summary>ContentRevision được tạo khi Accept; null với Reject / RequestChanges.</summary>
    public Guid? ResultRevisionId { get; private set; }
    /// <summary>Mức người chỉnh sửa trước khi áp dụng: demo luôn "None" (áp dụng nguyên văn payload).</summary>
    public string ModificationLevel { get; private set; } = "None";

    private AiSuggestionDecision() { }

    public static AiSuggestionDecision Create(
        Guid suggestionId, Guid decidedByUserId, AiSuggestionStatus decision, string? reason,
        long chapterVersionAtDecision, Guid? resultRevisionId, bool appliesOutsideContent = false)
    {
        if (decision is AiSuggestionStatus.Pending) throw new ArgumentException("Quyết định không hợp lệ.", nameof(decision));
        // Đề xuất cấp Series (metadata phát hành) không có content revision của Scene; provenance = payload (giá trị cũ + mới) + audit log Series
        if (decision == AiSuggestionStatus.Accepted && resultRevisionId is null && !appliesOutsideContent)
            throw new ArgumentException("Accept phải kèm content revision (BR-10).", nameof(resultRevisionId));

        return new AiSuggestionDecision
        {
            SuggestionId = suggestionId,
            DecidedByUserId = decidedByUserId,
            Decision = decision,
            Reason = reason?.Trim(),
            DecidedAtUtc = DateTime.UtcNow,
            ChapterVersionAtDecision = chapterVersionAtDecision,
            ResultRevisionId = resultRevisionId
        };
    }
}