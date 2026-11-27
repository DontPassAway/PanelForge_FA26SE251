using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.PageComments.Common;
using PanelForge.Application.Features.PageComments.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PageComments.Queries.GetSeriesPageComments;

public sealed record GetSeriesPageCommentsQuery(
    Guid SeriesId,
    Guid? ChapterId = null,
    PageCommentStatus? Status = null)
    : IRequest<Result<IReadOnlyList<PageCommentDto>>>;

public sealed class GetSeriesPageCommentsQueryHandler
    : IRequestHandler<GetSeriesPageCommentsQuery, Result<IReadOnlyList<PageCommentDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetSeriesPageCommentsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<PageCommentDto>>> Handle(GetSeriesPageCommentsQuery query, CancellationToken ct)
    {
        var seriesExists = await _db.Series.AsNoTracking().AnyAsync(s => s.Id == query.SeriesId, ct);
        if (!seriesExists)
            return Result<IReadOnlyList<PageCommentDto>>.Failure("Không tìm thấy bộ truyện.", ResultErrorCodes.NotFound);

        var q = _db.PageComments.AsNoTracking().Where(c => c.SeriesId == query.SeriesId);
        if (query.ChapterId.HasValue)
            q = q.Where(c => c.ChapterId == query.ChapterId.Value);
        if (query.Status.HasValue)
            q = q.Where(c => c.Status == query.Status.Value);

        var items = await q
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);

        var dtos = await PageCommentMapper.ToDtosAsync(_db, items, ct);
        return Result<IReadOnlyList<PageCommentDto>>.Success(dtos);
    }
}
