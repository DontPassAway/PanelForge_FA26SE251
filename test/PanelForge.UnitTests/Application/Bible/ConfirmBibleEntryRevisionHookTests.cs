using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Features.Bible.Commands.ConfirmBibleEntryRevision;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class ConfirmBibleEntryRevisionHookTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly ChapterCastInheritanceResolver _resolver;
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _editorId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _writerId = Guid.NewGuid();
    private readonly BibleEntry _hero;
    private readonly BibleEntryRevision _v1Old;
    private readonly BibleEntryRevision _v2Replacement;
    private readonly Chapter _ch1;
    private readonly Chapter _ch2Inheriting;
    private readonly Chapter _ch3WriterChosen;
    private readonly Chapter _ch4Frozen;

    public ConfirmBibleEntryRevisionHookTests()
    {
        _resolver = new ChapterCastInheritanceResolver(_db);

        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");
        _hero = bible.AddEntry(BibleEntryCategory.Character, "CHR-01", "Naruto", "Hero");

        _v1Old = _hero.AddRevision("v1", "{}", "hash-v1", authorId: _writerId, stageName: "Trưởng thành", isInitialVersion: true);
        _v1Old.SubmitConcept(_artistId, "https://r2/v1.png", null);
        _v1Old.Lock(_editorId);

        _v2Replacement = _hero.AddRevision("v2", "{}", "hash-v2", authorId: _writerId, stageName: "Trưởng thành", supersedesRevisionId: _v1Old.Id);
        _v2Replacement.SubmitConcept(_artistId, "https://r2/v2.png", "Concept v2");

        _db.SeriesBibles.Add(bible);
        _db.BibleEntries.Add(_hero);
        _db.BibleEntryRevisions.AddRange(_v1Old, _v2Replacement);

        _ch1 = Chapter.Create(_seriesId, 1, "Ch1");
        _ch2Inheriting = Chapter.Create(_seriesId, 2, "Ch2");
        _ch3WriterChosen = Chapter.Create(_seriesId, 3, "Ch3");
        _ch4Frozen = Chapter.Create(_seriesId, 4, "Ch4");
        _ch4Frozen.SubmitScript(_writerId);
        _ch4Frozen.StartProduction(_writerId, "hash-ch4");

        _db.Chapters.AddRange(_ch1, _ch2Inheriting, _ch3WriterChosen, _ch4Frozen);

        // Ch1 gắn v1 (writer chọn)
        var b1 = ChapterBibleBinding.Create(_ch1.Id, _hero.Id, _v1Old.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);
        // Ch2 kế thừa Ch1 (bản cũ v1)
        var b2 = ChapterBibleBinding.Create(_ch2Inheriting.Id, _hero.Id, _v1Old.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false);
        // Ch3 Writer tự chọn gắn v1
        var b3 = ChapterBibleBinding.Create(_ch3WriterChosen.Id, _hero.Id, _v1Old.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);
        // Ch4 Frozen với v1
        var b4 = ChapterBibleBinding.Create(_ch4Frozen.Id, _hero.Id, _v1Old.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false);

        _db.ChapterBibleBindings.AddRange(b1, b2, b3, b4);
        _db.SaveChanges();
    }

    [Fact]
    public async Task ConfirmCanon_ReplacementRevision_MarksOldSuperseded_AndReResolvesInheritingChapters()
    {
        var handler = new ConfirmBibleEntryRevisionCommandHandler(_db, _resolver);
        var cmd = new ConfirmBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: _hero.Id,
            RevisionId: _v2Replacement.Id,
            UserId: _editorId,
            ConceptEventId: _v2Replacement.CurrentConceptEventId);

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        _v2Replacement.IsLocked.Should().BeTrue();

        // 1. Bản cũ _v1Old bị đánh dấu Superseded
        _v1Old.Events.Should().Contain(e => e.Type == BibleRevisionEventType.Superseded);

        // 2. Ch2 đang kế thừa (chosenByWriter = false) tự động chuyển sang bản mới v2
        var b2 = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == _ch2Inheriting.Id && b.BibleEntryId == _hero.Id);
        b2.BoundRevisionId.Should().Be(_v2Replacement.Id);

        // 3. Ch1 do Writer tự chọn v1 -> giữ nguyên v1
        var b1 = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == _ch1.Id && b.BibleEntryId == _hero.Id);
        b1.BoundRevisionId.Should().Be(_v1Old.Id);

        // 4. Ch3 do Writer tự chọn v1 -> giữ nguyên v1
        var b3 = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == _ch3WriterChosen.Id && b.BibleEntryId == _hero.Id);
        b3.BoundRevisionId.Should().Be(_v1Old.Id);

        // 5. Ch4 đã Frozen -> giữ nguyên v1
        var b4 = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == _ch4Frozen.Id && b.BibleEntryId == _hero.Id);
        b4.BoundRevisionId.Should().Be(_v1Old.Id);
    }

    [Fact]
    public async Task ConfirmCanon_NewContentRevision_NotReplacement_RebindsUnfrozenChapterStillOnDraft()
    {
        // Artist nộp lại concept kèm sửa nội dung → revision mới (không phải supersede); chương chưa chốt vẫn gắn bản nháp cũ
        var bible = await _db.SeriesBibles.FirstAsync(b => b.SeriesId == _seriesId);
        var ryu = bible.AddEntry(BibleEntryCategory.Character, "CHR-02", "Ryu", "Trưởng làng");
        var draftV1 = ryu.AddRevision("v1", "{}", "hash-r1", authorId: _writerId, isInitialVersion: true);
        var contentV2 = ryu.AddRevision("v2", "{\"d\":1}", "hash-r2", authorId: _artistId);
        contentV2.SubmitConcept(_artistId, "https://r2/ryu.png", "Concept");
        _db.BibleEntries.Add(ryu);
        _db.BibleEntryRevisions.AddRange(draftV1, contentV2);
        var ch5 = Chapter.Create(_seriesId, 5, "Ch5");
        _db.Chapters.Add(ch5);
        _db.ChapterBibleBindings.Add(ChapterBibleBinding.Create(
            ch5.Id, ryu.Id, draftV1.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false));
        await _db.SaveChangesAsync();

        var handler = new ConfirmBibleEntryRevisionCommandHandler(_db, _resolver);
        var result = await handler.Handle(new ConfirmBibleEntryRevisionCommand(
            SeriesId: _seriesId,
            BibleEntryId: ryu.Id,
            RevisionId: contentV2.Id,
            UserId: _editorId,
            ConceptEventId: contentV2.CurrentConceptEventId), default);

        result.IsSuccess.Should().BeTrue();
        var binding = await _db.ChapterBibleBindings.FirstAsync(b => b.ChapterId == ch5.Id && b.BibleEntryId == ryu.Id);
        binding.BoundRevisionId.Should().Be(contentV2.Id);
    }
}
