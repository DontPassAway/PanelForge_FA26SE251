using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Interfaces.Persistence;

namespace PanelForge.Application.Features.Bible.Queries.GetBibleEntryHistory;

public sealed record GetBibleEntryHistoryQuery(Guid SeriesId, Guid BibleEntryId) : IRequest<Result<IReadOnlyList<BibleEntryRevisionDto>>>;

public sealed class GetBibleEntryHistoryQueryHandler : IRequestHandler<GetBibleEntryHistoryQuery, Result<IReadOnlyList<BibleEntryRevisionDto>>>
{
    private readonly IPanelForgeDbContext _dbContext;

    public GetBibleEntryHistoryQueryHandler(IPanelForgeDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<BibleEntryRevisionDto>>> Handle(GetBibleEntryHistoryQuery request, CancellationToken cancellationToken)
    {
        // Entry phải thuộc SeriesBible của seriesId trên route (chống IDOR)
        var bibleId = await _dbContext.SeriesBibles
            .Where(b => b.SeriesId == request.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var entryExists = bibleId != null && await _dbContext.BibleEntries
            .AnyAsync(e => e.Id == request.BibleEntryId && e.SeriesBibleId == bibleId.Value, cancellationToken);

        if (!entryExists)
        {
            return Result<IReadOnlyList<BibleEntryRevisionDto>>.Failure($"Mục BibleEntry {request.BibleEntryId} không tồn tại.");
        }

        var revisions = await _dbContext.BibleEntryRevisions
            .Where(r => r.BibleEntryId == request.BibleEntryId)
            .OrderByDescending(r => r.VersionNumber)
            .Select(r => new BibleEntryRevisionDto(
                r.Id,
                r.BibleEntryId,
                r.VersionNumber,
                r.Summary,
                r.SnapshotJson,
                r.ContentHash,
                r.AuthorId,
                r.AssociatedChapterId,
                r.EffectiveFromChapterNumber,
                r.IsInitialVersion,
                r.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<BibleEntryRevisionDto>>.Success(revisions);
    }
}
