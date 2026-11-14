using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.Infrastructure.Services;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Bible;

public class ChapterCastInheritanceResolverTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly ChapterCastInheritanceResolver _resolver;
    private readonly Guid _seriesId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly BibleEntry _hero;
    private readonly BibleEntryRevision _v1Locked;
    private readonly BibleEntryRevision _v2LockedReplacement;
    private readonly BibleEntryRevision _v3DraftReplacement;

    public ChapterCastInheritanceResolverTests()
    {
        _resolver = new ChapterCastInheritanceResolver(_db);

        var bible = SeriesBible.Create(_seriesId, Guid.NewGuid(), "Master Bible");
        _hero = bible.AddEntry(BibleEntryCategory.Character, "CHR-HERO", "Naruto", "Hero");

        _v1Locked = _hero.AddRevision("v1", "{}", "hash-v1", authorId: _userId, stageName: "Trưởng thành", isInitialVersion: true);
        _v1Locked.SubmitConcept(_userId, "https://r2/v1.png", null);
        _v1Locked.Lock(_userId);

        _v2LockedReplacement = _hero.AddRevision("v2", "{}", "hash-v2", authorId: _userId, stageName: "Trưởng thành", supersedesRevisionId: _v1Locked.Id);
        _v2LockedReplacement.SubmitConcept(_userId, "https://r2/v2.png", null);
        _v2LockedReplacement.Lock(_userId);

        _v3DraftReplacement = _hero.AddRevision("v3", "{}", "hash-v3", authorId: _userId, stageName: "Trưởng thành", supersedesRevisionId: _v2LockedReplacement.Id);

        _db.SeriesBibles.Add(bible);
        _db.BibleEntries.Add(_hero);
        _db.BibleEntryRevisions.AddRange(_v1Locked, _v2LockedReplacement, _v3DraftReplacement);
        _db.SaveChanges();
    }

    [Fact]
    public async Task CascadingInheritance_Ch1ToCh2ToCh3_FollowsClosestPreceding()
    {
        var ch1 = Chapter.Create(_seriesId, 1, "Ch1");
        var ch2 = Chapter.Create(_seriesId, 2, "Ch2");
        var ch3 = Chapter.Create(_seriesId, 3, "Ch3");
        _db.Chapters.AddRange(ch1, ch2, ch3);

        // Ch1 Writer chọn v1
        var b1 = ChapterBibleBinding.Create(ch1.Id, _hero.Id, _v1Locked.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);
        // Ch2 và Ch3 là kế thừa (chưa gắn hoặc null)
        var b2 = ChapterBibleBinding.Create(ch2.Id, _hero.Id, null, ChapterBibleBindingSource.Speaker, chosenByWriter: false);
        var b3 = ChapterBibleBinding.Create(ch3.Id, _hero.Id, null, ChapterBibleBindingSource.Speaker, chosenByWriter: false);

        _db.ChapterBibleBindings.AddRange(b1, b2, b3);
        await _db.SaveChangesAsync();

        // Chạy resolve từ Ch1
        await _resolver.ResolveInheritedAsync(_seriesId, 1);

        // v1Locked có v2LockedReplacement thay thế và đã locked -> chuỗi thay thế dẫn tới v2LockedReplacement
        b2.BoundRevisionId.Should().Be(_v2LockedReplacement.Id);
        b3.BoundRevisionId.Should().Be(_v2LockedReplacement.Id);
    }

    [Fact]
    public async Task Inheritance_DoesNotAdvanceToDraftReplacement()
    {
        var ch1 = Chapter.Create(_seriesId, 1, "Ch1");
        var ch2 = Chapter.Create(_seriesId, 2, "Ch2");
        _db.Chapters.AddRange(ch1, ch2);

        // Ch1 gắn v2 (đã locked)
        var b1 = ChapterBibleBinding.Create(ch1.Id, _hero.Id, _v2LockedReplacement.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);
        // Ch2 kế thừa
        var b2 = ChapterBibleBinding.Create(ch2.Id, _hero.Id, null, ChapterBibleBindingSource.Speaker, chosenByWriter: false);

        _db.ChapterBibleBindings.AddRange(b1, b2);
        await _db.SaveChangesAsync();

        await _resolver.ResolveInheritedAsync(_seriesId, 1);

        // v3DraftReplacement chưa locked -> Ch2 dừng ở v2LockedReplacement, không kế thừa v3
        b2.BoundRevisionId.Should().Be(_v2LockedReplacement.Id);
    }

    [Fact]
    public async Task Inheritance_DoesNotOverrideWriterChoiceOrFrozenChapter()
    {
        var ch1 = Chapter.Create(_seriesId, 1, "Ch1");
        var ch2 = Chapter.Create(_seriesId, 2, "Ch2");
        var ch3 = Chapter.Create(_seriesId, 3, "Ch3");
        ch3.SubmitScript(_userId);
        ch3.StartProduction(_userId, "hash-ch3");

        _db.Chapters.AddRange(ch1, ch2, ch3);

        var b1 = ChapterBibleBinding.Create(ch1.Id, _hero.Id, _v2LockedReplacement.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);
        // Ch2 Writer tự chọn v1
        var b2 = ChapterBibleBinding.Create(ch2.Id, _hero.Id, _v1Locked.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: true);
        // Ch3 Frozen với v1
        var b3 = ChapterBibleBinding.Create(ch3.Id, _hero.Id, _v1Locked.Id, ChapterBibleBindingSource.Speaker, chosenByWriter: false);

        _db.ChapterBibleBindings.AddRange(b1, b2, b3);
        await _db.SaveChangesAsync();

        await _resolver.ResolveInheritedAsync(_seriesId, 1);

        // Ch2 giữ nguyên v1 vì chosen_by_writer = true
        b2.BoundRevisionId.Should().Be(_v1Locked.Id);
        b2.ChosenByWriter.Should().BeTrue();

        // Ch3 giữ nguyên v1 vì đã Frozen
        b3.BoundRevisionId.Should().Be(_v1Locked.Id);
    }

    [Fact]
    public async Task Inheritance_WhenNoPrecedingChapter_ResolvesToDefaultVariantLocked()
    {
        var ch1 = Chapter.Create(_seriesId, 1, "Ch1");
        _db.Chapters.Add(ch1);

        var b1 = ChapterBibleBinding.Create(ch1.Id, _hero.Id, null, ChapterBibleBindingSource.Speaker, chosenByWriter: false);
        _db.ChapterBibleBindings.Add(b1);
        await _db.SaveChangesAsync();

        await _resolver.ResolveInheritedAsync(_seriesId, 1);

        // Không có chương trước: lấy CanonLocked mới nhất chưa bị Superseded của biến thể mặc định (v2LockedReplacement)
        b1.BoundRevisionId.Should().Be(_v2LockedReplacement.Id);
    }
}
