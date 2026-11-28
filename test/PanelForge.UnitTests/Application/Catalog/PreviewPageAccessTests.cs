using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.Chapters.Commands.Editions;
using PanelForge.Application.Features.Payments.Commands.UnlockChapter;
using PanelForge.Application.Features.Payments.Models;
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
/// BR-33 / D4: quy định thương mại thuộc ấn bản ngôn ngữ (Free/Trả phí, giá, N trang đầu xem trước), chỉ ghi qua
/// <c>SetEditionMonetization</c>. Người chưa mua ấn bản trả phí chỉ xem N trang đầu (trang khác không có ImageUrl, ảnh 403).
/// Ấn bản chưa cấu hình không phát hành được và không mua được.
/// </summary>
public class PreviewPageAccessTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly ChapterEdition _vi;
    private readonly List<Page> _pages = [];
    private readonly Guid _publisherId = Guid.NewGuid();

    public PreviewPageAccessTests()
    {
        _studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        _db.StudioWorkspaces.Add(_studio);
        _series = DomainSeries.Create(_studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _db.Series.Add(_series);

        _chapter = Chapter.Create(_series.Id, 1, "Trả phí");
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(_chapter, ChapterStatus.HandedOffAndPublished);
        typeof(Chapter).GetProperty(nameof(Chapter.PublishedAt))!.SetValue(_chapter, DateTime.UtcNow.AddDays(-1));
        _db.Chapters.Add(_chapter);

        _vi = TestEditions.Monetize(_db, _chapter.Id, isPaid: true, priceVnd: 10000m, freePreviewPages: 2);

        for (var n = 1; n <= 5; n++)
        {
            var page = Page.CreateNew(_chapter.Id, n, default, 1200, 1800, 300);
            _pages.Add(page);
            _db.Pages.Add(page);
        }
        _db.SaveChanges();
    }

    private Guid AddUser(SystemRole role = SystemRole.User, bool vip = false, decimal balance = 0m)
    {
        var user = User.Create($"{Guid.NewGuid():N}@test.local", "Reader", role: role);
        _db.Users.Add(user);
        _db.ReaderWallets.Add(ReaderWallet.Create(user.Id, balance, isVip: vip,
            vipExpiresAt: vip ? DateTime.UtcNow.AddDays(5) : null));
        _db.SaveChanges();
        return user.Id;
    }

    private async Task<PublicChapterReadDto> Read(Guid? userId, string? language = null)
    {
        var result = await new GetPublicChapterQueryHandler(_db, new FakeCurrentUser(userId))
            .Handle(new GetPublicChapterQuery(_chapter.Id, language), default);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    private Task<Result<PanelForge.Application.Features.Assets.Models.AssetDownloadDto>> Image(Guid? userId, Page page) =>
        new GetPublicPageImageQueryHandler(_db, null!, null!, null!, new FakeCurrentUser(userId))
            .Handle(new GetPublicPageImageQuery(_chapter.Id, page.Id), default);

    private Task<Result<ChapterManageDto>> Configure(
        bool isPaid, decimal price, int preview, string language = ContentLanguages.Vietnamese, string? reason = null) =>
        new SetEditionMonetizationCommandHandler(_db).Handle(
            new SetEditionMonetizationCommand(_chapter.Id, language, isPaid, price, preview, reason, _publisherId), default);

    // ─── Đọc / ảnh trang ─────────────────────────────────────────────────────

    [Fact]
    public async Task Guest_SeesFirstNPagesOfEdition_OthersLockedWithoutImage()
    {
        var dto = await Read(null);

        dto.IsUnlocked.Should().BeFalse();
        dto.PriceVnd.Should().Be(10000m);
        dto.Pages.Where(p => p.IsPreview).Select(p => p.PageNumber).Should().Equal(1, 2);
        dto.Pages.Where(p => p.IsLocked).Select(p => p.PageNumber).Should().Equal(3, 4, 5);
        dto.Pages.Where(p => p.IsLocked).Should().OnlyContain(p => !p.IsUnlocked && p.ImageUrl == null);
        dto.Pages.Where(p => p.IsPreview).Should().OnlyContain(p => p.IsUnlocked);
    }

    [Fact]
    public async Task TypePriceAndPreview_ArePerLanguageEdition()
    {
        TestEditions.Monetize(_db, _chapter.Id, isPaid: false, language: ContentLanguages.English);
        await _db.SaveChangesAsync();

        var vi = await Read(null, ContentLanguages.Vietnamese);
        vi.PriceVnd.Should().Be(10000m);
        vi.Pages.Count(p => p.IsPreview).Should().Be(2);

        var en = await Read(null, ContentLanguages.English);
        en.IsFree.Should().BeTrue();
        en.Pages.Should().OnlyContain(p => p.IsUnlocked && !p.IsLocked);
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
    public async Task BuyingEdition_UnlocksEveryPage()
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
    public async Task StudioMember_SeesAllPages_LegacyVipDoesNot_OPS11()
    {
        var artist = AddUser();
        _db.WorkspaceMembers.Add(WorkspaceMember.Create(_studio.Id, artist, WorkspaceRole.Artist));
        await _db.SaveChangesAsync();

        (await Read(AddUser(vip: true))).Pages.Should().Contain(p => !p.IsUnlocked);
        (await Read(artist)).Pages.Should().OnlyContain(p => p.IsUnlocked);
    }

    [Fact]
    public async Task FreeEdition_HasNoLockedOrPreviewPages()
    {
        (await Configure(false, 0m, 0, reason: "Mở miễn phí")).IsSuccess.Should().BeTrue();

        var dto = await Read(null);

        dto.IsFree.Should().BeTrue();
        dto.Pages.Should().OnlyContain(p => !p.IsLocked && !p.IsPreview && p.IsUnlocked);
    }

    [Fact]
    public void Resolve_TakesFirstPages_AndNeverCoversEveryPage()
    {
        var pages = Enumerable.Range(1, 3).Select(n => (Guid.NewGuid(), n)).ToList();

        ChapterReadAccess.Resolve(1, pages).Select(p => p.IsPreview).Should().Equal(true, false, false);
        ChapterReadAccess.Resolve(10, pages).Count(p => p.IsPreview).Should().Be(2);
        ChapterReadAccess.Resolve(0, pages).Should().OnlyContain(p => !p.IsPreview);
    }

    // ─── D4: chưa cấu hình ───────────────────────────────────────────────────

    [Fact]
    public async Task UnconfiguredEdition_IsFullyLocked_AndCannotBeBought()
    {
        var en = ChapterEdition.Create(_chapter.Id, ContentLanguages.English, EditionStatus.Published);
        _db.ChapterEditions.Add(en);
        await _db.SaveChangesAsync();
        var reader = AddUser(balance: 50000m);

        (await Read(reader, ContentLanguages.English)).Pages.Should().OnlyContain(p => p.IsLocked && p.ImageUrl == null);
        var unlock = await new UnlockChapterCommandHandler(_db, new DefaultBankSettings()).Handle(
            new UnlockChapterCommand(reader, new UnlockChapterRequest(_chapter.Id, Language: ContentLanguages.English)), default);
        unlock.ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task PublishEdition_BlockedUntilPublisherConfiguresFreeOrPaid()
    {
        var en = ChapterEdition.Create(_chapter.Id, ContentLanguages.English, EditionStatus.Approved);
        _db.ChapterEditions.Add(en);
        await _db.SaveChangesAsync();
        var publish = () => new PublishEditionCommandHandler(_db).Handle(
            new PublishEditionCommand(_chapter.Id, ContentLanguages.English, _publisherId), default);

        (await publish()).ErrorCode.Should().Be(ResultErrorCodes.Conflict);

        (await Configure(true, 15000m, 1, ContentLanguages.English)).IsSuccess.Should().BeTrue();
        (await publish()).IsSuccess.Should().BeTrue();
        en.Status.Should().Be(EditionStatus.Published);
    }

    // ─── SetEditionMonetization ──────────────────────────────────────────────

    [Fact]
    public async Task Publisher_SetsPreviewCount_ReaderSeesFirstNPages_AndAuditIsWritten()
    {
        var result = await Configure(true, 10000m, 3);

        result.IsSuccess.Should().BeTrue();
        var vi = result.Value!.Languages.Single(l => l.Language == ContentLanguages.Vietnamese);
        vi.FreePreviewPages.Should().Be(3);
        vi.IsMonetizationConfigured.Should().BeTrue();
        (await Read(null)).Pages.Where(p => p.IsUnlocked).Select(p => p.PageNumber).Should().Equal(1, 2, 3);

        var audit = _db.AuditLogs.Single(a => a.Action == SetEditionMonetizationCommandHandler.AuditAction);
        audit.EntityId.Should().Be(_vi.Id.ToString());
        audit.UserId.Should().Be(_publisherId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(9)]
    public async Task PaidEdition_PreviewCount_MustLeaveAtLeastOneLockedPage(int n)
    {
        (await Configure(true, 10000m, n)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        _vi.FreePreviewPages.Should().Be(2);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 500)]
    [InlineData(true, 1500.5)]
    [InlineData(false, 5000)]
    public async Task Price_MustMatchType(bool isPaid, decimal price)
    {
        (await Configure(isPaid, price, 0, reason: "x")).ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }

    [Fact]
    public async Task PublishedEdition_ChangingTypeOrPrice_RequiresReason_PreviewCountDoesNot()
    {
        (await Configure(true, 12000m, 2)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Configure(false, 0m, 0, reason: "   ")).ErrorCode.Should().Be(ResultErrorCodes.Validation);
        (await Configure(true, 10000m, 1)).IsSuccess.Should().BeTrue("chỉ đổi số trang xem trước");
        (await Configure(true, 12000m, 1, reason: "Tăng giá")).IsSuccess.Should().BeTrue();
        _db.AuditLogs.Last(a => a.Action == SetEditionMonetizationCommandHandler.AuditAction)
            .Details.Should().Contain("Tăng giá");
    }

    [Fact]
    public async Task PriceChange_DoesNotAffectReadersWhoAlreadyBought()
    {
        var reader = AddUser();
        _db.ChapterEntitlements.Add(ChapterEntitlement.Create(reader, _chapter.Id, _series.Id, _studio.Id, 10000m));
        await _db.SaveChangesAsync();

        (await Configure(true, 20000m, 0, reason: "Tăng giá")).IsSuccess.Should().BeTrue();

        (await Read(reader)).IsUnlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Publisher_CanConfigureMissingEdition_WhichIsCreatedAsDraft()
    {
        var result = await Configure(true, 8000m, 4, ContentLanguages.English);

        result.IsSuccess.Should().BeTrue();
        var en = _db.ChapterEditions.Single(e => e.ChapterId == _chapter.Id && e.LanguageCode == ContentLanguages.English);
        en.Status.Should().Be(EditionStatus.Draft);
        en.IsMonetizationConfigured.Should().BeTrue();
        en.PriceVnd.Should().Be(8000m);
        en.FreePreviewPages.Should().Be(4);
    }

    [Fact]
    public async Task LanguageOutsideReleaseLanguages_IsRejected()
    {
        _series.SetLanguages(ContentLanguages.Vietnamese, [ContentLanguages.Vietnamese]);
        await _db.SaveChangesAsync();

        (await Configure(true, 10000m, 1, ContentLanguages.English)).ErrorCode.Should().Be(ResultErrorCodes.Validation);
    }
}
