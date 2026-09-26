using PanelForge.Domain.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Domain.Entities.Auth;

/// <summary>
/// E-29 AIUsageRecord: một bản ghi cho mỗi lần gọi AI (workspace, tính năng, model, token, chi phí, trạng thái).
/// Dùng để áp hạn mức theo workspace (UC-02) và dựng báo cáo AI usage (UC-15). Chỉ thêm, không sửa.
/// </summary>
public class AiUsageRecord : BaseEntity
{
    public Guid WorkspaceId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Feature { get; private set; } = default!;
    public string Provider { get; private set; } = default!;
    public string? Model { get; private set; }
    public long PromptTokens { get; private set; }
    public long CompletionTokens { get; private set; }
    public long TotalTokens { get; private set; }
    public decimal? CostUsd { get; private set; }
    public AiUsageStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? DurationMs { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public StudioWorkspace Workspace { get; private set; } = default!;

    private AiUsageRecord() { }

    public static AiUsageRecord Create(
        Guid workspaceId,
        string feature,
        string provider,
        AiUsageStatus status,
        Guid? userId = null,
        string? model = null,
        long promptTokens = 0,
        long completionTokens = 0,
        decimal? costUsd = null,
        string? errorMessage = null,
        int? durationMs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentOutOfRangeException.ThrowIfNegative(promptTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(completionTokens);

        var now = DateTime.UtcNow;
        return new AiUsageRecord
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Feature = Truncate(feature.Trim(), 100),
            Provider = Truncate(provider.Trim(), 100),
            Model = model is null ? null : Truncate(model.Trim(), 100),
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens,
            CostUsd = costUsd,
            Status = status,
            ErrorMessage = errorMessage is null ? null : Truncate(errorMessage.Trim(), 1000),
            DurationMs = durationMs,
            OccurredAtUtc = now,
            CreatedAt = now
        };
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
