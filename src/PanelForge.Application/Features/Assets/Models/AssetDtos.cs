using PanelForge.Domain.Entities.Assets;

namespace PanelForge.Application.Features.Assets.Models;

/// <summary>
/// Một phiên bản artwork của Page ở một Stage. <see cref="Reused"/> chỉ có nghĩa ở response upload:
/// true = nội dung đã có trong Workspace nên không lưu thêm byte nào (BR-13).
/// </summary>
public sealed record AssetReferenceDto(
    Guid Id,
    Guid AssetId,
    Guid ChapterId,
    Guid PageId,
    Guid PipelineStageId,
    int VersionNumber,
    string? LayerType,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    bool Reused,
    Guid UploadedByUserId,
    DateTime CreatedAt)
{
    public static AssetReferenceDto From(AssetReference r, Asset a, bool reused = false) => new(
        r.Id, r.AssetId, r.ChapterId, r.PageId, r.PipelineStageId, r.VersionNumber, r.LayerType, r.FileName,
        a.ContentType, a.SizeBytes, a.Sha256, reused, r.UploadedByUserId, r.CreatedAt);
}

/// <summary>Link tải ngắn hạn: <c>Url</c> là đường dẫn tương đối của API, hết hạn lúc <c>ExpiresAtUtc</c> (BR-16).</summary>
public sealed record ArtworkAccessLinkDto(string Url, DateTime ExpiresAtUtc);

/// <summary>Nội dung để trả về cho client; người nhận dispose <see cref="Content"/>.</summary>
public sealed record AssetDownloadDto(Stream Content, string ContentType, string FileName);
