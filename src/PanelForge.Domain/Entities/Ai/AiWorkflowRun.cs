using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Ai;

/// <summary>
/// Một lần chạy workflow AI cho một Scene (BR-02, BR-10). Backend sở hữu bản ghi này; AI service chỉ báo từng step.
/// Snapshot context (Bible hiệu lực tại Chapter, revision IDs, script nền) được chụp một lần ở đầu run và bất biến.
/// </summary>
public class AiWorkflowRun : BaseEntity
{
    public Guid WorkspaceId { get; private set; }
    public Guid SeriesId { get; private set; }
    public Guid ChapterId { get; private set; }
    /// <summary>Null với workflow cấp Chapter (Producer). Các workflow nội dung luôn có Scene.</summary>
    public Guid? SceneId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    /// <summary>Nhiệm vụ của run (cột workflow_type, mặc định FullScene cho run tạo trước khi có loại workflow).</summary>
    public AiWorkflowType WorkflowType { get; private set; } = AiWorkflowType.FullScene;

    public AiRunStatus Status { get; private set; }
    public string Provider { get; private set; } = default!;
    public string Model { get; private set; } = default!;
    public string Brief { get; private set; } = default!;
    public ReadingDirection ReadingDirection { get; private set; }
    public int MaxRevisionRounds { get; private set; }
    public int RevisionRounds { get; private set; }

    /// <summary>Chapter.VersionVector lúc bắt đầu run — dùng làm base version khi Accept (BR-11).</summary>
    public long BaseChapterVersion { get; private set; }
    /// <summary>Id ContentRevision script mới nhất của Scene lúc bắt đầu run (null nếu chưa có).</summary>
    public Guid? BaseScriptRevisionId { get; private set; }

    public string ContextSnapshotJson { get; private set; } = "{}";
    public string BibleRevisionIdsJson { get; private set; } = "[]";

    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private AiWorkflowRun() { }

    public static AiWorkflowRun Create(
        Guid workspaceId, Guid seriesId, Guid chapterId, Guid? sceneId, Guid requestedByUserId,
        string provider, string model, string brief, ReadingDirection readingDirection, int maxRevisionRounds,
        long baseChapterVersion, Guid? baseScriptRevisionId, string contextSnapshotJson, string bibleRevisionIdsJson,
        AiWorkflowType workflowType = AiWorkflowType.FullScene)
    {
        // Báo cáo Producer và Copilot cấp Series (publisher.metadata) không gắn Scene
        if (sceneId is null && workflowType is not (AiWorkflowType.Producer or AiWorkflowType.Copilot or AiWorkflowType.Thumbnail))
            throw new ArgumentException("Workflow nội dung bắt buộc có Scene.", nameof(sceneId));
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(brief);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRevisionRounds);

        return new AiWorkflowRun
        {
            WorkspaceId = workspaceId,
            SeriesId = seriesId,
            ChapterId = chapterId,
            SceneId = sceneId,
            WorkflowType = workflowType,
            RequestedByUserId = requestedByUserId,
            Status = AiRunStatus.Queued,
            Provider = provider.Trim(),
            Model = model.Trim(),
            Brief = brief.Trim(),
            ReadingDirection = readingDirection,
            MaxRevisionRounds = maxRevisionRounds,
            BaseChapterVersion = baseChapterVersion,
            BaseScriptRevisionId = baseScriptRevisionId,
            ContextSnapshotJson = contextSnapshotJson,
            BibleRevisionIdsJson = bibleRevisionIdsJson,
            LastActivityAtUtc = DateTime.UtcNow
        };
    }

    public bool IsActive => Status is AiRunStatus.Queued or AiRunStatus.Running;

    /// <summary>Ghi nhận AI service còn sống; Queued chuyển sang Running ở lần báo đầu tiên.</summary>
    public void Touch()
    {
        if (!IsActive) return;
        if (Status == AiRunStatus.Queued) Status = AiRunStatus.Running;
        LastActivityAtUtc = DateTime.UtcNow;
    }

    public void RegisterWriterRound(int round)
    {
        if (round > MaxRevisionRounds)
            throw new InvalidOperationException($"Vượt số lượt sửa tối đa ({MaxRevisionRounds}).");
        RevisionRounds = Math.Max(RevisionRounds, round);
    }

    public void MarkWaitingForHuman() => Finish(AiRunStatus.WaitingForHuman, null, null);

    public void MarkCompleted() => Finish(AiRunStatus.Completed, null, null);

    public void MarkFailed(string errorCode, string? message) => Finish(AiRunStatus.Failed, errorCode, message);

    public void MarkInterrupted(string message) => Finish(AiRunStatus.Interrupted, "AI_INTERRUPTED", message);

    private void Finish(AiRunStatus status, string? errorCode, string? message)
    {
        if (!IsActive) throw new InvalidOperationException($"Run đã kết thúc ở trạng thái {Status}.");
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = message is { Length: > 2000 } ? message[..2000] : message;
        CompletedAtUtc = DateTime.UtcNow;
        LastActivityAtUtc = CompletedAtUtc.Value;
    }
}