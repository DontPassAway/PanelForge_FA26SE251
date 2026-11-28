using System.Reflection;
using FluentAssertions;
using PanelForge.API.Common.Attributes;
using PanelForge.API.Controllers;
using PanelForge.Domain.Enums;
using Xunit;

namespace PanelForge.UnitTests.API;

/// <summary>
/// Góp ý ghim trên trang (Read Series): Writer được xem và tạo góp ý (quyết định 2026-11-28);
/// đóng, mở lại góp ý vẫn chỉ Producer/Editor; chuyển thành yêu cầu làm lại chỉ Producer.
/// </summary>
public class PageCommentsControllerPermissionTests
{
    private static WorkspaceRole[] GetAllowedRoles(string methodName)
    {
        var method = typeof(PageCommentsController).GetMethod(methodName);
        method.Should().NotBeNull($"Action {methodName} phải tồn tại trên PageCommentsController");
        var attr = method!.GetCustomAttribute<RequireWorkspaceRoleAttribute>();
        attr.Should().NotBeNull($"Action {methodName} phải được bảo vệ bởi [RequireWorkspaceRole]");
        return attr!.Arguments is { Length: > 0 } && attr.Arguments[0] is WorkspaceRole[] roles
            ? roles
            : Array.Empty<WorkspaceRole>();
    }

    [Theory]
    [InlineData(nameof(PageCommentsController.List))]
    [InlineData(nameof(PageCommentsController.Create))]
    public void ListAndCreate_AllowProducerEditorWriter_Only(string action)
    {
        GetAllowedRoles(action).Should().BeEquivalentTo(
            new[] { WorkspaceRole.Producer, WorkspaceRole.Editor, WorkspaceRole.Writer });
    }

    [Theory]
    [InlineData(nameof(PageCommentsController.Resolve))]
    [InlineData(nameof(PageCommentsController.Reopen))]
    public void ResolveAndReopen_StayProducerEditor_AndRejectWriter(string action)
    {
        var roles = GetAllowedRoles(action);
        roles.Should().BeEquivalentTo(new[] { WorkspaceRole.Producer, WorkspaceRole.Editor });
        roles.Should().NotContain(WorkspaceRole.Writer);
    }

    [Fact]
    public void ConvertToRework_StaysProducerOnly()
    {
        GetAllowedRoles(nameof(PageCommentsController.ConvertToRework))
            .Should().ContainSingle().Which.Should().Be(WorkspaceRole.Producer);
    }
}
