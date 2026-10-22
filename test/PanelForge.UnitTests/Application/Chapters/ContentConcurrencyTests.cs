using PanelForge.Application.Common.Interfaces.Services;
using Moq;
using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Panels.Commands.UpdatePanelCoordinates;
using PanelForge.Application.Features.Scenes.Commands.UpdateScene;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

/// <summary>BR-11 / CF4 4.2: Panel và Scene có version riêng; lệnh sửa gửi expectedVersion cũ thì bị từ chối (409), không ghi đè.</summary>
public class ContentConcurrencyTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Scene _scene;
    private readonly Panel _panel;
    private readonly Panel _otherPanel;

    public ContentConcurrencyTests()
    {
        var studio = StudioWorkspace.Create("Studio", _userId);
        _db.StudioWorkspaces.Add(studio);
        var series = DomainSeries.Create(studio.Id, "Lantern", ReadingDirection.RightToLeft);
        _db.Series.Add(series);
        var chapter = Chapter.Create(series.Id, 1, "Lá thư");
        _db.Chapters.Add(chapter);
        _scene = Scene.Create(chapter.Id, 1, "Bến cảng");
        _db.Scenes.Add(_scene);
        var page = Page.CreateNew(chapter.Id, 1, LayoutFormat.StandardPage, 1200, 1800, 300);
        _db.Pages.Add(page);
        _panel = Panel.Create(page.Id, 1, 1, "{}");
        _otherPanel = Panel.Create(page.Id, 2, 2, "{}");
        _db.Panels.AddRange(_panel, _otherPanel);
        _db.SaveChanges();
    }

    private Task<Result<PanelForge.Application.Features.Panels.Models.PanelDto>> MovePanel(Panel panel, string bbox, int? expected) =>
        new UpdatePanelCoordinatesCommandHandler(_db).Handle(
            new UpdatePanelCoordinatesCommand(panel.Id, _userId, bbox, ExpectedVersion: expected), default);

    private Task<Result<PanelForge.Application.Features.Scenes.Models.SceneDto>> EditScene(string heading, int? expected) =>
        new UpdateSceneCommandHandler(_db, Mock.Of<IWorkspaceAuthorizationService>()).Handle(
            new UpdateSceneCommand(_scene.Id, _userId, heading, null, ExpectedVersion: expected), default);

    [Fact]
    public async Task Panel_WithStaleExpectedVersion_IsRejected_AndKeepsFirstWrite()
    {
        var first = await MovePanel(_panel, "{\"x\":10}", expected: 0);
        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        first.Value!.Version.Should().Be(1);

        // Người thứ hai vẫn cầm bản version 0
        var second = await MovePanel(_panel, "{\"x\":99}", expected: 0);

        second.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _panel.BoundingBox.Should().Be("{\"x\":10}");
        _panel.Version.Should().Be(1);
    }

    [Fact]
    public async Task Panel_EditingAnotherPanel_DoesNotConflict()
    {
        (await MovePanel(_panel, "{\"x\":10}", expected: 0)).IsSuccess.Should().BeTrue();
        (await MovePanel(_otherPanel, "{\"x\":20}", expected: 0)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Panel_WithoutExpectedVersion_StillWorks_ForOldClients()
    {
        await MovePanel(_panel, "{\"x\":10}", expected: 0);
        var result = await MovePanel(_panel, "{\"x\":20}", expected: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Version.Should().Be(2);
    }

    [Fact]
    public async Task Panel_SavingSameValuesAgain_DoesNotBumpVersion()
    {
        (await MovePanel(_panel, "{\"x\":10}", expected: 0)).Value!.Version.Should().Be(1);

        // Lưu lại y nguyên (vd bấm Lưu hai lần): không tạo phiên bản mới nên không làm người khác bị xung đột
        var again = await MovePanel(_panel, "{\"x\":10}", expected: 1);

        again.IsSuccess.Should().BeTrue();
        again.Value!.Version.Should().Be(1);
    }

    [Fact]
    public async Task Scene_WithStaleExpectedVersion_IsRejected_AndKeepsFirstWrite()
    {
        var first = await EditScene("Bến cảng đêm", expected: 0);
        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        first.Value!.Version.Should().Be(1);

        var second = await EditScene("Bến cảng sáng", expected: 0);

        second.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        _scene.Heading.Should().Be("Bến cảng đêm");
        (await EditScene("Bến cảng sáng", expected: 1)).IsSuccess.Should().BeTrue();
    }
}
