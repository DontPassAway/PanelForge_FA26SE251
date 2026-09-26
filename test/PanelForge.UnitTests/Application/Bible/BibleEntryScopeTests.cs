using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntryHistory;
using PanelForge.Application.Interfaces.Persistence;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>
/// Chống IDOR: BibleEntry chỉ được đọc/sửa qua đúng seriesId sở hữu nó
/// (RequireWorkspaceRole chỉ kiểm tra quyền theo seriesId trên route).
/// </summary>
public class BibleEntryScopeTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock = new();
    private readonly Guid _ownSeriesId = Guid.NewGuid();
    private readonly Guid _foreignSeriesId = Guid.NewGuid();
    private readonly BibleEntry _foreignEntry;

    public BibleEntryScopeTests()
    {
        var ownBible = SeriesBible.Create(_ownSeriesId, Guid.NewGuid(), "Own Bible");
        var foreignBible = SeriesBible.Create(_foreignSeriesId, Guid.NewGuid(), "Foreign Bible");
        _foreignEntry = foreignBible.AddEntry(BibleEntryCategory.Character, "HERO", "Hero", "Foreign hero");

        _dbContextMock.Setup(db => db.SeriesBibles)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { ownBible, foreignBible }));
        _dbContextMock.Setup(db => db.BibleEntries)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntry> { _foreignEntry }));
        _dbContextMock.Setup(db => db.BibleEntryRevisions)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntryRevision>()));
    }

    [Fact]
    public async Task AddRevision_WhenEntryBelongsToAnotherSeries_ShouldFail_AndNotSave()
    {
        var handler = new AddBibleEntryRevisionCommandHandler(_dbContextMock.Object);
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _ownSeriesId,
            BibleEntryId: _foreignEntry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Hijack",
            Name: "Hacked",
            Description: "Hacked");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _foreignEntry.Name.Should().Be("Hero");
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetHistory_WhenEntryBelongsToAnotherSeries_ShouldFail()
    {
        var handler = new GetBibleEntryHistoryQueryHandler(_dbContextMock.Object);

        var result = await handler.Handle(
            new GetBibleEntryHistoryQuery(_ownSeriesId, _foreignEntry.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetHistory_WhenEntryBelongsToRouteSeries_ShouldSucceed()
    {
        var handler = new GetBibleEntryHistoryQueryHandler(_dbContextMock.Object);

        var result = await handler.Handle(
            new GetBibleEntryHistoryQuery(_foreignSeriesId, _foreignEntry.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
