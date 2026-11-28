using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using PanelForge.API.Common.Attributes;
using PanelForge.API.Controllers;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.API;

/// <summary>Producer/Publisher/vai trò sản xuất không được chỉnh quota; chỉ Producer của Studio được gửi yêu cầu tăng tài nguyên.</summary>
public class ResourceEndpointAuthorizationTests
{
    [Fact]
    public void AdminResourcesController_IsAdminOnly()
    {
        typeof(AdminResourcesController).GetCustomAttributes<AuthorizeAttribute>()
            .Should().ContainSingle(a => a.Roles == "Admin");
        typeof(AdminResourcesController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(WorkspacesController.GetResourceRequests))]
    [InlineData(nameof(WorkspacesController.SubmitResourceRequest))]
    public void WorkspaceResourceRequestEndpoints_RequireProducer(string action)
    {
        var filter = typeof(WorkspacesController).GetMethod(action)!.GetCustomAttribute<RequireWorkspaceRoleAttribute>();
        filter.Should().NotBeNull();
        filter!.Arguments.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new[] { WorkspaceRole.Producer });
    }
}
