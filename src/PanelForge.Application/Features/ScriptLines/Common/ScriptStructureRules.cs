using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;
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
        IPanelForgeDbContext db, List<Panel> panels, Guid pageId, int panelNumber, Guid userId)
    {
        var panel = panels.FirstOrDefault(p => p.PageId == pageId && p.PanelNumber == panelNumber);
        if (panel is not null) return (panel, false);

        panel = Panel.Create(pageId, panelNumber, panelNumber, "{}");
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
