using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Common;

public sealed record PanelCastRevisionItem(Guid BibleEntryId, Guid RevisionId);

/// <summary>Khung Trang → Panel của kịch bản: tạo, xoá mềm, hướng panel, phiên bản Bible của panel. Dùng cho lưu kịch bản cả chương.</summary>
internal static class ScriptStructureRules
{
    /// <summary>Revision của cast phải tồn tại, đúng mục Bible, thuộc Series của chương và chưa Superseded (BIB-11, Q23, Q24). Trả lỗi hoặc null.</summary>
    public static async Task<string?> ValidateCastRevisionsAsync(
        IPanelForgeDbContext db, Guid chapterId, IReadOnlyList<PanelCastRevisionItem> castRevisions, CancellationToken ct)
    {
        if (castRevisions.Count == 0) return null;

        var chapterSeriesId = await db.Chapters.Where(c => c.Id == chapterId).Select(c => c.SeriesId).FirstOrDefaultAsync(ct);
        var seriesBible = await db.SeriesBibles.FirstOrDefaultAsync(sb => sb.SeriesId == chapterSeriesId, ct);
        var seriesBibleId = seriesBible?.Id ?? Guid.Empty;

        var revisionIds = castRevisions.Select(cr => cr.RevisionId).Distinct().ToList();
        var revisionMap = (await db.BibleEntryRevisions
                .Include(r => r.BibleEntry)
                .Where(r => revisionIds.Contains(r.Id))
                .ToListAsync(ct))
            .ToDictionary(r => r.Id);

        foreach (var cr in castRevisions)
        {
            if (!revisionMap.TryGetValue(cr.RevisionId, out var rev))
                return $"Revision '{cr.RevisionId}' không tồn tại.";
            if (rev.BibleEntryId != cr.BibleEntryId)
                return $"Revision '{cr.RevisionId}' không thuộc mục Bible '{cr.BibleEntryId}'.";
            if (rev.BibleEntry.SeriesBibleId != seriesBibleId)
                return $"Mục Bible '{cr.BibleEntryId}' không thuộc Series này.";
            if (rev.IsSuperseded)
                return $"Revision '{cr.RevisionId}' đã bị Superseded.";
        }
        return null;
    }

    public static async Task<(Page Page, bool Created)> EnsurePageAsync(
        IPanelForgeDbContext db, IPageRepository pageRepository, List<Page> pages, Guid chapterId, int pageNumber, CancellationToken ct)
    {
        var page = pages.FirstOrDefault(p => p.PageNumber == pageNumber);
        if (page is not null) return (page, false);

        page = Page.CreateNew(chapterId, pageNumber, LayoutFormat.StandardPage, 1200, 1800, 300);
        await pageRepository.SaveAsync(page, ct);
        db.Pages.Add(page);
        pages.Add(page);
        return (page, true);
    }

    public static (Panel Panel, bool Created) EnsurePanel(
        IPanelForgeDbContext db,
        List<Panel> panels,
        Guid pageId,
        Guid? panelId,
        int panelNumber,
        int temporaryPanelNumber,
        IReadOnlySet<Guid> claimedPanelIds,
        IReadOnlySet<Guid> touchedPanelIds,
        Guid userId)
    {
        var panel = panelId.HasValue
            ? panels.FirstOrDefault(p => p.Id == panelId.Value)
            : panels.FirstOrDefault(p =>
                p.PageId == pageId &&
                p.PanelNumber == panelNumber &&
                !claimedPanelIds.Contains(p.Id) &&
                !touchedPanelIds.Contains(p.Id));

        if (panelId.HasValue && panel is null)
            throw new InvalidOperationException($"Panel '{panelId}' không tồn tại trong chương này.");
        if (panel is not null && panel.PageId != pageId)
            throw new InvalidOperationException($"Panel '{panel.Id}' không thuộc trang đang lưu.");
        if (panel is not null) return (panel, false);

        // Panel mới dùng số tạm để không đụng unique index khi chèn vào giữa các panel hiện có.
        panel = Panel.Create(pageId, temporaryPanelNumber, temporaryPanelNumber, "{}");
        panel.CreatedBy = userId.ToString();
        db.Panels.Add(panel);
        panels.Add(panel);
        return (panel, true);
    }

    /// <summary>Đặt hướng panel. Trả lỗi (quá dài) hoặc null.</summary>
    public static string? ApplyDirection(Panel panel, string? direction, Guid userId)
    {
        try
        {
            panel.UpdateDirection(direction);
            panel.UpdatedBy = userId.ToString();
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Xoá mềm như DeletePanel / DeletePage: DbContext đổi Remove thành soft-delete; Page ghi thêm event vào stream.</summary>
    public static async Task RemoveAsync(
        IPanelForgeDbContext db, IPageRepository pageRepository, IReadOnlyCollection<Page> removedPages, IReadOnlyCollection<Panel> removedPanels,
        Guid userId, CancellationToken ct)
    {
        foreach (var panel in removedPanels)
        {
            panel.DeletedBy = userId.ToString();
            db.Panels.Remove(panel);
        }
        var removedPageIds = removedPages.Select(p => p.Id).ToHashSet();
        var preservedTaskPages = db is DbContext efDb && removedPageIds.Count > 0
            ? efDb.ChangeTracker.Entries<StageTask>()
                .Where(e => e.State != EntityState.Deleted &&
                            e.Entity.PageId is { } pid &&
                            removedPageIds.Contains(pid))
                .Select(e => (
                    Entry: e,
                    PageId: e.Entity.PageId!.Value,
                    OriginalState: e.State,
                    WasPageIdModified: e.Property(t => t.PageId).IsModified))
                .ToList()
            : [];

        foreach (var page in removedPages)
        {
            var stream = await pageRepository.GetAsync(page.Id, ct);
            if (stream is not null)
            {
                stream.Delete(userId);
                await pageRepository.SaveAsync(stream, ct);
            }
            db.Pages.Remove(page);
        }

        // DbSet.Remove(page) khiến EF Core ChangeTracker cắt FK PageId -> null trên StageTask đang track
        // trước khi SaveChangesAsync chuyển Delete thành soft-delete; khôi phục để RefreezeScript huỷ đúng ô việc.
        foreach (var (entry, pageId, originalState, wasPageIdModified) in preservedTaskPages)
        {
            if (entry.Entity.PageId != pageId)
            {
                var prop = entry.Property(t => t.PageId);
                prop.CurrentValue = pageId;
                prop.IsModified = wasPageIdModified;
                if (originalState == EntityState.Unchanged && !entry.Properties.Any(p => p.IsModified))
                    entry.State = EntityState.Unchanged;
            }
        }

        // FK cascade không chạy với xoá mềm
        var removedPanelIds = removedPanels.Select(p => p.Id).ToList();
        if (removedPanelIds.Count > 0)
        {
            var orphanVariants = await db.PanelBibleVariants.Where(v => v.PanelId.HasValue && removedPanelIds.Contains(v.PanelId.Value)).ToListAsync(ct);
            db.PanelBibleVariants.RemoveRange(orphanVariants);
        }
    }

    /// <summary>Đặt tập (mục Bible, revision) của Panel = <paramref name="wanted"/> (panel_bible_variants).</summary>
    public static void ApplyCastRevisions(
        IPanelForgeDbContext db, Guid panelId, IReadOnlyList<PanelCastRevisionItem> wanted, IReadOnlyList<PanelBibleVariant> current, Guid userId)
    {
        var wantedEntryIds = wanted.Select(x => x.BibleEntryId).ToHashSet();
        foreach (var oldVar in current.Where(v => !wantedEntryIds.Contains(v.BibleEntryId)))
            db.PanelBibleVariants.Remove(oldVar);

        foreach (var item in wanted)
        {
            var existingVar = current.FirstOrDefault(v => v.BibleEntryId == item.BibleEntryId);
            if (existingVar is not null)
                existingVar.UpdateRevision(item.RevisionId, userId);
            else
                db.PanelBibleVariants.Add(PanelBibleVariant.Create(panelId, item.BibleEntryId, item.RevisionId, userId));
        }
    }
}
