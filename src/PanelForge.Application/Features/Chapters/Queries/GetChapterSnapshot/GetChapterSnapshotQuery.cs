using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.Chapters.Models;

namespace PanelForge.Application.Features.Chapters.Queries.GetChapterSnapshot;

/// <summary>
/// CF4 / 4.1: chi tiết một snapshot kèm manifest và kết quả kiểm hash (manifest không bị sửa sau khi tạo).
/// Snapshot phải thuộc Chapter trên route (chặn IDOR). Quyền thành viên Workspace kiểm ở controller (BR-07).
/// </summary>
public sealed record GetChapterSnapshotQuery(Guid ChapterId, Guid SnapshotId) : IRequest<Result<ChapterSnapshotDetailDto>>;

public sealed class GetChapterSnapshotQueryHandler : IRequestHandler<GetChapterSnapshotQuery, Result<ChapterSnapshotDetailDto>>
{
    private readonly IPanelForgeDbContext _db;

    public GetChapterSnapshotQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<ChapterSnapshotDetailDto>> Handle(GetChapterSnapshotQuery q, CancellationToken ct)
    {
        var snapshot = await _db.ChapterSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == q.SnapshotId && s.ChapterId == q.ChapterId, ct);
        if (snapshot is null)
            return Result<ChapterSnapshotDetailDto>.Failure(
                $"Snapshot {q.SnapshotId} không tồn tại trong Chapter này.", ResultErrorCodes.NotFound);

        var latestNumber = await _db.ChapterSnapshots.AsNoTracking()
            .Where(s => s.ChapterId == q.ChapterId).MaxAsync(s => s.SnapshotNumber, ct);
        var createdBy = await _db.Users.AsNoTracking()
            .Where(u => u.Id == snapshot.CreatedByUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);

        using var manifest = JsonDocument.Parse(snapshot.ManifestJson);
        return Result<ChapterSnapshotDetailDto>.Success(new ChapterSnapshotDetailDto(
            ChapterSnapshotMapping.ToDto(snapshot, createdBy, snapshot.SnapshotNumber == latestNumber),
            manifest.RootElement.Clone(),
            AiContentValidator.Sha256(snapshot.ManifestJson) == snapshot.ManifestHash));
    }
}
