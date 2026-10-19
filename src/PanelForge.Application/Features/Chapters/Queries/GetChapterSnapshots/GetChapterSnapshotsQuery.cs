using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.Chapters.Models;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterSnapshots;

/// <summary>
/// CF4 / 4.1: danh sách snapshot của chương (mỗi lần Producer duyệt một bản), mới nhất trước.
/// Quyền thành viên Workspace kiểm ở controller qua {chapterId} (BR-07).
/// </summary>
public sealed record GetChapterSnapshotsQuery(Guid ChapterId) : IRequest<Result<IReadOnlyList<ChapterSnapshotDto>>>;

public sealed class GetChapterSnapshotsQueryHandler
    : IRequestHandler<GetChapterSnapshotsQuery, Result<IReadOnlyList<ChapterSnapshotDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetChapterSnapshotsQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<ChapterSnapshotDto>>> Handle(GetChapterSnapshotsQuery q, CancellationToken ct)
    {
        if (!await _db.Chapters.AnyAsync(c => c.Id == q.ChapterId, ct))
            return Result<IReadOnlyList<ChapterSnapshotDto>>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var rows = await _db.ChapterSnapshots.AsNoTracking()
            .Where(s => s.ChapterId == q.ChapterId)
            .OrderByDescending(s => s.SnapshotNumber)
            .ToListAsync(ct);

        var userIds = rows.Select(s => s.CreatedByUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var latest = rows.FirstOrDefault()?.Id;

        return Result<IReadOnlyList<ChapterSnapshotDto>>.Success(rows
            .Select(s => ChapterSnapshotMapping.ToDto(s, names.GetValueOrDefault(s.CreatedByUserId), s.Id == latest))
            .ToList());
    }
}
