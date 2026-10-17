using System.Text.Json;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.Chapters.Models;

/// <param name="IsLatest">Snapshot mới nhất của chương = bản duyệt hiện hành (xem thử, xuất file, phát hành trỏ vào đây).</param>
public sealed record ChapterSnapshotDto(
    Guid Id, Guid ChapterId, int SnapshotNumber, long ChapterVersion, Guid CreatedByUserId, string? CreatedByName,
    string ManifestHash, bool IsLatest, DateTime CreatedAt);

/// <param name="Manifest">Manifest bất biến lúc duyệt: trang, artwork + SHA-256, version canvas, revision script/Bible.</param>
/// <param name="HashVerified">SHA-256 của manifest đang lưu vẫn khớp ManifestHash (không bị sửa sau khi tạo).</param>
public sealed record ChapterSnapshotDetailDto(ChapterSnapshotDto Snapshot, JsonElement Manifest, bool HashVerified);

internal static class ChapterSnapshotMapping
{
    public static ChapterSnapshotDto ToDto(ChapterSnapshot s, string? createdByName, bool isLatest) => new(
        s.Id, s.ChapterId, s.SnapshotNumber, s.ChapterVersion, s.CreatedByUserId, createdByName, s.ManifestHash, isLatest, s.CreatedAt);
}
