using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PanelForge.Application.Interfaces;
using PanelForge.Domain.Enums;

namespace PanelForge.API.Common.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireWorkspaceRoleAttribute : TypeFilterAttribute
{
    public RequireWorkspaceRoleAttribute(params WorkspaceRole[] allowedRoles) 
        : base(typeof(RequireWorkspaceRoleFilter))
    {
        Arguments = new object[] { allowedRoles };
    }
}

public class RequireWorkspaceRoleFilter : IAsyncActionFilter
{
    private readonly WorkspaceRole[] _allowedRoles;
    private readonly IWorkspaceAuthorizationService _authorizationService;

    public RequireWorkspaceRoleFilter(WorkspaceRole[] allowedRoles, IWorkspaceAuthorizationService authorizationService)
    {
        _allowedRoles = allowedRoles;
        _authorizationService = authorizationService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        if (!Guid.TryParse(userIdStr, out var userId))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Chưa xác thực danh tính người dùng." });
            return;
        }

        // Tìm workspaceId từ RouteData (ví dụ: {id} hoặc {workspaceId})
        Guid workspaceId = Guid.Empty;
        if (context.RouteData.Values.TryGetValue("workspaceId", out var wsVal) && wsVal is string wsStr && Guid.TryParse(wsStr, out var parsedWsId))
        {
            workspaceId = parsedWsId;
        }
        else if (context.RouteData.Values.TryGetValue("id", out var idVal) && idVal is string idStr && Guid.TryParse(idStr, out var parsedId))
        {
            workspaceId = parsedId;
        }

        if (workspaceId == Guid.Empty)
        {
            context.Result = new BadRequestObjectResult(new { message = "Không tìm thấy tham số Workspace ID hợp lệ trên đường dẫn." });
            return;
        }

        var hasPermission = await _authorizationService.HasWorkspaceRoleAsync(userId, workspaceId, _allowedRoles, context.HttpContext.RequestAborted);
        if (!hasPermission)
        {
            context.Result = new ObjectResult(new
            {
                message = $"Bạn không có quyền thực hiện thao tác này trong Workspace. Yêu cầu một trong các vai trò: {string.Join(", ", _allowedRoles)}."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
