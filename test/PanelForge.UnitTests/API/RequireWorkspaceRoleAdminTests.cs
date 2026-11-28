using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.API;

/// <summary>BR-07: Admin không đi tắt qua quyền Workspace; chỉ đọc endpoint có [AllowAdminOversight].</summary>
public class RequireWorkspaceRoleAdminTests
{
    private static async Task<(IActionResult? Result, bool Executed)> RunAsAdmin(string method, bool oversight)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "Admin")], "Test"));
        var http = new DefaultHttpContext { User = user };
        http.Request.Method = method;
        var route = new RouteData();
        route.Values["id"] = Guid.NewGuid().ToString();
        var descriptor = new ActionDescriptor
        {
            EndpointMetadata = oversight ? [new AllowAdminOversightAttribute()] : []
        };
        var context = new ActionExecutingContext(
            new ActionContext(http, route, descriptor), [], new Dictionary<string, object?>(), new object());

        var executed = false;
        var filter = new RequireWorkspaceRoleFilter([WorkspaceRole.Producer], new Mock<IWorkspaceAuthorizationService>().Object);
        await filter.OnActionExecutionAsync(context, () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });
        return (context.Result, executed);
    }

    [Theory]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("GET", false)]
    [InlineData("POST", true)] // oversight chỉ cho đọc
    public async Task Admin_IsForbidden_OnWorkspaceEndpoints(string method, bool oversight)
    {
        var (result, executed) = await RunAsAdmin(method, oversight);

        executed.Should().BeFalse();
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Admin_CanRead_OversightEndpoint()
    {
        var (result, executed) = await RunAsAdmin("GET", oversight: true);

        executed.Should().BeTrue();
        result.Should().BeNull();
    }
}
