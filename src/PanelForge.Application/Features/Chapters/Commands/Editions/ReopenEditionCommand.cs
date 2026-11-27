using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.Editions;

/// <summary>
/// BR-34: Producer mở lại ấn bản ngôn ngữ về trạng thái Draft để hiệu đính / vẽ lại lettering dịch.
/// Bắt buộc kèm theo lý do (Reason).
/// </summary>
public sealed record ReopenEditionCommand(
    Guid ChapterId,
    string LanguageCode,
    Guid UserId,
    string Reason
) : IRequest<Result<ChapterEditionDto>>;

public sealed class ReopenEditionCommandHandler
    : IRequestHandler<ReopenEditionCommand, Result<ChapterEditionDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ReopenEditionCommandHandler(IPanelForgeDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ChapterEditionDto>> Handle(
        ReopenEditionCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return Result<ChapterEditionDto>.Failure("Mở lại ấn bản bắt buộc phải có lý do.", ResultErrorCodes.Validation);

        var chapter = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<ChapterEditionDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var normLang = ContentLanguages.Normalize(cmd.LanguageCode);
        if (normLang is null)
            return Result<ChapterEditionDto>.Failure($"Mã ngôn ngữ '{cmd.LanguageCode}' không hợp lệ.", ResultErrorCodes.Validation);

        var edition = await _db.ChapterEditions
            .Include(e => e.TranslatorUser)
            .FirstOrDefaultAsync(e => e.ChapterId == cmd.ChapterId && e.LanguageCode == normLang, ct);

        if (edition is null)
            return Result<ChapterEditionDto>.Failure($"Ấn bản '{normLang}' của chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (edition.Status == EditionStatus.Draft)
            return Result<ChapterEditionDto>.Failure($"Ấn bản '{normLang}' đã ở trạng thái Draft.", ResultErrorCodes.Conflict);

        if (edition.Status == EditionStatus.Published)
            return Result<ChapterEditionDto>.Failure(
                $"Ấn bản '{normLang}' đã phát hành, không thể mở lại về Draft (D1).",
                ResultErrorCodes.Conflict);

        edition.Reopen();

        var review = ChapterReview.Create(
            chapter.Id,
            ChapterReviewAction.ProducerReopened,
            cmd.UserId,
            chapter.VersionVector,
            $"Mở lại ấn bản dịch [{normLang}]: {cmd.Reason.Trim()}");
        _db.ChapterReviews.Add(review);

        await _db.SaveChangesAsync(ct);

        var approver = edition.ApprovedById.HasValue
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == edition.ApprovedById.Value, ct)
            : null;

        return Result<ChapterEditionDto>.Success(ChapterEditionMapping.ToDto(
            edition, edition.TranslatorUser?.FullName, approver?.FullName));
    }
}
