using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Progress.Queries.GetStageProgressByChapter;

/// <summary>Một Panel ở một Stage. Panel chưa có dòng nào ở stage đó = NotStarted (không được trả về).</summary>
public sealed record StageProgressDto(
    Guid Id,
    Guid PageId,
    Guid PanelId,
    Guid PipelineStageId,
    StageProgressStatus Status,
    DateTime? SubmittedAt,
    Guid? SubmittedByUserId,
    DateTime? UpdatedAt);

/// <summary>Tiến độ theo Panel của cả Chapter (nguồn cho dashboard UC-16). Lọc tuỳ chọn theo Page.</summary>
public sealed record GetStageProgressByChapterQuery(Guid ChapterId, Guid? PageId = null)
    : IRequest<Result<IReadOnlyList<StageProgressDto>>>;

public sealed class GetStageProgressByChapterQueryHandler
    : IRequestHandler<GetStageProgressByChapterQuery, Result<IReadOnlyList<StageProgressDto>>>
{
    private readonly IPanelForgeDbContext _db;

    public GetStageProgressByChapterQueryHandler(IPanelForgeDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<StageProgressDto>>> Handle(GetStageProgressByChapterQuery q, CancellationToken ct)
    {
        if (!await _db.Chapters.AsNoTracking().AnyAsync(c => c.Id == q.ChapterId, ct))
            return Result<IReadOnlyList<StageProgressDto>>.Failure($"Chapter {q.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);

        var pageIds = await _db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == q.ChapterId && (q.PageId == null || p.Id == q.PageId))
            .Select(p => p.Id).ToListAsync(ct);

        var panelPage = (await _db.Panels.AsNoTracking()
                .Where(p => pageIds.Contains(p.PageId))
                .Select(p => new { p.Id, p.PageId }).ToListAsync(ct))
            .ToDictionary(p => p.Id, p => p.PageId);

        var panelIds = panelPage.Keys.ToList();
        var rows = await _db.StageProgresses.AsNoTracking()
            .Where(s => panelIds.Contains(s.PanelId)).ToListAsync(ct);

        var items = rows
            .Select(s => new StageProgressDto(s.Id, panelPage[s.PanelId], s.PanelId, s.PipelineStageId,
                s.Status, s.SubmittedAt, s.SubmittedByUserId, s.UpdatedAt))
            .OrderBy(s => s.PageId).ThenBy(s => s.PanelId)
            .ToList();

        return Result<IReadOnlyList<StageProgressDto>>.Success(items);
    }
}
