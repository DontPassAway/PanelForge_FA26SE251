using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Assets.Common;
using PanelForge.Application.Features.Catalog;
using PanelForge.Domain.Entities.Audit;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Publisher.Monetization;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Trạng thái và quy định thương mại của một ấn bản ngôn ngữ (BR-33, BR-34).</summary>
/// <param name="TranslatedPages">Số trang đã có bản dịch (ấn bản đã nộp trở đi = mọi trang).</param>
/// <param name="IsAvailable">Ấn bản đã phát hành cho Reader.</param>
/// <param name="EditionStatus">Draft | InReview | EditorApproved | Approved | Published; null = chưa có ấn bản.</param>
/// <param name="IsMonetizationConfigured">D4: Publisher đã chốt Free/Trả phí; false → chưa được phát hành / hẹn giờ.</param>
/// <param name="IsPaid">Trả phí (true) hay Free (false); chỉ có nghĩa khi đã cấu hình.</param>
/// <param name="PriceVnd">Giá mở khóa VNĐ (Free = 0).</param>
/// <param name="FreePreviewPages">N trang đầu người chưa mua được xem (Free = 0).</param>
public sealed record ChapterLanguageStatusDto(
    string Language,
    int TranslatedPages,
    bool IsAvailable,
    string? EditionStatus,
    bool IsMonetizationConfigured,
    bool IsPaid,
    decimal PriceVnd,
    int FreePreviewPages);

/// <param name="ImageUrls">Ảnh xem trước cho Publisher theo ngôn ngữ (null nếu trang chưa có artwork).</param>
public sealed record ManagedPageDto(
    Guid PageId,
    int PageNumber,
    IReadOnlyDictionary<string, string?> ImageUrls);

/// <param name="Languages">Mỗi ngôn ngữ một ấn bản với loại Free/Trả phí, giá và N trang xem trước riêng.</param>
/// <param name="Pages">Trang theo thứ tự đọc; trang xem trước của một ấn bản = <c>FreePreviewPages</c> trang đầu.</param>
public sealed record ChapterManageDto(
    Guid ChapterId,
    Guid SeriesId,
    decimal ChapterNumber,
    string? Title,
    bool IsPublished,
    IReadOnlyList<ChapterLanguageStatusDto> Languages,
    IReadOnlyList<ManagedPageDto> Pages,
    decimal MinPriceVnd = SetEditionMonetizationCommandHandler.MinPriceVnd,
    decimal MaxPriceVnd = SetEditionMonetizationCommandHandler.MaxPriceVnd);

/// <param name="IsPaid">Trả phí (true) hay Free (false) — bắt buộc chọn rõ (D4).</param>
/// <param name="PriceVnd">Trả phí: giá VNĐ nguyên trong khoảng cho phép. Free: 0.</param>
/// <param name="FreePreviewPages">Trả phí: 0 ≤ N &lt; tổng số trang. Free: bỏ qua (lưu 0).</param>
/// <param name="Reason">Bắt buộc khi đổi loại hoặc giá của ấn bản đã phát hành (D4); ghi vào Audit.</param>
public sealed record SetEditionMonetizationRequest(bool IsPaid, decimal PriceVnd, int FreePreviewPages, string? Reason = null);

// ─── Query: màn Manage của một chương ────────────────────────────────────────

public sealed record GetChapterManageQuery(Guid ChapterId) : IRequest<Result<ChapterManageDto>>;

public sealed class GetChapterManageQueryHandler : IRequestHandler<GetChapterManageQuery, Result<ChapterManageDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetChapterManageQueryHandler(IPanelForgeDbContext db) => _db = db;

    public Task<Result<ChapterManageDto>> Handle(GetChapterManageQuery q, CancellationToken ct) =>
        ChapterManageReader.ReadAsync(_db, q.ChapterId, ct);
}

public static class ChapterManageReader
{
    public static string PreviewImageUrl(Guid chapterId, Guid pageId, string language) =>
        $"/api/publisher/chapters/{chapterId}/pages/{pageId}/image?lang={language}";

    public static async Task<Result<ChapterManageDto>> ReadAsync(IPanelForgeDbContext db, Guid chapterId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var chapter = await db.Chapters.AsNoTracking()
            .Where(c => c.Id == chapterId)
            .Select(c => new
            {
                c.Id, c.SeriesId, c.ChapterNumber, c.Title,
                IsPublished = c.Status == ChapterStatus.HandedOffAndPublished && c.PublishedAt <= now
            })
            .FirstOrDefaultAsync(ct);
        if (chapter is null)
            return Result<ChapterManageDto>.Failure("Không tìm thấy chương.", ResultErrorCodes.NotFound);

        var orderedPages = await db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId)
            .OrderBy(p => p.PageNumber)
            .Select(p => new { p.Id, p.PageNumber })
            .ToListAsync(ct);
        var withArtwork = (await db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId && db.AssetReferences.Any(r => r.PageId == p.Id))
            .Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var editions = await db.ChapterEditions.AsNoTracking()
            .Where(e => e.ChapterId == chapterId && !e.IsDeleted)
            .ToListAsync(ct);

        var pageCount = orderedPages.Count;
        var languages = ContentLanguages.All.Select(lang =>
        {
            var ed = editions.FirstOrDefault(e => e.LanguageCode == lang);
            var translated = ed is not null && ed.Status != EditionStatus.Draft ? pageCount : 0;
            return new ChapterLanguageStatusDto(
                lang, translated, ed?.Status == EditionStatus.Published, ed?.Status.ToString(),
                ed?.IsMonetizationConfigured ?? false,
                ed?.IsPaid ?? false,
                ed?.PriceVnd ?? 0m,
                ed?.FreePreviewPages ?? 0);
        }).ToList();

        var pages = orderedPages.Select(p => new ManagedPageDto(
            p.Id, p.PageNumber,
            ContentLanguages.All.ToDictionary(
                lang => lang,
                lang => withArtwork.Contains(p.Id) ? PreviewImageUrl(chapterId, p.Id, lang) : null))).ToList();

        return Result<ChapterManageDto>.Success(new ChapterManageDto(
            chapter.Id, chapter.SeriesId, chapter.ChapterNumber, chapter.Title, chapter.IsPublished,
            languages, pages));
    }
}

// ─── Command: Publisher quy định thương mại của một ấn bản ngôn ngữ ───────────

/// <summary>
/// BR-33 / D4 (§6.1.5 <c>SetEditionMonetization</c>): Publisher đặt loại Free/Trả phí, <c>price_vnd</c> và
/// <c>free_preview_pages</c> cho ấn bản (chương, ngôn ngữ). Được phép ở mọi trạng thái ấn bản; ghi Audit.
/// Trả phí: giá &gt; 0 và N &lt; tổng số trang. Free: giá 0. Đổi loại hoặc giá của ấn bản đã phát hành cần lý do.
/// Không ảnh hưởng người đã mở khóa (quyền đọc lưu riêng ở entitlement).
/// </summary>
public sealed record SetEditionMonetizationCommand(
    Guid ChapterId, string Language, bool IsPaid, decimal PriceVnd, int FreePreviewPages, string? Reason, Guid UserId)
    : IRequest<Result<ChapterManageDto>>;

public sealed class SetEditionMonetizationCommandHandler
    : IRequestHandler<SetEditionMonetizationCommand, Result<ChapterManageDto>>
{
    public const string AuditAction = "SET_EDITION_MONETIZATION";
    public const decimal MinPriceVnd = 1000m;
    public const decimal MaxPriceVnd = 1_000_000m;

    private readonly IPanelForgeDbContext _db;

    public SetEditionMonetizationCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterManageDto>> Handle(SetEditionMonetizationCommand cmd, CancellationToken ct)
    {
        var language = ContentLanguages.Normalize(cmd.Language);
        if (language is null)
            return Invalid($"Ngôn ngữ '{cmd.Language}' không được hỗ trợ.");

        var chapter = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == cmd.ChapterId)
            .Select(c => new
            {
                c.Id, c.ChapterNumber, c.Series.WorkspaceId,
                c.Series.OriginalLanguage, c.Series.ReleaseLanguages
            })
            .FirstOrDefaultAsync(ct);
        if (chapter is null)
            return Result<ChapterManageDto>.Failure("Không tìm thấy chương.", ResultErrorCodes.NotFound);

        if (language != chapter.OriginalLanguage && !chapter.ReleaseLanguages.Contains(language))
            return Invalid($"Ngôn ngữ '{language}' không nằm trong ngôn ngữ phát hành của bộ truyện.");

        var pageCount = await _db.Pages.CountAsync(p => p.ChapterId == chapter.Id, ct);
        var freePreviewPages = cmd.IsPaid ? cmd.FreePreviewPages : 0;
        if (cmd.IsPaid)
        {
            if (cmd.PriceVnd != decimal.Truncate(cmd.PriceVnd))
                return Invalid("Giá VNĐ phải là số nguyên đồng.");
            if (cmd.PriceVnd is < MinPriceVnd or > MaxPriceVnd)
                return Invalid($"Giá ấn bản trả phí phải từ {MinPriceVnd:N0}đ đến {MaxPriceVnd:N0}đ.");
            if (freePreviewPages < 0)
                return Invalid("Số trang xem trước không được âm.");
            if (freePreviewPages >= pageCount)
                return Invalid(pageCount == 0
                    ? "Chương chưa có trang nên chưa thể đặt Trả phí."
                    : $"Số trang xem trước phải nhỏ hơn tổng số trang của chương ({pageCount}) để còn ít nhất một trang khóa.");
        }
        else if (cmd.PriceVnd != 0)
        {
            return Invalid("Ấn bản Free có giá 0đ.");
        }

        var edition = await _db.ChapterEditions
            .FirstOrDefaultAsync(e => e.ChapterId == chapter.Id && e.LanguageCode == language && !e.IsDeleted, ct);
        if (edition is null)
        {
            edition = ChapterEdition.Create(chapter.Id, language, EditionStatus.Draft);
            _db.ChapterEditions.Add(edition);
        }

        var wasConfigured = edition.IsMonetizationConfigured;
        var before = new { isPaid = edition.IsPaid, priceVnd = edition.PriceVnd, freePreviewPages = edition.FreePreviewPages };
        if (wasConfigured && before.isPaid == cmd.IsPaid && before.priceVnd == cmd.PriceVnd
            && before.freePreviewPages == freePreviewPages)
            return await ChapterManageReader.ReadAsync(_db, chapter.Id, ct);

        var reason = cmd.Reason?.Trim();
        var commercialChange = wasConfigured && (before.isPaid != cmd.IsPaid || before.priceVnd != cmd.PriceVnd);
        if (edition.Status == EditionStatus.Published && commercialChange && string.IsNullOrEmpty(reason))
            return Invalid("Ấn bản đã phát hành: cần nhập lý do khi đổi loại Free/Trả phí hoặc giá.");

        edition.SetMonetization(cmd.IsPaid, cmd.PriceVnd, freePreviewPages, cmd.UserId);

        _db.AuditLogs.Add(AuditLog.Create(
            action: AuditAction,
            entityName: nameof(ChapterEdition),
            entityId: edition.Id.ToString(),
            workspaceId: chapter.WorkspaceId,
            userId: cmd.UserId,
            changesJson: JsonSerializer.Serialize(new
            {
                language,
                from = wasConfigured ? before : null,
                to = new { isPaid = cmd.IsPaid, priceVnd = edition.PriceVnd, freePreviewPages = edition.FreePreviewPages }
            }),
            details: $"Chương {chapter.ChapterNumber} [{language}]: "
                     + (cmd.IsPaid ? $"Trả phí {edition.PriceVnd:N0}đ, xem trước {edition.FreePreviewPages} trang" : "Free")
                     + (string.IsNullOrEmpty(reason) ? "" : $". Lý do: {reason}")));

        await _db.SaveChangesAsync(ct);
        return await ChapterManageReader.ReadAsync(_db, chapter.Id, ct);
    }

    private static Result<ChapterManageDto> Invalid(string message) =>
        Result<ChapterManageDto>.Failure(message, ResultErrorCodes.Validation);
}

// ─── Query: ảnh xem trước cho Publisher (không cần chương đã phát hành) ──────

public sealed record GetPublisherPageImageQuery(Guid ChapterId, Guid PageId, string? Language)
    : IRequest<Result<Assets.Models.AssetDownloadDto>>;

public sealed class GetPublisherPageImageQueryHandler
    : IRequestHandler<GetPublisherPageImageQuery, Result<Assets.Models.AssetDownloadDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAssetStorage _storage;
    private readonly IPageRepository _pages;
    private readonly IPageLetteringCompositor _compositor;

    public GetPublisherPageImageQueryHandler(
        IPanelForgeDbContext db, IAssetStorage storage, IPageRepository pages, IPageLetteringCompositor compositor)
    {
        _db = db;
        _storage = storage;
        _pages = pages;
        _compositor = compositor;
    }

    public async Task<Result<Assets.Models.AssetDownloadDto>> Handle(GetPublisherPageImageQuery q, CancellationToken ct)
    {
        var isHandedOff = await _db.Chapters.AsNoTracking()
            .AnyAsync(c => c.Id == q.ChapterId
                           && c.IsHandedOff
                           && (c.Status == ChapterStatus.ChapterApproved
                               || c.Status == ChapterStatus.HandedOffAndPublished), ct);
        if (!isHandedOff)
            return Result<Assets.Models.AssetDownloadDto>.Failure(
                "Không tìm thấy chương đã bàn giao.", ResultErrorCodes.NotFound);

        var language = ContentLanguages.Normalize(q.Language);
        var image = language is null
            ? null
            : await GetPublicPageImageQueryHandler.LoadImageAsync(_db, _storage, _pages, _compositor, q.ChapterId, q.PageId, language, ct);
        return image is null
            ? Result<Assets.Models.AssetDownloadDto>.Failure("Trang chưa có ảnh cho ngôn ngữ này.", ResultErrorCodes.NotFound)
            : Result<Assets.Models.AssetDownloadDto>.Success(image);
    }
}
