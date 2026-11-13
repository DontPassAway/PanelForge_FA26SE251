using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Catalog;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Monetization;

/// <summary>Bộ số liệu Publisher: lượt xem, người đọc, lượt mua (có trả tiền), doanh thu, đánh giá.</summary>
public sealed record InsightStatsDto(
    int Views,
    int UniqueReaders,
    int ChapterPurchases,
    decimal RevenueVnd,
    double RatingAverage,
    int RatingCount)
{
    public int Purchases => ChapterPurchases;
}

public sealed record LanguageInsightDto(string Language, InsightStatsDto Stats);

public sealed record ChapterInsightDto(
    Guid ChapterId,
    decimal ChapterNumber,
    string? Title,
    string Status,
    bool IsPublished,
    bool IsLocked,
    decimal PriceVnd,
    int PageCount,
    int PreviewPages,
    IReadOnlyList<string> AvailableLanguages,
    InsightStatsDto Stats);

public sealed record SeriesInsightsDto(
    Guid SeriesId,
    string Title,
    string? CoverUrl,
    Guid WorkspaceId,
    string WorkspaceName,
    string? Language,
    InsightStatsDto Totals,
    IReadOnlyList<LanguageInsightDto> ByLanguage,
    IReadOnlyList<ChapterInsightDto> Chapters,
    int DefaultPreviewPageCount);

/// <summary>
/// Publisher chọn một bộ truyện → dashboard tổng (lượt xem, lượt mua, đánh giá, doanh thu) và danh sách chương
/// kèm số liệu từng chương. <paramref name="Language"/> lọc theo bản ngôn ngữ (null = tất cả).
/// Lượt mua chỉ tính quyền đọc có trả tiền (không tính VIP mở miễn phí). Doanh thu = giao dịch mở khóa đã duyệt.
/// </summary>
public sealed record GetSeriesInsightsQuery(Guid SeriesId, string? Language = null) : IRequest<Result<SeriesInsightsDto>>;

public sealed class GetSeriesInsightsQueryHandler : IRequestHandler<GetSeriesInsightsQuery, Result<SeriesInsightsDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetSeriesInsightsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<SeriesInsightsDto>> Handle(GetSeriesInsightsQuery q, CancellationToken ct)
    {
        string? filter = null;
        if (!string.IsNullOrWhiteSpace(q.Language) && !q.Language.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            filter = ContentLanguages.Normalize(q.Language);
            if (filter is null)
                return Result<SeriesInsightsDto>.Failure("Ngôn ngữ không được hỗ trợ.", ResultErrorCodes.Validation);
        }

        var series = await _db.Series.AsNoTracking()
            .Where(s => s.Id == q.SeriesId)
            .Select(s => new
            {
                s.Id, s.Title, s.CoverImageSha256, s.WorkspaceId, WorkspaceName = s.Workspace.Name,
                s.DefaultPreviewPageCount
            })
            .FirstOrDefaultAsync(ct);
        if (series is null)
            return Result<SeriesInsightsDto>.Failure("Không tìm thấy bộ truyện.", ResultErrorCodes.NotFound);

        var now = DateTime.UtcNow;
        var chapters = await _db.Chapters.AsNoTracking()
            .Where(c => c.SeriesId == series.Id)
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new
            {
                c.Id, c.ChapterNumber, c.Title, c.Status, c.PublishedAt, c.IsLocked, c.PriceVnd
            })
            .ToListAsync(ct);
        var chapterIds = chapters.Select(c => c.Id).ToList();

        // Số liệu thô theo (chương, ngôn ngữ) — mỗi bảng một truy vấn gom nhóm.
        var views = await _db.ChapterViews.AsNoTracking()
            .Where(v => v.SeriesId == series.Id)
            .GroupBy(v => new { v.ChapterId, v.Language, v.ViewerKey })
            .Select(g => new { g.Key.ChapterId, g.Key.Language, g.Key.ViewerKey, Count = g.Count() })
            .ToListAsync(ct);
        var chapterBuys = await _db.ChapterEntitlements.AsNoTracking()
            .Where(e => e.SeriesId == series.Id && !e.IsDeleted && e.PricePaidVnd > 0)
            .GroupBy(e => new { e.ChapterId, e.Language })
            .Select(g => new { g.Key.ChapterId, g.Key.Language, Count = g.Count() })
            .ToListAsync(ct);
        var revenue = await _db.PaymentTransactions.AsNoTracking()
            .Where(t => t.SeriesId == series.Id && !t.IsDeleted && t.Status == PaymentTransactionStatus.Approved
                        && t.Type == PaymentTransactionType.ChapterUnlock)
            .GroupBy(t => new { t.ChapterId, Language = t.Language ?? ContentLanguages.Original })
            .Select(g => new { g.Key.ChapterId, g.Key.Language, Amount = g.Sum(t => t.AmountVnd) })
            .ToListAsync(ct);
        var ratings = await _db.SeriesRatings.AsNoTracking()
            .Where(r => r.SeriesId == series.Id && !r.IsDeleted)
            .Select(r => new { r.Language, r.Stars })
            .ToListAsync(ct);

        // Bản ngôn ngữ và số trang xem trước hiệu lực từng chương.
        var pageCounts = await _db.Pages.AsNoTracking()
            .Where(p => chapterIds.Contains(p.ChapterId))
            .GroupBy(p => p.ChapterId)
            .Select(g => new { ChapterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ChapterId, x => x.Count, ct);
        var translated = await _db.PageTranslations.AsNoTracking()
            .Where(t => chapterIds.Contains(t.ChapterId) && !t.IsDeleted)
            .GroupBy(t => new { t.ChapterId, t.Language })
            .Select(g => new { g.Key.ChapterId, g.Key.Language, Pages = g.Select(t => t.PageId).Distinct().Count() })
            .ToListAsync(ct);

        InsightStatsDto Stats(Guid? chapterId, string? language, bool includeRatings)
        {
            bool Match(Guid? cid, string lang) =>
                (chapterId is null || cid == chapterId) && (language is null || lang == language);

            var v = views.Where(x => Match(x.ChapterId, x.Language)).ToList();
            var rs = includeRatings ? ratings.Where(r => language is null || r.Language == language).ToList() : [];
            return new InsightStatsDto(
                v.Sum(x => x.Count),
                v.Select(x => x.ViewerKey).Distinct().Count(),
                chapterBuys.Where(x => Match(x.ChapterId, x.Language)).Sum(x => x.Count),
                revenue.Where(x => Match(x.ChapterId, x.Language)).Sum(x => x.Amount),
                rs.Count == 0 ? 0 : Math.Round(rs.Average(r => r.Stars), 2),
                rs.Count);
        }

        var chapterDtos = new List<ChapterInsightDto>();
        foreach (var c in chapters)
        {
            var pageCount = pageCounts.GetValueOrDefault(c.Id);
            var langs = new List<string> { ContentLanguages.Original };
            langs.AddRange(ContentLanguages.Translations.Where(l =>
                pageCount > 0 && translated.Any(t => t.ChapterId == c.Id && t.Language == l && t.Pages >= pageCount)));
            var previews = await ChapterReadAccess.ResolvePreviewPagesAsync(_db, c.Id, ct);

            chapterDtos.Add(new ChapterInsightDto(
                c.Id, c.ChapterNumber, c.Title, c.Status.ToString(),
                c.Status == ChapterStatus.Published && c.PublishedAt <= now,
                c.IsLocked, c.PriceVnd, pageCount, previews.Count(p => p.IsPreview), langs,
                Stats(c.Id, filter, includeRatings: false)));
        }

        return Result<SeriesInsightsDto>.Success(new SeriesInsightsDto(
            series.Id, series.Title,
            SetSeriesCoverCommandHandler.CoverUrl(series.Id, series.CoverImageSha256),
            series.WorkspaceId, series.WorkspaceName, filter,
            Stats(null, filter, includeRatings: true),
            ContentLanguages.All.Select(l => new LanguageInsightDto(l, Stats(null, l, includeRatings: true))).ToList(),
            chapterDtos,
            series.DefaultPreviewPageCount));
    }
}
