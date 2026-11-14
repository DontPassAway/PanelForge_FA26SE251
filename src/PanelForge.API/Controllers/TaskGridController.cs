using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.TaskGrid.Commands.ApproveStageTask;
using PanelForge.Application.Features.TaskGrid.Commands.AssignStageTaskLots;
using PanelForge.Application.Features.TaskGrid.Commands.GenerateStageTasks;
using PanelForge.Application.Features.TaskGrid.Commands.ReopenStageTask;
using PanelForge.Application.Features.TaskGrid.Commands.RequestStageTaskChanges;
using PanelForge.Application.Features.TaskGrid.Commands.ResolveStageTaskImpact;
using PanelForge.Application.Features.TaskGrid.Commands.StartStageTask;
using PanelForge.Application.Features.TaskGrid.Commands.UnassignStageTasks;
using PanelForge.Application.Features.TaskGrid.Commands.SubmitStageTasks;
using PanelForge.Application.Features.TaskGrid.Models;
using PanelForge.Application.Features.TaskGrid.Queries.GetChapterTaskGrid;
using PanelForge.Application.Features.TaskGrid.Queries.GetStageTaskSubmissions;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

/// <summary>Lưới giao việc V3 (Trang × Công đoạn). Chạy song song với /assignments cũ cho tới Clean Cutover.</summary>
[ApiController]
[Authorize]
[Route("api/chapters/{chapterId:guid}/task-grid")]
public sealed class TaskGridController : ControllerBase
{
    private readonly IMediator _mediator;

    public TaskGridController(IMediator mediator) => _mediator = mediator;

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>GET — lưới của chương: công đoạn (kèm tiền đề theo nhánh), trang, ô, lô.</summary>
    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist, WorkspaceRole.Letterer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(TaskGridDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid chapterId, CancellationToken ct) =>
        ToResult(await _mediator.Send(new GetChapterTaskGridQuery(chapterId), ct));

    /// <summary>POST /generate — Producer sinh các ô còn thiếu (gọi lại an toàn).</summary>
    [HttpPost("generate")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(GenerateStageTasksResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Generate(Guid chapterId, CancellationToken ct) =>
        ToResult(await _mediator.Send(new GenerateStageTasksCommand(chapterId, CurrentUserId), ct));

    /// <summary>POST /lots — Producer giao nhiều lô một lần, tất cả hoặc không; dryRun = chỉ kiểm tra.</summary>
    [HttpPost("lots")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(AssignStageTaskLotsResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignLots(Guid chapterId, [FromBody] AssignStageTaskLotsRequest body, CancellationToken ct) =>
        ToResult(await _mediator.Send(new AssignStageTaskLotsCommand(chapterId, CurrentUserId, body.Lots, body.DryRun), ct));

    /// <summary>POST /tasks/{taskId}/start — người nhận bắt đầu ô.</summary>
    [HttpPost("tasks/{taskId:guid}/start")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist, WorkspaceRole.Letterer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(StageTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Start(Guid chapterId, Guid taskId, CancellationToken ct) =>
        ToResult(await _mediator.Send(new StartStageTaskCommand(chapterId, taskId, CurrentUserId), ct));

    /// <summary>POST /tasks/submit — người nhận nộp danh sách ô, tất cả hoặc không.</summary>
    [HttpPost("tasks/submit")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist, WorkspaceRole.Letterer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(IReadOnlyList<StageTaskDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid chapterId, [FromBody] SubmitStageTasksRequest body, CancellationToken ct) =>
        ToResult(await _mediator.Send(new SubmitStageTasksCommand(chapterId, body.TaskIds, CurrentUserId), ct));

    /// <summary>POST /tasks/{taskId}/approve — Editor/Producer duyệt ô (BR-06: khác người làm).</summary>
    [HttpPost("tasks/{taskId:guid}/approve")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(StageTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid chapterId, Guid taskId, CancellationToken ct) =>
        ToResult(await _mediator.Send(new ApproveStageTaskCommand(chapterId, taskId, CurrentUserId), ct));

    /// <summary>POST /tasks/{taskId}/request-changes — Editor/Producer trả ô về ReworkRequired (BR-06).</summary>
    [HttpPost("tasks/{taskId:guid}/request-changes")]
    [RequireWorkspaceRole(WorkspaceRole.Editor, WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(StageTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestChanges(Guid chapterId, Guid taskId, [FromBody] RequestStageTaskChangesRequest body, CancellationToken ct) =>
        ToResult(await _mediator.Send(new RequestStageTaskChangesCommand(chapterId, taskId, CurrentUserId, body.Comment), ct));

    /// <summary>GET /tasks/{taskId}/submissions — lịch sử lần nộp của ô, mới nhất trước.</summary>
    [HttpGet("tasks/{taskId:guid}/submissions")]
    [RequireWorkspaceRole(WorkspaceRole.Producer, WorkspaceRole.Writer, WorkspaceRole.Artist, WorkspaceRole.Letterer, WorkspaceRole.Editor)]
    [ProducesResponseType(typeof(IReadOnlyList<StageTaskSubmissionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submissions(Guid chapterId, Guid taskId, CancellationToken ct) =>
        ToResult(await _mediator.Send(new GetStageTaskSubmissionsQuery(chapterId, taskId), ct));

    /// <summary>POST /tasks/{taskId}/reopen — Producer mở lại ô đã duyệt; ô hạ nguồn đã bắt đầu bị gắn cờ lỗi thời.</summary>
    [HttpPost("tasks/{taskId:guid}/reopen")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(ReopenStageTaskResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen(Guid chapterId, Guid taskId, [FromBody] ReopenStageTaskRequest body, CancellationToken ct) =>
        ToResult(await _mediator.Send(new ReopenStageTaskCommand(chapterId, taskId, CurrentUserId, body.Reason), ct));

    /// <summary>POST /tasks/{taskId}/resolve-impact — Producer xử lý Impact Review (keep / rework).</summary>
    [HttpPost("tasks/{taskId:guid}/resolve-impact")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(StageTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveImpact(Guid chapterId, Guid taskId, [FromBody] ResolveStageTaskImpactRequest body, CancellationToken ct) =>
        ToResult(await _mediator.Send(new ResolveStageTaskImpactCommand(chapterId, taskId, CurrentUserId, body.Keep), ct));

    /// <summary>POST /tasks/unassign — Producer gỡ người khỏi danh sách ô, tất cả hoặc không.</summary>
    [HttpPost("tasks/unassign")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(IReadOnlyList<StageTaskDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Unassign(Guid chapterId, [FromBody] UnassignStageTasksRequest body, CancellationToken ct) =>
        ToResult(await _mediator.Send(new UnassignStageTasksCommand(chapterId, body.TaskIds, CurrentUserId), ct));

    private IActionResult ToResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : result.ErrorCode switch
        {
            ResultErrorCodes.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultErrorCodes.Conflict => Conflict(new { error = result.ErrorMessage, code = result.ErrorCode }),
            ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorMessage, code = result.ErrorCode }),
            _ => BadRequest(new { error = result.ErrorMessage })
        };
}
