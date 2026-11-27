using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Commands.PublishChapter;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.Editions;

/// <summary>
/// AUD-BE-03 / BR-34: Phát hành hoặc hẹn giờ ấn bản theo <c>ChapterEdition.Id</c>.
/// Ấn bản gốc đi qua <see cref="PublishChapterCommandHandler"/> (cập nhật trạng thái chương &amp; lịch phát hành);
/// ấn bản dịch đi qua <see cref="PublishEditionCommandHandler"/> (chưa hỗ trợ hẹn giờ — GAP-01).
/// </summary>
public sealed record PublishEditionByIdCommand(
    Guid EditionId,
    Guid UserId,
    DateTime? PublishAt,
    bool IsSchedule = false
) : IRequest<Result<ChapterReviewDto>>;

public sealed class PublishEditionByIdCommandHandler
    : IRequestHandler<PublishEditionByIdCommand, Result<ChapterReviewDto>>
{
    private readonly IPanelForgeDbContext _db;

    public PublishEditionByIdCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterReviewDto>> Handle(PublishEditionByIdCommand cmd, CancellationToken ct)
    {
        var edition = await _db.ChapterEditions.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == cmd.EditionId, ct);
        if (edition is null)
            return Result<ChapterReviewDto>.Failure(
                $"Ấn bản {cmd.EditionId} không tồn tại.", ResultErrorCodes.NotFound);

        var originalLanguage = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == edition.ChapterId)
            .Join(_db.Series.AsNoTracking(), c => c.SeriesId, s => s.Id, (_, s) => s.OriginalLanguage)
            .FirstOrDefaultAsync(ct) ?? ContentLanguages.Original;

        var isOriginal = edition.LanguageCode == ContentLanguages.Normalize(originalLanguage);

        if (isOriginal)
        {
            return await new PublishChapterCommandHandler(_db)
                .Handle(new PublishChapterCommand(edition.ChapterId, cmd.UserId, cmd.PublishAt), ct);
        }

        if (cmd.IsSchedule || cmd.PublishAt.HasValue)
        {
            return Result<ChapterReviewDto>.Failure(
                $"Hẹn giờ phát hành cho ấn bản dịch '{edition.LanguageCode}' chưa được hỗ trợ (GAP-01).",
                ResultErrorCodes.Conflict);
        }

        var publishResult = await new PublishEditionCommandHandler(_db)
            .Handle(new PublishEditionCommand(edition.ChapterId, edition.LanguageCode, cmd.UserId), ct);

        if (!publishResult.IsSuccess)
            return Result<ChapterReviewDto>.Failure(publishResult.ErrorMessage!, publishResult.ErrorCode);

        var review = await _db.ChapterReviews.AsNoTracking()
            .Where(r => r.ChapterId == edition.ChapterId && r.Action == ChapterReviewAction.PublisherPublished)
            .OrderByDescending(r => r.CreatedAt)
            .FirstAsync(ct);

        return Result<ChapterReviewDto>.Success(ChapterReviewDto.From(review));
    }
}
