using PanelForge.Domain.Entities.Preview;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.PreviewLinks.Models;

/// <summary>Kết quả tạo link: <see cref="Token"/> chỉ trả MỘT lần (server chỉ lưu hash). FE ghép link: {origin}/preview/{token}.</summary>
public sealed record CreatedPreviewLinkDto(Guid Id, string RecipientEmail, DateTime ExpiresAt, string Token);

/// <summary>Status: Active | Expired | Revoked (tính lúc đọc).</summary>
public sealed record PreviewLinkDto(
    Guid Id,
    Guid ChapterId,
    string RecipientEmail,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? RevokedAt,
    string? RevokeReason,
    long ChapterVersion,
    int PageCount,
    int AccessCount,
    int DeniedCount,
    DateTime? LastAccessAt,
    int FeedbackCount);

public sealed record PreviewPanelDto(Guid PanelId, int PanelNumber, string BoundingBox);

/// <summary>ImageUrl: đường dẫn tương đối của API, link ảnh ngắn hạn (BR-16), xin lại bằng cách mở lại preview.</summary>
public sealed record PreviewPageDto(
    Guid PageId, int PageNumber, Guid AssetReferenceId, int VersionNumber, string ImageUrl, IReadOnlyList<PreviewPanelDto> Panels);

/// <summary>Nội dung người nhận thấy qua link. WatermarkText phải hiển thị đè lên mọi trang (BR-17).</summary>
public sealed record PreviewChapterDto(
    Guid ChapterId,
    string SeriesTitle,
    decimal ChapterNumber,
    string? ChapterTitle,
    ReadingDirection ReadingDirection,
    string RecipientEmail,
    string WatermarkText,
    DateTime ExpiresAt,
    long ChapterVersion,
    IReadOnlyList<PreviewPageDto> Pages);

public sealed record PreviewFeedbackDto(
    Guid Id,
    Guid PreviewLinkId,
    Guid ChapterId,
    Guid? PageId,
    Guid? PanelId,
    Guid? AssetReferenceId,
    long ChapterVersion,
    string RecipientEmail,
    string Message,
    PreviewFeedbackStatus Status,
    string? TriageNote,
    Guid? TriagedByUserId,
    DateTime? TriagedAt,
    DateTime CreatedAt)
{
    public static PreviewFeedbackDto From(PreviewFeedback f) => new(
        f.Id, f.PreviewLinkId, f.ChapterId, f.PageId, f.PanelId, f.AssetReferenceId, f.ChapterVersion, f.RecipientEmail,
        f.Message, f.Status, f.TriageNote, f.TriagedByUserId, f.TriagedAt, f.CreatedAt);
}

public sealed record CreatePreviewLinkRequest(string RecipientEmail, int? ExpiresInHours = null);
public sealed record PreviewTokenRequest(string Token);
public sealed record SubmitPreviewFeedbackRequest(string Token, string Message, Guid? PageId = null, Guid? PanelId = null);
public sealed record TriagePreviewFeedbackRequest(PreviewFeedbackStatus Decision, string? Note);
