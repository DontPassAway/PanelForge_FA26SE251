using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Queries.GetPublisherRevenue;

/// <summary>Doanh thu một bộ truyện (giao dịch mở khóa chương đã duyệt) kèm lượt đọc.</summary>
public sealed record PublisherSeriesRevenueDto(
    Guid SeriesId,
    string SeriesTitle,
    string? CoverUrl,
    Guid WorkspaceId,
    string WorkspaceName,
    decimal TotalRevenueVnd,
    decimal ChapterRevenueVnd,
    int PendingCount,
    int ChapterUnlocks,
    int TotalViews,
    int UniqueReaders);

public sealed record PublisherRevenueSummaryDto(
    decimal TotalRevenueVnd,
    decimal ChapterRevenueVnd,
    int PendingCount,
    int TotalViews);

public sealed record PublisherRevenueDto(
    PublisherRevenueSummaryDto Summary,
    IReadOnlyList<PublisherSeriesRevenueDto> Series);

/// <summary>
/// Publisher quản lý doanh thu theo bộ truyện trên toàn hệ thống (lọc theo Studio / tên truyện).
/// Chỉ tính giao dịch mở khóa chương trả phí (nạp Ví và gói VIP không gắn với bộ truyện).
/// </summary>
public sealed record GetPublisherRevenueQuery(Guid? WorkspaceId = null, string? Search = null)
    : IRequest<Result<PublisherRevenueDto>>;

public sealed class GetPublisherRevenueQueryHandler : IRequestHandler<GetPublisherRevenueQuery, Result<PublisherRevenueDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetPublisherRevenueQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<PublisherRevenueDto>> Handle(GetPublisherRevenueQuery q, CancellationToken ct)
    {
        var seriesQuery = _db.Series.AsNoTracking();
        if (q.WorkspaceId.HasValue)
            seriesQuery = seriesQuery.Where(s => s.WorkspaceId == q.WorkspaceId.Value);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim().ToLower();
            seriesQuery = seriesQuery.Where(s => s.Title.ToLower().Contains(search));
        }

        var series = await seriesQuery
            .Select(s => new { s.Id, s.Title, s.CoverImageSha256, s.WorkspaceId, WorkspaceName = s.Workspace.Name })
            .ToListAsync(ct);
        var ids = series.Select(s => s.Id).ToList();

        var tx = await _db.PaymentTransactions.AsNoTracking()
            .Where(t => !t.IsDeleted && t.SeriesId != null && ids.Contains(t.SeriesId.Value) && t.Type == PaymentTransactionType.ChapterUnlock)
            .GroupBy(t => new { SeriesId = t.SeriesId!.Value, t.Type, t.Status })
            .Select(g => new { g.Key.SeriesId, g.Key.Type, g.Key.Status, Count = g.Count(), Amount = g.Sum(t => t.AmountVnd) })
            .ToListAsync(ct);

        var chapterUnlocks = await _db.ChapterEntitlements.AsNoTracking()
            .Where(e => !e.IsDeleted && ids.Contains(e.SeriesId))
            .GroupBy(e => e.SeriesId)
            .Select(g => new { SeriesId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SeriesId, x => x.Count, ct);

        var views = await ReaderStats.ViewsBySeriesAsync(_db, ids, ct);

        var rows = series.Select(s =>
        {
            decimal Sum(PaymentTransactionType type) => tx
                .Where(t => t.SeriesId == s.Id && t.Type == type && t.Status == PaymentTransactionStatus.Approved)
                .Sum(t => t.Amount);
            var chapterRevenue = Sum(PaymentTransactionType.ChapterUnlock);
            views.TryGetValue(s.Id, out var v);
            return new PublisherSeriesRevenueDto(
                s.Id, s.Title,
                SetSeriesCoverCommandHandler.CoverUrl(s.Id, s.CoverImageSha256),
                s.WorkspaceId, s.WorkspaceName,
                chapterRevenue, chapterRevenue,
                tx.Where(t => t.SeriesId == s.Id && t.Status == PaymentTransactionStatus.Pending).Sum(t => t.Count),
                chapterUnlocks.GetValueOrDefault(s.Id),
                v.Views, v.UniqueReaders);
        })
        .OrderByDescending(r => r.TotalRevenueVnd)
        .ThenBy(r => r.SeriesTitle)
        .ToList();

        var summary = new PublisherRevenueSummaryDto(
            rows.Sum(r => r.TotalRevenueVnd),
            rows.Sum(r => r.ChapterRevenueVnd),
            rows.Sum(r => r.PendingCount),
            rows.Sum(r => r.TotalViews));

        return Result<PublisherRevenueDto>.Success(new PublisherRevenueDto(summary, rows));
    }
}

/// <summary>Lượt đọc (bản ghi <c>ChapterView</c>) và số người đọc khác nhau theo bộ truyện / chương.</summary>
public static class ReaderStats
{
    public static async Task<Dictionary<Guid, (int Views, int UniqueReaders)>> ViewsBySeriesAsync(
        IPanelForgeDbContext db, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct)
    {
        var rows = await db.ChapterViews.AsNoTracking()
            .Where(v => seriesIds.Contains(v.SeriesId))
            .GroupBy(v => v.SeriesId)
            .Select(g => new
            {
                SeriesId = g.Key,
                Views = g.Count(),
                UniqueReaders = g.Select(v => v.ViewerKey).Distinct().Count()
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.SeriesId, r => (r.Views, r.UniqueReaders));
    }

    public static async Task<Dictionary<Guid, (int Views, int UniqueReaders)>> ViewsByChapterAsync(
        IPanelForgeDbContext db, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct)
    {
        var rows = await db.ChapterViews.AsNoTracking()
            .Where(v => seriesIds.Contains(v.SeriesId))
            .GroupBy(v => v.ChapterId)
            .Select(g => new
            {
                ChapterId = g.Key,
                Views = g.Count(),
                UniqueReaders = g.Select(v => v.ViewerKey).Distinct().Count()
            })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.ChapterId, r => (r.Views, r.UniqueReaders));
    }
}
