using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Monetization;

// ─── Settings: mặc định N trang đầu xem trước cho người chưa mua chương trả phí ──

public sealed record SeriesMonetizationSettingsDto(Guid SeriesId, int DefaultPreviewPageCount, int MaxDefaultPreviewPageCount);

public sealed record UpdateSeriesMonetizationSettingsRequest(int DefaultPreviewPageCount);

/// <summary>
/// Publisher cấu hình theo từng bộ truyện: mặc định N trang đầu mỗi chương trả phí là trang xem trước (0 = khóa toàn bộ).
/// Áp ngay cho mọi chương chưa chọn tay trang xem trước; chương đã chọn tay giữ nguyên.
/// </summary>
public sealed record UpdateSeriesMonetizationSettingsCommand(Guid SeriesId, int DefaultPreviewPageCount)
    : IRequest<Result<SeriesMonetizationSettingsDto>>;

public sealed class UpdateSeriesMonetizationSettingsCommandHandler
    : IRequestHandler<UpdateSeriesMonetizationSettingsCommand, Result<SeriesMonetizationSettingsDto>>
{
    private readonly IPanelForgeDbContext _db;

    public UpdateSeriesMonetizationSettingsCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<SeriesMonetizationSettingsDto>> Handle(UpdateSeriesMonetizationSettingsCommand cmd, CancellationToken ct)
    {
        var series = await _db.Series.FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null)
            return Result<SeriesMonetizationSettingsDto>.Failure("Không tìm thấy bộ truyện.", ResultErrorCodes.NotFound);

        if (cmd.DefaultPreviewPageCount is < 0 or > Domain.Entities.Content.Series.MaxDefaultPreviewPageCount)
            return Result<SeriesMonetizationSettingsDto>.Failure(
                $"Số trang xem trước mặc định từ 0 đến {Domain.Entities.Content.Series.MaxDefaultPreviewPageCount}.",
                ResultErrorCodes.Validation);

        series.SetDefaultPreviewPages(cmd.DefaultPreviewPageCount);
        await _db.SaveChangesAsync(ct);
        return Result<SeriesMonetizationSettingsDto>.Success(ToDto(series));
    }

    public static SeriesMonetizationSettingsDto ToDto(Domain.Entities.Content.Series s) =>
        new(s.Id, s.DefaultPreviewPageCount, Domain.Entities.Content.Series.MaxDefaultPreviewPageCount);
}

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
            .AnyAsync(c => c.SeriesId == cmd.SeriesId && c.Status == ChapterStatus.Published && c.PublishedAt <= now, ct);
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
