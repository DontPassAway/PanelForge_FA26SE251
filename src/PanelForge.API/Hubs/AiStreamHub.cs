using Microsoft.AspNetCore.SignalR;

namespace PanelForge.API.Hubs;

/// <summary>
/// SignalR Hub phát sự kiện Token Streaming và Tiến trình AI Workflow theo thời gian thực về Frontend.
/// </summary>
public sealed class AiStreamHub : Hub
{
    public async Task JoinRun(string runId)
    {
        if (!string.IsNullOrWhiteSpace(runId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"run_{runId.Trim().ToLowerInvariant()}");
    }

    public async Task LeaveRun(string runId)
    {
        if (!string.IsNullOrWhiteSpace(runId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"run_{runId.Trim().ToLowerInvariant()}");
    }

    public async Task JoinSeries(string seriesId)
    {
        if (!string.IsNullOrWhiteSpace(seriesId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"series_{seriesId.Trim().ToLowerInvariant()}");
    }

    public async Task LeaveSeries(string seriesId)
    {
        if (!string.IsNullOrWhiteSpace(seriesId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"series_{seriesId.Trim().ToLowerInvariant()}");
    }
}
