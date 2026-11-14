using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Bible.Models;

namespace PanelForge.Application.Features.Bible.Queries.GetBibleConceptComments;

public sealed record GetBibleConceptCommentsQuery(
    Guid SeriesId,
    Guid BibleEntryId,
    Guid RevisionId
) : IRequest<Result<IReadOnlyList<BibleConceptCommentDto>>>;

public sealed class GetBibleConceptCommentsQueryHandler
    : IRequestHandler<GetBibleConceptCommentsQuery, Result<IReadOnlyList<BibleConceptCommentDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetBibleConceptCommentsQueryHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<BibleConceptCommentDto>>> Handle(
        GetBibleConceptCommentsQuery query,
        CancellationToken ct)
    {
        var bibleId = await _db.SeriesBibles
            .Where(b => b.SeriesId == query.SeriesId)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        var entryExists = bibleId is not null && await _db.BibleEntries
            .AnyAsync(e => e.Id == query.BibleEntryId && e.SeriesBibleId == bibleId.Value, ct);

        if (!entryExists)
            return Result<IReadOnlyList<BibleConceptCommentDto>>.Failure("Mục Bible không tồn tại.", ResultErrorCodes.NotFound);

        var comments = await _db.BibleConceptComments
            .Include(c => c.Replies.Where(r => !r.IsDeleted))
            .Where(c => c.RevisionId == query.RevisionId && c.ParentCommentId == null && !c.IsDeleted)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var dtos = comments.Select(BibleConceptCommentDto.From).ToList();
        return Result<IReadOnlyList<BibleConceptCommentDto>>.Success(dtos);
    }
}
