using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.AddBibleEntryRevision;
using PanelForge.Application.Features.Bible.Commands.CancelReplacementRevision;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class CancelReplacementRevisionTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _chapterId = Guid.NewGuid();
    private readonly BibleEntry _entry;
    private readonly BibleEntryRevision _v1;

    public CancelReplacementRevisionTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");
        _entry = bible.AddEntry(BibleEntryCategory.Character, "CHR-01", "Naruto", "Ninja làng Lá");
        _v1 = _entry.AddRevision(
            summary: "Bản v1 ban đầu",
            snapshotJson: "{}",
            contentHash: "hash-v1",
            authorId: _userId,
            stageName: "Trưởng thành",
            isInitialVersion: true
        );
        _v1.SubmitConcept(_userId, "https://r2.example/v1.png", null);
        _v1.Lock(_userId);

        _db.SeriesBibles.Add(bible);
        _db.BibleEntries.Add(_entry);
        _db.BibleEntryRevisions.Add(_v1);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Supersede_DifferentVariant_ShouldReturnConflict()
    {
        var handler = new AddBibleEntryRevisionCommandHandler(_db, Moq.Mock.Of<PanelForge.Application.Common.Interfaces.Services.IAssetStorage>());
        var cmd = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: _userId,
            Summary: "Sửa tạo hình",
            Name: _entry.Name,
            Description: _entry.Description,
            SupersedesRevisionId: _v1.Id,
            SupersedeReason: "Muốn đổi sang biến thể khác",
            StageName: "Lúc nhỏ" // Khác với "Trưởng thành" của v1
        );

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("cùng biến thể");
    }

    [Fact]
    public async Task Supersede_WithChapterId_BindsToChapterN_AndDoesNotMarkSupersededEarly()
    {
        var handler = new AddBibleEntryRevisionCommandHandler(_db, Moq.Mock.Of<PanelForge.Application.Common.Interfaces.Services.IAssetStorage>());
        var cmd = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: _userId,
            Summary: "Sửa màu áo",
            Name: _entry.Name,
            Description: _entry.Description,
            SupersedesRevisionId: _v1.Id,
            SupersedeReason: "Áo cam đậm hơn",
            StageName: "Trưởng thành",
            ChapterId: _chapterId
        );

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        var v2Dto = result.Value!;
        v2Dto.VersionNumber.Should().Be(2);
        v2Dto.SupersedesRevisionId.Should().Be(_v1.Id);
        v2Dto.ReviewStatus.Should().Be(BibleRevisionStatus.Draft);
        v2Dto.IsLocked.Should().BeFalse();

        // v1 vẫn là CanonLocked
        _v1.IsLocked.Should().BeTrue();
        _v1.ReviewStatus.Should().Be(BibleRevisionStatus.CanonLocked);

        // Binding chương N được tạo và gắn v2 với chosen_by_writer = true
        var binding = await _db.ChapterBibleBindings
            .FirstOrDefaultAsync(b => b.ChapterId == _chapterId && b.BibleEntryId == _entry.Id);
        binding.Should().NotBeNull();
        binding!.BoundRevisionId.Should().Be(v2Dto.Id);
        binding.ChosenByWriter.Should().BeTrue();
    }

    [Fact]
    public async Task CancelReplacementRevision_ValidDraftReplacement_RevertsBindingAndDeletesRevision()
    {
        // Tạo bản v2 thay thế v1 và gắn vào chapter
        var addHandler = new AddBibleEntryRevisionCommandHandler(_db, Moq.Mock.Of<PanelForge.Application.Common.Interfaces.Services.IAssetStorage>());
        var addCmd = new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: _userId,
            Summary: "Sửa áo",
            Name: _entry.Name,
            Description: _entry.Description,
            SupersedesRevisionId: _v1.Id,
            SupersedeReason: "Thử mẫu mới",
            StageName: "Trưởng thành",
            ChapterId: _chapterId
        );
        var addResult = await addHandler.Handle(addCmd, default);
        addResult.IsSuccess.Should().BeTrue();
        var v2Id = addResult.Value!.Id;

        // Cancel bản v2
        var cancelHandler = new CancelReplacementRevisionCommandHandler(_db);
        var cancelResult = await cancelHandler.Handle(
            new CancelReplacementRevisionCommand(_seriesId, _entry.Id, v2Id, _userId), default);

        cancelResult.IsSuccess.Should().BeTrue();

        // Bản v2 đã bị xoá
        var v2InDb = await _db.BibleEntryRevisions.FirstOrDefaultAsync(r => r.Id == v2Id);
        v2InDb.Should().BeNull();
        _entry.Revisions.Should().NotContain(r => r.Id == v2Id);

        // Binding quay về v1 với chosen_by_writer = false
        var binding = await _db.ChapterBibleBindings
            .FirstOrDefaultAsync(b => b.ChapterId == _chapterId && b.BibleEntryId == _entry.Id);
        binding.Should().NotBeNull();
        binding!.BoundRevisionId.Should().Be(_v1.Id);
        binding.ChosenByWriter.Should().BeFalse();
    }

    [Fact]
    public async Task CancelReplacementRevision_NotAReplacement_ShouldReturnConflict()
    {
        var cancelHandler = new CancelReplacementRevisionCommandHandler(_db);
        // v1 không có supersedes_revision_id
        var result = await cancelHandler.Handle(
            new CancelReplacementRevisionCommand(_seriesId, _entry.Id, _v1.Id, _userId), default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("supersedes_revision_id");
    }

    [Fact]
    public async Task CancelReplacementRevision_HasReviewHistory_ShouldReturnConflict()
    {
        var addHandler = new AddBibleEntryRevisionCommandHandler(_db, Moq.Mock.Of<PanelForge.Application.Common.Interfaces.Services.IAssetStorage>());
        var addResult = await addHandler.Handle(new AddBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _entry.Id,
            UserId: _userId,
            Summary: "Sửa",
            SupersedesRevisionId: _v1.Id,
            SupersedeReason: "Lý do",
            StageName: "Trưởng thành"
        ), default);
        var v2 = _entry.Revisions.First(r => r.Id == addResult.Value!.Id);

        // Nộp concept -> có review history
        v2.SubmitConcept(_userId, "https://r2.example/v2.png", "Concept v2");
        await _db.SaveChangesAsync();

        var cancelHandler = new CancelReplacementRevisionCommandHandler(_db);
        var cancelResult = await cancelHandler.Handle(
            new CancelReplacementRevisionCommand(_seriesId, _entry.Id, v2.Id, _userId), default);

        cancelResult.IsSuccess.Should().BeFalse();
        cancelResult.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        cancelResult.ErrorMessage.Should().Contain("sự kiện kiểm duyệt");
    }
}
