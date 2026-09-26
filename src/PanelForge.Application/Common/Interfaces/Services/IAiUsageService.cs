using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Interfaces;

/// <summary>
/// Cổng kiểm soát và ghi nhận mọi lần gọi AI theo workspace (UC-02, UC-15, E-29).
/// Mọi tính năng AI phải gọi <see cref="CheckAvailabilityAsync"/> trước và <see cref="RecordAsync"/> sau mỗi lần gọi provider.
/// Khi không khả dụng, tính năng phải rơi về luồng thủ công (UC-02 luồng phụ).
/// </summary>
public interface IAiUsageService
{
    Task<AiAvailability> CheckAvailabilityAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Ghi một AiUsageRecord; lần gọi thành công được cộng vào UsedTokensCurrentMonth của workspace.</summary>
    Task<AiUsageRecord> RecordAsync(AiUsageEntry entry, CancellationToken cancellationToken = default);
}

public sealed record AiAvailability(bool IsAvailable, AiUsageStatus? BlockedReason, string? Message)
{
    public static AiAvailability Available() => new(true, null, null);
    public static AiAvailability Blocked(AiUsageStatus reason, string message) => new(false, reason, message);
}

public sealed record AiUsageEntry(
    Guid WorkspaceId,
    string Feature,
    AiUsageStatus Status,
    string? Model = null,
    long PromptTokens = 0,
    long CompletionTokens = 0,
    decimal? CostUsd = null,
    string? ErrorMessage = null,
    int? DurationMs = null);
