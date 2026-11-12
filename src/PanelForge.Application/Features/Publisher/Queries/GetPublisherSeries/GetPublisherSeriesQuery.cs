using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Publisher.Models;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Queries.GetPublisherSeries;

public sealed record GetPublisherSeriesQuery(
    string? Filter = null,
    Guid? WorkspaceId = null,
    string? Search = null,
    string? SortBy = null
) : IRequest<Result<IReadOnlyList<PublisherSeriesListItemDto>>>;

public sealed class GetPublisherSeriesQueryHandler
    : IRequestHandler<GetPublisherSeriesQuery, Result<IReadOnlyList<PublisherSeriesListItemDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetPublisherSeriesQueryHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<PublisherSeriesListItemDto>>> Handle(
        GetPublisherSeriesQuery query,
        CancellationToken ct)
    {
        var baseQuery = _db.Series
            .AsNoTracking()
            .Include(s => s.Workspace)
            .AsQueryable();

        if (query.WorkspaceId.HasValue && query.WorkspaceId.Value != Guid.Empty)
        {
            baseQuery = baseQuery.Where(s => s.WorkspaceId == query.WorkspaceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            baseQuery = baseQuery.Where(s =>
                s.Title.ToLower().Contains(search) ||
                s.Workspace.Name.ToLower().Contains(search));
        }

        var utcNow = DateTime.UtcNow;
        if (string.Equals(query.Filter, "needs_review", StringComparison.OrdinalIgnoreCase))
        {
            baseQuery = baseQuery.Where(s => s.Chapters.Any(c => c.Status == ChapterStatus.Approved));
        }
        else if (string.Equals(query.Filter, "scheduled", StringComparison.OrdinalIgnoreCase))
        {
            baseQuery = baseQuery.Where(s => s.Chapters.Any(c => c.Status == ChapterStatus.Published && c.PublishedAt > utcNow));
        }

        if (string.Equals(query.SortBy, "az", StringComparison.OrdinalIgnoreCase))
        {
            baseQuery = baseQuery.OrderBy(s => s.Title);
        }
        else
        {
            // Default or "latest": Series with Approved chapters first, then by updated date / created date descending
            baseQuery = baseQuery
                .OrderByDescending(s => s.Chapters.Count(c => c.Status == ChapterStatus.Approved))
                .ThenByDescending(s => s.UpdatedAt ?? s.CreatedAt);
        }

        var rawList = await baseQuery
            .Select(s => new
            {
                s.Id,
                s.WorkspaceId,
                WorkspaceName = s.Workspace.Name,
                s.Title,
                s.Synopsis,
                s.Genre,
                s.Format,
                s.ReadingDirection,
                s.CoverImageSha256,
                TotalChaptersCount = s.Chapters.Count,
                ApprovedChaptersCount = s.Chapters.Count(c => c.Status == ChapterStatus.Approved),
                PublishedChaptersCount = s.Chapters.Count(c => c.Status == ChapterStatus.Published),
                LatestChapter = s.Chapters
                    .OrderByDescending(c => c.ChapterNumber)
                    .Select(c => new
                    {
                        c.Id,
                        c.ChapterNumber,
                        c.Title,
                        c.Status,
                        UpdatedAt = c.UpdatedAt ?? c.CreatedAt
                    })
                    .FirstOrDefault(),
                s.UpdatedAt
            })
            .ToListAsync(ct);

        var result = rawList.Select(s =>
        {
            var genreList = (s.Genre ?? "Action")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var coverUrl = SetSeriesCoverCommandHandler.CoverUrl(s.Id, s.CoverImageSha256);

            PublisherLatestChapterDto? latestChapterDto = null;
            if (s.LatestChapter != null)
            {
                latestChapterDto = new PublisherLatestChapterDto(
                    ChapterId: s.LatestChapter.Id,
                    ChapterNumber: s.LatestChapter.ChapterNumber,
                    Title: s.LatestChapter.Title,
                    Status: s.LatestChapter.Status,
                    UpdatedAt: s.LatestChapter.UpdatedAt
                );
            }

            return new PublisherSeriesListItemDto(
                SeriesId: s.Id,
                Id: s.Id,
                Title: s.Title,
                CoverImageUrl: coverUrl,
                CoverUrl: coverUrl,
                Status: "Ongoing",
                Genres: genreList,
                Genre: s.Genre ?? "Action",
                AgeRating: "All",
                Synopsis: s.Synopsis,
                WorkspaceId: s.WorkspaceId,
                WorkspaceName: s.WorkspaceName,
                TotalChaptersCount: s.TotalChaptersCount,
                ApprovedChaptersCount: s.ApprovedChaptersCount,
                PublishedChaptersCount: s.PublishedChaptersCount,
                LatestChapter: latestChapterDto,
                UpdatedAt: s.UpdatedAt,
                Format: s.Format,
                ReadingDirection: s.ReadingDirection
            );
        }).ToList();

        return Result<IReadOnlyList<PublisherSeriesListItemDto>>.Success(result);
    }
}
