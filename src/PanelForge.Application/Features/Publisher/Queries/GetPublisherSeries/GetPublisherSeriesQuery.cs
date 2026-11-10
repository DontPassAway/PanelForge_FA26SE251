using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Publisher.Models;
using PanelForge.Application.Features.Series.Commands.SetSeriesCover;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Queries.GetPublisherSeries;

public sealed record GetPublisherSeriesQuery(
    Guid? WorkspaceId = null,
    string? Search = null
) : IRequest<Result<IReadOnlyList<PublisherSeriesDto>>>;

public sealed class GetPublisherSeriesQueryHandler
    : IRequestHandler<GetPublisherSeriesQuery, Result<IReadOnlyList<PublisherSeriesDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetPublisherSeriesQueryHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<PublisherSeriesDto>>> Handle(
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

        var rawList = await baseQuery
            .OrderByDescending(s => s.Chapters.Count(c => c.Status == ChapterStatus.Approved))
            .ThenByDescending(s => s.UpdatedAt ?? s.CreatedAt)
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
                s.UpdatedAt
            })
            .ToListAsync(ct);

        var result = rawList.Select(s => new PublisherSeriesDto(
            Id: s.Id,
            WorkspaceId: s.WorkspaceId,
            WorkspaceName: s.WorkspaceName,
            Title: s.Title,
            Synopsis: s.Synopsis,
            Genre: s.Genre,
            Format: s.Format,
            ReadingDirection: s.ReadingDirection,
            CoverUrl: SetSeriesCoverCommandHandler.CoverUrl(s.Id, s.CoverImageSha256),
            TotalChaptersCount: s.TotalChaptersCount,
            ApprovedChaptersCount: s.ApprovedChaptersCount,
            PublishedChaptersCount: s.PublishedChaptersCount,
            UpdatedAt: s.UpdatedAt
        )).ToList();

        return Result<IReadOnlyList<PublisherSeriesDto>>.Success(result);
    }
}

