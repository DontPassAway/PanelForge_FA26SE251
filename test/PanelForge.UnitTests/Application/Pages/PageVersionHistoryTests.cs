using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Pages.Commands.RestorePageVersion;
using PanelForge.Application.Features.Pages.Queries.ComparePageVersions;
using PanelForge.Application.Features.Pages.Queries.GetPageHistory;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Enums;
using PanelForge.Domain.Events;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;

namespace PanelForge.UnitTests.Application.Pages;

/// <summary>CF4 4.3 (UC-18, US-12, BR-01, BR-08, BR-11, BR-12): lịch sử, so sánh và khôi phục canvas Page theo version stream.</summary>
public class PageVersionHistoryTests
{
    /// <summary>Event store trong bộ nhớ: dựng lại Page theo version như Marten (version = số event đã áp dụng).</summary>
    private sealed class EventPageRepository : IPageRepository
    {
        public readonly Dictionary<Guid, List<IDomainEvent>> Streams = new();

        public void Seed(Page page)
        {
            Streams[page.Id] = page.UncommittedEvents.ToList();
            page.ClearUncommittedEvents();
        }

        private Page? Replay(Guid id, long? upTo)
        {
            if (!Streams.TryGetValue(id, out var events)) return null;
            var page = (Page)Activator.CreateInstance(typeof(Page), nonPublic: true)!;
            var count = (int)Math.Min(upTo ?? events.Count, events.Count);
            foreach (var e in events.Take(count)) page.Apply(e);
            page.SetVersion(count);
            return page;
        }

        public Task<Page?> GetAsync(Guid pageId, CancellationToken ct = default) =>
            Task.FromResult(Replay(pageId, null) is { IsDeleted: false } p ? p : null);

        public Task<Page?> GetAtVersionAsync(Guid pageId, long version, CancellationToken ct = default) =>
            Task.FromResult(Replay(pageId, version));

        public Task<long> GetStreamVersionAsync(Guid pageId, CancellationToken ct = default) =>
            Task.FromResult(Streams.TryGetValue(pageId, out var e) ? (long)e.Count : -1);

        public Task<IReadOnlyList<IDomainEvent>> GetEventsAsync(Guid pageId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IDomainEvent>>(Streams.TryGetValue(pageId, out var e) ? e.ToList() : []);

        public Task SaveAsync(Page page, CancellationToken ct = default)
        {
            var stream = Streams[page.Id];
            if (page.Version != stream.Count) throw new ConcurrentPageUpdateException(page.Id);
            stream.AddRange(page.UncommittedEvents);
            page.SetVersion(stream.Count);
            page.ClearUncommittedEvents();
            return Task.CompletedTask;
        }
    }

    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly EventPageRepository _repo = new();
    private readonly User _artist = User.Create("artist@test.com", "Họa sĩ An");
    private readonly Chapter _chapter;
    private readonly Page _page;
    private readonly Guid _balloon;
    private readonly Guid _character;

    public PageVersionHistoryTests()
    {
        _db.Users.Add(_artist);
        _chapter = Chapter.Create(Guid.NewGuid(), 1).InProduction();
        _db.Chapters.Add(_chapter);
        _db.SaveChanges();

        // v1 tạo trang, v2 thêm bóng, v3 thêm nhân vật, v4 kéo bóng, v5 xoá nhân vật
        _page = Page.CreateNew(_chapter.Id, 1, LayoutFormat.StandardPage, 1000, 1500, 300);
        _page.AddElement("SPEECH_BALLOON", 10, 10, 100, 50, 2, _artist.Id, "Chào");
        _page.AddElement("CHARACTER", 200, 200, 300, 400, 1, _artist.Id);
        _balloon = _page.Elements.Values.Single(e => e.ElementType == "SPEECH_BALLOON").ElementId;
        _character = _page.Elements.Values.Single(e => e.ElementType == "CHARACTER").ElementId;
        _page.MoveElement(_balloon, 50, 60, 100, 50, _artist.Id);
        _page.RemoveElement(_character, "USER_DELETE", _artist.Id);
        _repo.Seed(_page);
    }

    private Task<Result<RestorePageVersionResult>> Restore(long version, long expected) =>
        new RestorePageVersionCommandHandler(_repo, _db).Handle(new RestorePageVersionCommand(_page.Id, version, expected, _artist.Id), default);

    [Fact]
    public async Task History_ListsStreamEventsNewestFirst_WithActorAndPaging()
    {
        var r = await new GetPageHistoryQueryHandler(_repo, _db).Handle(new GetPageHistoryQuery(_page.Id, 1, 2), default);

        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        r.Value!.CurrentVersion.Should().Be(5);
        r.Value.TotalCount.Should().Be(5);
        r.Value.Items.Select(i => i.Version).Should().Equal(5, 4);
        r.Value.Items[0].EventType.Should().Be("ElementRemoved");
        r.Value.Items[0].ElementId.Should().Be(_character);
        r.Value.Items[0].ActorName.Should().Be("Họa sĩ An");
        r.Value.Items[0].Summary.Should().Contain("CHARACTER");

        var last = await new GetPageHistoryQueryHandler(_repo, _db).Handle(new GetPageHistoryQuery(_page.Id, 3, 2), default);
        last.Value!.Items.Should().ContainSingle(i => i.Version == 1 && i.EventType == "PageCreated" && i.ActorUserId == null);

        (await new GetPageHistoryQueryHandler(_repo, _db).Handle(new GetPageHistoryQuery(Guid.NewGuid()), default))
            .ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Compare_MatchesByElementId_AddedRemovedModified()
    {
        var handler = new ComparePageVersionsQueryHandler(_repo);

        var r = await handler.Handle(new ComparePageVersionsQuery(_page.Id, 3, 5), default);

        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        r.Value!.Removed.Should().Be(1);
        r.Value.Modified.Should().Be(1);
        r.Value.Added.Should().Be(0);
        r.Value.Elements.Single(d => d.ElementId == _character).Change.Should().Be("Removed");
        var moved = r.Value.Elements.Single(d => d.ElementId == _balloon);
        moved.Change.Should().Be("Modified");
        moved.ChangedFields.Should().BeEquivalentTo("x", "y");
        (moved.Left!.X, moved.Right!.X).Should().Be((10d, 50d));

        (await handler.Handle(new ComparePageVersionsQuery(_page.Id, 1, 2), default))
            .Value!.Elements.Should().ContainSingle(d => d.Change == "Added" && d.ElementId == _balloon);
        (await handler.Handle(new ComparePageVersionsQuery(_page.Id, 2, 2), default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await handler.Handle(new ComparePageVersionsQuery(_page.Id, 1, 99), default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task Restore_AppendsEventsToOldState_KeepsHistory_AndBumpsVersion()
    {
        var oldEvents = _repo.Streams[_page.Id].ToList();

        var r = await Restore(version: 3, expected: 5);

        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
        r.Value!.NewVersion.Should().Be(7);
        r.Value.Changes.Should().Be(2); // bóng về chỗ cũ + nhân vật đã xoá được dựng lại
        _repo.Streams[_page.Id].Take(5).Should().Equal(oldEvents); // không sửa/xoá event cũ (BR-01)
        _repo.Streams[_page.Id].Skip(5).Should().AllBeOfType<ElementRestoredEvent>();

        var now = (await _repo.GetAsync(_page.Id))!;
        var at3 = (await _repo.GetAtVersionAsync(_page.Id, 3))!;
        foreach (var e in at3.Elements.Values)
            Page.SameState(now.Elements[e.ElementId], e).Should().BeTrue();
        now.Elements[_character].IsRemoved.Should().BeFalse();
        now.Elements[_balloon].Version.Should().Be(3); // version element tiếp tục tăng, không quay về

        // Khôi phục về v2 (trước khi có nhân vật) → nhân vật bị xoá mềm bằng event, không mất khỏi lịch sử
        var back = await Restore(version: 2, expected: 7);
        back.IsSuccess.Should().BeTrue(back.ErrorMessage);
        (await _repo.GetAsync(_page.Id))!.Elements[_character].IsRemoved.Should().BeTrue();
        _repo.Streams[_page.Id].OfType<ElementRemovedEvent>().Last().Reason.Should().Be("RESTORE_TO_VERSION_2");
    }

    [Fact]
    public async Task Restore_RejectsStaleVersion_ReadOnlyChapter_AndInvalidTarget()
    {
        (await Restore(version: 3, expected: 4)).ErrorCode.Should().Be(ResultErrorCodes.Conflict);
        (await Restore(version: 5, expected: 5)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Restore(version: 0, expected: 5)).ErrorCode.Should().Be(ResultErrorCodes.Validation);

        _chapter.Approve();
        await _db.SaveChangesAsync();
        (await Restore(version: 3, expected: 5)).ErrorCode.Should().Be(ResultErrorCodes.ChapterReadOnly);
        _repo.Streams[_page.Id].Should().HaveCount(5);
    }
}
