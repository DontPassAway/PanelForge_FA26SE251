using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.AiConfig.Queries.GetWorkspaceAiConfig;
using PanelForge.Application.Features.Audit.Queries.GetAuditLogs;
using PanelForge.Application.Features.Workspaces.Commands.AddMember;
using PanelForge.Application.Features.Workspaces.Commands.CreateWorkspace;
using PanelForge.Application.Features.Workspaces.Commands.DeleteWorkspace;
using PanelForge.Application.Features.Workspaces.Commands.RemoveMember;
using PanelForge.Application.Features.Workspaces.Commands.ResendInvitation;
using PanelForge.Application.Features.Workspaces.Commands.UpdateMemberRole;
using PanelForge.Application.Features.Workspaces.Commands.UpdateWorkspace;
using PanelForge.Application.Features.Workspaces.Queries.GetMyWorkspaces;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceById;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceMembers;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceUsage;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceActiveSessions;
using PanelForge.Application.Features.Workspaces.Commands.RevokeWorkspaceSession;
using PanelForge.Application.Features.Workspaces.Commands.AcceptInvitation;
using PanelForge.Application.Features.Workspaces.Commands.RejectInvitation;
using PanelForge.Application.Features.Workspaces.Queries.GetPendingInvitations;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Application.Features.Audit.Models;
using PanelForge.Domain.Enums;
using PanelForge.API.Common;
using PanelForge.Application.Features.Workspaces.Commands.SetWorkspaceCover;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceCover;

namespace PanelForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkspacesController : ControllerBase
{
    private readonly IMediator _mediator;

    public WorkspacesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(WorkspaceDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateWorkspace([FromBody] CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(
            new CreateWorkspaceCommand(userId, request.Name, request.StorageQuotaBytes), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetWorkspaceById), new { id = result.Value!.Id }, result.Value)
            : ToError(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WorkspaceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyWorkspaces(CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new GetMyWorkspacesQuery(userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/workspaces/pending-invitations — danh sách lời mời đang chờ người dùng hiện tại chấp nhận.</summary>
    [HttpGet("pending-invitations")]
    [ProducesResponseType(typeof(IReadOnlyList<PendingInvitationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingInvitations(CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new GetMyPendingInvitationsQuery(userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkspaceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaceById(Guid id, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new GetWorkspaceByIdQuery(userId, id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(WorkspaceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateWorkspace(Guid id, [FromBody] UpdateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new UpdateWorkspaceCommand(userId, id, request.Name), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteWorkspace(Guid id, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new DeleteWorkspaceCommand(userId, id), cancellationToken);
        return result.IsSuccess ? Ok(new { message = "Xóa Workspace thành công." }) : ToError(result);
    }

    /// <summary>
    /// PUT /api/workspaces/{id}/cover (multipart/form-data: file) — Producer đặt ảnh bìa Studio (PNG/JPEG/WebP, ≤ 5 MB).
    /// </summary>
    [HttpPut("{id:guid}/cover")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SetWorkspaceCoverCommandHandler.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = SetWorkspaceCoverCommandHandler.MaxBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetCover([FromRoute] Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Thiếu file ảnh bìa." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var result = await _mediator.Send(new SetWorkspaceCoverCommand(id, userId, buffer.ToArray()), cancellationToken);
        return result.IsSuccess
            ? Ok(new TextValueResponse(result.Value))
            : ToError(result);
    }

    /// <summary>
    /// DELETE /api/workspaces/{id}/cover — Producer xoá ảnh bìa (quay về bìa mặc định).
    /// </summary>
    [HttpDelete("{id:guid}/cover")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(TextValueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearCover([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new SetWorkspaceCoverCommand(id, userId, null), cancellationToken);
        return result.IsSuccess
            ? Ok(new TextValueResponse(result.Value))
            : ToError(result);
    }

    /// <summary>
    /// GET /api/workspaces/{id}/cover — Ảnh bìa Studio hiện tại.
    /// </summary>
    [HttpGet("{id:guid}/cover")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCover([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkspaceCoverQuery(id), cancellationToken);
        if (!result.IsSuccess)
            return NotFound(new { message = result.ErrorMessage });

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, result.Value.ContentType);
    }

    [HttpGet("{id:guid}/members")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkspaceMemberDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new GetWorkspaceMembersQuery(userId, id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    [HttpPost("{id:guid}/members")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(WorkspaceMemberDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddWorkspaceMemberRequest request, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } actorId)
            return UnknownUser();

        var result = await _mediator.Send(
            new AddWorkspaceMemberCommand(actorId, id, request.Email, request.Role), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMembers), new { id }, result.Value)
            : ToError(result, notFoundStatus: StatusCodes.Status400BadRequest);
    }

    /// <summary>Gửi lại OTP kích hoạt cho thành viên được mời nhưng chưa kích hoạt tài khoản.</summary>
    [HttpPost("{id:guid}/members/{userId:guid}/resend-invitation")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(WorkspaceMemberDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendInvitation(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } actorId)
            return UnknownUser();

        var result = await _mediator.Send(new ResendWorkspaceInvitationCommand(actorId, id, userId), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : ToError(result, notFoundStatus: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// POST /api/workspaces/{id}/accept-invitation
    /// Thành viên được mời chấp nhận lời mời tham gia Workspace.
    /// </summary>
    [HttpPost("{id:guid}/accept-invitation")]
    [ProducesResponseType(typeof(WorkspaceMemberDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptInvitation(Guid id, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new AcceptWorkspaceInvitationCommand(userId, id), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : ToError(result, notFoundStatus: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// POST /api/workspaces/{id}/reject-invitation
    /// Thành viên từ chối lời mời tham gia Workspace.
    /// </summary>
    [HttpPost("{id:guid}/reject-invitation")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectInvitation(Guid id, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } userId)
            return UnknownUser();

        var result = await _mediator.Send(new RejectWorkspaceInvitationCommand(userId, id), cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "Đã từ chối lời mời tham gia Workspace." })
            : ToError(result, notFoundStatus: StatusCodes.Status400BadRequest);
    }

    [HttpPut("{id:guid}/members/{userId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(WorkspaceMemberDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMemberRole(Guid id, Guid userId, [FromBody] UpdateMemberRoleRequest request, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } actorId)
            return UnknownUser();

        var result = await _mediator.Send(
            new UpdateMemberRoleCommand(actorId, id, userId, request.NewRole), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : ToError(result, notFoundStatus: StatusCodes.Status400BadRequest);
    }

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        if (GetCurrentUserId() is not { } actorId)
            return UnknownUser();

        var result = await _mediator.Send(new RemoveWorkspaceMemberCommand(actorId, id, userId), cancellationToken);
        return result.IsSuccess
            ? Ok(new { message = "Xóa thành viên khỏi Workspace thành công." })
            : ToError(result, notFoundStatus: StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// GET /api/workspaces/{id}/ai-config
    /// Lấy cấu hình AI Provider, Models và Token Quota của Workspace (UC-02).
    /// </summary>
    [HttpGet("{id:guid}/ai-config")]
    [RequireWorkspaceRole]
    [ProducesResponseType(typeof(WorkspaceAiConfigDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAiConfig(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkspaceAiConfigQuery(id), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/workspaces/{id}/ai-config
    /// UC-02: Chỉ Administrator mới có quyền cấu hình AI Provider và Token Quota cho Workspace.
    /// Endpoint này từ chối quyền thao tác của Producer/Member và yêu cầu thực hiện qua /api/admin/workspaces/{id}/ai-config.
    /// </summary>
    [HttpPut("{id:guid}/ai-config")]
    public IActionResult UpdateAiConfig(Guid id)
    {
        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "Theo quy định nghiệp vụ (UC-02), chỉ Administrator mới có quyền cấu hình AI Provider và hạn mức Token cho Workspace. Producer chỉ có quyền xem (Read-Only)."
        });
    }

    /// <summary>
    /// GET /api/workspaces/{id}/usage
    /// Mức dùng storage và token AI so với giới hạn của Workspace (NFR Workspace Resource Governance).
    /// </summary>
    [HttpGet("{id:guid}/usage")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(WorkspaceUsageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsage(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkspaceUsageQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// GET /api/workspaces/{id}/sessions
    /// Danh sách thiết bị và phiên làm việc đồng thời của các thành viên Studio (UC-15, NFR Resource Governance).
    /// </summary>
    [HttpGet("{id:guid}/sessions")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkspaceSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveSessions(Guid id, CancellationToken cancellationToken)
    {
        Guid? currentSessionId = Guid.TryParse(User.FindFirstValue("session_id"), out var sid) ? sid : null;

        var result = await _mediator.Send(new GetWorkspaceActiveSessionsQuery(id, currentSessionId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// DELETE /api/workspaces/{id}/sessions/{sessionId}
    /// Producer thu hồi phiên làm việc bất thường của thành viên trong Studio.
    /// </summary>
    [HttpDelete("{id:guid}/sessions/{sessionId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeSession(Guid id, Guid sessionId, CancellationToken cancellationToken)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid.TryParse(userIdStr, out var currentUserId);

        var result = await _mediator.Send(new RevokeWorkspaceSessionCommand(id, sessionId, currentUserId), cancellationToken);
        return result.IsSuccess ? Ok(new { message = "Thu hồi phiên làm việc thành công." }) : ToError(result);
    }

    /// <summary>
    /// GET /api/workspaces/{id}/audit-logs
    /// Tra cứu lịch sử Audit Log của Workspace (UC-15, BR-18).
    /// </summary>
    [HttpGet("{id:guid}/audit-logs")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    [ProducesResponseType(typeof(AuditLogsPagedResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaceAuditLogs(
        Guid id,
        [FromQuery] string? action,
        [FromQuery] Guid? userId,
        [FromQuery] string? entityName,
        [FromQuery] string? entityId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        // WorkspaceId luôn lấy từ route (đã qua RequireWorkspaceRole), Producer không xem được workspace khác
        var query = new GetAuditLogsQuery(
            WorkspaceId: id, Action: action, Page: page, PageSize: pageSize,
            UserId: userId, EntityName: entityName, EntityId: entityId, From: from, To: to);
        var result = await _mediator.Send(query, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Value);
    }

    private Guid? GetCurrentUserId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(userIdStr, out var userId) ? userId : null;
    }

    private IActionResult UnknownUser()
        => Unauthorized(new { message = "Không xác định được danh tính người dùng." });

    /// <summary>
    /// UNAUTHORIZED → 401, FORBIDDEN → 403, NOT_FOUND → <paramref name="notFoundStatus"/>, còn lại → 400.
    /// Các endpoint thành viên trả 400 cho "không tồn tại" như hành vi trước đây.
    /// </summary>
    private IActionResult ToError<T>(Result<T> result, int notFoundStatus = StatusCodes.Status404NotFound)
    {
        var body = new { message = result.ErrorMessage };
        return result.ErrorCode switch
        {
            ResultErrorCodes.Unauthorized => Unauthorized(body),
            ResultErrorCodes.Forbidden => StatusCode(StatusCodes.Status403Forbidden, body),
            ResultErrorCodes.NotFound => StatusCode(notFoundStatus, body),
            _ => BadRequest(body)
        };
    }
}
