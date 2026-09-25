using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.DTOs.Auth;
using PanelForge.Application.Interfaces.Authentication;
using PanelForge.Application.Features.Audit.Queries;
using PanelForge.Application.Features.Users.Commands.GrantStudioPermission;
using PanelForge.Application.Features.Users.Commands.RevokeStudioPermission;
using PanelForge.Application.Features.MasterData.ElementTypes.Commands;
using PanelForge.Application.Features.MasterData.ElementTypes.Queries;
using PanelForge.Application.Features.MasterData.ExportPresets.Commands;
using PanelForge.Application.Features.MasterData.ExportPresets.Queries;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Commands;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Queries;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Auth;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IPanelForgeDbContext _dbContext;
    private readonly IMediator _mediator;
    private readonly IPasswordHasher _passwordHasher;

    public AdminController(
        IPanelForgeDbContext dbContext,
        IMediator mediator,
        IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _mediator = mediator;
        _passwordHasher = passwordHasher;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var totalUsers = await _dbContext.Users.CountAsync(cancellationToken);
        var users = await _dbContext.Users
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.PhoneNumber,
                Role = u.Role.ToString(),
                u.IsActive,
                u.IsEmailConfirmed,
                u.TwoFactorEnabled,
                u.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            Total = totalUsers,
            Page = page,
            PageSize = pageSize,
            Items = users
        });
    }

    [HttpPost("assign-role")]
    public async Task<IActionResult> AssignRole(
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        user.AssignSystemRole(request.NewRole);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = $"Đã cập nhật vai trò của người dùng {user.Email} thành {request.NewRole}.",
            userId = user.Id,
            newRole = user.Role.ToString()
        });
    }

    /// <summary>
    /// POST /api/admin/users
    /// Tạo tài khoản người dùng mới bởi Administrator (UC-01).
    /// </summary>
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserByAdminRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existingUser = await _dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existingUser)
        {
            return BadRequest(new { message = $"Email '{request.Email}' đã tồn tại trong hệ thống." });
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var newUser = PanelForge.Domain.Entities.Auth.User.Create(
            email: normalizedEmail,
            fullName: request.FullName,
            passwordHash: passwordHash,
            phoneNumber: request.PhoneNumber,
            role: request.Role);

        if (request.IsActive)
        {
            newUser.Activate();
            newUser.ConfirmEmail();
        }
        else
        {
            newUser.Deactivate();
        }

        _dbContext.Users.Add(newUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            newUser.Id,
            newUser.Email,
            newUser.FullName,
            newUser.PhoneNumber,
            Role = newUser.Role.ToString(),
            newUser.IsActive,
            newUser.IsEmailConfirmed,
            newUser.CreatedAt
        });
    }

    /// <summary>
    /// PUT /api/admin/users/{id}
    /// Cập nhật thông tin và vai trò của tài khoản người dùng (UC-01).
    /// </summary>
    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        [FromBody] UpdateUserByAdminRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        user.UpdateProfile(request.FullName, request.PhoneNumber, user.AvatarUrl);
        user.AssignSystemRole(request.Role);

        if (request.IsActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            user.Id,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            Role = user.Role.ToString(),
            user.IsActive,
            user.IsEmailConfirmed,
            user.CreatedAt
        });
    }

    /// <summary>
    /// DELETE /api/admin/users/{id}
    /// Vô hiệu hóa (Soft delete) người dùng bởi Administrator.
    /// </summary>
    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        user.Deactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"Đã vô hiệu hóa tài khoản {user.Email} thành công." });
    }

    /// <summary>
    /// PATCH /api/admin/users/{id}/toggle-status
    /// Khóa hoặc mở khóa tài khoản người dùng nhanh chóng.
    /// </summary>
    [HttpPatch("users/{id:guid}/toggle-status")]
    public async Task<IActionResult> ToggleUserStatus(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        if (user.IsActive)
        {
            user.Deactivate();
        }
        else
        {
            user.Activate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            user.Id,
            user.Email,
            user.IsActive,
            message = user.IsActive ? "Đã kích hoạt tài khoản." : "Đã khóa tài khoản."
        });
    }

    /// <summary>
    /// POST /api/admin/users/{id}/reset-password
    /// Đặt lại mật khẩu tài khoản người dùng bởi Administrator.
    /// </summary>
    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetUserPassword(
        Guid id,
        [FromBody] ResetPasswordByAdminRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        var newHash = _passwordHasher.HashPassword(request.NewPassword);
        user.ResetPassword(newHash);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { message = $"Đã đặt lại mật khẩu cho tài khoản {user.Email} thành công." });
    }

    [HttpGet("system-stats")]
    public async Task<IActionResult> GetSystemStats(CancellationToken cancellationToken)
    {
        var totalUsers    = await _dbContext.Users.CountAsync(cancellationToken);
        var adminCount    = await _dbContext.Users.CountAsync(u => u.Role == SystemRole.Admin, cancellationToken);
        var userCount     = await _dbContext.Users.CountAsync(u => u.Role == SystemRole.User, cancellationToken);
        var workspaceCount = await _dbContext.StudioWorkspaces.CountAsync(cancellationToken);
        var memberCount   = await _dbContext.WorkspaceMembers.CountAsync(cancellationToken);

        return Ok(new
        {
            TotalUsers = totalUsers,
            Admins = adminCount,
            RegisteredUsers = userCount,
            TotalWorkspaces = workspaceCount,
            TotalWorkspaceMembers = memberCount,
            ServerTimeUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// GET /api/admin/audit-logs
    /// Tra cứu toàn bộ Audit Logs hệ thống (UC-15, BR-18).
    /// </summary>
    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? action,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAuditLogsQuery(workspaceId, action, page, pageSize);
        var result = await _mediator.Send(query, cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/admin/ai-usage
    /// Thống kê mức độ sử dụng AI Token theo tất cả các Workspace (UC-15).
    /// </summary>
    [HttpGet("ai-usage")]
    public async Task<IActionResult> GetAiUsage(CancellationToken cancellationToken)
    {
        var usageList = await _dbContext.WorkspaceAiConfigs
            .Include(c => c.Workspace)
            .Select(c => new
            {
                c.WorkspaceId,
                WorkspaceName = c.Workspace.Name,
                c.Provider,
                c.MonthlyTokenQuota,
                c.UsedTokensCurrentMonth,
                UsagePercent = c.MonthlyTokenQuota > 0 ? (double)c.UsedTokensCurrentMonth / c.MonthlyTokenQuota * 100 : 0,
                c.IsEnabled
            })
            .ToListAsync(cancellationToken);

        return Ok(usageList);
    }

    // ─── BR-22: Studio Creation Permission ────────────────────────────────────

    /// <summary>
    /// POST /api/admin/users/{id}/grant-studio-permission
    /// Grant CanCreateStudio permission to a User (BR-22).
    /// </summary>
    [HttpPost("users/{id:guid}/grant-studio-permission")]
    public async Task<IActionResult> GrantStudioPermission(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GrantStudioCreationPermissionCommand(id), cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(new
        {
            message = $"Đã cấp quyền tạo Studio cho người dùng {result.Value!.Email}.",
            result.Value.UserId,
            result.Value.Email,
            result.Value.CanCreateStudio
        });
    }

    /// <summary>
    /// POST /api/admin/users/{id}/revoke-studio-permission
    /// Revoke CanCreateStudio permission from a User (BR-22).
    /// </summary>
    [HttpPost("users/{id:guid}/revoke-studio-permission")]
    public async Task<IActionResult> RevokeStudioPermission(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RevokeStudioCreationPermissionCommand(id), cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(new
        {
            message = $"Đã thu hồi quyền tạo Studio của người dùng {result.Value!.Email}.",
            result.Value.UserId,
            result.Value.Email,
            result.Value.CanCreateStudio
        });
    }

    // ─── Element Type Master Data ──────────────────────────────────────────────

    [HttpGet("element-types")]
    public async Task<IActionResult> GetElementTypes(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetElementTypesQuery(), cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("element-types/{id:guid}")]
    public async Task<IActionResult> GetElementTypeById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetElementTypeByIdQuery(id), cancellationToken);
        if (!result.IsSuccess) return NotFound(new { message = result.ErrorMessage });
        return Ok(result.Value);
    }

    [HttpPost("element-types")]
    public async Task<IActionResult> CreateElementType(
        [FromBody] CreateElementTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateElementTypeCommand(request.Code, request.Name, request.Description, request.AllowedPropertiesJson),
            cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("element-types/{id:guid}")]
    public async Task<IActionResult> UpdateElementType(
        Guid id, [FromBody] UpdateElementTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateElementTypeCommand(id, request.Name, request.Description, request.AllowedPropertiesJson, request.IsActive),
            cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(result.Value);
    }

    [HttpDelete("element-types/{id:guid}")]
    public async Task<IActionResult> DeleteElementType(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateElementTypeCommand(id), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }

    // ─── Export Preset Master Data ─────────────────────────────────────────────

    [HttpGet("export-presets")]
    public async Task<IActionResult> GetExportPresets(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetExportPresetsQuery(), cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("export-presets/{id:guid}")]
    public async Task<IActionResult> GetExportPresetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetExportPresetByIdQuery(id), cancellationToken);
        if (!result.IsSuccess) return NotFound(new { message = result.ErrorMessage });
        return Ok(result.Value);
    }

    [HttpPost("export-presets")]
    public async Task<IActionResult> CreateExportPreset(
        [FromBody] CreateExportPresetRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateExportPresetCommand(request.Code, request.Name, request.FormatName, request.ConfigOptionsJson),
            cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("export-presets/{id:guid}")]
    public async Task<IActionResult> UpdateExportPreset(
        Guid id, [FromBody] UpdateExportPresetRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateExportPresetCommand(id, request.Name, request.FormatName, request.ConfigOptionsJson, request.IsActive),
            cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(result.Value);
    }

    [HttpDelete("export-presets/{id:guid}")]
    public async Task<IActionResult> DeleteExportPreset(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateExportPresetCommand(id), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }

    // ─── Pipeline Template Master Data ────────────────────────────────────────

    [HttpGet("pipeline-templates")]
    public async Task<IActionResult> GetPipelineTemplates(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPipelineTemplatesQuery(), cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("pipeline-templates/{id:guid}")]
    public async Task<IActionResult> GetPipelineTemplateById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPipelineTemplateByIdQuery(id), cancellationToken);
        if (!result.IsSuccess) return NotFound(new { message = result.ErrorMessage });
        return Ok(result.Value);
    }

    [HttpPost("pipeline-templates")]
    public async Task<IActionResult> CreatePipelineTemplate(
        [FromBody] CreatePipelineTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreatePipelineTemplateCommand(request.Code, request.Name, request.Description, request.IsDefault, request.Stages),
            cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("pipeline-templates/{id:guid}")]
    public async Task<IActionResult> UpdatePipelineTemplate(
        Guid id, [FromBody] UpdatePipelineTemplateRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdatePipelineTemplateCommand(id, request.Name, request.Description, request.IsDefault, request.IsActive, request.Stages),
            cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(result.Value);
    }

    [HttpDelete("pipeline-templates/{id:guid}")]
    public async Task<IActionResult> DeletePipelineTemplate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivatePipelineTemplateCommand(id), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }
}
