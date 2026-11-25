using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.Payments.Commands.UnlockChapter;
using PanelForge.Application.Features.Payments.Models;
using PanelForge.Application.Features.Publisher.Monetization;
using PanelForge.Domain.Entities.Assets;
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
/// Bản phát hành theo ngôn ngữ (vi gốc, en/ja khi đủ ảnh dịch), mua theo từng ngôn ngữ (bản chưa mua chỉ
/// thấy trang xem trước), đánh giá sao và dashboard Publisher lọc theo ngôn ngữ.
/// </summary>
public class LanguageEditionsAndInsightsTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly ChapterEdition _vi;
    private readonly List<Page> _pages = [];

    public LanguageEditionsAndInsightsTests()
    {
        _studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        _db.StudioWorkspaces.Add(_studio);
        _series = DomainSeries.Create(_studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _db.Series.Add(_series);

        _chapter = Chapter.Create(_series.Id, 1, "Khởi đầu");
        typeof(Chapter).GetProperty(nameof(Chapter.Status))!.SetValue(_chapter, ChapterStatus.HandedOffAndPublished);
        typeof(Chapter).GetProperty(nameof(Chapter.PublishedAt))!.SetValue(_chapter, DateTime.UtcNow.AddDays(-1));
        _db.Chapters.Add(_chapter);

        // BR-34: ChapterEdition cho bản gốc vi (BR-33: Publisher đã cấu hình Free)
        _vi = TestEditions.Monetize(_db, _chapter.Id, isPaid: false);

        var asset = Asset.Create(_studio.Id, new string('a', 64), 1024, "image/png");
        _db.Assets.Add(asset);

        for (var n = 1; n <= 4; n++)
        {
            var page = Page.CreateNew(_chapter.Id, n, default, 1200, 1800, 300);
            _pages.Add(page);
            _db.Pages.Add(page);
            _db.AssetReferences.Add(AssetReference.Create(
                asset.Id, _studio.Id, _chapter.Id, page.Id, Guid.NewGuid(), 1, null, $"page{n}.png", Guid.NewGuid()));
        }
        _db.SaveChanges();
    }

    private Guid AddUser(decimal balance = 0m, bool vip = false)
    {
        var user = User.Create($"{Guid.NewGuid():N}@test.local", "Reader");
        _db.Users.Add(user);
        _db.ReaderWallets.Add(ReaderWallet.Create(user.Id, balance, isVip: vip,
            vipExpiresAt: vip ? DateTime.UtcNow.AddDays(5) : null));
        _db.SaveChanges();
        return user.Id;
    }

    private void PublishEdition(string language, bool isPaid = false, decimal priceVnd = 0m, int freePreviewPages = 0)
    {
        TestEditions.Monetize(_db, _chapter.Id, isPaid, priceVnd, freePreviewPages, language);
        _db.SaveChanges();
    }

    private void MakeOriginalPaid(decimal priceVnd, int freePreviewPages = 0)
    {
        _vi.SetMonetization(true, priceVnd, freePreviewPages, Guid.Empty);
        _db.SaveChanges();
    }

    private Task<Result<PublicChapterReadDto>> Read(Guid? userId, string? lang = null) =>
        new GetPublicChapterQueryHandler(_db, new FakeCurrentUser(userId))
            .Handle(new GetPublicChapterQuery(_chapter.Id, lang), default);

    // ─── Bản ngôn ngữ ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TranslationEdition_IsAvailableOnlyWhenPublished()
    {
        var draft = ChapterEdition.Create(_chapter.Id, "en", EditionStatus.Draft);
        _db.ChapterEditions.Add(draft);
        await _db.SaveChangesAsync();

        (await Read(null, "en")).ErrorCode.Should().Be(ResultErrorCodes.NotFound);

        draft.Publish();
        await _db.SaveChangesAsync();

        var dto = (await Read(null, "en")).Value!;
        dto.Language.Should().Be("en");
        dto.Languages!.Select(l => l.Language).Should().Equal("vi", "en");
    }

    [Fact]
    public async Task PaidChapter_IsBoughtPerLanguage_AtLanguagePrice()
    {
        MakeOriginalPaid(10000m);
        PublishEdition(ContentLanguages.English, isPaid: true, priceVnd: 15000m);
        var reader = AddUser(balance: 20000m);

        var unlock = await new UnlockChapterCommandHandler(_db, new DefaultBankSettings()).Handle(
            new UnlockChapterCommand(reader, new UnlockChapterRequest(_chapter.Id, Language: "en")), default);

        unlock.Value!.RemainingBalanceVnd.Should().Be(5000);
        (await Read(reader, "en")).Value!.IsUnlocked.Should().BeTrue();
        (await Read(reader, "vi")).Value!.IsUnlocked.Should().BeFalse();
        _db.PaymentTransactions.Single().Language.Should().Be("en");
    }

    [Fact]
    public async Task ChapterPurchase_InOneLanguage_LeavesOtherEditionInPreview()
    {
        MakeOriginalPaid(10000m, freePreviewPages: 1);
        PublishEdition(ContentLanguages.English, isPaid: true, priceVnd: 10000m, freePreviewPages: 1);
        var reader = AddUser();
        _db.ChapterEntitlements.Add(ChapterEntitlement.Create(reader, _chapter.Id, _series.Id, _studio.Id, 10000m, language: "vi"));
        await _db.SaveChangesAsync();

        (await Read(reader, "vi")).Value!.Pages.Should().OnlyContain(p => p.IsUnlocked);
        var en = (await Read(reader, "en")).Value!;
        en.Pages[1].IsUnlocked.Should().BeFalse();
        en.Pages[1].ImageUrl.Should().BeNull("trang ngoài xem trước của bản en chưa mua thì server không trả ảnh");
        en.Pages[0].ImageUrl.Should().EndWith("?lang=en", "trang xem trước của bản en dùng ảnh dịch");
    }

    [Fact]
    public async Task TranslatedPageImageUrl_CarriesLanguage()
    {
        PublishEdition(ContentLanguages.English);

        var dto = (await Read(null, "en")).Value!;

        dto.Pages.Should().OnlyContain(p => p.ImageUrl != null && p.ImageUrl.EndsWith("?lang=en"));
    }

    // ─── Đánh giá + dashboard ─────────────────────────────────────────────────

    [Fact]
    public async Task Rating_IsOnePerReader_AndShownOnSeriesDetail()
    {
        var a = AddUser();
        var b = AddUser();
        var handler = new RateSeriesCommandHandler(_db);
        await handler.Handle(new RateSeriesCommand(_series.Id, a, 5, "vi"), default);
        await handler.Handle(new RateSeriesCommand(_series.Id, b, 2, "en"), default);
        var summary = (await handler.Handle(new RateSeriesCommand(_series.Id, b, 4, "en"), default)).Value!;

        summary.RatingCount.Should().Be(2);
        summary.RatingAverage.Should().Be(4.5);
        (await handler.Handle(new RateSeriesCommand(_series.Id, a, 6, "vi"), default))
            .ErrorCode.Should().Be(ResultErrorCodes.Validation);

        var detail = (await new GetPublicSeriesDetailQueryHandler(_db, new FakeCurrentUser(a))
            .Handle(new GetPublicSeriesDetailQuery(_series.Id), default)).Value!;
        detail.RatingAverage.Should().Be(4.5);
        detail.MyRating.Should().Be(5);
    }

    [Fact]
    public async Task Insights_FilterByLanguage()
    {
        _db.ChapterViews.AddRange(
            ChapterView.Create(_chapter.Id, _series.Id, "u:a", DateTime.UtcNow, "vi"),
            ChapterView.Create(_chapter.Id, _series.Id, "u:b", DateTime.UtcNow, "vi"),
            ChapterView.Create(_chapter.Id, _series.Id, "u:c", DateTime.UtcNow, "en"));
        _db.SeriesRatings.Add(SeriesRating.Create(_series.Id, Guid.NewGuid(), 3, "en"));
        var tx = PaymentTransaction.Create(Guid.NewGuid(), "PF1", PaymentTransactionType.ChapterUnlock, 2000m, "", "970422",
            "0123", "X", _studio.Id, _series.Id, _chapter.Id, "en");
        tx.Approve(Guid.NewGuid());
        _db.PaymentTransactions.Add(tx);
        _db.ChapterEntitlements.Add(ChapterEntitlement.Create(Guid.NewGuid(), _chapter.Id, _series.Id, _studio.Id,
            2000m, tx.Id, language: "en"));
        await _db.SaveChangesAsync();
        var handler = new GetSeriesInsightsQueryHandler(_db);

        var all = (await handler.Handle(new GetSeriesInsightsQuery(_series.Id), default)).Value!;
        all.Totals.Views.Should().Be(3);
        all.Totals.RevenueVnd.Should().Be(2000m);
        all.Totals.Purchases.Should().Be(1);

        var en = (await handler.Handle(new GetSeriesInsightsQuery(_series.Id, "en"), default)).Value!;
        en.Totals.Views.Should().Be(1);
        en.Totals.RatingAverage.Should().Be(3);
        en.Chapters.Single().Stats.RevenueVnd.Should().Be(2000m);

        var vi = (await handler.Handle(new GetSeriesInsightsQuery(_series.Id, "vi"), default)).Value!;
        vi.Totals.Views.Should().Be(2);
        vi.Totals.RevenueVnd.Should().Be(0m);
        vi.Totals.RatingCount.Should().Be(0);
    }
}
