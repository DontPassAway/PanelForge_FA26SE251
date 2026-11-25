using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.Editions;

/// <summary>
/// BR-34: Producer phát hành ấn bản ngôn ngữ đã được nghiệm thu (Approved).
/// </summary>
public sealed record PublishEditionCommand(
    Guid ChapterId,
    string LanguageCode,
    Guid UserId
) : IRequest<Result<ChapterEditionDto>>;

public sealed class PublishEditionCommandHandler
    : IRequestHandler<PublishEditionCommand, Result<ChapterEditionDto>>
{
    private readonly IPanelForgeDbContext _db;

    public PublishEditionCommandHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ChapterEditionDto>> Handle(
        PublishEditionCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<ChapterEditionDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (chapter.Status != ChapterStatus.ChapterApproved && chapter.Status != ChapterStatus.HandedOffAndPublished)
            return Result<ChapterEditionDto>.Failure(
                "Chương phải được duyệt nghiệm thu (ChapterApproved hoặc Published) trước khi phát hành ấn bản dịch (BR-34).",
                ResultErrorCodes.Conflict);

        var normLang = ContentLanguages.Normalize(cmd.LanguageCode);
        if (normLang is null)
            return Result<ChapterEditionDto>.Failure($"Mã ngôn ngữ '{cmd.LanguageCode}' không hợp lệ.", ResultErrorCodes.Validation);

        // Ấn bản ngôn ngữ gốc phát hành cùng chương (PublishChapter đổi trạng thái chương → Catalog). Phát hành riêng ấn bản
        // gốc khi chương chưa phát hành sẽ để chương kẹt ở ChapterApproved, Reader không thấy (BR-24, BR-34).
        var originalLanguage = await _db.Series.AsNoTracking()
            .Where(s => s.Id == chapter.SeriesId)
            .Select(s => s.OriginalLanguage)
            .FirstOrDefaultAsync(ct) ?? ContentLanguages.Original;
        if (normLang == ContentLanguages.Normalize(originalLanguage) && chapter.Status == ChapterStatus.ChapterApproved)
            return Result<ChapterEditionDto>.Failure(
                $"Ấn bản ngôn ngữ gốc '{normLang}' được phát hành cùng chương: Publisher dùng \"Phát hành chương\" để chương lên Catalog.",
                ResultErrorCodes.Conflict);

        var edition = await _db.ChapterEditions
            .Include(e => e.TranslatorUser)
            .FirstOrDefaultAsync(e => e.ChapterId == cmd.ChapterId && e.LanguageCode == normLang, ct);

        if (edition is null)
            return Result<ChapterEditionDto>.Failure($"Ấn bản '{normLang}' của chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (edition.Status != EditionStatus.Approved)
            return Result<ChapterEditionDto>.Failure(
                $"Ấn bản '{normLang}' đang ở trạng thái {edition.Status}, chỉ có thể phát hành khi đã được Producer nghiệm thu (Approved) (BR-34).",
                ResultErrorCodes.Conflict);

        var pageCount = await _db.Pages.CountAsync(p => p.ChapterId == chapter.Id, ct);
        if (EditionMonetizationGate.PublishBlocker(edition, pageCount) is { } blocker)
            return Result<ChapterEditionDto>.Failure(blocker, ResultErrorCodes.Conflict);

        edition.Publish();

        var review = ChapterReview.Create(
            chapter.Id,
            ChapterReviewAction.PublisherPublished,
            cmd.UserId,
            chapter.VersionVector,
            $"Phát hành ấn bản dịch [{normLang}]");
        _db.ChapterReviews.Add(review);

        await _db.SaveChangesAsync(ct);

        var approver = edition.ApprovedById.HasValue
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == edition.ApprovedById.Value, ct)
            : null;

        return Result<ChapterEditionDto>.Success(ChapterEditionMapping.ToDto(
            edition, edition.TranslatorUser?.FullName, approver?.FullName));
    }
}

/// <summary>
/// D4: điều kiện thương mại bắt buộc trước khi Publisher phát hành / hẹn giờ một ấn bản ngôn ngữ.
/// </summary>
public static class EditionMonetizationGate
{
    /// <returns>Lý do chặn (tiếng Việt) hoặc null nếu đủ điều kiện.</returns>
    public static string? PublishBlocker(ChapterEdition edition, int pageCount)
    {
        if (!edition.IsMonetizationConfigured)
            return $"Ấn bản '{edition.LanguageCode}' chưa được Publisher chọn Free hoặc Trả phí (D4).";
        if (edition.IsPaid && edition.PriceVnd <= 0)
            return $"Ấn bản '{edition.LanguageCode}' trả phí phải có giá lớn hơn 0 (D4).";
        if (edition.IsPaid && edition.FreePreviewPages >= pageCount)
            return $"Ấn bản '{edition.LanguageCode}' trả phí phải xem trước ít hơn tổng số trang ({pageCount}) (D4).";
        return null;
    }
}
