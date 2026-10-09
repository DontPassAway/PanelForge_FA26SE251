using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PanelForge.API.Common.Attributes;
using Xunit;

namespace PanelForge.UnitTests.API;

/// <summary>
/// Chặn tái phát lỗ hổng "endpoint quên gắn quyền" (Scenes/Pages từng truy cập được không cần đăng nhập):
/// mọi action HTTP phải có [Authorize] (ở class hoặc method), hoặc bộ lọc quyền Workspace, hoặc khai báo [AllowAnonymous] rõ ràng.
/// </summary>
public class ControllerAuthorizationCoverageTests
{
    // Endpoint xác thực công khai có chủ đích (người dùng chưa có token); thêm endpoint công khai mới phải khai báo ở đây hoặc dùng [AllowAnonymous]
    private static readonly HashSet<string> IntentionallyPublicAuthActions =
    [
        "AuthController.Register", "AuthController.Login", "AuthController.Refresh", "AuthController.ForgotPassword",
        "AuthController.ResetPassword", "AuthController.VerifyEmail", "AuthController.ResendVerification",
        "AuthController.ExternalLogin", "AuthController.VerifyTwoFactor", "AuthController.ForgetDevice"
    ];

    [Fact]
    public void EveryHttpAction_IsAuthorized_OrExplicitlyAnonymous()
    {
        var controllers = typeof(RequireWorkspaceRoleAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var unprotected = new List<string>();
        foreach (var controller in controllers)
        {
            var classAuthorized = controller.GetCustomAttributes<AuthorizeAttribute>(true).Any();
            var classAnonymous = controller.GetCustomAttributes<AllowAnonymousAttribute>(true).Any();

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true).Any()))
            {
                var authorized = classAuthorized
                                 || action.GetCustomAttributes<AuthorizeAttribute>(true).Any()
                                 || action.GetCustomAttributes<RequireWorkspaceRoleAttribute>(true).Any();
                var anonymous = classAnonymous || action.GetCustomAttributes<AllowAnonymousAttribute>(true).Any();

                var name = $"{controller.Name}.{action.Name}";
                if (!authorized && !anonymous && !IntentionallyPublicAuthActions.Contains(name)) unprotected.Add(name);
            }
        }

        unprotected.Should().BeEmpty("endpoint không có [Authorize], quyền Workspace hay [AllowAnonymous] rõ ràng sẽ truy cập được không cần đăng nhập");
    }
}
