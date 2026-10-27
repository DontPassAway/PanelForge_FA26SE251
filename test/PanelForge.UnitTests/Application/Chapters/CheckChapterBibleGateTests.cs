using FluentAssertions;
using PanelForge.Application.Features.Chapters.Queries.CheckChapterBibleGate;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Chapters;

public class CheckChapterBibleGateTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly Scene _scene;
    private readonly SeriesBible _bible;

    public CheckChapterBibleGateTests()
    {
        var studio = StudioWorkspace.Create("Studio", _userId);
        _db.StudioWorkspaces.Add(studio);
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(studio.Id, _userId, WorkspaceRole.Producer));

        _series = DomainSeries.Create(studio.Id, "Series Test", ReadingDirection.RightToLeft);
        _db.Series.Add(_series);

        _bible = SeriesBible.Create(_series.Id, studio.Id, "Series Bible");
        _db.SeriesBibles.Add(_bible);

        _chapter = Chapter.Create(_series.Id, 1, "Chương 1");
        _db.Chapters.Add(_chapter);

        _scene = Scene.Create(_chapter.Id, 1, "Cảnh 1");
        _db.Scenes.Add(_scene);

        _db.SaveChanges();
    }

    [Fact]
    public async Task Gate_WhenNoBibleEntriesReferenced_PassesImmediately()
    {
        var line = ScriptLine.Create(_scene.Id, 1, "Xin chào!");
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        var query = new CheckChapterBibleGateQuery(_chapter.Id);
        var handler = new CheckChapterBibleGateQueryHandler(_db);

        var result = await handler.Handle(query, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsPassed.Should().BeTrue();
        result.Value.TotalReferenced.Should().Be(0);
    }

    [Fact]
    public async Task Gate_WhenCharacterNotLocked_FailsWithCharacterNotLocked()
    {
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "HERO", "Hero", "Nhân vật chính");
        var heroRev = hero.AddRevision("v1", "{}", "hash-hero", isInitialVersion: true, effectiveFromChapterNumber: 1);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.Add(heroRev);

        var line = ScriptLine.Create(_scene.Id, 1, "Tôi đi đây", null, hero.Id);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        var query = new CheckChapterBibleGateQuery(_chapter.Id);
        var handler = new CheckChapterBibleGateQueryHandler(_db);

        var result = await handler.Handle(query, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsPassed.Should().BeFalse();
        result.Value.PendingCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(i => i.WarningCode == "CHARACTER_NOT_LOCKED");
    }

    [Fact]
    public async Task Gate_WhenCharacterIsLocked_Passes()
    {
        var hero = _bible.AddEntry(BibleEntryCategory.Character, "HERO2", "Hero 2", "Nhân vật chính");
        var heroRev = hero.AddRevision("v1", "{}", "hash-hero2", isInitialVersion: true, effectiveFromChapterNumber: 1);
        heroRev.Lock(_userId);
        _db.BibleEntries.Add(hero);
        _db.BibleEntryRevisions.Add(heroRev);

        var line = ScriptLine.Create(_scene.Id, 1, "Tôi là anh hùng", null, hero.Id);
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        var query = new CheckChapterBibleGateQuery(_chapter.Id);
        var handler = new CheckChapterBibleGateQueryHandler(_db);

        var result = await handler.Handle(query, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsPassed.Should().BeTrue();
        result.Value.LockedCount.Should().Be(1);
    }

    [Fact]
    public async Task Gate_WhenLocationLinkedWithoutLock_PassesBecauseLocationCoversChapter()
    {
        var location = _bible.AddEntry(BibleEntryCategory.Location, "RAIN_STATION", "Ga Mưa", "Bối cảnh ga xe lửa");
        var locRev = location.AddRevision("v1", "{}", "hash-loc", isInitialVersion: true, effectiveFromChapterNumber: 1);
        _db.BibleEntries.Add(location);
        _db.BibleEntryRevisions.Add(locRev);

        var line = ScriptLine.Create(_scene.Id, 1, "Trời mưa tại ga");
        _db.ScriptLines.Add(line);
        await _db.SaveChangesAsync();

        _db.ScriptLineBibleLinks.Add(ScriptLineBibleLink.Create(line.Id, location.Id));
        await _db.SaveChangesAsync();

        var query = new CheckChapterBibleGateQuery(_chapter.Id);
        var handler = new CheckChapterBibleGateQueryHandler(_db);

        var result = await handler.Handle(query, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsPassed.Should().BeTrue();
        result.Value.TotalReferenced.Should().Be(1);
        result.Value.Items.Should().ContainSingle(i => i.EntryId == location.Id && i.WarningCode == "OK");
    }
}

