using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Models;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterReviews;

/// <summary>Lịch sử phê duyệt cấp Chapter (Producer duyệt, Publisher duyệt/từ chối, mở lại), cũ nhất trước.</summary>
public sealed record GetChapterReviewsQuery(Guid ChapterId) : IRequest<Result<IReadOnlyList<ChapterReviewDto>>>;

public sealed class GetChapterReviewsQueryHandler : IRequestHandler<GetChapterReviewsQuery, Result<IReadOnlyList<ChapterReviewDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetChapterReviewsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ChapterReviewDto>>> Handle(GetChapterReviewsQuery q, CancellationToken ct)
    {
        if (!await _db.Chapters.AnyAsync(c => c.Id == q.ChapterId, ct))
            return Result<IReadOnlyList<ChapterReviewDto>>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var rows = await _db.ChapterReviews.AsNoTracking()
            .Where(r => r.ChapterId == q.ChapterId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        return Result<IReadOnlyList<ChapterReviewDto>>.Success(rows.Select(ChapterReviewDto.From).ToList());
    }
}
