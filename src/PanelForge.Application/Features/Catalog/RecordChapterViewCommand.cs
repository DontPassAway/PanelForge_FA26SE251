using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Catalog;

/// <summary>
/// Reader mở xong nội dung một chương → ghi một lượt đọc. Chỉ tính chương đã phát hành mà người xem có quyền đọc
/// (không tính lượt bị paywall chặn); cùng người xem đọc lại trong <see cref="ChapterView.DedupWindow"/> không tính thêm.
/// Trả <c>true</c> nếu lượt được ghi.
/// </summary>
public sealed record RecordChapterViewCommand(Guid ChapterId, string? IpAddress) : IRequest<Result<bool>>;

public sealed class RecordChapterViewCommandHandler : IRequestHandler<RecordChapterViewCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public RecordChapterViewCommandHandler(IPanelForgeDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<bool>> Handle(RecordChapterViewCommand cmd, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var chapter = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == cmd.ChapterId && c.Status == ChapterStatus.Published && c.PublishedAt <= now)
            .Select(c => new { c.Id, c.SeriesId, c.IsLocked, c.Series.WorkspaceId })
            .FirstOrDefaultAsync(ct);
        if (chapter is null)
            return Result<bool>.Failure("Không tìm thấy chương đã phát hành.", ResultErrorCodes.NotFound);

        var userId = _currentUser.UserId;
        if (!await ChapterReadAccess.CanReadAsync(_db, userId, chapter.Id, chapter.WorkspaceId, chapter.IsLocked, ct))
            return Result<bool>.Failure(ChapterReadAccess.LockedMessage, ResultErrorCodes.Forbidden);

        var viewerKey = ViewerKey(userId, cmd.IpAddress);
        if (viewerKey is null) return Result<bool>.Success(false);

        var since = now - ChapterView.DedupWindow;
        var recent = await _db.ChapterViews.AnyAsync(
            v => v.ChapterId == chapter.Id && v.ViewerKey == viewerKey && v.ViewedAt >= since, ct);
        if (recent) return Result<bool>.Success(false);

        _db.ChapterViews.Add(ChapterView.Create(chapter.Id, chapter.SeriesId, viewerKey, now));
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>"u:{userId}" hoặc "a:{sha256(ip)}" — không lưu IP thô.</summary>
    internal static string? ViewerKey(Guid? userId, string? ip)
    {
        if (userId is { } id) return $"u:{id:N}";
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ip.Trim()));
        return $"a:{Convert.ToHexString(hash)[..40]}";
    }
}
