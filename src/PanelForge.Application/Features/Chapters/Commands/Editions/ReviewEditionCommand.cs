using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.Editions;

/// <summary>
/// BR-34: Editor thẩm định bản dịch của ấn bản ngôn ngữ.
/// Luật 4 mắt (BR-06): Editor thẩm định phải khác với người dịch (TranslatorUserId).
/// </summary>
public sealed record ReviewEditionCommand(
    Guid ChapterId,
    string LanguageCode,
    Guid UserId,
    ReviewEditionAction Action,
    string? Comment = null
) : IRequest<Result<ChapterEditionDto>>;

public sealed class ReviewEditionCommandHandler
    : IRequestHandler<ReviewEditionCommand, Result<ChapterEditionDto>>
{
    private readonly IPanelForgeDbContext _db;

    public ReviewEditionCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterEditionDto>> Handle(
        ReviewEditionCommand cmd, CancellationToken ct)
    {
        var normLang = ContentLanguages.Normalize(cmd.LanguageCode);
        if (normLang is null)
            return Result<ChapterEditionDto>.Failure($"Mã ngôn ngữ '{cmd.LanguageCode}' không hợp lệ.", ResultErrorCodes.Validation);

        var edition = await _db.ChapterEditions
            .Include(e => e.TranslatorUser)
            .FirstOrDefaultAsync(e => e.ChapterId == cmd.ChapterId && e.LanguageCode == normLang, ct);

        if (edition is null)
            return Result<ChapterEditionDto>.Failure($"Ấn bản '{normLang}' của chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        if (edition.Status != EditionStatus.InReview)
            return Result<ChapterEditionDto>.Failure(
                $"Ấn bản '{normLang}' đang ở trạng thái {edition.Status}, chỉ có thể thẩm định khi ở trạng thái InReview.",
                ResultErrorCodes.Conflict);

        // Luật 4 mắt (BR-06): Editor thẩm định phải khác với người dịch
        if (edition.TranslatorUserId.HasValue && edition.TranslatorUserId.Value == cmd.UserId)
            return Result<ChapterEditionDto>.Failure(
                "Quy tắc 4 mắt (BR-06): Editor thẩm định bản dịch phải khác với người dịch.",
                ResultErrorCodes.Forbidden);

        if (cmd.Action == ReviewEditionAction.Approve)
        {
            edition.ReviewApprove();
        }
        else if (cmd.Action == ReviewEditionAction.RequestChange)
        {
            if (string.IsNullOrWhiteSpace(cmd.Comment))
                return Result<ChapterEditionDto>.Failure("Yêu cầu sửa bản dịch bắt buộc phải có nhận xét.", ResultErrorCodes.Validation);

            // Tạo ChangeRequest gắn LanguageCode. change_requests.assignment_id là FK bắt buộc → gắn vào
            // assignment mới nhất của người dịch trong chương (Artist/Letterer phụ trách chữ).
            var responsibleUserId = edition.TranslatorUserId ?? cmd.UserId;
            var assignmentId = await _db.Assignments.AsNoTracking()
                .Where(a => a.ChapterId == cmd.ChapterId && a.AssigneeUserId == responsibleUserId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (assignmentId is null)
                return Result<ChapterEditionDto>.Failure(
                    "Không tìm thấy phân công của người dịch trong chương để gắn phiếu sửa bản dịch.",
                    ResultErrorCodes.Conflict);

            edition.ReviewReject();

            var cr = ChangeRequest.Create(
                assignmentId: assignmentId.Value,
                chapterId: cmd.ChapterId,
                submissionId: null,
                responsibleUserId: responsibleUserId,
                createdByUserId: cmd.UserId,
                description: $"[Bản dịch {normLang}] {cmd.Comment.Trim()}",
                languageCode: normLang
            );
            _db.ChangeRequests.Add(cr);
        }
        else
        {
            return Result<ChapterEditionDto>.Failure("Hành động thẩm định không hợp lệ.", ResultErrorCodes.Validation);
        }

        await _db.SaveChangesAsync(ct);
        return Result<ChapterEditionDto>.Success(ChapterEditionMapping.ToDto(edition, edition.TranslatorUser?.FullName));
    }
}
