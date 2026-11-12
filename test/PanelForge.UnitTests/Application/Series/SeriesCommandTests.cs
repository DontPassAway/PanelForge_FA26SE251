using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Series.Commands.CreateSeries;
using PanelForge.Application.Features.Series.Commands.DeleteSeries;
using PanelForge.Application.Features.Series.Commands.UpdateSeries;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.SeriesTests;

public class SeriesCommandTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock;

    public SeriesCommandTests()
    {
        _dbContextMock = new Mock<IPanelForgeDbContext>();
    }

    [Fact]
    public async Task CreateSeries_WhenWorkspaceExists_ShouldCreateSeriesAndBible()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        typeof(StudioWorkspace).GetProperty("Id")!.SetValue(workspace, workspaceId);

        var member = WorkspaceMember.Create(workspaceId, userId, WorkspaceRole.Producer);

        var template = PanelForge.Domain.Entities.MasterData.PipelineTemplate.Create("STANDARD_MANGA", "Standard Manga", isDefault: true);
        var stage = PanelForge.Domain.Entities.MasterData.PipelineTemplateStage.Create(template.Id, "SCRIPT", "Script", 1, WorkspaceRole.Writer);
        template.Stages.Add(stage);

        var workspacesDbSet = DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace });
        var membersDbSet = DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember> { member });
        var templatesDbSet = DbSetMockHelper.CreateDbSetMock(new List<PanelForge.Domain.Entities.MasterData.PipelineTemplate> { template });
        var pipelinesDbSet = DbSetMockHelper.CreateDbSetMock(new List<PanelForge.Domain.Entities.Workflow.PipelineDefinition>());
        var seriesList = new List<Series>();
        var seriesDbSet = DbSetMockHelper.CreateDbSetMock(seriesList);
        var biblesDbSet = DbSetMockHelper.CreateDbSetMock(new List<SeriesBible>());

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(workspacesDbSet);
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(membersDbSet);
        _dbContextMock.Setup(db => db.PipelineTemplates).Returns(templatesDbSet);
        _dbContextMock.Setup(db => db.PipelineDefinitions).Returns(pipelinesDbSet);
        _dbContextMock.Setup(db => db.Series).Returns(seriesDbSet);
        _dbContextMock.Setup(db => db.SeriesBibles).Returns(biblesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateSeriesCommandHandler(_dbContextMock.Object);
        var command = new CreateSeriesCommand(
            WorkspaceId: workspaceId,
            UserId: userId,
            Title: "Naruto"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("Naruto");
        result.Value.WorkspaceId.Should().Be(workspaceId);
    }

    [Fact]
    public async Task CreateSeries_WithoutTemplate_FallbackPipelineAssignsReviewAndApprovedToEditor()
    {
        // Arrange: không có PipelineTemplate nào → dùng nhánh dự phòng, phải khớp seed STANDARD_MANGA (Review/Approved do Editor)
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        typeof(StudioWorkspace).GetProperty("Id")!.SetValue(workspace, workspaceId);
        var member = WorkspaceMember.Create(workspaceId, userId, WorkspaceRole.Producer);

        var pipelines = new List<PanelForge.Domain.Entities.Workflow.PipelineDefinition>();

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember> { member }));
        _dbContextMock.Setup(db => db.PipelineTemplates).Returns(DbSetMockHelper.CreateDbSetMock(new List<PanelForge.Domain.Entities.MasterData.PipelineTemplate>()));
        _dbContextMock.Setup(db => db.PipelineDefinitions).Returns(DbSetMockHelper.CreateDbSetMock(pipelines));
        _dbContextMock.Setup(db => db.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series>()));
        _dbContextMock.Setup(db => db.SeriesBibles).Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible>()));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateSeriesCommandHandler(_dbContextMock.Object);

        // Act
        var result = await handler.Handle(new CreateSeriesCommand(WorkspaceId: workspaceId, UserId: userId, Title: "Naruto"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var stages = pipelines.Single().GetOrderedStages();
        stages.Should().HaveCount(7);
        stages.Single(s => s.Slug == "review").AllowedRole.Should().Be(WorkspaceRole.Editor);
        stages.Single(s => s.Slug == "review").IsApprovalGate.Should().BeTrue();
        stages.Single(s => s.Slug == "approved").AllowedRole.Should().Be(WorkspaceRole.Editor);
    }

    [Fact]
    public async Task CreateSeries_WhenUserIsNotProducer_ShouldReturnFailure()
    {
        // Arrange (UC-03: Writer cannot create Series)
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspace = StudioWorkspace.Create("Test Studio", Guid.NewGuid());
        typeof(StudioWorkspace).GetProperty("Id")!.SetValue(workspace, workspaceId);

        var member = WorkspaceMember.Create(workspaceId, userId, WorkspaceRole.Writer);

        var workspacesDbSet = DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace });
        var membersDbSet = DbSetMockHelper.CreateDbSetMock(new List<WorkspaceMember> { member });

        _dbContextMock.Setup(db => db.StudioWorkspaces).Returns(workspacesDbSet);
        _dbContextMock.Setup(db => db.WorkspaceMembers).Returns(membersDbSet);

        var handler = new CreateSeriesCommandHandler(_dbContextMock.Object);
        var command = new CreateSeriesCommand(
            WorkspaceId: workspaceId,
            UserId: userId,
            Title: "Naruto"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Producer");
    }

    [Fact]
    public async Task UpdateSeries_WhenSeriesExists_ShouldUpdateDetails()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var series = Series.Create(Guid.NewGuid(), "Old Title", ReadingDirection.LeftToRight);
        typeof(Series).GetProperty("Id")!.SetValue(series, seriesId);

        var seriesDbSet = DbSetMockHelper.CreateDbSetMock(new List<Series> { series });
        _dbContextMock.Setup(db => db.Series).Returns(seriesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdateSeriesCommandHandler(_dbContextMock.Object);
        var command = new UpdateSeriesCommand(
            SeriesId: seriesId,
            UserId: Guid.NewGuid(),
            Title: "New Title",
            Synopsis: "Updated Synopsis",
            ReadingDirection: ReadingDirection.Vertical,
            PipelineDefinitionId: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("New Title");
        result.Value.ReadingDirection.Should().Be(ReadingDirection.Vertical);
    }

    [Fact]
    public async Task UpdateSeries_SetsCompleted_AndNullLeavesItUnchanged()
    {
        var series = Series.Create(Guid.NewGuid(), "Title", ReadingDirection.LeftToRight);
        _dbContextMock.Setup(db => db.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { series }));
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var handler = new UpdateSeriesCommandHandler(_dbContextMock.Object);

        var done = await handler.Handle(new UpdateSeriesCommand(
            series.Id, Guid.NewGuid(), "Title", null, ReadingDirection.LeftToRight, null, IsCompleted: true), CancellationToken.None);
        done.Value!.IsCompleted.Should().BeTrue();

        var untouched = await handler.Handle(new UpdateSeriesCommand(
            series.Id, Guid.NewGuid(), "Title 2", null, ReadingDirection.LeftToRight, null), CancellationToken.None);
        untouched.Value!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteSeries_WhenSeriesExists_ShouldCallRemoveOnDbContext()
    {
        // Arrange
        var seriesId = Guid.NewGuid();
        var series = Series.Create(Guid.NewGuid(), "To Delete", ReadingDirection.RightToLeft);
        typeof(Series).GetProperty("Id")!.SetValue(series, seriesId);

        var seriesList = new List<Series> { series };
        var seriesDbSet = DbSetMockHelper.CreateDbSetMock(seriesList);
        _dbContextMock.Setup(db => db.Series).Returns(seriesDbSet);
        _dbContextMock.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeleteSeriesCommandHandler(_dbContextMock.Object);
        var command = new DeleteSeriesCommand(seriesId, Guid.NewGuid());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        seriesList.Should().BeEmpty();
        _dbContextMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
