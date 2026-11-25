using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Users.Library;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Users;

/// <summary>
/// Thư viện cá nhân của độc giả đồng bộ giữa các thiết bị: Đọc sau / Theo dõi / Yêu thích / Lịch sử đọc.
/// Chỉ nhận bộ truyện đã phát hành (BR-23); mỗi người chỉ thấy dữ liệu của mình.
/// </summary>
public class ReaderLibraryTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly DomainSeries _published;
    private readonly DomainSeries _draft;
    private readonly Chapter _chapter1;
    private readonly Chapter _chapter2;
    private readonly Chapter _draftChapter;

    public ReaderLibraryTests()
    {
        var studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        _db.StudioWorkspaces.Add(studio);
        _published = DomainSeries.Create(studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _draft = DomainSeries.Create(studio.Id, "Draft", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _db.Series.AddRange(_published, _draft);

        _chapter1 = Publish(Chapter.Create(_published.Id, 1, "Một"));
        _chapter2 = Publish(Chapter.Create(_published.Id, 2, "Hai"));
        _draftChapter = Chapter.Create(_draft.Id, 1, "Nháp");
        _db.Chapters.AddRange(_chapter1, _chapter2, _draftChapter);
        _db.SaveChanges();
    }

    private static Chapter Publish(Chapter chapter)
    {
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(chapter, ChapterStatus.HandedOffAndPublished);
        typeof(Chapter).GetProperty(nameof(Chapter.PublishedAt))!.SetValue(chapter, DateTime.UtcNow.AddDays(-1));
        return chapter;
    }

    private Guid AddUser()
    {
        var user = User.Create($"{Guid.NewGuid():N}@test.local", "Reader");
        _db.Users.Add(user);
        _db.SaveChanges();
        return user.Id;
    }

    private Task<Result<ReaderLibraryDto>> Load(Guid userId) =>
        new GetMyLibraryQueryHandler(_db).Handle(new GetMyLibraryQuery(userId), default);

    private ReadingProgressDto Progress(Chapter chapter, int page, DateTime readAt) =>
        new(_published.Id, chapter.Id, chapter.ChapterNumber, page, 20, "vi", readAt);

    [Fact]
    public async Task AddAndRemove_AreIdempotent_AndScopedToCaller()
    {
        var me = AddUser();
        var other = AddUser();
        var set = new SetLibraryItemCommandHandler(_db);

        (await set.Handle(new SetLibraryItemCommand(me, ReaderLibraryList.ReadLater, _published.Id, true), default)).IsSuccess.Should().BeTrue();
        (await set.Handle(new SetLibraryItemCommand(me, ReaderLibraryList.ReadLater, _published.Id, true), default)).IsSuccess.Should().BeTrue();
        await set.Handle(new SetLibraryItemCommand(me, ReaderLibraryList.Favorite, _published.Id, true), default);

        var mine = (await Load(me)).Value!;
        mine.ReadLater.Should().Equal(_published.Id);
        mine.Favorites.Should().Equal(_published.Id);
        (await Load(other)).Value!.ReadLater.Should().BeEmpty();

        await set.Handle(new SetLibraryItemCommand(me, ReaderLibraryList.ReadLater, _published.Id, false), default);
        (await set.Handle(new SetLibraryItemCommand(me, ReaderLibraryList.ReadLater, _published.Id, false), default)).IsSuccess.Should().BeTrue();
        (await Load(me)).Value!.ReadLater.Should().BeEmpty();
        (await Load(me)).Value!.Favorites.Should().Equal(_published.Id);
    }

    [Fact]
    public async Task UnpublishedSeries_CannotBeAdded()
    {
        var me = AddUser();
        var result = await new SetLibraryItemCommandHandler(_db)
            .Handle(new SetLibraryItemCommand(me, ReaderLibraryList.Following, _draft.Id, true), default);
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Following_LastSeenAt_OnlyMovesForward()
    {
        var me = AddUser();
        var set = new SetLibraryItemCommandHandler(_db);
        var t1 = DateTime.UtcNow.AddHours(-3);
        await set.Handle(new SetLibraryItemCommand(me, ReaderLibraryList.Following, _published.Id, true, t1), default);
        await set.Handle(new SetLibraryItemCommand(me, ReaderLibraryList.Following, _published.Id, true, t1.AddHours(-5)), default);

        (await Load(me)).Value!.Following.Single().LastSeenAt.Should().BeCloseTo(t1, TimeSpan.FromSeconds(1));

        var t2 = t1.AddHours(1);
        await set.Handle(new SetLibraryItemCommand(me, ReaderLibraryList.Following, _published.Id, true, t2), default);
        (await Load(me)).Value!.Following.Single().LastSeenAt.Should().BeCloseTo(t2, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ReadingProgress_LatestReadWins_AndValidatesChapter()
    {
        var me = AddUser();
        var save = new SaveReadingProgressCommandHandler(_db);
        var now = DateTime.UtcNow;

        (await save.Handle(new SaveReadingProgressCommand(me, Progress(_chapter2, 5, now.AddMinutes(-1))), default)).IsSuccess.Should().BeTrue();
        // Thiết bị cũ gửi bản đọc trước đó → không ghi đè.
        await save.Handle(new SaveReadingProgressCommand(me, Progress(_chapter1, 9, now.AddMinutes(-10))), default);

        var history = (await Load(me)).Value!.History.Single();
        history.ChapterId.Should().Be(_chapter2.Id);
        history.LastPageNumber.Should().Be(5);

        var wrongSeries = new ReadingProgressDto(_draft.Id, _chapter1.Id, 1, 1, 10, "vi", now);
        (await save.Handle(new SaveReadingProgressCommand(me, wrongSeries), default)).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
        var badLanguage = Progress(_chapter1, 1, now) with { Language = "xx" };
        (await save.Handle(new SaveReadingProgressCommand(me, badLanguage), default)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task ClearHistory_RemovesOnlyCallerRows()
    {
        var me = AddUser();
        var other = AddUser();
        var save = new SaveReadingProgressCommandHandler(_db);
        await save.Handle(new SaveReadingProgressCommand(me, Progress(_chapter1, 1, DateTime.UtcNow)), default);
        await save.Handle(new SaveReadingProgressCommand(other, Progress(_chapter1, 1, DateTime.UtcNow)), default);

        (await new ClearReadingHistoryCommandHandler(_db).Handle(new ClearReadingHistoryCommand(me), default)).Value.Should().Be(1);

        (await Load(me)).Value!.History.Should().BeEmpty();
        (await Load(other)).Value!.History.Should().HaveCount(1);
    }

    [Fact]
    public async Task Merge_UnionsDeviceAndServer_AndDropsUnknownSeries()
    {
        var me = AddUser();
        var now = DateTime.UtcNow;
        await new SetLibraryItemCommandHandler(_db)
            .Handle(new SetLibraryItemCommand(me, ReaderLibraryList.Favorite, _published.Id, true), default);
        await new SaveReadingProgressCommandHandler(_db)
            .Handle(new SaveReadingProgressCommand(me, Progress(_chapter1, 3, now.AddMinutes(-30))), default);

        var device = new MergeReaderLibraryRequest(
            ReadLater: [_published.Id, _draft.Id, Guid.NewGuid()],
            Following: [new FollowedSeriesDto(_published.Id, now.AddHours(-1))],
            Favorites: null,
            History: [Progress(_chapter2, 7, now.AddMinutes(-5)), new ReadingProgressDto(_draft.Id, _draftChapter.Id, 1, 1, 1, "vi", now)]);

        var merged = (await new MergeMyLibraryCommandHandler(_db).Handle(new MergeMyLibraryCommand(me, device), default)).Value!;

        merged.ReadLater.Should().Equal(_published.Id);
        merged.Favorites.Should().Equal(_published.Id);
        merged.Following.Single().SeriesId.Should().Be(_published.Id);
        merged.History.Should().ContainSingle();
        merged.History[0].ChapterId.Should().Be(_chapter2.Id);
        merged.History[0].LastPageNumber.Should().Be(7);

        // Gộp lại lần nữa không nhân đôi dữ liệu.
        var again = (await new MergeMyLibraryCommandHandler(_db).Handle(new MergeMyLibraryCommand(me, device), default)).Value!;
        again.ReadLater.Should().HaveCount(1);
        again.History.Should().HaveCount(1);
    }

    [Fact]
    public async Task Merge_RejectsOversizedPayload()
    {
        var me = AddUser();
        var tooMany = Enumerable.Range(0, MergeMyLibraryCommandHandler.MaxItemsPerList + 1).Select(_ => Guid.NewGuid()).ToList();
        var result = await new MergeMyLibraryCommandHandler(_db)
            .Handle(new MergeMyLibraryCommand(me, new MergeReaderLibraryRequest(tooMany, null, null, null)), default);
        result.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }
}
