using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Publisher.Models;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Queries.GetPublisherSeriesDetail;

public sealed record GetPublisherSeriesDetailQuery(Guid SeriesId) : IRequest<Result<PublisherSeriesDetailDto>>;

public sealed class GetPublisherSeriesDetailQueryHandler
    : IRequestHandler<GetPublisherSeriesDetailQuery, Result<PublisherSeriesDetailDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetPublisherSeriesDetailQueryHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PublisherSeriesDetailDto>> Handle(
        GetPublisherSeriesDetailQuery query,
        CancellationToken ct)
    {
        var series = await _db.Series
            .AsNoTracking()
            .Include(s => s.Workspace)
                .ThenInclude(w => w.Owner)
            .Include(s => s.Workspace)
                .ThenInclude(w => w.Members.Where(m => m.Role == WorkspaceRole.Producer && m.Status == WorkspaceMemberStatus.Active && !m.IsDeleted))
                    .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(s => s.Id == query.SeriesId, ct);

        if (series == null)
        {
            return Result<PublisherSeriesDetailDto>.Failure("Không tìm thấy bộ truyện.", ResultErrorCodes.NotFound);
        }

        var producerMember = series.Workspace.Members.FirstOrDefault();
        var producerName = producerMember?.User?.FullName ?? series.Workspace.Owner?.FullName;

        var chapters = await _db.Chapters
            .AsNoTracking()
            .Where(c => c.SeriesId == query.SeriesId &&
                (c.Status == ChapterStatus.Approved || c.Status == ChapterStatus.Published))
            .OrderByDescending(c => c.ChapterNumber)
            .Select(c => new
            {
                c.Id,
                c.ChapterNumber,
                c.Title,
                c.Status,
                c.PublishedAt,
                PageCount = c.Pages.Count,
                c.UpdatedAt,
                c.CreatedAt,
                c.IsLocked,
                c.PriceVnd
            })
            .ToListAsync(ct);

        var chapterIds = chapters.Select(c => c.Id).ToList();

        var reviews = await _db.ChapterReviews
            .AsNoTracking()
            .Where(r => chapterIds.Contains(r.ChapterId) && r.Action == ChapterReviewAction.ProducerApproved)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var latestApprovalByChapter = reviews
            .GroupBy(r => r.ChapterId)
            .ToDictionary(g => g.Key, g => g.First());

        var actorUserIds = latestApprovalByChapter.Values.Select(r => r.ActorUserId).Distinct().ToList();

        var userDict = await _db.Users
            .AsNoTracking()
            .Where(u => actorUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var managedChapters = chapters.Select(c =>
        {
            DateTime? approvedAt = null;
            string? approvedBy = null;

            if (latestApprovalByChapter.TryGetValue(c.Id, out var review))
            {
                approvedAt = review.CreatedAt;
                approvedBy = userDict.GetValueOrDefault(review.ActorUserId) ?? producerName;
            }
            else if (c.Status == ChapterStatus.Approved)
            {
                approvedAt = c.UpdatedAt ?? c.CreatedAt;
                approvedBy = producerName;
            }
            else if (c.Status == ChapterStatus.Published)
            {
                approvedAt = c.PublishedAt ?? c.UpdatedAt ?? c.CreatedAt;
                approvedBy = producerName;
            }

            return new PublisherManagedChapterDto(
                ChapterId: c.Id,
                Id: c.Id,
                ChapterNumber: c.ChapterNumber,
                Title: c.Title,
                Status: c.Status,
                PublishAt: c.PublishedAt,
                PublishedAt: c.PublishedAt,
                PageCount: c.PageCount,
                PagesCount: c.PageCount,
                ApprovedAt: approvedAt,
                ApprovedBy: approvedBy,
                IsLocked: c.IsLocked,
                PriceVnd: c.PriceVnd
            );
        }).ToList();

        var genreList = (series.Genre ?? "Action")
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var coverUrl = SetSeriesCoverCommandHandler.CoverUrl(series.Id, series.CoverImageSha256);

        var dto = new PublisherSeriesDetailDto(
            SeriesId: series.Id,
            Id: series.Id,
            Title: series.Title,
            CoverImageUrl: coverUrl,
            CoverUrl: coverUrl,
            WorkspaceId: series.WorkspaceId,
            WorkspaceName: series.Workspace.Name,
            ProducerName: producerName,
            Genres: genreList,
            Genre: series.Genre ?? "Action",
            AgeRating: "All",
            Status: "Ongoing",
            Logline: series.StoryArc,
            Synopsis: series.Synopsis,
            Chapters: managedChapters
        );

        return Result<PublisherSeriesDetailDto>.Success(dto);
    }
}

