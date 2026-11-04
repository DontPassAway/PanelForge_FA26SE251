using PanelForge.AI.Contracts;

namespace PanelForge.Application.Common.Interfaces.Services;

/// <summary>
/// Phát sự kiện thời gian thực (SignalR Hub / SSE) khi có token streaming hoặc cập nhật step của AI workflow.
/// </summary>
public interface IAiStreamPublisher
{
    Task PublishTokenChunkAsync(Guid runId, StreamTokenPayload payload, CancellationToken ct = default);
    Task PublishStepRecordedAsync(Guid runId, AgentStepReport report, string runStatus, CancellationToken ct = default);
    Task PublishRunStatusChangedAsync(Guid runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default);
}
