using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using PanelForge.API.Common.Attributes;
using PanelForge.Application.Interfaces;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.API;

public class RequireWorkspaceRoleFilterTests
{
    private readonly Mock<IWorkspaceAuthorizationService> _authServiceMock;
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public RequireWorkspaceRoleFilterTests()
    {
        _authServiceMock = new Mock<IWorkspaceAuthorizationService>();
        _dbContextMock = new Mock<IPanelForgeDbContext>();
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
        var seriesId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();

        var series = Series.Create(workspaceId, "Test Series", ReadingDirection.RightToLeft);
        typeof(Series).GetProperty("Id")!.SetValue(series, seriesId);

        var chapter = Chapter.Create(seriesId, 1, "Chapter 1");
        typeof(Chapter).GetProperty("Id")!.SetValue(chapter, chapterId);
        typeof(Chapter).GetProperty("Series")!.SetValue(chapter, series);

        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter> { chapter });
        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);

        _authServiceMock
            .Setup(a => a.HasWorkspaceRoleAsync(userId, workspaceId, It.Is<WorkspaceRole[]>(r => r.Contains(WorkspaceRole.Producer)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var routeData = new RouteData();
        routeData.Values.Add("chapterId", chapterId.ToString());

        var (context, _) = CreateContext(userId, routeData);
        var filter = new RequireWorkspaceRoleFilter(
            [WorkspaceRole.Producer], _authServiceMock.Object, _dbContextMock.Object);

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

        var chaptersDbSet = DbSetMockHelper.CreateDbSetMock(new List<Chapter>());
        _dbContextMock.Setup(db => db.Chapters).Returns(chaptersDbSet);

        var routeData = new RouteData();
        routeData.Values.Add("chapterId", chapterId.ToString());

        var (context, _) = CreateContext(userId, routeData);
        var filter = new RequireWorkspaceRoleFilter(
            [WorkspaceRole.Producer], _authServiceMock.Object, _dbContextMock.Object);

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
            [WorkspaceRole.Producer], _authServiceMock.Object, _dbContextMock.Object);

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
}
