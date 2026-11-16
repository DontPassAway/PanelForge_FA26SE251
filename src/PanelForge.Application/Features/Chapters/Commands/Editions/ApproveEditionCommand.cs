using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.Editions;

/// <summary>
/// BR-34: Producer cho phép phát hành / nghiệm thu ấn bản ngôn ngữ dịch.
/// Điều kiện: Chương đã qua Gate 3 (ChapterApproved hoặc Published), ấn bản đã được Editor duyệt (EditorApproved).
/// Tạo Snapshot riêng của ấn bản lưu SHA-256.
/// </summary>
public sealed record ApproveEditionCommand(
    Guid ChapterId,
    string LanguageCode,
    Guid UserId
) : IRequest<Result<ChapterEditionDto>>;

public sealed class ApproveEditionCommandHandler
    : IRequestHandler<ApproveEditionCommand, Result<ChapterEditionDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;

    public ApproveEditionCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<ChapterEditionDto>> Handle(
        ApproveEditionCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.Include(c => c.Series).FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<ChapterEditionDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (chapter.Status != ChapterStatus.Approved && chapter.Status != ChapterStatus.Published)
            return Result<ChapterEditionDto>.Failure(
                "Chương phải được Producer duyệt khóa (ChapterApproved) trước khi nghiệm thu ấn bản dịch (BR-34).",
                ResultErrorCodes.Conflict);

        var normLang = ContentLanguages.Normalize(cmd.LanguageCode);
        if (normLang is null)
            return Result<ChapterEditionDto>.Failure($"Mã ngôn ngữ '{cmd.LanguageCode}' không hợp lệ.", ResultErrorCodes.Validation);

        var edition = await _db.ChapterEditions
            .Include(e => e.TranslatorUser)
            .FirstOrDefaultAsync(e => e.ChapterId == cmd.ChapterId && e.LanguageCode == normLang, ct);

        if (edition is null)
            return Result<ChapterEditionDto>.Failure($"Ấn bản '{normLang}' của chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (edition.Status != EditionStatus.EditorApproved)
            return Result<ChapterEditionDto>.Failure(
                $"Ấn bản '{normLang}' đang ở trạng thái {edition.Status}, chỉ có thể nghiệm thu khi Editor đã duyệt (EditorApproved) (BR-34).",
                ResultErrorCodes.Conflict);

        // Tạo Snapshot cho ấn bản ngôn ngữ này
        var snapshot = await ChapterSnapshotBuilder.CreateAsync(_db, _pages, chapter, cmd.UserId, ct, normLang);
        _db.ChapterSnapshots.Add(snapshot);

        edition.Approve(snapshot.Id, cmd.UserId);

        var review = ChapterReview.Create(
            chapter.Id,
            ChapterReviewAction.ProducerApproved,
            cmd.UserId,
            chapter.VersionVector,
            $"Producer nghiệm thu ấn bản dịch [{normLang}]");
        _db.ChapterReviews.Add(review);

        await _db.SaveChangesAsync(ct);

        var approver = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        return Result<ChapterEditionDto>.Success(ChapterEditionMapping.ToDto(
            edition, edition.TranslatorUser?.FullName, approver?.FullName));
    }
}
