using System.Text.Json;
using FluentAssertions;
using Moq;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Enums;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

/// <summary>
/// BIB-02 (Q16): Validation biến thể tạo hình mới và sửa nội dung:
/// 1. Tạo biến thể mới: variant_label (cột StageName) bắt buộc (400 nếu rỗng) và không trùng nhãn biến thể khác đang hoạt động (409).
/// 2. Tạo biến thể mới trùng nhãn với biến thể đã bị Superseded: cho phép.
/// 3. Sửa nội dung / bản thay thế: cấm đổi variant_label (tránh tách biến thể) -> 409.
/// </summary>
public class BibleVariantValidationTests
{
    private readonly Mock<IPanelForgeDbContext> _dbContextMock = new();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly BibleEntry _entry;
    private readonly AddBibleEntryRevisionCommandHandler _handler;

    public BibleVariantValidationTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Ninja Bible");
        _entry = bible.AddEntry(BibleEntryCategory.Character, "NARUTO", "Naruto Uzumaki", "Hokage");
        _entry.AddRevision("v1", Snapshot("Naruto Uzumaki", "Hokage"), "hash1", isInitialVersion: true, stageName: "Default");

        _dbContextMock.Setup(db => db.SeriesBibles)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<SeriesBible> { bible }));
        _dbContextMock.Setup(db => db.BibleEntries)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntry> { _entry }));
        _dbContextMock.Setup(db => db.BibleEntryRevisions)
            .Returns(DbSetMockHelper.CreateDbSetMock(new List<BibleEntryRevision>()));

        _handler = new AddBibleEntryRevisionCommandHandler(_dbContextMock.Object, Moq.Mock.Of<PanelForge.Application.Common.Interfaces.Services.IAssetStorage>());
    }

    private static string Snapshot(string name, string description)
        => JsonSerializer.Serialize(new { Name = name, Description = description });

    [Fact]
    public async Task CreateVariant_WithValidLabel_ShouldSucceed()
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Tạo biến thể Thiếu niên",
            IsNewVariant: true,
            VariantLabel: "Teen"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.StageName.Should().Be("Teen");
        result.Value.ReviewStatus.Should().Be(BibleRevisionStatus.Draft);
        _entry.Revisions.Should().Contain(r => r.StageName == "Teen");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateVariant_WithEmptyOrWhitespaceLabel_ShouldReturnValidationFailure_400(string? label)
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Tạo biến thể rỗng",
            IsNewVariant: true,
            VariantLabel: label
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
        result.ErrorMessage.Should().Contain("variant_label");
    }

    [Fact]
    public async Task CreateVariant_WithDuplicateActiveLabel_ExactMatch_ShouldReturnConflict_409()
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Tạo biến thể trùng tên",
            IsNewVariant: true,
            VariantLabel: "Default"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("đã tồn tại và đang hoạt động");
    }

    [Theory]
    [InlineData("default")]
    [InlineData("  DEFAULT  ")]
    [InlineData("Default  ")]
    public async Task CreateVariant_WithDuplicateActiveLabel_Normalized_ShouldReturnConflict_409(string duplicateLabel)
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Tạo biến thể trùng tên chuẩn hoá",
            IsNewVariant: true,
            VariantLabel: duplicateLabel
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("đã tồn tại và đang hoạt động");
    }

    [Fact]
    public async Task CreateVariant_WithDuplicateOfSupersededVariant_ShouldSucceed()
    {
        // 1. Tạo biến thể Armor và khoá
        var rev1 = _entry.Revisions.First();
        var armorRev = _entry.AddRevision("v2 Armor", Snapshot("Naruto", "Armor"), "h2", isInitialVersion: false, stageName: "Armor");
        armorRev.Lock(Guid.NewGuid());

        // 2. Đánh dấu Armor bị Superseded
        armorRev.MarkSuperseded(Guid.NewGuid(), 3, "Thay thế giáp cũ");

        _entry.HasActiveVariant("Armor").Should().BeFalse();

        // 3. Tạo lại biến thể mới mang tên "Armor" -> Cho phép vì biến thể cũ đã superseded
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Tạo lại biến thể Armor mới",
            IsNewVariant: true,
            VariantLabel: "Armor"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.StageName.Should().Be("Armor");
        result.Value.VersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task ContentEdit_ChangingVariantLabel_ShouldReturnConflict_409()
    {
        // Entry đang có v1 với StageName = "Default"
        // Writer sửa nội dung (IsNewVariant = false) nhưng cố tình đổi StageName = "Timeskip"
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Sửa nội dung đổi nhãn",
            IsNewVariant: false,
            Name: "Naruto Uzumaki",
            StageName: "Timeskip"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Không thể thay đổi tên biến thể");
    }

    [Fact]
    public async Task ContentEdit_KeepingSameVariantLabel_ShouldSucceed()
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Sửa nội dung giữ nguyên nhãn",
            IsNewVariant: false,
            Name: "Naruto Uzumaki",
            StageName: "Default"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.StageName.Should().Be("Default");
        _entry.Name.Should().Be("Naruto Uzumaki");
    }

    [Fact]
    public async Task ContentEdit_WithNullVariantLabel_ShouldKeepBaseVariantLabel_AndSucceed()
    {
        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Sửa nội dung không truyền StageName",
            IsNewVariant: false,
            Name: "Naruto Uzumaki",
            StageName: null
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.StageName.Should().Be("Default");
    }

    [Fact]
    public async Task ReplacementRevision_ChangingVariantLabel_ShouldReturnConflict_409()
    {
        var rev1 = _entry.Revisions.First();
        rev1.Lock(Guid.NewGuid());

        var command = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: Guid.NewGuid(),
            Summary: "Thay thế bản khoá nhưng đổi label",
            SupersedesRevisionId: rev1.Id,
            SupersedeReason: "Đổi thiết kế",
            StageName: "DifferentStage"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Bản thay thế phải thuộc cùng biến thể");
    }
}
