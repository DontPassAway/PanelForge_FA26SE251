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

        if (chapter.Status != ChapterStatus.Approved && chapter.Status != ChapterStatus.Published)
            return Result<ChapterEditionDto>.Failure(
                "Chương phải được duyệt nghiệm thu (ChapterApproved hoặc Published) trước khi phát hành ấn bản dịch (BR-34).",
                ResultErrorCodes.Conflict);

        var normLang = ContentLanguages.Normalize(cmd.LanguageCode);
        if (normLang is null)
            return Result<ChapterEditionDto>.Failure($"Mã ngôn ngữ '{cmd.LanguageCode}' không hợp lệ.", ResultErrorCodes.Validation);

        var edition = await _db.ChapterEditions
            .Include(e => e.TranslatorUser)
            .FirstOrDefaultAsync(e => e.ChapterId == cmd.ChapterId && e.LanguageCode == normLang, ct);

        if (edition is null)
            return Result<ChapterEditionDto>.Failure($"Ấn bản '{normLang}' của chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (edition.Status != EditionStatus.Approved)
            return Result<ChapterEditionDto>.Failure(
                $"Ấn bản '{normLang}' đang ở trạng thái {edition.Status}, chỉ có thể phát hành khi đã được Producer nghiệm thu (Approved) (BR-34).",
                ResultErrorCodes.Conflict);

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
