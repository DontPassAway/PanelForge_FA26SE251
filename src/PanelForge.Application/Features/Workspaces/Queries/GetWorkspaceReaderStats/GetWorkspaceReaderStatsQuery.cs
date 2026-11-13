using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Publisher.Queries.GetPublisherRevenue;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Workspaces.Queries.GetWorkspaceReaderStats;

public sealed record ChapterReaderStatsDto(
    Guid ChapterId,
    decimal ChapterNumber,
    string? Title,
    bool IsPublished,
    int Views,
    int UniqueReaders);

public sealed record SeriesReaderStatsDto(
    Guid SeriesId,
    string Title,
    int Views,
    int UniqueReaders,
    IReadOnlyList<ChapterReaderStatsDto> Chapters);

public sealed record WorkspaceReaderStatsDto(
    int TotalViews,
    int TotalUniqueReaders,
    IReadOnlyList<SeriesReaderStatsDto> Series);

/// <summary>
/// Producer xem bộ truyện của Studio có bao nhiêu lượt đọc / người đọc (không kèm doanh thu — thuộc Publisher).
/// </summary>
public sealed record GetWorkspaceReaderStatsQuery(Guid WorkspaceId) : IRequest<Result<WorkspaceReaderStatsDto>>;

public sealed class GetWorkspaceReaderStatsQueryHandler
    : IRequestHandler<GetWorkspaceReaderStatsQuery, Result<WorkspaceReaderStatsDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetWorkspaceReaderStatsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<WorkspaceReaderStatsDto>> Handle(GetWorkspaceReaderStatsQuery q, CancellationToken ct)
    {
        var series = await _db.Series.AsNoTracking()
            .Where(s => s.WorkspaceId == q.WorkspaceId)
            .OrderBy(s => s.Title)
            .Select(s => new { s.Id, s.Title })
            .ToListAsync(ct);
        var ids = series.Select(s => s.Id).ToList();

        var now = DateTime.UtcNow;
        var chapters = await _db.Chapters.AsNoTracking()
            .Where(c => ids.Contains(c.SeriesId))
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new
            {
                c.Id, c.SeriesId, c.ChapterNumber, c.Title,
                IsPublished = c.Status == ChapterStatus.Published && c.PublishedAt <= now
            })
            .ToListAsync(ct);

        var bySeries = await ReaderStats.ViewsBySeriesAsync(_db, ids, ct);
        var byChapter = await ReaderStats.ViewsByChapterAsync(_db, ids, ct);
        var totalUnique = await _db.ChapterViews.AsNoTracking()
            .Where(v => ids.Contains(v.SeriesId))
            .Select(v => v.ViewerKey)
            .Distinct()
            .CountAsync(ct);

        var rows = series.Select(s =>
        {
            bySeries.TryGetValue(s.Id, out var sv);
            return new SeriesReaderStatsDto(
                s.Id, s.Title, sv.Views, sv.UniqueReaders,
                chapters.Where(c => c.SeriesId == s.Id).Select(c =>
                {
                    byChapter.TryGetValue(c.Id, out var cv);
                    return new ChapterReaderStatsDto(
                        c.Id, c.ChapterNumber, c.Title, c.IsPublished, cv.Views, cv.UniqueReaders);
                }).ToList());
        }).ToList();

        return Result<WorkspaceReaderStatsDto>.Success(
            new WorkspaceReaderStatsDto(rows.Sum(r => r.Views), totalUnique, rows));
    }
}
