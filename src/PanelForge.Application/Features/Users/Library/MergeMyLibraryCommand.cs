using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Users.Library;

/// <summary>
/// Gộp dữ liệu đang có trên thiết bị vào thư viện trên server (gọi khi đăng nhập / "Đồng bộ ngay"), rồi trả về
/// thư viện đầy đủ để thiết bị ghi đè bản cục bộ. Danh sách: hợp của hai bên; Theo dõi: giữ mốc đã xem mới hơn;
/// Lịch sử: bản đọc sau cùng thắng. Bộ truyện không còn phát hành / chương không thuộc bộ thì bỏ qua.
/// </summary>
public sealed record MergeMyLibraryCommand(Guid UserId, MergeReaderLibraryRequest Local) : IRequest<Result<ReaderLibraryDto>>;

public sealed class MergeMyLibraryCommandHandler : IRequestHandler<MergeMyLibraryCommand, Result<ReaderLibraryDto>>
{
    /// <summary>Giới hạn mỗi danh sách trong một lần gộp để chặn payload bất thường.</summary>
    public const int MaxItemsPerList = 500;

    private readonly IPanelForgeDbContext _db;

    public MergeMyLibraryCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ReaderLibraryDto>> Handle(MergeMyLibraryCommand cmd, CancellationToken ct)
    {
        var local = cmd.Local;
        var readLater = (local.ReadLater ?? []).Distinct().ToList();
        var following = (local.Following ?? []).GroupBy(f => f.SeriesId)
            .Select(g => new FollowedSeriesDto(g.Key, g.Max(f => f.LastSeenAt))).ToList();
        var favorites = (local.Favorites ?? []).Distinct().ToList();
        var history = (local.History ?? []).GroupBy(h => h.SeriesId).Select(g => g.MaxBy(h => h.ReadAt)!).ToList();

        if (readLater.Count > MaxItemsPerList || following.Count > MaxItemsPerList ||
            favorites.Count > MaxItemsPerList || history.Count > MaxItemsPerList)
            return Result<ReaderLibraryDto>.Failure($"Mỗi danh sách tối đa {MaxItemsPerList} mục.", ResultErrorCodes.Validation);

        var published = await ReaderLibrary.PublishedSeriesAsync(
            _db, readLater.Concat(following.Select(f => f.SeriesId)).Concat(favorites).Concat(history.Select(h => h.SeriesId)), ct);

        var existing = await _db.ReaderLibraryItems.Where(i => i.UserId == cmd.UserId).ToListAsync(ct);

        void Upsert(ReaderLibraryList list, Guid seriesId, DateTime? lastSeenAt)
        {
            if (!published.Contains(seriesId)) return;
            var item = existing.FirstOrDefault(i => i.List == list && i.SeriesId == seriesId);
            if (item is not null)
            {
                item.MarkSeen(lastSeenAt);
                return;
            }
            item = ReaderLibraryItem.Create(cmd.UserId, seriesId, list, lastSeenAt);
            existing.Add(item);
            _db.ReaderLibraryItems.Add(item);
        }

        foreach (var id in readLater) Upsert(ReaderLibraryList.ReadLater, id, null);
        foreach (var f in following) Upsert(ReaderLibraryList.Following, f.SeriesId, f.LastSeenAt);
        foreach (var id in favorites) Upsert(ReaderLibraryList.Favorite, id, null);

        if (history.Count > 0)
        {
            var now = DateTime.UtcNow;
            var chapterIds = history.Select(h => h.ChapterId).Distinct().ToList();
            var validChapters = await _db.Chapters.AsNoTracking()
                .Where(c => chapterIds.Contains(c.Id) && c.Status == ChapterStatus.HandedOffAndPublished && c.PublishedAt <= now)
                .Select(c => new { c.Id, c.SeriesId })
                .ToListAsync(ct);
            foreach (var h in history)
            {
                if (h.LastPageNumber < 1 || ContentLanguages.Normalize(h.Language ?? ContentLanguages.Original) is null) continue;
                if (!validChapters.Any(c => c.Id == h.ChapterId && c.SeriesId == h.SeriesId)) continue;
                await SaveReadingProgressCommandHandler.ApplyAsync(_db, cmd.UserId, h, now, ct);
            }
        }

        await _db.SaveChangesAsync(ct);
        return Result<ReaderLibraryDto>.Success(await ReaderLibrary.LoadAsync(_db, cmd.UserId, ct));
    }
}
