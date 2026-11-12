using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Models;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Commands.SetChapterCover;
using PanelForge.Application.Features.Publisher.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Queries.GetPublisherChapters;

public sealed record GetPublisherChaptersQuery(
    string? Status = null,
    Guid? WorkspaceId = null,
    Guid? SeriesId = null,
    string? Search = null,
    int PageIndex = 1,
    int PageSize = 50
) : IRequest<Result<PagedResult<PublisherChapterDto>>>;

public sealed class GetPublisherChaptersQueryHandler
    : IRequestHandler<GetPublisherChaptersQuery, Result<PagedResult<PublisherChapterDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetPublisherChaptersQueryHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<PagedResult<PublisherChapterDto>>> Handle(
        GetPublisherChaptersQuery query,
        CancellationToken ct)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var baseQuery = _db.Chapters
            .AsNoTracking()
            .Include(c => c.Series)
            .ThenInclude(s => s.Workspace)
            .AsQueryable();

        // Filter status
        if (!string.IsNullOrWhiteSpace(query.Status) && !query.Status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (Enum.TryParse<ChapterStatus>(query.Status, true, out var parsedStatus))
            {
                baseQuery = baseQuery.Where(c => c.Status == parsedStatus);
            }
        }
        else
        {
            // Default "all" for Publisher Portal: Approved, Published, or InProduction
            baseQuery = baseQuery.Where(c =>
                c.Status == ChapterStatus.Approved ||
                c.Status == ChapterStatus.Published ||
                c.Status == ChapterStatus.InProduction);
        }

        // Filter workspace
        if (query.WorkspaceId.HasValue && query.WorkspaceId.Value != Guid.Empty)
        {
            baseQuery = baseQuery.Where(c => c.Series.WorkspaceId == query.WorkspaceId.Value);
        }

        // Filter series
        if (query.SeriesId.HasValue && query.SeriesId.Value != Guid.Empty)
        {
            baseQuery = baseQuery.Where(c => c.SeriesId == query.SeriesId.Value);
        }

        // Filter search keyword
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            baseQuery = baseQuery.Where(c =>
                (c.Title != null && c.Title.ToLower().Contains(search)) ||
                c.Series.Title.ToLower().Contains(search) ||
                c.Series.Workspace.Name.ToLower().Contains(search));
        }

        var totalCount = await baseQuery.CountAsync(ct);

        // Sorting: Approved first (needs action), then by updated date / created date descending
        var rawItems = await baseQuery
            .OrderByDescending(c => c.Status == ChapterStatus.Approved)
            .ThenByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.SeriesId,
                SeriesTitle = c.Series.Title,
                WorkspaceId = c.Series.WorkspaceId,
                WorkspaceName = c.Series.Workspace.Name,
                c.ChapterNumber,
                c.Title,
                c.TargetReleaseDate,
                c.VersionVector,
                c.CreatedAt,
                c.UpdatedAt,
                c.CreatedBy,
                ScenesCount = c.Scenes.Count,
                PagesCount = c.Pages.Count,
                c.Status,
                c.PublishedAt,
                c.Outline,
                c.IsScriptFrozen,
                c.ScriptFrozenAt,
                c.ScriptFrozenById,
                c.ScriptReopenStatus,
                c.ScriptReopenReason,
                c.ScriptReopenRequestedById,
                c.ScriptReopenRequestedAt,
                c.CoverImageSha256,
                c.ScriptContentHash,
                c.IsLocked,
                c.PriceVnd
            })
            .ToListAsync(ct);

        var items = rawItems.Select(c => new PublisherChapterDto(
            Id: c.Id,
            SeriesId: c.SeriesId,
            SeriesTitle: c.SeriesTitle,
            WorkspaceId: c.WorkspaceId,
            WorkspaceName: c.WorkspaceName,
            ChapterNumber: c.ChapterNumber,
            Title: c.Title,
            TargetReleaseDate: c.TargetReleaseDate,
            VersionVector: c.VersionVector,
            CreatedAt: c.CreatedAt,
            UpdatedAt: c.UpdatedAt,
            CreatedBy: c.CreatedBy,
            ScenesCount: c.ScenesCount,
            PagesCount: c.PagesCount,
            Status: c.Status,
            PublishedAt: c.PublishedAt,
            Outline: c.Outline,
            IsScriptFrozen: c.IsScriptFrozen,
            ScriptFrozenAt: c.ScriptFrozenAt,
            ScriptFrozenById: c.ScriptFrozenById,
            ScriptReopenStatus: c.ScriptReopenStatus,
            ScriptReopenReason: c.ScriptReopenReason,
            ScriptReopenRequestedById: c.ScriptReopenRequestedById,
            ScriptReopenRequestedAt: c.ScriptReopenRequestedAt,
            CoverUrl: SetChapterCoverCommandHandler.CoverUrl(c.Id, c.CoverImageSha256),
            ScriptContentHash: c.ScriptContentHash,
            IsLocked: c.IsLocked,
            PriceVnd: c.PriceVnd
        )).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Result<PagedResult<PublisherChapterDto>>.Success(new PagedResult<PublisherChapterDto>(
            Items: items,
            TotalCount: totalCount,
            PageIndex: pageIndex,
            PageSize: pageSize,
            TotalPages: totalPages
        ));
    }
}
