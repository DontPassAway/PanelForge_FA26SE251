using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Users.Library;

/// <summary>Bộ truyện đang theo dõi kèm mốc đã xem chương mới.</summary>
public sealed record FollowedSeriesDto(Guid SeriesId, DateTime? LastSeenAt);

/// <summary>Tiến độ đọc của một bộ truyện (chương + trang đang đọc dở).</summary>
public sealed record ReadingProgressDto(
    Guid SeriesId,
    Guid ChapterId,
    decimal ChapterNumber,
    int LastPageNumber,
    int TotalPages,
    string? Language,
    DateTime ReadAt);

/// <summary>Toàn bộ thư viện cá nhân của độc giả: Đọc sau, Theo dõi, Yêu thích và Lịch sử đọc.</summary>
public sealed record ReaderLibraryDto(
    IReadOnlyList<Guid> ReadLater,
    IReadOnlyList<FollowedSeriesDto> Following,
    IReadOnlyList<Guid> Favorites,
    IReadOnlyList<ReadingProgressDto> History);

/// <summary>Dữ liệu đang lưu trên thiết bị, gộp vào thư viện trên server khi đăng nhập / đồng bộ.</summary>
public sealed record MergeReaderLibraryRequest(
    IReadOnlyList<Guid>? ReadLater,
    IReadOnlyList<FollowedSeriesDto>? Following,
    IReadOnlyList<Guid>? Favorites,
    IReadOnlyList<ReadingProgressDto>? History);

public sealed record MarkFollowSeenRequest(DateTime? LastSeenAt);

internal static class ReaderLibrary
{
    /// <summary>Lịch sử đọc giữ tối đa 100 bộ gần nhất cho mỗi độc giả.</summary>
    public const int MaxHistory = 100;

    /// <summary>Bộ truyện độc giả thấy được: còn tồn tại và có ít nhất một chương đã phát hành (BR-23).</summary>
    public static async Task<HashSet<Guid>> PublishedSeriesAsync(IPanelForgeDbContext db, IEnumerable<Guid> seriesIds, CancellationToken ct)
    {
        var ids = seriesIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var now = DateTime.UtcNow;
        var found = await db.Chapters.AsNoTracking()
            .Where(c => ids.Contains(c.SeriesId) && c.Status == ChapterStatus.HandedOffAndPublished && c.PublishedAt <= now)
            .Select(c => c.SeriesId)
            .Distinct()
            .ToListAsync(ct);
        return [.. found];
    }

    public static async Task<ReaderLibraryDto> LoadAsync(IPanelForgeDbContext db, Guid userId, CancellationToken ct)
    {
        var items = await db.ReaderLibraryItems.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new { i.SeriesId, i.List, i.LastSeenAt })
            .ToListAsync(ct);
        var history = await db.ReaderReadingProgress.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.ReadAt)
            .Take(MaxHistory)
            .Select(p => new ReadingProgressDto(p.SeriesId, p.ChapterId, p.ChapterNumber, p.LastPageNumber, p.TotalPages, p.Language, p.ReadAt))
            .ToListAsync(ct);

        return new ReaderLibraryDto(
            items.Where(i => i.List == ReaderLibraryList.ReadLater).Select(i => i.SeriesId).ToList(),
            items.Where(i => i.List == ReaderLibraryList.Following).Select(i => new FollowedSeriesDto(i.SeriesId, i.LastSeenAt)).ToList(),
            items.Where(i => i.List == ReaderLibraryList.Favorite).Select(i => i.SeriesId).ToList(),
            history);
    }
}
