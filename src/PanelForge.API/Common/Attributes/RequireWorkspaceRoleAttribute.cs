using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PanelForge.Application.Common.Interfaces.Services;
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

    public RequireWorkspaceRoleFilter(
        WorkspaceRole[] allowedRoles,
        IWorkspaceAuthorizationService authorizationService)
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

        // Tìm workspaceId và seriesId (nếu có) từ RouteData
        Guid workspaceId = Guid.Empty;
        Guid? resolvedSeriesId = null;

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
            resolvedSeriesId = parsedSeriesId;
            var seriesWsId = await _authorizationService.GetWorkspaceIdOfSeriesAsync(
                parsedSeriesId, context.HttpContext.RequestAborted);

            if (seriesWsId == null)
            {
                context.Result = new NotFoundObjectResult(new { message = "Không tìm thấy Series tương ứng." });
                return;
            }

            workspaceId = seriesWsId.Value;
        }
        else if (context.RouteData.Values.TryGetValue("chapterId", out var chapterVal) && chapterVal is string chapterStr && Guid.TryParse(chapterStr, out var parsedChapterId))
        {
            resolvedSeriesId = await _authorizationService.GetSeriesIdOfChapterAsync(
                parsedChapterId, context.HttpContext.RequestAborted);

            var chapterWsId = await _authorizationService.GetWorkspaceIdOfChapterAsync(
                parsedChapterId, context.HttpContext.RequestAborted);

            if (chapterWsId == null)
            {
                context.Result = new NotFoundObjectResult(new { message = "Không tìm thấy Chapter tương ứng." });
                return;
            }

            workspaceId = chapterWsId.Value;
        }
        else if (context.RouteData.Values.TryGetValue("pageId", out var pageVal) && pageVal is string pageStr && Guid.TryParse(pageStr, out var parsedPageId))
        {
            resolvedSeriesId = await _authorizationService.GetSeriesIdOfPageAsync(
                parsedPageId, context.HttpContext.RequestAborted);

            var pageWsId = await _authorizationService.GetWorkspaceIdOfPageAsync(
                parsedPageId, context.HttpContext.RequestAborted);

            if (pageWsId == null)
            {
                context.Result = new NotFoundObjectResult(new { message = "Không tìm thấy Page tương ứng." });
                return;
            }

            workspaceId = pageWsId.Value;
        }
        else if (context.RouteData.Values.TryGetValue("sceneId", out var sceneVal) && sceneVal is string sceneStr && Guid.TryParse(sceneStr, out var parsedSceneId))
        {
            resolvedSeriesId = await _authorizationService.GetSeriesIdOfSceneAsync(
                parsedSceneId, context.HttpContext.RequestAborted);

            var sceneWsId = await _authorizationService.GetWorkspaceIdOfSceneAsync(
                parsedSceneId, context.HttpContext.RequestAborted);

            if (sceneWsId == null)
            {
                context.Result = new NotFoundObjectResult(new { message = "Không tìm thấy Scene tương ứng." });
                return;
            }

            workspaceId = sceneWsId.Value;
        }
        else if (context.RouteData.Values.TryGetValue("panelId", out var panelVal) && panelVal is string panelStr && Guid.TryParse(panelStr, out var parsedPanelId))
        {
            resolvedSeriesId = await _authorizationService.GetSeriesIdOfPanelAsync(
                parsedPanelId, context.HttpContext.RequestAborted);

            var panelWsId = await _authorizationService.GetWorkspaceIdOfPanelAsync(
                parsedPanelId, context.HttpContext.RequestAborted);

            if (panelWsId == null)
            {
                context.Result = new NotFoundObjectResult(new { message = "Không tìm thấy Panel tương ứng." });
                return;
            }

            workspaceId = panelWsId.Value;
        }

        if (workspaceId == Guid.Empty)
        {
            context.Result = new BadRequestObjectResult(new { message = "Không tìm thấy tham số Workspace ID, Series ID hoặc Chapter ID hợp lệ trên đường dẫn." });
            return;
        }

        bool isPublisher = user.IsInRole("Publisher") ||
            user.HasClaim(c => (c.Type == ClaimTypes.Role || c.Type == "role") && c.Value == "Publisher");
        bool isAdmin = user.IsInRole("Admin") ||
            user.HasClaim(c => (c.Type == ClaimTypes.Role || c.Type == "role") && (c.Value == "Admin" || c.Value == "Administrator"));

        if (isAdmin)
        {
            await next();
            return;
        }

        if (isPublisher && (_allowedRoles.Length == 0 || _allowedRoles.Contains(WorkspaceRole.Publisher)))
        {
            await next();
            return;
        }

        bool hasPermission;
        if (resolvedSeriesId.HasValue)
        {
            if (_allowedRoles.Length == 0)
            {
                hasPermission = await _authorizationService.IsSeriesMemberAsync(userId, resolvedSeriesId.Value, context.HttpContext.RequestAborted);
            }
            else
            {
                hasPermission = await _authorizationService.HasSeriesRoleAsync(userId, resolvedSeriesId.Value, _allowedRoles, context.HttpContext.RequestAborted);
            }
        }
        else
        {
            if (_allowedRoles.Length == 0)
            {
                hasPermission = await _authorizationService.IsMemberAsync(userId, workspaceId, context.HttpContext.RequestAborted);
            }
            else
            {
                hasPermission = await _authorizationService.HasWorkspaceRoleAsync(userId, workspaceId, _allowedRoles, context.HttpContext.RequestAborted);
            }
        }

        if (!hasPermission)
        {
            var roleMsg = resolvedSeriesId.HasValue
                ? (_allowedRoles.Length == 0
                    ? "Bạn không phải là thành viên được phân công vào Series này."
                    : $"Bạn không có quyền thực hiện thao tác này trong Series. Yêu cầu một trong các vai trò: {string.Join(", ", _allowedRoles)}.")
                : (_allowedRoles.Length == 0
                    ? "Bạn không phải là thành viên của Workspace này."
                    : $"Bạn không có quyền thực hiện thao tác này trong Workspace. Yêu cầu một trong các vai trò: {string.Join(", ", _allowedRoles)}.");

            context.Result = new ObjectResult(new { message = roleMsg })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
