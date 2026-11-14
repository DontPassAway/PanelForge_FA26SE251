using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.DefineBibleEntry;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class DefineBibleEntryTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly SeriesBible _bible;
    private readonly BibleEntry _stubEntry;
    private readonly BibleEntry _existingNormalEntry;

    public DefineBibleEntryTests()
    {
        _bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");
        _stubEntry = BibleEntry.CreateStub(_bible.Id, BibleEntryCategory.Character, "Kuro");
        var stubRev1 = _stubEntry.AddRevision("Stub v1", "{}", "hash-stub", authorId: _userId, isInitialVersion: true);

        _existingNormalEntry = _bible.AddEntry(BibleEntryCategory.Character, "CHR_SHIRO", "Shiro", "Bạch xà");
        var existingRev1 = _existingNormalEntry.AddRevision("v1", "{}", "hash-shiro", authorId: _userId, isInitialVersion: true);

        _db.SeriesBibles.Add(_bible);
        _db.BibleEntries.AddRange(_stubEntry, _existingNormalEntry);
        _db.BibleEntryRevisions.AddRange(stubRev1, existingRev1);

        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Kuro", _stubEntry.Id, isPrimary: true));
        _db.BibleEntryNames.Add(BibleEntryName.Create(_bible.Id, "Shiro", _existingNormalEntry.Id, isPrimary: true));

        _db.SaveChanges();
    }

    [Fact]
    public async Task Define_ValidStub_PromotesAndOverwritesDraftV1()
    {
        var handler = new DefineBibleEntryCommandHandler(_db);
        var cmd = new DefineBibleEntryCommand(
            SeriesId: _seriesId,
            BibleEntryId: _stubEntry.Id,
            UserId: _userId,
            Name: "Kurogane",
            Description: "Hắc kiếm sĩ bí ẩn vùng biên cảnh",
            Aliases: new[] { "Black Steel", "Kiếm Đen" },
            Subtitle: "Hắc Kiếm",
            Code: null // Tự sinh
        );

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        var entry = result.Value;

        entry.IsStub.Should().BeFalse();
        entry.Name.Should().Be("Kurogane");
        entry.Description.Should().Be("Hắc kiếm sĩ bí ẩn vùng biên cảnh");
        entry.Code.Should().StartWith("CHR_");
        entry.Code.Should().NotStartWith("STUB_");
        entry.Aliases.Should().BeEquivalentTo(new[] { "Black Steel", "Kiếm Đen" });

        // Chỉ có đúng 1 revision (v1 bị ghi đè, không sinh v2)
        var revisions = await _db.BibleEntryRevisions.Where(r => r.BibleEntryId == _stubEntry.Id).ToListAsync();
        revisions.Should().HaveCount(1);
        var rev = revisions[0];
        rev.VersionNumber.Should().Be(1);
        rev.Summary.Should().Contain("Kurogane");
        rev.SnapshotJson.Should().Contain("Kurogane");

        // Bảng bible_entry_names cập nhật đúng
        var names = await _db.BibleEntryNames.Where(n => n.BibleEntryId == _stubEntry.Id).ToListAsync();
        names.Should().HaveCount(3); // Kurogane (primary) + 2 aliases
        names.Should().Contain(n => n.NormalizedName == "kurogane" && n.IsPrimary);
        names.Should().Contain(n => n.NormalizedName == "black steel" && !n.IsPrimary);
        names.Should().Contain(n => n.NormalizedName == "kiếm đen" && !n.IsPrimary);
    }

    [Fact]
    public async Task Define_WhenEntryNotStub_ReturnsConflict()
    {
        var handler = new DefineBibleEntryCommandHandler(_db);
        var cmd = new DefineBibleEntryCommand(
            SeriesId: _seriesId,
            BibleEntryId: _existingNormalEntry.Id,
            UserId: _userId,
            Name: "Shiro Reborn",
            Description: "Mô tả mới"
        );

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("không phải mục nháp");
    }

    [Fact]
    public async Task Define_EmptyDescription_ReturnsValidation()
    {
        var handler = new DefineBibleEntryCommandHandler(_db);
        var cmd = new DefineBibleEntryCommand(
            SeriesId: _seriesId,
            BibleEntryId: _stubEntry.Id,
            UserId: _userId,
            Name: "Kurogane",
            Description: "   " // Rỗng
        );

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("Mô tả");
    }

    [Fact]
    public async Task Define_DuplicateNameWithAnotherEntry_ReturnsConflict()
    {
        var handler = new DefineBibleEntryCommandHandler(_db);
        var cmd = new DefineBibleEntryCommand(
            SeriesId: _seriesId,
            BibleEntryId: _stubEntry.Id,
            UserId: _userId,
            Name: "Shiro", // Đã có mục Shiro
            Description: "Mô tả hợp lệ"
        );

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("trùng");
    }

    [Fact]
    public async Task Define_DuplicateAliasWithAnotherEntry_ReturnsConflict()
    {
        var handler = new DefineBibleEntryCommandHandler(_db);
        var cmd = new DefineBibleEntryCommand(
            SeriesId: _seriesId,
            BibleEntryId: _stubEntry.Id,
            UserId: _userId,
            Name: "Kuro New",
            Description: "Mô tả hợp lệ",
            Aliases: new[] { "shiro" } // Trùng với tên mục Shiro
        );

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("trùng");
    }

    [Fact]
    public async Task Define_NonExistentEntry_ReturnsNotFound()
    {
        var handler = new DefineBibleEntryCommandHandler(_db);
        var cmd = new DefineBibleEntryCommand(
            SeriesId: _seriesId,
            BibleEntryId: Guid.NewGuid(),
            UserId: _userId,
            Name: "Test",
            Description: "Mô tả"
        );

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }
}
