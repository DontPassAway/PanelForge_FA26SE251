using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
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
    private readonly IPanelForgeDbContext _dbContext;

    public RequireWorkspaceRoleFilter(
        WorkspaceRole[] allowedRoles,
        IWorkspaceAuthorizationService authorizationService,
        IPanelForgeDbContext dbContext)
    {
        _allowedRoles = allowedRoles;
        _authorizationService = authorizationService;
        _dbContext = dbContext;
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

        // Tìm workspaceId từ RouteData (ví dụ: {id}, {workspaceId} hoặc gián tiếp qua {seriesId})
        Guid workspaceId = Guid.Empty;
        if (context.RouteData.Values.TryGetValue("workspaceId", out var wsVal) && wsVal is string wsStr && Guid.TryParse(wsStr, out var parsedWsId))
        {
            workspaceId = parsedWsId;
        }
        else if (context.RouteData.Values.TryGetValue("id", out var idVal) && idVal is string idStr && Guid.TryParse(idStr, out var parsedId))
        {
            workspaceId = parsedId;
        }
        else if (context.RouteData.Values.TryGetValue("seriesId", out var seriesVal) && seriesVal is string seriesStr && Guid.TryParse(seriesStr, out var parsedSeriesId))
        {
            var seriesWsId = await _dbContext.Series
                .Where(s => s.Id == parsedSeriesId)
                .Select(s => (Guid?)s.WorkspaceId)
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            if (seriesWsId == null)
            {
                context.Result = new NotFoundObjectResult(new { message = "Không tìm thấy Series tương ứng." });
                return;
            }

            workspaceId = seriesWsId.Value;
        }

        if (workspaceId == Guid.Empty)
        {
            context.Result = new BadRequestObjectResult(new { message = "Không tìm thấy tham số Workspace ID hoặc Series ID hợp lệ trên đường dẫn." });
            return;
        }

        bool hasPermission;
        if (_allowedRoles.Length == 0)
        {
            hasPermission = await _authorizationService.IsMemberAsync(userId, workspaceId, context.HttpContext.RequestAborted);
        }
        else
        {
            hasPermission = await _authorizationService.HasWorkspaceRoleAsync(userId, workspaceId, _allowedRoles, context.HttpContext.RequestAborted);
        }

        if (!hasPermission)
        {
            var roleMsg = _allowedRoles.Length == 0
                ? "Bạn không phải là thành viên của Workspace này."
                : $"Bạn không có quyền thực hiện thao tác này trong Workspace. Yêu cầu một trong các vai trò: {string.Join(", ", _allowedRoles)}.";

            context.Result = new ObjectResult(new { message = roleMsg })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
