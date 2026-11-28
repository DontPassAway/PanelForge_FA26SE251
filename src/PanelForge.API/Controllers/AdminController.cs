using MediatR;
using PanelForge.Application.Features.Quotas.Commands.UpdateUploadLimits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Admin.Commands.AssignUserRole;
using PanelForge.Application.Features.Admin.Commands.CreateUserByAdmin;
using PanelForge.Application.Features.Admin.Commands.DeactivateUser;
using PanelForge.Application.Features.Admin.Commands.ResetUserPasswordByAdmin;
using PanelForge.Application.Features.Admin.Commands.RestoreEmailSettings;
using PanelForge.Application.Features.Admin.Commands.SendTestEmail;
using PanelForge.Application.Features.Admin.Commands.SetStudioPackagePrice;
using PanelForge.Application.Features.Admin.Commands.UpdateEmailSettings;
using PanelForge.Application.Features.Admin.Commands.SendTestEmailTemplate;
using PanelForge.Application.Features.Admin.Commands.UpdateEmailTemplate;
using PanelForge.Application.Common.Email;
using PanelForge.Application.Features.Admin.Queries.GetEmailSettings;
using PanelForge.Application.Features.Admin.Queries.GetEmailSettingsHistory;
using PanelForge.Application.Features.Admin.Queries.GetEmailTemplates;
using PanelForge.Application.Features.Admin.Queries.PreviewEmailTemplate;
using PanelForge.Application.Features.Admin.Commands.ToggleUserStatus;
using PanelForge.Application.Features.Admin.Commands.UpdateUserByAdmin;
using PanelForge.Application.Features.Admin.Commands.UpdateWorkspaceQuota;
using PanelForge.Application.Features.Admin.Models;
using PanelForge.Application.Features.Admin.Queries.GetAdminStudioPackage;
using PanelForge.Application.Features.Admin.Queries.GetAdminUsers;
using PanelForge.Application.Features.Admin.Queries.GetAdminWorkspaces;
using PanelForge.Application.Features.Admin.Queries.GetSystemStats;
using PanelForge.Application.Features.Admin.Queries.GetAdminUserDetail;
using PanelForge.Application.Features.Admin.Queries.GetAdminWorkspaceDetail;
using PanelForge.Application.Features.Admin.Queries.GetSystemHealth;
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
using PanelForge.Application.Features.AiConfig.Commands.CreateAiProviderCredential;
using PanelForge.Application.Features.AiConfig.Commands.UpdateAiProviderCredential;
using PanelForge.Application.Features.AiConfig.Commands.DeleteAiProviderCredential;
using PanelForge.Application.Features.AiConfig.Commands.TestAiProviderCredential;
using PanelForge.Application.Features.AiConfig.Commands.SetWorkspaceAiProvider;
using PanelForge.Application.Features.AiConfig.Commands.SetDefaultAiProvider;
using PanelForge.Application.Features.AiConfig.Commands.SetWorkspaceAiImageProvider;
using PanelForge.Application.Features.AiConfig.Queries.GetAiProviderCredentials;
using PanelForge.Application.Features.AiConfig.Queries.GetAiProviderModels;
using PanelForge.Application.Features.Workspaces.Commands.UpdateWorkspaceSessionPolicy;
using PanelForge.Application.Features.Workspaces.Models;
using PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceSessionPolicy;
using PanelForge.Application.Features.AiUsage.Queries.GetAiUsageReport;
using PanelForge.Application.Features.AiUsage.Queries.GetAiUsageRecords;
using PanelForge.Application.Features.AiUsage.Queries.ExportAiUsageRecords;
using PanelForge.Domain.Enums;
using System.Security.Claims;
using PanelForge.Application.Features.Quotas.Models;
using PanelForge.Application.Features.Quotas.Queries.GetQuotaPolicy;
using PanelForge.Application.Features.Quotas.Commands.UpdateQuotaPolicy;
using PanelForge.Application.Features.Quotas.Queries.GetQuotaIncreaseRequests;
using PanelForge.Application.Features.Quotas.Commands.ApproveQuotaIncreaseRequest;
using PanelForge.Application.Features.Quotas.Commands.RejectQuotaIncreaseRequest;
using PanelForge.Application.Features.Quotas.Queries.GetAdminResources;
using PanelForge.Application.Features.Quotas.Commands.UpdateWorkspaceResources;
using PanelForge.Application.Features.Quotas.Commands.ReconcileStorage;
using PanelForge.Application.Features.Quotas.Commands.DeleteOrphanFiles;
using PanelForge.API.Common;
using PanelForge.Application.Features.AiUsage.Models;
using PanelForge.Application.Features.Audit.Models;

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
    [ProducesResponseType(typeof(AdminUserListResponse), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(AssignUserRoleResponse), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status201Created)]
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
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(SystemStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSystemStats(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSystemStatsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// GET /api/admin/system-health — gọi thử DB, kho file, service AI và đếm việc nền kẹt/lỗi (OPS-32).
    /// Luôn 200: thành phần hỏng nằm trong body với status Down.
    /// </summary>
    [HttpGet("system-health")]
    [ProducesResponseType(typeof(SystemHealthDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSystemHealth(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSystemHealthQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/admin/users/{id} — chi tiết tài khoản: phiên, Studio, Ví, AI tháng này, nhật ký gần đây (OPS-30).</summary>
    [HttpGet("users/{id:guid}")]
    [ProducesResponseType(typeof(AdminUserDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserDetail(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminUserDetailQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/admin/workspaces/{id} — chi tiết Studio: tài nguyên, thành viên, Series, yêu cầu, AI 30 ngày (OPS-31).</summary>
    [HttpGet("workspaces/{id:guid}")]
    [ProducesResponseType(typeof(AdminWorkspaceDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaceDetail(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminWorkspaceDetailQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    // NOT_FOUND → 404, CONFLICT → 409, lỗi nghiệp vụ khác → 400; body { message } như trước
    private IActionResult ToError<T>(Result<T> result) =>
        result.ErrorCode switch
        {
            ResultErrorCodes.NotFound => NotFound(new { message = result.ErrorMessage }),
            ResultErrorCodes.Conflict => Conflict(new { message = result.ErrorMessage, code = result.ErrorCode }),
            _ => BadRequest(new { message = result.ErrorMessage })
        };

    /// <summary>
    /// GET /api/admin/audit-logs
    /// Tra cứu toàn bộ Audit Logs hệ thống (UC-15, BR-18).
    /// </summary>
    [HttpGet("audit-logs")]
    [ProducesResponseType(typeof(AuditLogsPagedResponse), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(IReadOnlyList<AiUsageReportItemDto>), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(AiUsageRecordsPagedResponse), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(WorkspaceAiConfigDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaceAiConfig(Guid workspaceId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkspaceAiConfigQuery(workspaceId), cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Value);
    }

    /// <summary>
    /// PUT /api/admin/workspaces/{workspaceId}/ai-config
    /// Bật/tắt AI, Models được phép và quota theo Workspace (UC-02). API chọn qua ai-provider và ai-image-provider.
    /// </summary>
    [HttpPut("workspaces/{workspaceId:guid}/ai-config")]
    [ProducesResponseType(typeof(WorkspaceAiConfigDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateWorkspaceAiConfig(
        Guid workspaceId,
        [FromBody] UpdateWorkspaceAiConfigRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateWorkspaceAiConfigCommand(
            WorkspaceId: workspaceId,
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

    // ─── UC-02 mở rộng: nhiều API AI (Gemini / Cloudflare Workers AI) ─────────

    /// <summary>GET /api/admin/ai-providers — danh sách API AI đã khai báo (không trả key, chỉ KeyHint).</summary>
    [HttpGet("ai-providers")]
    [ProducesResponseType(typeof(IReadOnlyList<AiProviderCredentialDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAiProviders(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAiProviderCredentialsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>POST /api/admin/ai-providers — thêm API AI; key được gọi thử rồi mã hoá trước khi lưu.</summary>
    [HttpPost("ai-providers")]
    [ProducesResponseType(typeof(AiProviderCredentialDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAiProvider([FromBody] CreateAiProviderCredentialRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateAiProviderCredentialCommand(request), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : ToError(result);
    }

    /// <summary>
    /// PUT /api/admin/ai-providers/default — TASK-355: API mặc định cho Studio chưa tự chọn API.
    /// Body { kind: "text"|"image", providerCredentialId } (null = bỏ mặc định). Trả lại danh sách API.
    /// </summary>
    [HttpPut("ai-providers/default")]
    [ProducesResponseType(typeof(IReadOnlyList<AiProviderCredentialDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDefaultAiProvider([FromBody] SetDefaultAiProviderRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetDefaultAiProviderCommand(request.Kind, request.ProviderCredentialId), cancellationToken);
        if (!result.IsSuccess) return ToError(result);
        var list = await _mediator.Send(new GetAiProviderCredentialsQuery(), cancellationToken);
        return list.IsSuccess ? Ok(list.Value) : ToError(list);
    }

    /// <summary>PUT /api/admin/ai-providers/{id} — sửa API AI (ApiKey trống = giữ key cũ).</summary>
    [HttpPut("ai-providers/{id:guid}")]
    [ProducesResponseType(typeof(AiProviderCredentialDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAiProvider(Guid id, [FromBody] UpdateAiProviderCredentialRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateAiProviderCredentialCommand(id, request), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>DELETE /api/admin/ai-providers/{id} — xoá mềm; 409 nếu còn Workspace đang dùng.</summary>
    [HttpDelete("ai-providers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAiProvider(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteAiProviderCredentialCommand(id), cancellationToken);
        if (result.IsSuccess) return NoContent();
        return result.ErrorCode == ResultErrorCodes.Conflict ? Conflict(new { message = result.ErrorMessage }) : ToError(result);
    }

    /// <summary>
    /// GET /api/admin/ai-providers/{id}/models — model sinh chữ (kind=text) và sinh ảnh (kind=image) của API Cloudflare đã lưu.
    /// free=true: model @cf/ (phần miễn phí Workers AI); false: model đối tác cần credit.
    /// </summary>
    [HttpGet("ai-providers/{id:guid}/models")]
    [ProducesResponseType(typeof(IReadOnlyList<AiProviderModelDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAiProviderModels(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAiProviderModelsQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>POST /api/admin/ai-providers/{id}/test — gọi thử provider bằng key đã lưu.</summary>
    [HttpPost("ai-providers/{id:guid}/test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TestAiProvider(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new TestAiProviderCredentialCommand(id), cancellationToken);
        return result.IsSuccess ? Ok(new { ok = true }) : ToError(result);
    }

    /// <summary>
    /// PUT /api/admin/workspaces/{workspaceId}/ai-provider — chọn API AI cho Workspace (body providerCredentialId, null = bỏ chọn).
    /// Trả lại cấu hình AI của Workspace.
    /// </summary>
    [HttpPut("workspaces/{workspaceId:guid}/ai-provider")]
    [ProducesResponseType(typeof(WorkspaceAiConfigDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetWorkspaceAiProvider(Guid workspaceId, [FromBody] SetWorkspaceAiProviderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetWorkspaceAiProviderCommand(workspaceId, request.ProviderCredentialId), cancellationToken);
        if (!result.IsSuccess) return ToError(result);
        var config = await _mediator.Send(new GetWorkspaceAiConfigQuery(workspaceId), cancellationToken);
        return config.IsSuccess ? Ok(config.Value) : ToError(config);
    }

    /// <summary>
    /// PUT /api/admin/workspaces/{workspaceId}/ai-image-provider — chọn API riêng cho sinh ảnh (credential phải có model ảnh).
    /// Body providerCredentialId, null = sinh ảnh theo API chữ như trước. Trả lại cấu hình AI của Workspace.
    /// </summary>
    [HttpPut("workspaces/{workspaceId:guid}/ai-image-provider")]
    [ProducesResponseType(typeof(WorkspaceAiConfigDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetWorkspaceAiImageProvider(Guid workspaceId, [FromBody] SetWorkspaceAiProviderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetWorkspaceAiImageProviderCommand(workspaceId, request.ProviderCredentialId), cancellationToken);
        if (!result.IsSuccess) return ToError(result);
        var config = await _mediator.Send(new GetWorkspaceAiConfigQuery(workspaceId), cancellationToken);
        return config.IsSuccess ? Ok(config.Value) : ToError(config);
    }

    // ─── V1.2: Workspace Session Policy (Admin Only) ──────────────────────────

    /// <summary>GET /api/admin/workspaces/{workspaceId}/session-policy — thời gian sống, idle timeout, số phiên đồng thời.</summary>
    [HttpGet("workspaces/{workspaceId:guid}/session-policy")]
    [ProducesResponseType(typeof(WorkspaceSessionPolicyDto), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(WorkspaceSessionPolicyDto), StatusCodes.Status200OK)]
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

    // ─── Cấu hình email hệ thống (SMTP) ─────────────────────────────────────────

    /// <summary>GET /api/admin/email-settings — cấu hình SMTP đang dùng, không kèm mật khẩu.</summary>
    [HttpGet("email-settings")]
    [ProducesResponseType(typeof(AdminEmailSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmailSettings(CancellationToken cancellationToken)
        => Ok((await _mediator.Send(new GetEmailSettingsQuery(), cancellationToken)).Value);

    /// <summary>PUT /api/admin/email-settings — lưu cấu hình SMTP (append-only, có audit). Password trống = giữ mật khẩu cũ.</summary>
    [HttpPut("email-settings")]
    [ProducesResponseType(typeof(AdminEmailSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateEmailSettings(
        [FromBody] UpdateEmailSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = CurrentUserId();
        if (adminId is null) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new UpdateEmailSettingsCommand(
            adminId.Value, request.SmtpServer, request.SmtpPort, request.SenderName, request.SenderEmail, request.Password),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.ErrorMessage, code = result.ErrorCode });
    }

    /// <summary>GET /api/admin/email-settings/history — các phiên bản cấu hình SMTP, mới nhất trước.</summary>
    [HttpGet("email-settings/history")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminEmailSettingsVersionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmailSettingsHistory(CancellationToken cancellationToken)
        => Ok((await _mediator.Send(new GetEmailSettingsHistoryQuery(), cancellationToken)).Value);

    /// <summary>POST /api/admin/email-settings/history/{versionId}/restore — dùng lại phiên bản cũ (thêm bản mới, có audit).</summary>
    [HttpPost("email-settings/history/{versionId:guid}/restore")]
    [ProducesResponseType(typeof(AdminEmailSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreEmailSettings(Guid versionId, CancellationToken cancellationToken)
    {
        var adminId = CurrentUserId();
        if (adminId is null) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new RestoreEmailSettingsCommand(adminId.Value, versionId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>POST /api/admin/email-settings/test — gửi email thử tới chính Admin. 503 khi SMTP không gửi được.</summary>
    [HttpPost("email-settings/test")]
    [ProducesResponseType(typeof(SendTestEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SendTestEmail(CancellationToken cancellationToken)
    {
        var adminId = CurrentUserId();
        if (adminId is null) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new SendTestEmailCommand(adminId.Value), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ErrorCode == ResultErrorCodes.EmailDeliveryFailed
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = result.ErrorMessage, code = result.ErrorCode })
                : ToError(result);
    }

    // ─── Mẫu email hệ thống ─────────────────────────────────────────────────────

    /// <summary>GET /api/admin/email-templates — mẫu đang dùng của từng loại email, kèm mẫu mặc định và biến dùng được.</summary>
    [HttpGet("email-templates")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminEmailTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmailTemplates(CancellationToken cancellationToken)
        => Ok((await _mediator.Send(new GetEmailTemplatesQuery(), cancellationToken)).Value);

    /// <summary>PUT /api/admin/email-templates/{key} — lưu nội dung một loại email (append-only, có audit).</summary>
    [HttpPut("email-templates/{key}")]
    [ProducesResponseType(typeof(AdminEmailTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateEmailTemplate(
        string key,
        [FromBody] EmailTemplateContent request,
        CancellationToken cancellationToken)
    {
        var adminId = CurrentUserId();
        if (adminId is null) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new UpdateEmailTemplateCommand(adminId.Value, key, request), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>POST /api/admin/email-templates/{key}/preview — dựng email từ nội dung đang soạn với dữ liệu mẫu (không lưu).</summary>
    [HttpPost("email-templates/{key}/preview")]
    [ProducesResponseType(typeof(EmailTemplatePreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreviewEmailTemplate(
        string key,
        [FromBody] EmailTemplateContent request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PreviewEmailTemplateQuery(key, request), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>POST /api/admin/email-templates/{key}/test — gửi nội dung đang soạn tới chính Admin. 503 khi SMTP không gửi được.</summary>
    [HttpPost("email-templates/{key}/test")]
    [ProducesResponseType(typeof(SendTestEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SendTestEmailTemplate(
        string key,
        [FromBody] EmailTemplateContent request,
        CancellationToken cancellationToken)
    {
        var adminId = CurrentUserId();
        if (adminId is null) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new SendTestEmailTemplateCommand(adminId.Value, key, request), cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : result.ErrorCode == ResultErrorCodes.EmailDeliveryFailed
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = result.ErrorMessage, code = result.ErrorCode })
                : ToError(result);
    }

    // ─── D11: Gói mở Studio ────────────────────────────────────────────────────

    /// <summary>GET /api/admin/studio-package — giá gói mở Studio hiện hành và số lượt đã mua.</summary>
    [HttpGet("studio-package")]
    [ProducesResponseType(typeof(AdminStudioPackageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStudioPackage(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminStudioPackageQuery(), cancellationToken);
        return Ok(result.Value);
    }

    /// <summary>PUT /api/admin/studio-package/price — đặt giá gói mở Studio (append-only, có audit).</summary>
    [HttpPut("studio-package/price")]
    [ProducesResponseType(typeof(AdminStudioPackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetStudioPackagePrice(
        [FromBody] SetStudioPackagePriceRequest request,
        CancellationToken cancellationToken)
    {
        var adminId = CurrentUserId();
        if (adminId is null) return Unauthorized(new { message = "Chưa xác thực người dùng." });

        var result = await _mediator.Send(new SetStudioPackagePriceCommand(adminId.Value, request.PriceVnd), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(new { message = result.ErrorMessage });
    }

    // ─── Element Type Master Data ──────────────────────────────────────────────

    [HttpGet("element-types")]
    [ProducesResponseType(typeof(IReadOnlyList<ElementTypeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetElementTypes(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetElementTypesQuery(), cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("element-types/{id:guid}")]
    [ProducesResponseType(typeof(ElementTypeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetElementTypeById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetElementTypeByIdQuery(id), cancellationToken);
        if (!result.IsSuccess) return NotFound(new { message = result.ErrorMessage });
        return Ok(result.Value);
    }

    [HttpPost("element-types")]
    [ProducesResponseType(typeof(ElementTypeDto), StatusCodes.Status201Created)]
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
    [ProducesResponseType(typeof(ElementTypeDto), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteElementType(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateElementTypeCommand(id), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }

    // ─── Export Preset Master Data ─────────────────────────────────────────────

    [HttpGet("export-presets")]
    [ProducesResponseType(typeof(IReadOnlyList<ExportPresetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExportPresets(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetExportPresetsQuery(), cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("export-presets/{id:guid}")]
    [ProducesResponseType(typeof(ExportPresetDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExportPresetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetExportPresetByIdQuery(id), cancellationToken);
        if (!result.IsSuccess) return NotFound(new { message = result.ErrorMessage });
        return Ok(result.Value);
    }

    [HttpPost("export-presets")]
    [ProducesResponseType(typeof(ExportPresetDto), StatusCodes.Status201Created)]
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
    [ProducesResponseType(typeof(ExportPresetDto), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteExportPreset(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivateExportPresetCommand(id), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }

    // ─── Pipeline Template Master Data ────────────────────────────────────────

    [HttpGet("pipeline-templates")]
    [ProducesResponseType(typeof(IReadOnlyList<PipelineTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPipelineTemplates(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPipelineTemplatesQuery(), cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("pipeline-templates/{id:guid}")]
    [ProducesResponseType(typeof(PipelineTemplateDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPipelineTemplateById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPipelineTemplateByIdQuery(id), cancellationToken);
        if (!result.IsSuccess) return NotFound(new { message = result.ErrorMessage });
        return Ok(result.Value);
    }

    [HttpPost("pipeline-templates")]
    [ProducesResponseType(typeof(PipelineTemplateDto), StatusCodes.Status201Created)]
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
    [ProducesResponseType(typeof(PipelineTemplateDto), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeletePipelineTemplate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeactivatePipelineTemplateCommand(id), cancellationToken);
        if (!result.IsSuccess) return BadRequest(new { message = result.ErrorMessage });
        return Ok(new { message = result.Value });
    }

    /// <summary>
    /// GET /api/admin/workspaces
    /// Danh sách tất cả Studio Workspaces trong hệ thống cho Administrator (BR-16).
    /// </summary>
    [HttpGet("workspaces")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminWorkspaceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaces(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminWorkspacesQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// PUT /api/admin/workspaces/{id}/quota
    /// Cập nhật dung lượng lưu trữ (Storage Quota) của Studio Workspace bởi Administrator (BR-16).
    /// </summary>
    [HttpPut("workspaces/{id:guid}/quota")]
    [ProducesResponseType(typeof(AdminWorkspaceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateWorkspaceQuota(
        Guid id,
        [FromBody] UpdateWorkspaceQuotaRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateWorkspaceQuotaCommand(id, request.StorageQuotaBytes), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }
    // ─── OPS-20..23: chính sách hạn mức, yêu cầu tăng hạn mức, màn Tài nguyên ───

    /// <summary>GET /api/admin/quota-policy — hạn mức cấp cho Studio mới (OPS-20).</summary>
    [HttpGet("quota-policy")]
    [ProducesResponseType(typeof(QuotaPolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuotaPolicy(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetQuotaPolicyQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>PUT /api/admin/quota-policy — lưu chính sách mới, chỉ áp cho Studio tạo sau đó (OPS-20).</summary>
    [HttpPut("quota-policy")]
    [ProducesResponseType(typeof(QuotaPolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateQuotaPolicy([FromBody] UpdateQuotaPolicyRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } adminId) return Unauthorized();
        var result = await _mediator.Send(new UpdateQuotaPolicyCommand(
            adminId, request.StorageQuotaBytes, request.MonthlyTokenQuota, request.MonthlyImageQuota,
            request.AiEnabledByDefault, request.WarningThresholdPercent,
            request.MonthlyRequestQuota, request.RequestsPerMinute), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// PUT /api/admin/upload-limits — giới hạn kích thước một file tải lên cho toàn nền tảng, áp ngay (đọc qua
    /// <c>GET /api/assets/upload-limits</c>). Mỗi lần lưu là một bản ghi mới.
    /// </summary>
    [HttpPut("upload-limits")]
    [ProducesResponseType(typeof(UploadLimitsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateUploadLimits([FromBody] UpdateUploadLimitsRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } adminId) return Unauthorized();
        var result = await _mediator.Send(new UpdateUploadLimitsCommand(
            adminId, request.ArtworkMaxBytes, request.ReferenceImageMaxBytes, request.CoverImageMaxBytes), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/admin/quota-requests?status=Pending — yêu cầu tăng hạn mức của mọi Studio (OPS-21).</summary>
    [HttpGet("quota-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<QuotaIncreaseRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuotaRequests(
        [FromQuery] QuotaIncreaseRequestStatus? status, [FromQuery] QuotaResourceType? resourceType, [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetQuotaIncreaseRequestsQuery(null, status, ResourceType: resourceType, Search: search), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>POST /api/admin/quota-requests/{id}/approve — duyệt, cấp mức xin hoặc mức Admin chọn (OPS-21).</summary>
    [HttpPost("quota-requests/{id:guid}/approve")]
    [ProducesResponseType(typeof(QuotaIncreaseRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveQuotaRequest(
        Guid id, [FromBody] ApproveQuotaIncreaseRequestBody request, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } adminId) return Unauthorized();
        var result = await _mediator.Send(
            new ApproveQuotaIncreaseRequestCommand(id, adminId, request.GrantedQuota, request.Note), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>POST /api/admin/quota-requests/{id}/reject — từ chối kèm lý do (OPS-21).</summary>
    [HttpPost("quota-requests/{id:guid}/reject")]
    [ProducesResponseType(typeof(QuotaIncreaseRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectQuotaRequest(
        Guid id, [FromBody] RejectQuotaIncreaseRequestBody request, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } adminId) return Unauthorized();
        var result = await _mediator.Send(new RejectQuotaIncreaseRequestCommand(id, adminId, request.Note), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>GET /api/admin/resources — mức dùng và mức cấp của mọi Studio (OPS-23).</summary>
    [HttpGet("resources")]
    [ProducesResponseType(typeof(AdminResourceOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetResources(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminResourcesQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// PUT /api/admin/workspaces/{id}/resources — chỉnh cấp phát một Studio (OPS-23); null = giữ, 0 = không giới hạn.
    /// Bắt buộc lý do; Nhật ký hệ thống ghi giá trị cũ, mới và lý do.
    /// </summary>
    [HttpPut("workspaces/{id:guid}/resources")]
    [ProducesResponseType(typeof(AdminWorkspaceResourceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateWorkspaceResources(
        Guid id, [FromBody] UpdateWorkspaceResourcesRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } adminId) return Unauthorized();
        var result = await _mediator.Send(new UpdateWorkspaceResourcesCommand(
            id, adminId, request.StorageQuotaBytes, request.MonthlyTokenQuota, request.MonthlyImageQuota,
            request.AiEnabled, request.Reason ?? string.Empty,
            request.MonthlyRequestQuota, request.RequestsPerMinute), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// POST /api/admin/storage/reconcile?apply=false — đối soát kho file (OPS-22): dung lượng thật của từng Studio từ file còn
    /// được dùng, kèm danh sách file mồ côi. apply=true ghi lại dung lượng đã tính. Không xoá file nào.
    /// </summary>
    [HttpPost("storage/reconcile")]
    [ProducesResponseType(typeof(StorageReconcileReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReconcileStorage([FromQuery] bool apply, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ReconcileStorageCommand(apply), cancellationToken);
        // 409 chỉ trả message: lời nhắc "chạy lại" cụ thể hơn câu dịch chung của mã CONFLICT ở FE.
        if (result.ErrorCode == ResultErrorCodes.Conflict) return Conflict(new { message = result.ErrorMessage });
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    /// <summary>
    /// POST /api/admin/storage/orphans/delete — xoá file mồ côi đã cũ hơn 7 ngày của Studio đang hoạt động (OPS-22).
    /// Không khôi phục được; FE bắt Admin xác nhận. File không rõ chủ và bản ghi lịch sử không bị đụng.
    /// </summary>
    [HttpPost("storage/orphans/delete")]
    [ProducesResponseType(typeof(DeleteOrphanFilesResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteOrphanFiles(CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } adminId) return Unauthorized();
        var result = await _mediator.Send(new DeleteOrphanFilesCommand(adminId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ToError(result);
    }

    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : null;
}
