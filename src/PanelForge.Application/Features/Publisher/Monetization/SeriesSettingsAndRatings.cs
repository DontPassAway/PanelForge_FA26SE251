using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Monetization;

// ─── Reader: đánh giá 1–5 sao cho bộ truyện ─────────────────────────────────

public sealed record RateSeriesRequest(int Stars, string? Language = null);

public sealed record SeriesRatingSummaryDto(Guid SeriesId, double RatingAverage, int RatingCount, int? MyRating);

/// <summary>Độc giả đánh giá (hoặc sửa đánh giá) bộ truyện đã phát hành; mỗi người một đánh giá.</summary>
public sealed record RateSeriesCommand(Guid SeriesId, Guid UserId, int Stars, string? Language)
    : IRequest<Result<SeriesRatingSummaryDto>>;

public sealed class RateSeriesCommandHandler : IRequestHandler<RateSeriesCommand, Result<SeriesRatingSummaryDto>>
{
    private readonly IPanelForgeDbContext _db;

    public RateSeriesCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<SeriesRatingSummaryDto>> Handle(RateSeriesCommand cmd, CancellationToken ct)
    {
        if (cmd.Stars is < SeriesRating.MinStars or > SeriesRating.MaxStars)
            return Result<SeriesRatingSummaryDto>.Failure("Đánh giá phải từ 1 đến 5 sao.", ResultErrorCodes.Validation);
        var language = ContentLanguages.Normalize(cmd.Language);
        if (language is null)
            return Result<SeriesRatingSummaryDto>.Failure("Ngôn ngữ không được hỗ trợ.", ResultErrorCodes.Validation);

        var now = DateTime.UtcNow;
        var published = await _db.Chapters.AsNoTracking()
            .AnyAsync(c => c.SeriesId == cmd.SeriesId && c.Status == ChapterStatus.HandedOffAndPublished && c.PublishedAt <= now, ct);
        if (!published)
            return Result<SeriesRatingSummaryDto>.Failure("Không tìm thấy bộ truyện đã phát hành.", ResultErrorCodes.NotFound);

        var rating = await _db.SeriesRatings.FirstOrDefaultAsync(r => r.SeriesId == cmd.SeriesId && r.UserId == cmd.UserId, ct);
        if (rating is null) _db.SeriesRatings.Add(SeriesRating.Create(cmd.SeriesId, cmd.UserId, cmd.Stars, language));
        else rating.Update(cmd.Stars, language);
        await _db.SaveChangesAsync(ct);

        var stars = await _db.SeriesRatings.AsNoTracking()
            .Where(r => r.SeriesId == cmd.SeriesId && !r.IsDeleted)
            .Select(r => r.Stars)
            .ToListAsync(ct);
        return Result<SeriesRatingSummaryDto>.Success(new SeriesRatingSummaryDto(
            cmd.SeriesId, Math.Round(stars.Average(), 2), stars.Count, cmd.Stars));
    }
}
