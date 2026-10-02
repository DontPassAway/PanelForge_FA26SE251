using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Ai;

/// <summary>
/// Đề xuất của AI (BR-02). PayloadJson bất biến; chỉ Status đổi, và chỉ một lần Pending → quyết định của người.
/// </summary>
public class AiSuggestion : BaseEntity
{
    public Guid RunId { get; private set; }
    public Guid StepId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid SceneId { get; private set; }
    public AiSuggestionKind Kind { get; private set; }
    public AiSuggestionStatus Status { get; private set; }
    public string PayloadJson { get; private set; } = default!;
    public string PayloadHash { get; private set; } = default!;
    public string Summary { get; private set; } = default!;
    public long BaseChapterVersion { get; private set; }
    public Guid? BaseScriptRevisionId { get; private set; }
    public string BibleRevisionIdsJson { get; private set; } = "[]";
    /// <summary>Layout → Script suggestion mà nó dựa vào (sourceScriptLineIds trỏ vào script đó).</summary>
    public Guid? ParentSuggestionId { get; private set; }
    /// <summary>Còn finding chặn khi dừng ở WaitingForHuman: chỉ được Reject / RequestChanges.</summary>
    public bool HasBlockingFindings { get; private set; }

    private AiSuggestion() { }

    public static AiSuggestion Create(
        Guid runId, Guid stepId, Guid workspaceId, Guid seriesId, Guid chapterId, Guid sceneId,
        AiSuggestionKind kind, string payloadJson, string payloadHash, string summary,
        long baseChapterVersion, Guid? baseScriptRevisionId, string bibleRevisionIdsJson,
        Guid? parentSuggestionId, bool hasBlockingFindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        return new AiSuggestion
        {
            RunId = runId,
            StepId = stepId,
            WorkspaceId = workspaceId,
            SeriesId = seriesId,
            ChapterId = chapterId,
            SceneId = sceneId,
            Kind = kind,
            Status = AiSuggestionStatus.Pending,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            Summary = summary,
            BaseChapterVersion = baseChapterVersion,
            BaseScriptRevisionId = baseScriptRevisionId,
            BibleRevisionIdsJson = bibleRevisionIdsJson,
            ParentSuggestionId = parentSuggestionId,
            HasBlockingFindings = hasBlockingFindings
        };
    }

    /// <summary>Áp một quyết định của người; chỉ hợp lệ khi đang Pending.</summary>
    public void Decide(AiSuggestionStatus decision)
    {
        if (decision == AiSuggestionStatus.Pending) throw new ArgumentException("Quyết định không hợp lệ.", nameof(decision));
        if (Status != AiSuggestionStatus.Pending)
            throw new InvalidOperationException($"Đề xuất đã được quyết định ({Status}).");
        Status = decision;
    }
}