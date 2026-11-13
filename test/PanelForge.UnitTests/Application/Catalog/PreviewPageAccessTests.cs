using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.Publisher.Monetization;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Identity;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Catalog;

/// <summary>
/// Chương trả phí: người chưa mua chỉ xem trang xem trước (Publisher chọn tay, hoặc mặc định N trang đầu của Series);
/// trang khác không có ImageUrl và ảnh trả 403. Mua chương / VIP / staff mở mọi trang. Chương miễn phí mở toàn bộ.
/// </summary>
public class PreviewPageAccessTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly List<Page> _pages = [];

    public PreviewPageAccessTests()
    {
        _studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        _db.StudioWorkspaces.Add(_studio);
        _series = DomainSeries.Create(_studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _series.SetDefaultPreviewPages(2);
        _db.Series.Add(_series);

        _chapter = Chapter.Create(_series.Id, 1, "Trả phí");
        _chapter.SetPricing(true, 10000m);
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(_chapter, ChapterStatus.Published);
        typeof(Chapter).GetProperty(nameof(Chapter.PublishedAt))!.SetValue(_chapter, DateTime.UtcNow.AddDays(-1));
        _db.Chapters.Add(_chapter);

        for (var n = 1; n <= 5; n++)
        {
            var page = Page.CreateNew(_chapter.Id, n, default, 1200, 1800, 300);
            _pages.Add(page);
            _db.Pages.Add(page);
        }
        _db.SaveChanges();
    }

    private Guid AddUser(SystemRole role = SystemRole.User, bool vip = false)
    {
        var user = User.Create($"{Guid.NewGuid():N}@test.local", "Reader", role: role);
        _db.Users.Add(user);
        _db.ReaderWallets.Add(ReaderWallet.Create(user.Id, 0m, isVip: vip,
            vipExpiresAt: vip ? DateTime.UtcNow.AddDays(5) : null));
        _db.SaveChanges();
        return user.Id;
    }

    private async Task<PublicChapterReadDto> Read(Guid? userId)
    {
        var result = await new GetPublicChapterQueryHandler(_db, new FakeCurrentUser(userId))
            .Handle(new GetPublicChapterQuery(_chapter.Id), default);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    private Task<Result<PanelForge.Application.Features.Assets.Models.AssetDownloadDto>> Image(Guid? userId, Page page) =>
        new GetPublicPageImageQueryHandler(_db, null!, null!, null!, new FakeCurrentUser(userId))
            .Handle(new GetPublicPageImageQuery(_chapter.Id, page.Id), default);

    [Fact]
    public async Task Guest_SeesDefaultFirstNPreviewPages_OthersLockedWithoutImage()
    {
        var dto = await Read(null);

        dto.IsUnlocked.Should().BeFalse();
        dto.Pages.Where(p => p.IsPreview).Select(p => p.PageNumber).Should().Equal(1, 2);
        dto.Pages.Where(p => p.IsLocked).Select(p => p.PageNumber).Should().Equal(3, 4, 5);
        dto.Pages.Where(p => p.IsLocked).Should().OnlyContain(p => !p.IsUnlocked && p.ImageUrl == null);
        dto.Pages.Where(p => p.IsPreview).Should().OnlyContain(p => p.IsUnlocked);
    }

    [Fact]
    public async Task PageImage_ForbiddenForLockedPage_AllowedPastGateForPreview()
    {
        var reader = AddUser();

        (await Image(reader, _pages[4])).ErrorCode.Should().Be(ResultErrorCodes.Forbidden);
        // Trang xem trước qua được kiểm tra quyền (không có artwork trong test → 404, không phải 403).
        (await Image(reader, _pages[0])).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task BuyingChapter_UnlocksEveryPage()
    {
        var reader = AddUser();
        _db.ChapterEntitlements.Add(ChapterEntitlement.Create(reader, _chapter.Id, _series.Id, _studio.Id, 10000m));
        await _db.SaveChangesAsync();

        var dto = await Read(reader);

        dto.IsUnlocked.Should().BeTrue();
        dto.Pages.Should().OnlyContain(p => p.IsUnlocked);
        (await Image(reader, _pages[4])).ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task Vip_AndStudioMember_SeeAllPages()
    {
        var artist = AddUser();
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_studio.Id, artist, WorkspaceRole.Artist));
        await _db.SaveChangesAsync();

        (await Read(AddUser(vip: true))).Pages.Should().OnlyContain(p => p.IsUnlocked);
        (await Read(artist)).Pages.Should().OnlyContain(p => p.IsUnlocked);
    }

    [Fact]
    public async Task FreeChapter_HasNoLockedOrPreviewPages()
    {
        _chapter.SetPricing(false, 0m);
        await _db.SaveChangesAsync();

        var dto = await Read(null);

        dto.Pages.Should().OnlyContain(p => !p.IsLocked && !p.IsPreview && p.IsUnlocked);
    }

    [Fact]
    public void DefaultPreview_NeverCoversEveryPage()
    {
        var pages = Enumerable.Range(1, 3).Select(n => (Guid.NewGuid(), n, false)).ToList();

        ChapterReadAccess.Resolve(false, 10, pages).Count(p => p.IsPreview).Should().Be(2);
        ChapterReadAccess.Resolve(false, 0, pages).Should().OnlyContain(p => !p.IsPreview);
    }

    [Fact]
    public async Task Publisher_PicksPreviewPages_ThenResetsToSeriesDefault()
    {
        var set = await new SetChapterPreviewPagesCommandHandler(_db).Handle(
            new SetChapterPreviewPagesCommand(_chapter.Id, [_pages[0].Id, _pages[3].Id]), default);

        set.IsSuccess.Should().BeTrue();
        set.Value!.PreviewPagesCustomized.Should().BeTrue();
        set.Value.Pages.Where(p => p.IsPreview).Select(p => p.PageNumber).Should().Equal(1, 4);
        (await Read(null)).Pages.Where(p => p.IsUnlocked).Select(p => p.PageNumber).Should().Equal(1, 4);

        var reset = await new ResetChapterPreviewPagesCommandHandler(_db).Handle(
            new ResetChapterPreviewPagesCommand(_chapter.Id), default);
        reset.Value!.PreviewPagesCustomized.Should().BeFalse();
        reset.Value.Pages.Where(p => p.IsPreview).Select(p => p.PageNumber).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Publisher_CannotPickPageFromAnotherChapter()
    {
        var result = await new SetChapterPreviewPagesCommandHandler(_db).Handle(
            new SetChapterPreviewPagesCommand(_chapter.Id, [Guid.NewGuid()]), default);

        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task SeriesSettings_ValidatesPreviewCount()
    {
        var handler = new UpdateSeriesMonetizationSettingsCommandHandler(_db);

        (await handler.Handle(new UpdateSeriesMonetizationSettingsCommand(_series.Id, -1), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Validation);
        var ok = await handler.Handle(new UpdateSeriesMonetizationSettingsCommand(_series.Id, 3), default);
        ok.Value!.DefaultPreviewPageCount.Should().Be(3);
        (await Read(null)).Pages.Count(p => p.IsPreview).Should().Be(3);
    }
}
