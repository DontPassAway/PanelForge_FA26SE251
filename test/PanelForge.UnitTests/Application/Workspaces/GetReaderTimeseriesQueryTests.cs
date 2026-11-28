using FluentAssertions;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Workspaces.Queries.GetReaderTimeseries;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Payments;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;
using PanelForge.Infrastructure.Persistence;
using PanelForge.UnitTests.Common;
using Xunit;
using DomainSeries = PanelForge.Domain.Entities.Content.Series;

namespace PanelForge.UnitTests.Application.Workspaces;

/// <summary>BR-32: chuỗi thời gian lượt đọc / doanh thu theo ngày, tuần (thứ Hai), tháng — UTC, kỳ trống = 0.</summary>
public class GetReaderTimeseriesQueryTests
{
    private readonly PanelForgeDbContext _db = InMemoryDb.Create();
    private readonly StudioWorkspace _studio;
    private readonly DomainSeries _series;
    private readonly Chapter _chapter;
    private readonly GetReaderTimeseriesQueryHandler _handler;

    private static DateTime Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    public GetReaderTimeseriesQueryTests()
    {
        _studio = StudioWorkspace.Create("Studio Moon", Guid.NewGuid());
        _db.StudioWorkspaces.Add(_studio);
        _series = DomainSeries.Create(_studio.Id, "Moon Blade", ReadingDirection.RightToLeft, "Synopsis", "Fantasy");
        _db.Series.Add(_series);
        _chapter = Chapter.Create(_series.Id, 1, "Khởi đầu");
        _db.Chapters.Add(_chapter);
        _db.SaveChanges();
        _handler = new GetReaderTimeseriesQueryHandler(_db);
    }

    private void View(string viewer, DateTime at, Guid? seriesId = null) =>
        _db.ChapterViews.Add(ChapterView.Create(_chapter.Id, seriesId ?? _series.Id, viewer, at));

    private PaymentTransaction Unlock(decimal amount, Guid? seriesId = null)
    {
        var tx = PaymentTransaction.Create(Guid.NewGuid(), $"PF{Guid.NewGuid():N}"[..12], PaymentTransactionType.ChapterUnlock,
            amount, "", "970422", "0123", "X", _studio.Id, seriesId ?? _series.Id, _chapter.Id);
        _db.PaymentTransactions.Add(tx);
        return tx;
    }

    private Task<Result<ReaderTimeseriesDto>> Run(
        DateTime from, DateTime to, TimeseriesGranularity granularity,
        bool includeRevenue = true, Guid? workspaceId = null, Guid? seriesId = null) =>
        _handler.Handle(new GetReaderTimeseriesQuery(workspaceId ?? _studio.Id, seriesId, from, to, granularity, includeRevenue), default);

    [Fact]
    public async Task Day_GroupsViewsPerDay_AndFillsEmptyDaysWithZero()
    {
        View("u:a", Utc(2026, 11, 2, 8));
        View("u:a", Utc(2026, 11, 2, 20));
        View("u:b", Utc(2026, 11, 2, 21));
        View("u:a", Utc(2026, 11, 4, 1));
        View("u:z", Utc(2026, 11, 5, 0)); // ngoài [From, To)
        await _db.SaveChangesAsync();

        var dto = (await Run(Utc(2026, 11, 2), Utc(2026, 11, 5), TimeseriesGranularity.Day)).Value!;

        dto.Granularity.Should().Be("Day");
        dto.Points.Select(p => p.PeriodStart).Should().Equal(Utc(2026, 11, 2), Utc(2026, 11, 3), Utc(2026, 11, 4));
        dto.Points.Select(p => p.Views).Should().Equal(3, 0, 1);
        dto.Points.Select(p => p.UniqueReaders).Should().Equal(2, 0, 1);
    }

    [Fact]
    public async Task Week_StartsOnMonday_AndCountsUniqueReadersPerPeriod()
    {
        // 2026-11-02 là thứ Hai.
        View("u:a", Utc(2026, 11, 1));   // Chủ nhật → tuần 26/10
        View("u:a", Utc(2026, 11, 2));   // tuần 02/11
        View("u:a", Utc(2026, 11, 8));   // Chủ nhật, vẫn tuần 02/11
        View("u:b", Utc(2026, 11, 9));   // tuần 09/11
        await _db.SaveChangesAsync();

        var dto = (await Run(Utc(2026, 10, 28), Utc(2026, 11, 16), TimeseriesGranularity.Week)).Value!;

        dto.Points.Select(p => p.PeriodStart).Should().Equal(Utc(2026, 10, 26), Utc(2026, 11, 2), Utc(2026, 11, 9));
        dto.Points.Select(p => p.Views).Should().Equal(1, 2, 1);
        // u:a đọc ở cả hai tuần nên được đếm ở mỗi tuần, nhưng chỉ một lần trong tuần 02/11.
        dto.Points.Select(p => p.UniqueReaders).Should().Equal(1, 1, 1);
    }

    [Fact]
    public async Task Month_GroupsRevenue_OnlyApprovedChapterUnlocks()
    {
        Unlock(2000m).Approve(Guid.NewGuid(), Utc(2026, 9, 15));
        Unlock(3000m).Approve(Guid.NewGuid(), Utc(2026, 9, 30, 23));
        Unlock(5000m).Approve(Guid.NewGuid(), Utc(2026, 11, 1));
        Unlock(7000m); // Pending
        Unlock(9000m).Reject(Guid.NewGuid(), "sai nội dung");
        var topup = PaymentTransaction.Create(Guid.NewGuid(), "PFTOPUP1", PaymentTransactionType.Topup, 50000m, "",
            "970422", "0123", "X", _studio.Id, _series.Id);
        topup.Approve(null, Utc(2026, 9, 10));
        _db.PaymentTransactions.Add(topup);
        await _db.SaveChangesAsync();

        var dto = (await Run(Utc(2026, 9, 1), Utc(2026, 12, 1), TimeseriesGranularity.Month)).Value!;

        dto.Points.Select(p => p.PeriodStart).Should().Equal(Utc(2026, 9, 1), Utc(2026, 10, 1), Utc(2026, 11, 1));
        dto.Points.Select(p => p.RevenueVnd).Should().Equal(5000m, 0m, 5000m);
    }

    [Fact]
    public async Task WithoutIncludeRevenue_RevenueIsZero()
    {
        Unlock(2000m).Approve(Guid.NewGuid(), Utc(2026, 9, 15));
        await _db.SaveChangesAsync();

        var dto = (await Run(Utc(2026, 9, 1), Utc(2026, 10, 1), TimeseriesGranularity.Month, includeRevenue: false)).Value!;

        dto.Points.Should().ContainSingle().Which.RevenueVnd.Should().Be(0m);
    }

    [Fact]
    public async Task OnlyCountsSeriesOfTheWorkspace()
    {
        var other = StudioWorkspace.Create("Other", Guid.NewGuid());
        _db.StudioWorkspaces.Add(other);
        var otherSeries = DomainSeries.Create(other.Id, "Other", ReadingDirection.LeftToRight, "S", "Drama");
        _db.Series.Add(otherSeries);
        View("u:a", Utc(2026, 11, 2));
        View("u:b", Utc(2026, 11, 2), otherSeries.Id);
        await _db.SaveChangesAsync();

        var dto = (await Run(Utc(2026, 11, 2), Utc(2026, 11, 3), TimeseriesGranularity.Day)).Value!;

        dto.Points.Single().Views.Should().Be(1);
    }

    [Theory]
    [InlineData(TimeseriesGranularity.Day, 401)]
    [InlineData(TimeseriesGranularity.Week, 3 * 366 + 1)]
    [InlineData(TimeseriesGranularity.Month, 3 * 366 + 1)]
    public async Task RangeTooLong_IsRejected(TimeseriesGranularity granularity, int days)
    {
        var from = Utc(2024, 1, 1);

        var result = await Run(from, from.AddDays(days), granularity);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task FromNotBeforeTo_IsRejected()
    {
        var result = await Run(Utc(2026, 11, 2), Utc(2026, 11, 2), TimeseriesGranularity.Day);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SeriesOutsideWorkspace_IsNotFound()
    {
        var other = StudioWorkspace.Create("Other", Guid.NewGuid());
        _db.StudioWorkspaces.Add(other);
        var otherSeries = DomainSeries.Create(other.Id, "Other", ReadingDirection.LeftToRight, "S", "Drama");
        _db.Series.Add(otherSeries);
        await _db.SaveChangesAsync();

        var result = await Run(Utc(2026, 11, 2), Utc(2026, 11, 3), TimeseriesGranularity.Day, seriesId: otherSeries.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ResultErrorCodes.NotFound);
    }

    [Fact]
    public async Task SeriesFilter_WithoutWorkspace_CountsOnlyThatSeries()
    {
        var secondSeries = DomainSeries.Create(_studio.Id, "Second", ReadingDirection.LeftToRight, "S", "Drama");
        _db.Series.Add(secondSeries);
        View("u:a", Utc(2026, 11, 2));
        View("u:b", Utc(2026, 11, 2), secondSeries.Id);
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetReaderTimeseriesQuery(
            null, secondSeries.Id, Utc(2026, 11, 2), Utc(2026, 11, 3), TimeseriesGranularity.Day, true), default);

        result.Value!.Points.Single().Views.Should().Be(1);
    }
}
