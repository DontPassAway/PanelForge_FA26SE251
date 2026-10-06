using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Commands.AssignUserRole;
using PanelForge.Application.Features.Admin.Commands.CreateUserByAdmin;
using PanelForge.Application.Features.Admin.Commands.DeactivateUser;
using PanelForge.Application.Features.Admin.Commands.ResetUserPasswordByAdmin;
using PanelForge.Application.Features.Admin.Commands.ToggleUserStatus;
using PanelForge.Application.Features.Admin.Commands.UpdateUserByAdmin;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Admin.Queries.GetAdminUsers;
using PanelForge.Application.Features.Admin.Queries.GetSystemStats;
using PanelForge.Application.Features.Audit.Queries.ExportAuditLogs;
using PanelForge.Application.Features.Audit.Queries.GetAuditLogs;
using PanelForge.Application.Features.Users.Commands.GrantStudioPermission;
using PanelForge.Application.Features.Users.Commands.RevokeStudioPermission;
using PanelForge.Application.Features.MasterData.ElementTypes.Models;
using PanelForge.Application.Features.MasterData.ElementTypes.Commands.CreateElementType;
using PanelForge.Application.Features.MasterData.ElementTypes.Commands.UpdateElementType;
using PanelForge.Application.Features.MasterData.ElementTypes.Commands.DeactivateElementType;
using PanelForge.Application.Features.MasterData.ElementTypes.Queries.GetElementTypes;
using PanelForge.Application.Features.MasterData.ElementTypes.Queries.GetElementTypeById;
using PanelForge.Application.Features.MasterData.ExportPresets.Models;
using PanelForge.Application.Features.MasterData.ExportPresets.Commands.CreateExportPreset;
using PanelForge.Application.Features.MasterData.ExportPresets.Commands.UpdateExportPreset;
using PanelForge.Application.Features.MasterData.ExportPresets.Commands.DeactivateExportPreset;
using PanelForge.Application.Features.MasterData.ExportPresets.Queries.GetExportPresets;
using PanelForge.Application.Features.MasterData.ExportPresets.Queries.GetExportPresetById;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Models;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Commands.CreatePipelineTemplate;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Commands.UpdatePipelineTemplate;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Commands.DeactivatePipelineTemplate;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Queries.GetPipelineTemplates;
using PanelForge.Application.Features.MasterData.PipelineTemplates.Queries.GetPipelineTemplateById;
using PanelForge.Application.Features.AiConfig.Commands.UpdateWorkspaceAiConfig;
using PanelForge.Application.Features.AiConfig.Queries.GetWorkspaceAiConfig;
using PanelForge.Application.Features.AiConfig.Models;
using PanelForge.Application.Features.Workspaces.Commands.UpdateWorkspaceSessionPolicy;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceSessionPolicy;
using PanelForge.Application.Features.AiUsage.Queries.GetAiUsageReport;
using PanelForge.Application.Features.AiUsage.Queries.GetAiUsageRecords;
using PanelForge.Application.Features.AiUsage.Queries.ExportAiUsageRecords;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetAdminUsersQuery(page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// POST /api/admin/assign-producer (và POST /api/admin/assign-role)
    /// SystemRole chỉ gồm Admin | User (E-11). "Producer" là WorkspaceRole, nên gán "Producer" ở đây
    /// nghĩa là cấp quyền tạo Studio (CanCreateStudio = true, BR-22); SystemRole vẫn là User.
    /// Khi Studio được tạo, người tạo tự trở thành Producer trong workspace_members.
    /// </summary>
    [HttpPost("assign-producer")]
    [HttpPost("assign-role")]
    public async Task<IActionResult> AssignProducer(
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AssignUserRoleCommand(request.UserId, request.NewRole), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
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
        var result = await _mediator.Send(new CreateUserByAdminCommand(
            request.Email, request.FullName, request.Password, request.PhoneNumber, request.Role, request.IsActive),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToError(result);
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
        var result = await _mediator.Send(new UpdateUserByAdminCommand(
            id, request.FullName, request.PhoneNumber, request.Role, request.IsActive),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// DELETE /api/admin/users/{id}
    /// Vô hiệu hóa (Soft delete) người dùng bởi Administrator.
    /// </summary>
    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateUserCommand(id), cancellationToken);
        if (!result.IsSuccess) return ToError(result);

        return Ok(new { message = $"Đã vô hiệu hóa tài khoản {result.Value!.Email} thành công." });
    }

    /// <summary>
    /// PATCH /api/admin/users/{id}/toggle-status
    /// Khóa hoặc mở khóa tài khoản người dùng nhanh chóng.
    /// </summary>
    [HttpPatch("users/{id:guid}/toggle-status")]
    public async Task<IActionResult> ToggleUserStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ToggleUserStatusCommand(id), cancellationToken);
        if (!result.IsSuccess) return ToError(result);

        var user = result.Value!;
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
        var result = await _mediator.Send(new ResetUserPasswordByAdminCommand(id, request.NewPassword), cancellationToken);
        if (!result.IsSuccess) return ToError(result);

        return Ok(new { message = $"Đã đặt lại mật khẩu cho tài khoản {result.Value!.Email} thành công." });
    }

    [HttpGet("system-stats")]
    public async Task<IActionResult> GetSystemStats(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSystemStatsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    // NOT_FOUND → 404, lỗi nghiệp vụ khác → 400; body { message } như trước
    private IActionResult ToError<T>(Result<T> result)
        => result.ErrorCode == ResultErrorCodes.NotFound
            ? NotFound(new { message = result.ErrorMessage })
            : BadRequest(new { message = result.ErrorMessage });

    /// <summary>
    /// GET /api/admin/audit-logs
    /// Tra cứu toàn bộ Audit Logs hệ thống (UC-15, BR-18).
    /// </summary>
    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? action,
        [FromQuery] Guid? userId,
        [FromQuery] string? userEmail,
        [FromQuery] string? entityName,
        [FromQuery] string? entityId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAuditLogsQuery(workspaceId, action, page, pageSize, userId, userEmail, entityName, entityId, from, to);
        var result = await _mediator.Send(query, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/admin/audit-logs/export
    /// UC-15 bước 4: export audit log đã lọc ra CSV (cùng bộ lọc với GET audit-logs, tối đa 10.000 dòng mới nhất).
    /// Header X-Export-Truncated = true nếu kết quả bị cắt.
    /// </summary>
    [HttpGet("audit-logs/export")]
    public async Task<IActionResult> ExportAuditLogs(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? action,
        [FromQuery] Guid? userId,
        [FromQuery] string? userEmail,
        [FromQuery] string? entityName,
        [FromQuery] string? entityId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var filter = new GetAuditLogsQuery(workspaceId, action, 1, 1, userId, userEmail, entityName, entityId, from, to);
        var result = await _mediator.Send(new ExportAuditLogsQuery(filter), cancellationToken);
        return ToCsvFile(result);
    }

    /// <summary>
    /// GET /api/admin/ai-usage
    /// UC-15: báo cáo AI usage theo Workspace so với hạn mức, tổng hợp từ ai_usage_records trong kỳ [from, to).
    /// Mặc định kỳ = tháng hiện tại. Giữ nguyên các field cũ (workspaceId, workspaceName, provider,
    /// monthlyTokenQuota, usedTokensCurrentMonth, usagePercent, isEnabled).
    /// </summary>
    [HttpGet("ai-usage")]
    public async Task<IActionResult> GetAiUsage(
        [FromQuery] Guid? workspaceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAiUsageReportQuery(workspaceId, from, to), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/admin/ai-usage/records
    /// UC-15: danh sách chi tiết từng lần gọi AI (E-29), lọc theo workspace, user, tính năng, trạng thái, khoảng ngày.
    /// </summary>
    [HttpGet("ai-usage/records")]
    public async Task<IActionResult> GetAiUsageRecords(
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? userId,
        [FromQuery] string? feature,
        [FromQuery] AiUsageStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetAiUsageRecordsQuery(workspaceId, userId, feature, status, from, to, page, pageSize), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// GET /api/admin/ai-usage/export
    /// UC-15 bước 4: export chi tiết AI usage đã lọc ra CSV (tối đa 10.000 dòng mới nhất).
    /// </summary>
    [HttpGet("ai-usage/export")]
    public async Task<IActionResult> ExportAiUsage(
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? userId,
        [FromQuery] string? feature,
        [FromQuery] AiUsageStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var filter = new GetAiUsageRecordsQuery(workspaceId, userId, feature, status, from, to);
        var result = await _mediator.Send(new ExportAiUsageRecordsQuery(filter), cancellationToken);
        return ToCsvFile(result);
    }

    private IActionResult ToCsvFile(Result<CsvExport> result)
    {
        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        var export = result.Value!;
        Response.Headers["X-Export-Row-Count"] = export.RowCount.ToString();
        Response.Headers["X-Export-Truncated"] = export.Truncated ? "true" : "false";
        Response.Headers.AccessControlExposeHeaders = "Content-Disposition, X-Export-Row-Count, X-Export-Truncated";
        return File(export.Content, "text/csv; charset=utf-8", export.FileName);
    }

    // ─── UC-02: Workspace AI Configuration (Admin Only) ───────────────────────

    /// <summary>
    /// GET /api/admin/workspaces/{workspaceId}/ai-config
    /// Lấy cấu hình AI Provider, Models và Token Quota của Workspace bởi Administrator (UC-02).
    /// </summary>
    [HttpGet("workspaces/{workspaceId:guid}/ai-config")]
    public async Task<IActionResult> GetWorkspaceAiConfig(Guid workspaceId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkspaceAiConfigQuery(workspaceId), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/admin/workspaces/{workspaceId}/ai-config
    /// Cập nhật AI Provider, Models được phép và hạn ngạch Token Quota theo Workspace bởi Administrator (UC-02).
    /// </summary>
    [HttpPut("workspaces/{workspaceId:guid}/ai-config")]
    public async Task<IActionResult> UpdateWorkspaceAiConfig(
        Guid workspaceId,
        [FromBody] UpdateWorkspaceAiConfigRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateWorkspaceAiConfigCommand(
            WorkspaceId: workspaceId,
            Provider: request.Provider,
            ApiKey: request.ApiKey,
            IsEnabled: request.IsEnabled,
            MonthlyTokenQuota: request.MonthlyTokenQuota,
            AllowedModels: request.AllowedModels,
            MonthlyImageQuota: request.MonthlyImageQuota
        );

        var result = await _mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Value);
    }

    // ─── V1.2: Workspace Session Policy (Admin Only) ──────────────────────────

    /// <summary>GET /api/admin/workspaces/{workspaceId}/session-policy — thời gian sống, idle timeout, số phiên đồng thời.</summary>
    [HttpGet("workspaces/{workspaceId:guid}/session-policy")]
    public async Task<IActionResult> GetWorkspaceSessionPolicy(Guid workspaceId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkspaceSessionPolicyQuery(workspaceId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : SessionPolicyError(result);
    }

    /// <summary>
    /// PUT /api/admin/workspaces/{workspaceId}/session-policy — thay toàn bộ policy (field null = bỏ giới hạn đó).
    /// Áp dụng ở lần đăng nhập / làm mới token tiếp theo của các thành viên.
    /// </summary>
    [HttpPut("workspaces/{workspaceId:guid}/session-policy")]
    public async Task<IActionResult> UpdateWorkspaceSessionPolicy(
        Guid workspaceId,
        [FromBody] UpdateWorkspaceSessionPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateWorkspaceSessionPolicyCommand(
            workspaceId, request.SessionLifetimeMinutes, request.IdleTimeoutMinutes, request.MaxConcurrentSessions), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : SessionPolicyError(result);
    }

    private IActionResult SessionPolicyError<T>(Result<T> result)
        => result.ErrorCode == ResultErrorCodes.NotFound
            ? NotFound(new { message = result.ErrorMessage })
            : BadRequest(new { message = result.ErrorMessage });

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
