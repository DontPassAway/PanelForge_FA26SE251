using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Commands.SetChapterCastRevision;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class SetChapterCastRevisionTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly ChapterCastInheritanceResolver _resolver;
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly BibleEntry _entry;
    private readonly BibleEntryRevision _v1;
    private readonly BibleEntryRevision _v2;
    private readonly BibleEntryRevision _vSuperseded;
    private readonly Chapter _ch1;
    private readonly Chapter _ch2;
    private readonly Chapter _ch3Frozen;

    public SetChapterCastRevisionTests()
    {
        _resolver = new ChapterCastInheritanceResolver(_db);

        var ws = StudioWorkspace.Create("Studio", _userId);
        typeof(StudioWorkspace).GetProperty(nameof(StudioWorkspace.Id))?.SetValue(ws, _workspaceId);
        _db.StudioWorkspaces.Add(ws);

        var series = Series.Create(_workspaceId, "Manga Test", ReadingDirection.RightToLeft, "Mô tả");
        typeof(Series).GetProperty(nameof(Series.Id))?.SetValue(series, _seriesId);
        _db.Series.Add(series);

        var bible = SeriesBible.Create(_seriesId, _workspaceId, "Master Bible");
        _entry = bible.AddEntry(BibleEntryCategory.Character, "CHR-01", "Naruto", "Ninja Làng Lá");

        _v1 = _entry.AddRevision("v1", "{}", "hash-v1", authorId: _userId, stageName: "Trưởng thành", isInitialVersion: true);
        _v1.SubmitConcept(_userId, "https://r2/v1.png", null);
        _v1.Lock(_userId);

        _v2 = _entry.AddRevision("v2", "{}", "hash-v2", authorId: _userId, stageName: "Trưởng thành", reviewStatus: BibleRevisionStatus.Draft);

        _vSuperseded = _entry.AddRevision("v0-old", "{}", "hash-v0", authorId: _userId, stageName: "Lúc nhỏ");
        _vSuperseded.MarkSuperseded(_userId, 1, "Bản thử nghiệm bị thay");

        _db.SeriesBibles.Add(bible);
        _db.BibleEntries.Add(_entry);
        _db.BibleEntryRevisions.AddRange(_v1, _v2, _vSuperseded);

        _ch1 = Chapter.Create(_seriesId, 1, "Chương 1");
        _ch2 = Chapter.Create(_seriesId, 2, "Chương 2");
        _ch3Frozen = Chapter.Create(_seriesId, 3, "Chương 3");
        _ch3Frozen.SubmitScript(_userId);
        _ch3Frozen.StartProduction(_userId, "hash-ch3");

        _db.Chapters.AddRange(_ch1, _ch2, _ch3Frozen);

        // Binding Ch1
        var b1 = ChapterBibleBinding.Create(_ch1.Id, _entry.Id, _v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false);
        // Binding Ch2
        var b2 = ChapterBibleBinding.Create(_ch2.Id, _entry.Id, _v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false);
        // Binding Ch3 frozen với v1
        var b3 = ChapterBibleBinding.Create(_ch3Frozen.Id, _entry.Id, _v1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);

        _db.ChapterBibleBindings.AddRange(b1, b2, b3);
        _db.SaveChanges();
    }

    [Fact]
    public async Task SetRevision_ExplicitDraftRevision_SucceedsAndRecomputesInheritance()
    {
        var handler = new SetChapterCastRevisionCommandHandler(_db, _resolver);
        var cmd = new SetChapterCastRevisionCommand(_ch1.Id, _entry.Id, _v2.Id, _userId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.BoundRevisionId.Should().Be(_v2.Id);
        result.Value.ChosenByWriter.Should().BeTrue();

        // Ch2 kế thừa theo Ch1 -> đổi sang v2
        var b2 = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == _ch2.Id && b.BibleEntryId == _entry.Id);
        b2.BoundRevisionId.Should().Be(_v2.Id);
        result.Value.AffectedChapterNumbers.Should().Contain(2);

        // Ch3 đã Frozen với v1 (khác v2) -> có cảnh báo
        result.Value.Warnings.Should().NotBeEmpty();
        result.Value.Warnings.Should().Contain(w => w.Contains("Chương 3"));

        // Audit log ghi lại hành động
        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "SET_CHAPTER_CAST_REVISION");
        audit.Should().NotBeNull();
        audit!.UserId.Should().Be(_userId);
    }

    [Fact]
    public async Task SetRevision_Null_ResetsToInherited()
    {
        // Ch1 tự chọn v2 trước đó
        var b1 = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == _ch1.Id && b.BibleEntryId == _entry.Id);
        b1.UpdateBinding(_v2.Id, b1.Source, chosenByWriter: true, boundById: _userId);
        await _db.SaveChangesAsync();

        var handler = new SetChapterCastRevisionCommandHandler(_db, _resolver);
        var cmd = new SetChapterCastRevisionCommand(_ch1.Id, _entry.Id, RevisionId: null, UserId: _userId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.ChosenByWriter.Should().BeFalse();
        // Ch1 không có chương trước nên lấy bản CanonLocked mới nhất của variant mặc định (v1)
        result.Value.BoundRevisionId.Should().Be(_v1.Id);
    }

    [Fact]
    public async Task SetRevision_WhenChapterScriptFrozen_ReturnsConflict()
    {
        var handler = new SetChapterCastRevisionCommandHandler(_db, _resolver);
        var cmd = new SetChapterCastRevisionCommand(_ch3Frozen.Id, _entry.Id, _v2.Id, _userId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("đóng băng");
    }

    [Fact]
    public async Task SetRevision_WhenRevisionDoesNotBelongToEntry_ReturnsConflict()
    {
        var otherEntry = _entry.SeriesBible.AddEntry(BibleEntryCategory.Character, "CHR-02", "Sasuke", "Ninja");
        var otherRev = otherEntry.AddRevision("v1", "{}", "hash-sasuke", authorId: _userId, isInitialVersion: true);
        _db.BibleEntries.Add(otherEntry);
        _db.BibleEntryRevisions.Add(otherRev);
        await _db.SaveChangesAsync();

        var handler = new SetChapterCastRevisionCommandHandler(_db, _resolver);
        var cmd = new SetChapterCastRevisionCommand(_ch1.Id, _entry.Id, otherRev.Id, _userId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("không thuộc");
    }

    [Fact]
    public async Task SetRevision_WhenRevisionIsSuperseded_ReturnsConflict()
    {
        var handler = new SetChapterCastRevisionCommandHandler(_db, _resolver);
        var cmd = new SetChapterCastRevisionCommand(_ch1.Id, _entry.Id, _vSuperseded.Id, _userId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("Superseded");
    }

    [Fact]
    public async Task SetRevision_WhenChapterNotFound_ReturnsNotFound()
    {
        var handler = new SetChapterCastRevisionCommandHandler(_db, _resolver);
        var cmd = new SetChapterCastRevisionCommand(Guid.NewGuid(), _entry.Id, _v1.Id, _userId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }
}
