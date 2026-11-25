using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Pages.Commands.ReplaceKeepOutZones;
using PanelForge.Application.Features.Pages.Models;
using PanelForge.Application.Features.Pages.Queries.GetKeepOutZones;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Pages;

/// <summary>Vùng cấm chữ theo trang: toạ độ 0..1, panelId phải thuộc trang, ghi đè có kiểm version (BR-11).</summary>
public class KeepOutZonesTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Page _page;
    private readonly Panel _panel;
    private readonly Panel _otherPagePanel;

    public KeepOutZonesTests()
    {
        var studio = StudioWorkspace.Create("Studio", Guid.NewGuid());
        var series = DomainSeries.Create(studio.Id, "S", ReadingDirection.RightToLeft, "x", "Fantasy");
        var chapter = Chapter.Create(series.Id, 1, "Một");
        _page = Page.CreateNew(chapter.Id, 1, default, 1200, 1800, 300);
        var other = Page.CreateNew(chapter.Id, 2, default, 1200, 1800, 300);
        _panel = Panel.Create(_page.Id, 1, 1);
        _otherPagePanel = Panel.Create(other.Id, 1, 1);
        _db.AddRange(studio, series, chapter, _page, other, _panel, _otherPagePanel);
        _db.SaveChanges();
    }

    private Task<Result<PageKeepOutZonesDto>> Put(IReadOnlyList<KeepOutZoneDto> zones, int? expected = null) =>
        new ReplaceKeepOutZonesCommandHandler(_db).Handle(new ReplaceKeepOutZonesCommand(_page.Id, zones, expected, Guid.NewGuid()), default);

    [Fact]
    public async Task Put_then_get_round_trips_and_bumps_version()
    {
        (await new GetKeepOutZonesQueryHandler(_db).Handle(new GetKeepOutZonesQuery(_page.Id), default)).Value!.Version.Should().Be(0);

        var saved = await Put([new KeepOutZoneDto(0.1, 0.2, 0.3, 0.4, " mặt ", _panel.Id)], expected: 0);
        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved.Value!.Version.Should().Be(1);

        var got = (await new GetKeepOutZonesQueryHandler(_db).Handle(new GetKeepOutZonesQuery(_page.Id), default)).Value!;
        got.Zones.Should().ContainSingle().Which.Should().Be(new KeepOutZoneDto(0.1, 0.2, 0.3, 0.4, "mặt", _panel.Id));
    }

    [Fact]
    public async Task Stale_expected_version_is_conflict()
    {
        (await Put([], expected: 0)).IsSuccess.Should().BeTrue();
        var stale = await Put([], expected: 0);
        stale.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
    }

    [Theory]
    [InlineData(-0.1, 0, 0.5, 0.5)]
    [InlineData(0.6, 0, 0.5, 0.5)]
    [InlineData(0, 0, 0, 0.5)]
    public async Task Out_of_page_coordinates_are_rejected(double x, double y, double w, double h)
        => (await Put([new KeepOutZoneDto(x, y, w, h, null, null)])).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task Panel_from_another_page_is_rejected()
        => (await Put([new KeepOutZoneDto(0, 0, 0.5, 0.5, null, _otherPagePanel.Id)])).IsSuccess.Should().BeFalse();
}
