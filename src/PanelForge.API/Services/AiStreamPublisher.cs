using Microsoft.AspNetCore.SignalR;
using PanelForge.AI.Contracts;
using PanelForge.API.Hubs;
using PanelForge.Application.Common.Interfaces.Services;

namespace PanelForge.API.Services;

public sealed class AiStreamPublisher : IAiStreamPublisher
{
    private readonly IHubContext<AiStreamHub> _hub;

    public AiStreamPublisher(IHubContext<AiStreamHub> hub) => _hub = hub;

    public async Task PublishTokenChunkAsync(Guid runId, StreamTokenPayload payload, CancellationToken ct = default)
    {
        var group = $"run_{runId.ToString().ToLowerInvariant()}";
        await _hub.Clients.Group(group).SendAsync("TokenChunk", new
        {
            runId,
            agent = payload.Agent,
            stepIndex = payload.StepIndex,
            deltaText = payload.DeltaText,
            isFinal = payload.IsFinal
        }, ct);
    }

    public async Task PublishStepRecordedAsync(Guid runId, AgentStepReport report, string runStatus, CancellationToken ct = default)
    {
        var group = $"run_{runId.ToString().ToLowerInvariant()}";
        await _hub.Clients.Group(group).SendAsync("StepRecorded", new
        {
            runId,
            stepIndex = report.StepIndex,
            agent = report.Agent,
            round = report.Round,
            succeeded = report.Succeeded,
            outputJson = report.OutputJson,
            errorCode = report.ErrorCode,
            errorMessage = report.ErrorMessage,
            runStatus
        }, ct);
    }

    public async Task PublishRunStatusChangedAsync(Guid runId, string status, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default)
    {
        var group = $"run_{runId.ToString().ToLowerInvariant()}";
        await _hub.Clients.Group(group).SendAsync("RunStatusChanged", new
        {
            runId,
            status,
            errorCode,
            errorMessage
        }, ct);
    }
}
