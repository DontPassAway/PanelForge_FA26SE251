using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Commands.AddElement;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.CanvasElements;

/// <summary>Element ảnh (nhân vật, bối cảnh…) gắn Panel, có góc xoay và thứ tự lớp đổi được.</summary>
public class ImageElementTests
{
    private static Page NewPage() => Page.CreateNew(Guid.NewGuid(), 1, LayoutFormat.StandardPage, 1200, 1800, 300);

    private static async Task<AddElementCommandHandler> Setup(Page page, Panel panel)
    {
        var repo = new Mock<IPageRepository>();
        repo.Setup(r => r.GetAsync(page.Id, It.IsAny<CancellationToken>())).ReturnsAsync(page);
        var db = InMemoryDb.Create();
        db.Panels.Add(panel);
        await db.SaveChangesAsync();
        return new AddElementCommandHandler(repo.Object, db);
    }

    private static AddElementCommand Cmd(Page page, Guid? panelId) => new(
        page.Id, "CHARACTER_INSTANCE", 10, 20, 300, 500, 2, null, Guid.NewGuid().ToString(), Guid.NewGuid(),
        PanelId: panelId, Rotation: 15);

    [Fact]
    public async Task Add_WithPanelOfThePage_StoresPanelAndRotation()
    {
        var page = NewPage();
        var panel = Panel.Create(page.Id, 1, 1);
        var handler = await Setup(page, panel);

        var result = await handler.Handle(Cmd(page, panel.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var e = page.Elements[result.Value!.ElementId];
        e.PanelId.Should().Be(panel.Id);
        e.Rotation.Should().Be(15);
    }

    [Fact]
    public async Task Add_WithPanelOfAnotherPage_Fails()
    {
        var page = NewPage();
        var otherPagePanel = Panel.Create(Guid.NewGuid(), 1, 1);
        var handler = await Setup(page, otherPagePanel);

        var result = await handler.Handle(Cmd(page, otherPagePanel.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        page.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Move_CanChangeRotationAndZIndex_AndKeepsThemWhenOmitted()
    {
        var page = NewPage();
        page.AddElement("BACKGROUND", 0, 0, 100, 100, 1, Guid.NewGuid());
        var id = page.Elements.Keys.Single();

        page.MoveElement(id, 0, 0, 100, 100, Guid.NewGuid(), newRotation: 30, newZIndex: 5);
        page.Elements[id].Rotation.Should().Be(30);
        page.Elements[id].ZIndex.Should().Be(5);

        page.MoveElement(id, 50, 0, 100, 100, Guid.NewGuid());
        page.Elements[id].Rotation.Should().Be(30);
        page.Elements[id].ZIndex.Should().Be(5);
        page.Elements[id].Version.Should().Be(3);
    }
}
