using FluentAssertions;
using Moq;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Commands.CreateBibleEntry;
using PanelForge.Application.Features.Bible.Queries.GetSeriesBible;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>Series cũ/seed chưa có SeriesBible: màn hình Series Bible phải mở được (Bible rỗng) và mục đầu tiên tạo Bible.</summary>
public class MissingSeriesBibleTests
{
    private readonly Mock<IPanelForgeDbContext> _db = new();
    private readonly List<SeriesBible> _bibles = [];
    private readonly Series _series = Series.Create(Guid.NewGuid(), "Mặc Kiếm Lục", ReadingDirection.RightToLeft);

    public MissingSeriesBibleTests()
    {
        var bibleSet = DbSetMockHelper.CreateDbSetMock(_bibles);
        Mock.Get(bibleSet).Setup(s => s.Add(It.IsAny<SeriesBible>())).Callback<SeriesBible>(_bibles.Add);
        _db.Setup(d => d.SeriesBibles).Returns(bibleSet);
        _db.Setup(d => d.Series).Returns(DbSetMockHelper.CreateDbSetMock(new List<Series> { _series }));
        _db.Setup(d => d.BibleEntries).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntry>()));
        _db.Setup(d => d.BibleEntryRevisions).Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntryRevision>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    [Fact]
    public async Task GetSeriesBible_WhenSeriesHasNoBible_ReturnsEmptyBible()
    {
        var result = await new GetSeriesBibleQueryHandler(_db.Object)
            .Handle(new GetSeriesBibleQuery(_series.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.Entries.Should().BeEmpty();
        result.Value.WorkspaceId.Should().Be(_series.WorkspaceId);
    }

    [Fact]
    public async Task GetSeriesBible_WhenSeriesDoesNotExist_Fails()
    {
        var result = await new GetSeriesBibleQueryHandler(_db.Object)
            .Handle(new GetSeriesBibleQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CreateBibleEntry_WhenSeriesHasNoBible_CreatesBibleThenEntry()
    {
        var result = await new CreateBibleEntryCommandHandler(_db.Object).Handle(
            new CreateBibleEntryCommand(_series.Id, Guid.NewGuid(), BibleEntryCategory.Character, "hero", "Hero", "Kiếm khách"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        _bibles.Should().ContainSingle(b => b.SeriesId == _series.Id && b.WorkspaceId == _series.WorkspaceId);
        _bibles[0].Entries.Should().ContainSingle(e => e.Code == "HERO");
    }
}
