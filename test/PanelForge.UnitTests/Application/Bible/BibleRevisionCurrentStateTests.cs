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
/// Draft được lưu append-only và không ghi đè cache Canon của entry.
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
        string description, int? expectedVersion = null, string? name = null)
        => _handler.Handle(new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: $"Change to {description}",
            Name: name ?? _entry.Name,
            Description: description,
            ExpectedVersionNumber: expectedVersion), CancellationToken.None);

    [Fact]
    public async Task DraftRevision_ShouldNotUpdateEntryCache()
    {
        var result = await AddRevision("Desc v2");

        result.IsSuccess.Should().BeTrue();
        _entry.Name.Should().Be("Hero ch1");
        _entry.Description.Should().Be("Desc ch1");
        _entry.ReferenceImageUrl.Should().BeNull();
        _entry.GetLatestRevision()!.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task Revision_WithStaleExpectedVersion_ShouldReturnConflict_AndNotSave()
    {
        // Khóa Canon cho bản v1 để cho phép supersede tạo v2
        var v1 = _entry.Revisions.First();
        v1.SubmitConcept(Guid.NewGuid(), "https://r2/hero.png", null);
        v1.Lock(Guid.NewGuid());

        // Người A thay thế bản khóa (/supersede) -> sinh version 2
        var supersedeA = await _handler.Handle(new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Desc by A",
            Name: _entry.Name,
            Description: "Desc by A",
            SupersedesRevisionId: v1.Id,
            SupersedeReason: "Lý do thay thế A",
            ExpectedVersionNumber: 1), CancellationToken.None);
        supersedeA.IsSuccess.Should().BeTrue();
        supersedeA.Value!.VersionNumber.Should().Be(2);

        // Người B cố thay thế từ version 1 khi đã có version 2 -> Xung đột 409
        var resultB = await _handler.Handle(new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Desc by B",
            Name: _entry.Name,
            Description: "Desc by B",
            SupersedesRevisionId: v1.Id,
            SupersedeReason: "Lý do thay thế B",
            ExpectedVersionNumber: 1), CancellationToken.None);

        resultB.IsSuccess.Should().BeFalse();
        resultB.ErrorCode.Should().Be(PanelForge.Application.Common.ResultErrorCodes.Conflict);
        resultB.ErrorMessage.Should().Contain("version 2");
        _entry.Revisions.Should().HaveCount(2);
        _entry.Description.Should().Be("Desc ch1");
    }

    [Fact]
    public async Task Revision_WithCurrentExpectedVersion_ShouldSucceed()
    {
        var result = await AddRevision("Desc v2", expectedVersion: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task Revision_WithDifferentName_ShouldFail_AndNotAddVersion()
    {
        var v1 = _entry.Revisions.First();
        v1.SubmitConcept(Guid.NewGuid(), "https://r2/hero.png", null);
        v1.Lock(Guid.NewGuid());

        var result = await AddRevision("Desc v2", name: "Hero renamed");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PanelForge.Application.Common.ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("không thể thay đổi");
        _entry.Revisions.Should().HaveCount(1);
        _dbContextMock.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
