using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Queries.GetReaderTimeseries;

public enum TimeseriesGranularity
{
    Day,
    Week,
    Month
}

public sealed record ReaderTimeseriesPointDto(DateTime PeriodStart, int Views, int UniqueReaders, decimal RevenueVnd);

public sealed record ReaderTimeseriesDto(
    string Granularity,
    DateTime From,
    DateTime To,
    IReadOnlyList<ReaderTimeseriesPointDto> Points);

/// <summary>
/// BR-32: lượt đọc / người đọc (và doanh thu mở khóa chương khi <see cref="IncludeRevenue"/>) theo kỳ ngày/tuần/tháng
/// trong [From, To), UTC, tuần bắt đầu thứ Hai. Producer gọi với IncludeRevenue=false (doanh thu thuộc Publisher).
/// </summary>
public sealed record GetReaderTimeseriesQuery(
    Guid? WorkspaceId,
    Guid? SeriesId,
    DateTime From,
    DateTime To,
    TimeseriesGranularity Granularity,
    bool IncludeRevenue) : IRequest<Result<ReaderTimeseriesDto>>;

public sealed class GetReaderTimeseriesQueryHandler
    : IRequestHandler<GetReaderTimeseriesQuery, Result<ReaderTimeseriesDto>>
{
    public const int MaxDailyRangeDays = 400;
    public const int MaxWeeklyMonthlyRangeYears = 3;

    private readonly IPanelForgeDbContext _db;

    public GetReaderTimeseriesQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ReaderTimeseriesDto>> Handle(GetReaderTimeseriesQuery q, CancellationToken ct)
    {
        var from = ToUtc(q.From);
        var to = ToUtc(q.To);
        if (from >= to)
            return Result<ReaderTimeseriesDto>.Failure("Thời điểm bắt đầu phải trước thời điểm kết thúc.");

        var tooLong = q.Granularity == TimeseriesGranularity.Day
            ? to > from.AddDays(MaxDailyRangeDays)
            : to > from.AddYears(MaxWeeklyMonthlyRangeYears);
        if (tooLong)
        {
            return Result<ReaderTimeseriesDto>.Failure(q.Granularity == TimeseriesGranularity.Day
                ? $"Khoảng thời gian theo ngày tối đa {MaxDailyRangeDays} ngày."
                : $"Khoảng thời gian theo tuần/tháng tối đa {MaxWeeklyMonthlyRangeYears} năm.");
        }

        var seriesQuery = _db.Series.AsNoTracking();
        if (q.WorkspaceId.HasValue)
            seriesQuery = seriesQuery.Where(s => s.WorkspaceId == q.WorkspaceId.Value);
        if (q.SeriesId.HasValue)
        {
            seriesQuery = seriesQuery.Where(s => s.Id == q.SeriesId.Value);
            if (!await seriesQuery.AnyAsync(ct))
                return Result<ReaderTimeseriesDto>.Failure("Không tìm thấy bộ truyện.", ResultErrorCodes.NotFound);
        }
        var seriesIds = await seriesQuery.Select(s => s.Id).ToListAsync(ct);

        var views = await _db.ChapterViews.AsNoTracking()
            .Where(v => seriesIds.Contains(v.SeriesId) && v.ViewedAt >= from && v.ViewedAt < to)
            .Select(v => new { v.ViewedAt, v.ViewerKey })
            .ToListAsync(ct);

        // Cùng điều kiện doanh thu với GetPublisherRevenueQuery: mở khóa chương đã duyệt, chưa xóa.
        var revenue = q.IncludeRevenue
            ? await _db.PaymentTransactions.AsNoTracking()
                .Where(t => !t.IsDeleted
                    && t.SeriesId != null && seriesIds.Contains(t.SeriesId.Value)
                    && t.Type == PaymentTransactionType.ChapterUnlock
                    && t.Status == PaymentTransactionStatus.Approved
                    && (t.ConfirmedAt ?? t.CreatedAt) >= from
                    && (t.ConfirmedAt ?? t.CreatedAt) < to)
                .Select(t => new { At = t.ConfirmedAt ?? t.CreatedAt, t.AmountVnd })
                .ToListAsync(ct)
            : [];

        var viewsByPeriod = views
            .GroupBy(v => PeriodStart(ToUtc(v.ViewedAt), q.Granularity))
            .ToDictionary(g => g.Key, g => (Views: g.Count(), Unique: g.Select(v => v.ViewerKey).Distinct().Count()));
        var revenueByPeriod = revenue
            .GroupBy(t => PeriodStart(ToUtc(t.At), q.Granularity))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.AmountVnd));

        var points = new List<ReaderTimeseriesPointDto>();
        for (var period = PeriodStart(from, q.Granularity); period < to; period = Next(period, q.Granularity))
        {
            viewsByPeriod.TryGetValue(period, out var v);
            revenueByPeriod.TryGetValue(period, out var r);
            points.Add(new ReaderTimeseriesPointDto(period, v.Views, v.Unique, r));
        }

        return Result<ReaderTimeseriesDto>.Success(
            new ReaderTimeseriesDto(q.Granularity.ToString(), from, to, points));
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime PeriodStart(DateTime utc, TimeseriesGranularity granularity)
    {
        var day = new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
        return granularity switch
        {
            TimeseriesGranularity.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            TimeseriesGranularity.Month => new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => day
        };
    }

    private static DateTime Next(DateTime period, TimeseriesGranularity granularity) => granularity switch
    {
        TimeseriesGranularity.Week => period.AddDays(7),
        TimeseriesGranularity.Month => period.AddMonths(1),
        _ => period.AddDays(1)
    };
}
