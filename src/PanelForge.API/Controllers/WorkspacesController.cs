using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.DTOs.Workspaces;
using PanelForge.Application.Features.AiConfig.Commands;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Application.Features.AiConfig.Queries;
using PanelForge.Application.Features.Audit.Queries;
using PanelForge.Application.Interfaces;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkspacesController : ControllerBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IMediator _mediator;

    public WorkspacesController(IWorkspaceService workspaceService, IMediator mediator)
    {
        _workspaceService = workspaceService;
        _mediator = mediator;
    }

    private Guid GetCurrentUserId()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            throw new UnauthorizedAccessException("Không xác định được danh tính người dùng.");
        }
        return userId;
    }

    [HttpPost]
    public async Task<IActionResult> CreateWorkspace([FromBody] CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _workspaceService.CreateWorkspaceAsync(userId, request, cancellationToken);
            return CreatedAtAction(nameof(GetWorkspaceById), new { id = result.Id }, result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetMyWorkspaces(CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetCurrentUserId();
            var workspaces = await _workspaceService.GetUserWorkspacesAsync(userId, cancellationToken);
            return Ok(workspaces);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetWorkspaceById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetCurrentUserId();
            var workspace = await _workspaceService.GetWorkspaceByIdAsync(userId, id, cancellationToken);
            return Ok(workspace);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    public async Task<IActionResult> UpdateWorkspace(Guid id, [FromBody] UpdateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetCurrentUserId();
            var updated = await _workspaceService.UpdateWorkspaceAsync(userId, id, request, cancellationToken);
            return Ok(updated);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteWorkspace(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _workspaceService.DeleteWorkspaceAsync(userId, id, cancellationToken);
            return Ok(new { message = "Xóa Workspace thành công." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var userId = GetCurrentUserId();
            var members = await _workspaceService.GetMembersAsync(userId, id, cancellationToken);
            return Ok(members);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/members")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddWorkspaceMemberRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var newMember = await _workspaceService.AddMemberAsync(actorId, id, request, cancellationToken);
            return CreatedAtAction(nameof(GetMembers), new { id }, newMember);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}/members/{userId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
    public async Task<IActionResult> UpdateMemberRole(Guid id, Guid userId, [FromBody] UpdateMemberRoleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var updated = await _workspaceService.UpdateMemberRoleAsync(actorId, id, userId, request, cancellationToken);
            return Ok(updated);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var actorId = GetCurrentUserId();
            await _workspaceService.RemoveMemberAsync(actorId, id, userId, cancellationToken);
            return Ok(new { message = "Xóa thành viên khỏi Workspace thành công." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/workspaces/{id}/ai-config
    /// Lấy cấu hình AI Provider, Models và Token Quota của Workspace (UC-02).
    /// </summary>
    [HttpGet("{id:guid}/ai-config")]
    [RequireWorkspaceRole]
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
    /// GET /api/workspaces/{id}/audit-logs
    /// Tra cứu lịch sử Audit Log của Workspace (UC-15, BR-18).
    /// </summary>
    [HttpGet("{id:guid}/audit-logs")]
    [RequireWorkspaceRole(WorkspaceRole.Producer)]
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
}
