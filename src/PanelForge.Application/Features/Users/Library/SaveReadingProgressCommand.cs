using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Users.Library;

/// <summary>Ghi tiến độ đọc một bộ truyện; bản có <c>ReadAt</c> mới hơn thắng (đọc trên nhiều thiết bị).</summary>
public sealed record SaveReadingProgressCommand(Guid UserId, ReadingProgressDto Progress) : IRequest<Result<ReadingProgressDto>>;

public sealed class SaveReadingProgressCommandHandler : IRequestHandler<SaveReadingProgressCommand, Result<ReadingProgressDto>>
{
    private readonly IPanelForgeDbContext _db;

    public SaveReadingProgressCommandHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ReadingProgressDto>> Handle(SaveReadingProgressCommand cmd, CancellationToken ct)
    {
        var p = cmd.Progress;
        if (p.Language is not null && ContentLanguages.Normalize(p.Language) is null)
            return Result<ReadingProgressDto>.Failure("Ngôn ngữ không được hỗ trợ.", ResultErrorCodes.Validation);
        if (p.LastPageNumber < 1 || p.TotalPages < 0)
            return Result<ReadingProgressDto>.Failure("Số trang không hợp lệ.", ResultErrorCodes.Validation);

        var now = DateTime.UtcNow;
        // Chỉ chương đã phát hành của đúng bộ truyện đó.
        var chapterOk = await _db.Chapters.AsNoTracking().AnyAsync(c =>
            c.Id == p.ChapterId && c.SeriesId == p.SeriesId &&
            c.Status == ChapterStatus.HandedOffAndPublished && c.PublishedAt <= now, ct);
        if (!chapterOk)
            return Result<ReadingProgressDto>.Failure("Không tìm thấy chương đã phát hành.", ResultErrorCodes.NotFound);

        var row = await ApplyAsync(_db, cmd.UserId, p, now, ct);
        await _db.SaveChangesAsync(ct);
        return Result<ReadingProgressDto>.Success(new ReadingProgressDto(
            row.SeriesId, row.ChapterId, row.ChapterNumber, row.LastPageNumber, row.TotalPages, row.Language, row.ReadAt));
    }

    /// <summary>Upsert một dòng tiến độ (chưa SaveChanges). Không chấp nhận thời điểm ở tương lai.</summary>
    internal static async Task<ReaderReadingProgress> ApplyAsync(
        IPanelForgeDbContext db, Guid userId, ReadingProgressDto p, DateTime now, CancellationToken ct)
    {
        var row = db.ReaderReadingProgress.Local.FirstOrDefault(r => r.UserId == userId && r.SeriesId == p.SeriesId && !r.IsDeleted)
            ?? await db.ReaderReadingProgress.FirstOrDefaultAsync(r => r.UserId == userId && r.SeriesId == p.SeriesId, ct);
        if (row is null)
        {
            row = ReaderReadingProgress.Create(userId, p.SeriesId);
            db.ReaderReadingProgress.Add(row);
        }
        var readAt = p.ReadAt.Kind == DateTimeKind.Utc ? p.ReadAt : p.ReadAt.ToUniversalTime();
        row.Apply(p.ChapterId, p.ChapterNumber, p.LastPageNumber, p.TotalPages, p.Language, readAt > now ? now : readAt);
        return row;
    }
}
