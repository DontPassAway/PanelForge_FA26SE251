using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PanelForge.Application.Features.AiAgents.Queries.CanJoinRunStream;

namespace PanelForge.API.Hubs;

/// <summary>
/// SignalR Hub phát tiến trình AI Workflow (<c>StepRecorded</c>, <c>RunStatusChanged</c>) theo thời gian thực về Frontend.
/// Sự kiện chứa output từng bước AI của nội dung chưa phát hành, nên phải đăng nhập và là thành viên Series của run
/// (cùng luật với GET run, BR-07, BR-23). Group tên theo <see cref="RunGroup"/>, khớp <c>AiStreamPublisher</c>.
/// </summary>
[Authorize]
public sealed class AiStreamHub : Hub
{
    private readonly ISender _mediator;

    public AiStreamHub(ISender mediator) => _mediator = mediator;

    public static string RunGroup(Guid runId) => $"run_{runId:D}";

    public async Task JoinRun(string runId)
    {
        if (!Guid.TryParse(runId, out var id))
            throw new HubException("Mã run không hợp lệ.");

        var allowed = await _mediator.Send(new CanJoinRunStreamQuery(id, CurrentUserId()), Context.ConnectionAborted);
        if (!allowed)
            throw new HubException("Bạn không có quyền theo dõi run này.");

        await Groups.AddToGroupAsync(Context.ConnectionId, RunGroup(id));
    }

    public async Task LeaveRun(string runId)
    {
        if (Guid.TryParse(runId, out var id))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RunGroup(id));
    }

    private Guid CurrentUserId()
    {
        var claim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}
