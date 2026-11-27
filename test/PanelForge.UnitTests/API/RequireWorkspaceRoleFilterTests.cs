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
        RouteData routeData,
        params Claim[] extraClaims)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        claims.AddRange(extraClaims);
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor { EndpointMetadata = new List<object>() });
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

    // BR-07: Admin nền tảng không được đi tắt qua kiểm tra quyền Workspace.
    [Theory]
    [InlineData(ClaimTypes.Role, "Admin", new[] { WorkspaceRole.Producer })]
    [InlineData(ClaimTypes.Role, "Administrator", new[] { WorkspaceRole.Producer })]
    [InlineData("role", "Admin", new[] { WorkspaceRole.Writer })]
    [InlineData(ClaimTypes.Role, "Admin", new WorkspaceRole[0])]
    public async Task OnActionExecutionAsync_WhenUserIsPlatformAdmin_ShouldReturnForbidden(
        string claimType, string roleValue, WorkspaceRole[] allowedRoles)
    {
        // Arrange: dịch vụ phân quyền trả false cho Admin (như WorkspaceAuthorizationService thật)
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        _authServiceMock
            .Setup(a => a.HasWorkspaceRoleAsync(userId, workspaceId, It.IsAny<WorkspaceRole[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _authServiceMock
            .Setup(a => a.IsMemberAsync(userId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var routeData = new RouteData();
        routeData.Values.Add("workspaceId", workspaceId.ToString());

        var (context, _) = CreateContext(userId, routeData, new Claim(claimType, roleValue));
        var filter = new RequireWorkspaceRoleFilter(allowedRoles, _authServiceMock.Object);

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

    // BR-32: Publisher là vai trò hệ thống; chỉ vào endpoint khai báo rõ Publisher hoặc GET có [AllowPublisherRead].
    private (ActionExecutingContext context, Func<bool> executed, ActionExecutionDelegate next) CreatePublisherContext(
        string method, bool markedReadable)
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        _authServiceMock
            .Setup(a => a.IsMemberAsync(userId, workspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _authServiceMock
            .Setup(a => a.HasWorkspaceRoleAsync(userId, workspaceId, It.IsAny<WorkspaceRole[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var routeData = new RouteData();
        routeData.Values.Add("workspaceId", workspaceId.ToString());
        var (context, _) = CreateContext(userId, routeData, new Claim(ClaimTypes.Role, "Publisher"));
        context.HttpContext.Request.Method = method;
        if (markedReadable)
            context.ActionDescriptor.EndpointMetadata.Add(new AllowPublisherReadAttribute());

        var ran = false;
        ActionExecutionDelegate next = () =>
        {
            ran = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        };
        return (context, () => ran, next);
    }

    [Theory]
    [InlineData("POST", false)]
    [InlineData("PUT", true)]
    [InlineData("DELETE", true)]
    [InlineData("GET", false)]
    public async Task OnActionExecutionAsync_WhenPublisherOnBareEndpointWithoutReadMarker_ShouldReturnForbidden(
        string method, bool markedReadable)
    {
        var (context, executed, next) = CreatePublisherContext(method, markedReadable);
        var filter = new RequireWorkspaceRoleFilter([], _authServiceMock.Object);

        await filter.OnActionExecutionAsync(context, next);

        executed().Should().BeFalse();
        (context.Result as ObjectResult)!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenPublisherReadsMarkedBareEndpoint_ShouldProceed()
    {
        var (context, executed, next) = CreatePublisherContext("GET", markedReadable: true);
        var filter = new RequireWorkspaceRoleFilter([], _authServiceMock.Object);

        await filter.OnActionExecutionAsync(context, next);

        executed().Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenPublisherOnEndpointDeclaringPublisher_ShouldProceed()
    {
        var (context, executed, next) = CreatePublisherContext("POST", markedReadable: false);
        var filter = new RequireWorkspaceRoleFilter([WorkspaceRole.Publisher], _authServiceMock.Object);

        await filter.OnActionExecutionAsync(context, next);

        executed().Should().BeTrue();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenPublisherOnProducerOnlyEndpoint_ShouldReturnForbidden()
    {
        var (context, executed, next) = CreatePublisherContext("POST", markedReadable: false);
        var filter = new RequireWorkspaceRoleFilter([WorkspaceRole.Producer], _authServiceMock.Object);

        await filter.OnActionExecutionAsync(context, next);

        executed().Should().BeFalse();
        (context.Result as ObjectResult)!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
