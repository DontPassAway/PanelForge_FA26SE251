using System.Text.Json;
using FluentAssertions;
using Moq;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Queries.GetBibleEntriesAtChapter;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>
/// BR-15: bản sửa hồi tố (effective từ chương cũ hơn) được lưu thành version mới
/// nhưng KHÔNG ghi đè current state của entry.
/// </summary>
public class BibleRevisionCurrentStateTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock = new();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly BibleEntry _entry;
    private readonly AddBibleEntryRevisionCommandHandler _handler;

    public BibleRevisionCurrentStateTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Bible");
        _entry = bible.AddEntry(BibleEntryCategory.Character, "HERO", "Hero ch1", "Desc ch1");
        _entry.AddRevision("v1", Snapshot("Hero ch1", "Desc ch1"), "h1", isInitialVersion: true);

        _dbContextMock.Setup(db => db.SeriesBibles)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { bible }));
        _dbContextMock.Setup(db => db.BibleEntries)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntry> { _entry }));
        _dbContextMock.Setup(db => db.BibleEntryRevisions)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntryRevision>()));

        _handler = new AddBibleEntryRevisionCommandHandler(_dbContextMock.Object);
    }

    private static string Snapshot(string name, string description)
        => JsonSerializer.Serialize(new { Name = name, Description = description });

    private Task<PanelForge.Application.Common.Result<PanelForge.Application.Features.Bible.Models.BibleEntryRevisionDto>> AddRevision(
        string name, int? expectedVersion = null)
        => _handler.Handle(new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: $"Change to {name}",
            Name: name,
            Description: $"Desc for {name}",
            ExpectedVersionNumber: expectedVersion), CancellationToken.None);

    [Fact]
    public async Task ForwardRevision_ShouldUpdateCurrentState()
    {
        var result = await AddRevision("Hero v2");

        result.IsSuccess.Should().BeTrue();
        _entry.Name.Should().Be("Hero v2");
        _entry.GetCurrentRevision()!.VersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task Revision_WithStaleExpectedVersion_ShouldReturnConflict_AndNotSave()
    {
        // Người A và B cùng mở entry ở version 1; A lưu trước → version 2
        (await AddRevision("Hero by A", expectedVersion: 1)).IsSuccess.Should().BeTrue();

        var resultB = await AddRevision("Hero by B", expectedVersion: 1);

        resultB.IsSuccess.Should().BeFalse();
        resultB.ErrorCode.Should().Be(PanelForge.Application.Common.ResultErrorCodes.Conflict);
        resultB.ErrorMessage.Should().Contain("version 2");
        _entry.Revisions.Should().HaveCount(2);
        _entry.Name.Should().Be("Hero by A");
    }

    [Fact]
    public async Task Revision_WithCurrentExpectedVersion_ShouldSucceed()
    {
        var result = await AddRevision("Hero v2", expectedVersion: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.VersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task Revision_WithEmptyName_ShouldFail_AndNotAddVersion()
    {
        var result = await AddRevision("   ");

        result.IsSuccess.Should().BeFalse();
        _entry.Revisions.Should().HaveCount(1);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
