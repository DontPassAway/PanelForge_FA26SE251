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

public class RequireWorkspaceRoleFilterTests
{
    private readonly Mock<IWorkspaceAuthorizationService> _authServiceMock;

    public RequireWorkspaceRoleFilterTests()
    {
        _authServiceMock = new Mock<IWorkspaceAuthorizationService>();
    }

    private static (ActionExecutingContext context, bool executed) CreateContext(
        Guid userId,
        RouteData routeData)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object()
        );

        return (context, false);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenRouteHasChapterId_AndUserHasRole_ShouldProceed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();

        _authServiceMock
            .Setup(a => a.GetWorkspaceIdOfChapterAsync(chapterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspaceId);

        _authServiceMock
            .Setup(a => a.HasWorkspaceRoleAsync(userId, workspaceId, It.Is<WorkspaceRole[]>(r => r.Contains(WorkspaceRole.Producer)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var routeData = new RouteData();
        routeData.Values.Add("chapterId", chapterId.ToString());

        var (context, _) = CreateContext(userId, routeData);
        var filter = new RequireWorkspaceRoleFilter(
            [WorkspaceRole.Producer], _authServiceMock.Object);

        var executed = false;
        ActionExecutionDelegate next = () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        };

        // Act
        await filter.OnActionExecutionAsync(context, next);

        // Assert
        executed.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenRouteHasChapterId_AndChapterNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();

        _authServiceMock
            .Setup(a => a.GetWorkspaceIdOfChapterAsync(chapterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var routeData = new RouteData();
        routeData.Values.Add("chapterId", chapterId.ToString());

        var (context, _) = CreateContext(userId, routeData);
        var filter = new RequireWorkspaceRoleFilter(
            [WorkspaceRole.Producer], _authServiceMock.Object);

        var executed = false;
        ActionExecutionDelegate next = () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        };

        // Act
        await filter.OnActionExecutionAsync(context, next);

        // Assert
        executed.Should().BeFalse();
        context.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenUserLacksRequiredRole_ShouldReturnForbidden()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        _authServiceMock
            .Setup(a => a.HasWorkspaceRoleAsync(userId, workspaceId, It.IsAny<WorkspaceRole[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var routeData = new RouteData();
        routeData.Values.Add("workspaceId", workspaceId.ToString());

        var (context, _) = CreateContext(userId, routeData);
        var filter = new RequireWorkspaceRoleFilter(
            [WorkspaceRole.Producer], _authServiceMock.Object);

        var executed = false;
        ActionExecutionDelegate next = () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        };

        // Act
        await filter.OnActionExecutionAsync(context, next);

        // Assert
        executed.Should().BeFalse();
        context.Result.Should().BeOfType<ObjectResult>();
        (context.Result as ObjectResult)!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenRouteHasSeriesId_AndUserIsSeriesMember_ShouldProceed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();

        _authServiceMock
            .Setup(a => a.GetWorkspaceIdOfSeriesAsync(seriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspaceId);

        _authServiceMock
            .Setup(a => a.IsSeriesMemberAsync(userId, seriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var routeData = new RouteData();
        routeData.Values.Add("seriesId", seriesId.ToString());

        var (context, _) = CreateContext(userId, routeData);
        var filter = new RequireWorkspaceRoleFilter([], _authServiceMock.Object);

        var executed = false;
        ActionExecutionDelegate next = () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        };

        // Act
        await filter.OnActionExecutionAsync(context, next);

        // Assert
        executed.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenRouteHasSeriesId_AndUserIsNotSeriesMember_ShouldReturnForbidden()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();

        _authServiceMock
            .Setup(a => a.GetWorkspaceIdOfSeriesAsync(seriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspaceId);

        _authServiceMock
            .Setup(a => a.IsSeriesMemberAsync(userId, seriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var routeData = new RouteData();
        routeData.Values.Add("seriesId", seriesId.ToString());

        var (context, _) = CreateContext(userId, routeData);
        var filter = new RequireWorkspaceRoleFilter([], _authServiceMock.Object);

        var executed = false;
        ActionExecutionDelegate next = () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        };

        // Act
        await filter.OnActionExecutionAsync(context, next);

        // Assert
        executed.Should().BeFalse();
        context.Result.Should().BeOfType<ObjectResult>();
        (context.Result as ObjectResult)!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenRouteHasSeriesId_AndUserHasSeriesRole_ShouldProceed()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();

        _authServiceMock
            .Setup(a => a.GetWorkspaceIdOfSeriesAsync(seriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspaceId);

        _authServiceMock
            .Setup(a => a.HasSeriesRoleAsync(userId, seriesId, It.Is<WorkspaceRole[]>(r => r.Contains(WorkspaceRole.Writer)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var routeData = new RouteData();
        routeData.Values.Add("seriesId", seriesId.ToString());

        var (context, _) = CreateContext(userId, routeData);
        var filter = new RequireWorkspaceRoleFilter([WorkspaceRole.Writer], _authServiceMock.Object);

        var executed = false;
        ActionExecutionDelegate next = () =>
        {
            executed = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        };

        // Act
        await filter.OnActionExecutionAsync(context, next);

        // Assert
        executed.Should().BeTrue();
        context.Result.Should().BeNull();
    }
}
