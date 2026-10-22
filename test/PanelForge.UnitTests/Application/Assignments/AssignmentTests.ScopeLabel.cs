using FluentAssertions;
using PanelForge.Application.Features.Assignments.Queries.GetMyWorkspaceAssignments;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;
using Series = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Assignments;

public partial class AssignmentTests
{
    [Fact]
    public async Task MyWork_ScopeLabel_ShowsPagePanelAndScenes()
    {
        var f = new Fixture();
        var workspace = StudioWorkspace.Create("Studio", f.ProducerId);
        var series = Series.Create(workspace.Id, "Test", ReadingDirection.RightToLeft);
        var chapter = Chapter.Create(series.Id, 1);
        var page = Page.CreateNew(chapter.Id, 2, default, 1200, 1800, 300);
        var scene3 = Scene.Create(chapter.Id, 3);
        var scene4 = Scene.Create(chapter.Id, 4);
        var panel1 = Panel.Create(page.Id, 1, 1);
        panel1.AssignToScene(scene3.Id);
        var panel2 = Panel.Create(page.Id, 2, 2);
        panel2.AssignToScene(scene4.Id);

        f.Db.Setup(d => d.StudioWorkspaces).Returns(DbSetMockHelper.CreateDbSetMock(new List<StudioWorkspace> { workspace }));
        f.Db.Setup(d => d.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { series }));
        f.Db.Setup(d => d.Chapters).Returns(DbSetMockHelper.CreateDbSetMock(new List<Chapter> { chapter }));
        f.PagesList.Add(page);
        f.Panels.AddRange([panel1, panel2]);
        f.Scenes.AddRange([scene3, scene4]);

        Assignment Give(WorkflowEntityType type, Guid target)
        {
            var a = Assignment.Create(series.Id, chapter.Id, type, target, f.ArtistStage.Id, f.ArtistId, WorkspaceRole.Artist, "brief");
            f.Assignments.Add(a);
            return a;
        }

        var onPage = Give(WorkflowEntityType.Page, page.Id);
        var onPanel = Give(WorkflowEntityType.Panel, panel2.Id);
        var onChapter = Give(WorkflowEntityType.Chapter, chapter.Id);

        var result = await new GetMyWorkspaceAssignmentsQueryHandler(f.Db.Object)
            .Handle(new GetMyWorkspaceAssignmentsQuery(workspace.Id, f.ArtistId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var labels = result.Value!.ToDictionary(d => d.Id, d => d.ScopeLabel);
        labels[onPage.Id].Should().Be("Trang 2 · Cảnh 3, 4");
        labels[onPanel.Id].Should().Be("Trang 2 · Khung 2 · Cảnh 4");
        labels[onChapter.Id].Should().Be("Cả chương");
    }
}
