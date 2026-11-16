using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.CanvasElements.Common;
using PanelForge.Application.Features.Chapters.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Commands.Editions;

/// <summary>
/// BR-34: Artist/Letterer nộp bản dịch cho ấn bản ngôn ngữ.
/// Điều kiện: Chương đã qua Gate 3 (ChapterApproved hoặc Published), mọi bóng thoại có chữ dịch,
/// và không còn cảnh báo tràn chữ chưa xác nhận (BR-20).
/// </summary>
public sealed record SubmitEditionTranslationCommand(
    Guid ChapterId,
    string LanguageCode,
    Guid UserId
) : IRequest<Result<ChapterEditionDto>>;

public sealed class SubmitEditionTranslationCommandHandler
    : IRequestHandler<SubmitEditionTranslationCommand, Result<ChapterEditionDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;

    public SubmitEditionTranslationCommandHandler(IPanelForgeDbContext db, IPageRepository pages)
    {
        _db = db;
        _pages = pages;
    }

    public async Task<Result<ChapterEditionDto>> Handle(
        SubmitEditionTranslationCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.Include(c => c.Series).FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<ChapterEditionDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        // Chương phải đã đạt Gate 3 (ChapterApproved hoặc Published)
        if (chapter.Status != ChapterStatus.Approved && chapter.Status != ChapterStatus.Published)
            return Result<ChapterEditionDto>.Failure(
                "Chương phải được Producer duyệt khóa (ChapterApproved) trước khi nộp bản dịch ấn bản (BR-34).",
                ResultErrorCodes.Conflict);

        var normLang = ContentLanguages.Normalize(cmd.LanguageCode);
        if (normLang is null || !chapter.Series.ReleaseLanguages.Contains(normLang) ||
            normLang.Equals(chapter.Series.OriginalLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ChapterEditionDto>.Failure(
                $"Ngôn ngữ '{cmd.LanguageCode}' không hợp lệ hoặc trùng ngôn ngữ gốc.",
                ResultErrorCodes.Validation);
        }

        var edition = await _db.ChapterEditions.FirstOrDefaultAsync(e => e.ChapterId == cmd.ChapterId && e.LanguageCode == normLang, ct);
        if (edition is null)
        {
            edition = ChapterEdition.Create(cmd.ChapterId, normLang, EditionStatus.Draft);
            _db.ChapterEditions.Add(edition);
        }

        if (edition.Status != EditionStatus.Draft)
            return Result<ChapterEditionDto>.Failure(
                $"Ấn bản '{normLang}' đang ở trạng thái {edition.Status}, chỉ có thể nộp khi ở trạng thái Draft (BR-34).",
                ResultErrorCodes.Conflict);

        // Kiểm tra đủ chữ mọi bóng thoại trên tất cả các trang
        var pages = await _db.Pages.Where(p => p.ChapterId == cmd.ChapterId).OrderBy(p => p.PageNumber).ToListAsync(ct);
        var missingBalloons = new List<string>();

        foreach (var p in pages)
        {
            var pageAgg = await _pages.GetAsync(p.Id, ct);
            if (pageAgg is null) continue;

            var texts = pageAgg.Elements.Values
                .Where(e => !e.IsRemoved && Page.TextElementTypes.Contains(e.ElementType))
                .ToList();

            foreach (var e in texts)
            {
                if (e.Texts == null || !e.Texts.TryGetValue(normLang, out var text) || string.IsNullOrWhiteSpace(text))
                {
                    missingBalloons.Add($"Trang {p.PageNumber} (Element {e.ElementId})");
                }
            }

            // Kiểm tra BR-20 mức chặn cho ngôn ngữ này (tràn chữ chưa xác nhận)
            var analysis = await LetteringPageAnalysis.AnalyzeAsync(_db, pageAgg, ct, normLang);
            var unacked = analysis.Warnings.Where(w => w.Code == LetteringWarningRules.TextOverflow && !w.Acknowledged).ToList();
            if (unacked.Count > 0)
            {
                return Result<ChapterEditionDto>.Failure(
                    $"Trang {p.PageNumber} còn {unacked.Count} cảnh báo tràn chữ chưa xác nhận kèm lý do cho ngôn ngữ '{normLang}' (BR-20).",
                    ResultErrorCodes.Conflict);
            }
        }

        if (missingBalloons.Count > 0)
        {
            return Result<ChapterEditionDto>.Failure(
                $"Chưa đủ bản dịch cho mọi bóng thoại của chương. Còn thiếu {missingBalloons.Count} bóng thoại: {string.Join(", ", missingBalloons)}.",
                ResultErrorCodes.Conflict);
        }

        edition.SubmitTranslation(cmd.UserId);
        await _db.SaveChangesAsync(ct);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        return Result<ChapterEditionDto>.Success(ChapterEditionMapping.ToDto(edition, user?.FullName));
    }
}
