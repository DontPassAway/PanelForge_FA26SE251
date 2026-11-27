using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Features.Bible.Models;
using PanelForge.Application.Common.Interfaces.Persistence;

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
            .Include(r => r.Events)
            .Where(r => r.BibleEntryId == request.BibleEntryId)
            .OrderByDescending(r => r.VersionNumber)
            .ToListAsync(cancellationToken);

        var endorserIds = revisions
            .Where(r => r.WriterEndorsedByUserId.HasValue)
            .Select(r => r.WriterEndorsedByUserId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string>? userNames = null;
        if (endorserIds.Count > 0 && _dbContext.Users is not null)
        {
            userNames = await _dbContext.Users
                .AsNoTracking()
                .Where(u => endorserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        }

        var dtos = revisions.Select(r => BibleEntryRevisionDto.From(r, userNames)).ToList();
        return Result<IReadOnlyList<BibleEntryRevisionDto>>.Success(dtos);
    }
}
