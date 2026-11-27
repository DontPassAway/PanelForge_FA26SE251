using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.PageComments.Models;
using PanelForge.Domain.Entities.Preview;

namespace PanelForge.Application.Features.PageComments.Common;

public static class PageCommentMapper
{
    public static async Task<List<PageCommentDto>> ToDtosAsync(
        IPanelForgeDbContext db,
        IReadOnlyCollection<PageComment> items,
        CancellationToken ct)
    {
        if (items.Count == 0) return [];

        var chapterIds = items.Select(c => c.ChapterId).Distinct().ToList();
        var pageIds = items.Where(c => c.PageId.HasValue).Select(c => c.PageId!.Value).Distinct().ToList();
        var panelIds = items.Where(c => c.PanelId.HasValue).Select(c => c.PanelId!.Value).Distinct().ToList();
        var userIds = items.Select(c => c.AuthorUserId)
            .Concat(items.Where(c => c.ResolvedByUserId.HasValue).Select(c => c.ResolvedByUserId!.Value))
            .Distinct()
            .ToList();
        var stageTaskIds = items.Where(c => c.StageTaskId.HasValue).Select(c => c.StageTaskId!.Value).Distinct().ToList();

        var chapters = await db.Chapters.IgnoreQueryFilters().AsNoTracking()
            .Where(c => chapterIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.ChapterNumber, c.Title), ct);

        var pages = pageIds.Count > 0
            ? await db.Pages.IgnoreQueryFilters().AsNoTracking()
                .Where(p => pageIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.PageNumber, ct)
            : new Dictionary<Guid, int>();

        var panels = panelIds.Count > 0
            ? await db.Panels.IgnoreQueryFilters().AsNoTracking()
                .Where(p => panelIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.PanelNumber, ct)
            : new Dictionary<Guid, int>();

        var users = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName, ct);

        var stageNameByTaskId = new Dictionary<Guid, string>();
        if (stageTaskIds.Count > 0)
        {
            var taskStages = await db.StageTasks.AsNoTracking()
                .Where(t => stageTaskIds.Contains(t.Id))
                .Select(t => new { t.Id, t.PipelineStageId })
                .ToListAsync(ct);

            var stageIds = taskStages.Select(t => t.PipelineStageId).Distinct().ToList();
            if (stageIds.Count > 0)
            {
                var stages = await db.PipelineStages.AsNoTracking()
                    .Where(s => stageIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

                foreach (var ts in taskStages)
                {
                    if (stages.TryGetValue(ts.PipelineStageId, out var stageName))
                        stageNameByTaskId[ts.Id] = stageName;
                }
            }
        }

        return items.Select(c =>
        {
            chapters.TryGetValue(c.ChapterId, out var ch);
            return new PageCommentDto(
                c.Id,
                c.SeriesId,
                c.ChapterId,
                ch.ChapterNumber,
                ch.Title,
                c.PageId,
                c.PageId is { } pg && pages.TryGetValue(pg, out var pageNo) ? pageNo : null,
                c.PanelId,
                c.PanelId is { } pl && panels.TryGetValue(pl, out var panelNo) ? panelNo : null,
                c.AssetReferenceId,
                c.PinX,
                c.PinY,
                c.Message,
                c.AuthorUserId,
                users.TryGetValue(c.AuthorUserId, out var authorName) ? authorName : string.Empty,
                c.Status,
                c.ResolutionNote,
                c.ResolvedByUserId,
                c.ResolvedByUserId is { } rId && users.TryGetValue(rId, out var resolvedByName) ? resolvedByName : null,
                c.ResolvedAt,
                c.StageTaskId,
                c.StageTaskId is { } tId && stageNameByTaskId.TryGetValue(tId, out var stageName) ? stageName : null,
                c.CreatedAt,
                c.UpdatedAt);
        }).ToList();
    }

    public static async Task<PageCommentDto> ToDtoAsync(
        IPanelForgeDbContext db,
        PageComment item,
        CancellationToken ct) =>
        (await ToDtosAsync(db, [item], ct))[0];
}
