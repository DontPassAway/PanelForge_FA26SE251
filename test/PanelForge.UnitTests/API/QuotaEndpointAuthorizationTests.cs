using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using PanelForge.API.Common.Attributes;
using PanelForge.API.Controllers;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.API;

/// <summary>Chỉ Admin chỉnh hạn mức; chỉ Producer của Studio gửi/xem yêu cầu tăng hạn mức (OPS-21, BR-07).</summary>
public class QuotaEndpointAuthorizationTests
{
    [Fact]
    public void AdminController_IsAdminOnly_WithoutAnonymousActions()
    {
        typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>()
            .Should().ContainSingle(a => a.Roles == "Admin");
        typeof(AdminController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(WorkspacesController.GetQuotaRequests))]
    [InlineData(nameof(WorkspacesController.SubmitQuotaRequest))]
    public void WorkspaceQuotaRequestEndpoints_RequireProducer(string action)
    {
        var filter = typeof(WorkspacesController).GetMethod(action)!.GetCustomAttribute<RequireWorkspaceRoleAttribute>();
        filter.Should().NotBeNull();
        filter!.Arguments.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new[] { WorkspaceRole.Producer });
    }

    [Fact]
    public void OnlyReaderStats_AllowsAdminOversight()
    {
        typeof(WorkspacesController).GetMethods()
            .Where(m => m.GetCustomAttribute<AllowAdminOversightAttribute>() is not null)
            .Select(m => m.Name)
            .Should().BeEquivalentTo([nameof(WorkspacesController.GetReaderStats)]);
    }
}
