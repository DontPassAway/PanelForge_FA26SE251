using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.SetBibleEntryCrowd;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class SetBibleEntryCrowdTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly BibleEntry _entry;
    private readonly Chapter _chapterActive;
    private readonly Chapter _chapterFrozen;
    private readonly ChapterBibleBinding _bindingActive;
    private readonly ChapterBibleBinding _bindingFrozen;

    public SetBibleEntryCrowdTests()
    {
        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");
        _entry = bible.AddEntry(BibleEntryCategory.Character, "CHR-01", "Dân Làng", "Người dân làng Lá");
        _db.SeriesBibles.Add(bible);
        _db.BibleEntries.Add(_entry);

        // Chương 1 đang làm kịch bản (chưa Frozen)
        _chapterActive = Chapter.Create(_seriesId, 1, "Chương 1");
        _db.Chapters.Add(_chapterActive);

        // Chương 2 đã đóng băng kịch bản (Frozen)
        _chapterFrozen = Chapter.Create(_seriesId, 2, "Chương 2");
        _chapterFrozen.SubmitScript(_userId);
        _chapterFrozen.StartProduction(_userId, "hash-frozen");
        _db.Chapters.Add(_chapterFrozen);

        _bindingActive = ChapterBibleBinding.Create(
            _chapterActive.Id,
            _entry.Id,
            boundRevisionId: null,
            source: ChapterBibleBindingSource.Speaker,
            chosenByWriter: false,
            isCrowdAtBind: false);
        _db.ChapterBibleBindings.Add(_bindingActive);

        _bindingFrozen = ChapterBibleBinding.Create(
            _chapterFrozen.Id,
            _entry.Id,
            boundRevisionId: null,
            source: ChapterBibleBindingSource.Speaker,
            chosenByWriter: false,
            isCrowdAtBind: false);
        _db.ChapterBibleBindings.Add(_bindingFrozen);

        _db.SaveChanges();
    }

    [Fact]
    public async Task SetCrowd_True_UpdatesEntryAndSyncsNonFrozenChapters()
    {
        var handler = new SetBibleEntryCrowdCommandHandler(_db);
        var cmd = new SetBibleEntryCrowdCommand(_seriesId, _entry.Id, IsCrowd: true, UserId: _userId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        _entry.IsCrowd.Should().BeTrue();

        // Chương chưa Frozen được cập nhật is_crowd_at_bind = true
        _bindingActive.IsCrowdAtBind.Should().BeTrue();

        // Chương đã Frozen giữ nguyên is_crowd_at_bind = false
        _bindingFrozen.IsCrowdAtBind.Should().BeFalse();
    }

    [Fact]
    public async Task SetCrowd_False_RevertsEntryAndNonFrozenChapters()
    {
        // Ban đầu đặt là Crowd
        _entry.SetCrowd(true);
        _bindingActive.SetCrowdAtBind(true);
        _bindingFrozen.SetCrowdAtBind(true);
        _db.SaveChanges();

        var handler = new SetBibleEntryCrowdCommandHandler(_db);
        var cmd = new SetBibleEntryCrowdCommand(_seriesId, _entry.Id, IsCrowd: false, UserId: _userId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        _entry.IsCrowd.Should().BeFalse();

        // Chương chưa Frozen về false
        _bindingActive.IsCrowdAtBind.Should().BeFalse();

        // Chương đã Frozen giữ nguyên true lúc chốt
        _bindingFrozen.IsCrowdAtBind.Should().BeTrue();
    }

    [Fact]
    public async Task SetCrowd_NonExistentEntry_ReturnsNotFound()
    {
        var handler = new SetBibleEntryCrowdCommandHandler(_db);
        var cmd = new SetBibleEntryCrowdCommand(_seriesId, Guid.NewGuid(), IsCrowd: true, UserId: _userId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }
}
