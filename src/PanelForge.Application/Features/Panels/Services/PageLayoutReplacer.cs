using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.Panels.Services;

public sealed record PageLayoutReplacement(IReadOnlyList<Panel> Panels, int UnassignedArtworkCount);

public sealed class PageLayoutReplacer
{
    private readonly IPanelForgeDbContext _db;
    private readonly IPageRepository _pages;
    private readonly IUnitOfWork _unitOfWork;

    public PageLayoutReplacer(IPanelForgeDbContext db, IPageRepository pages, IUnitOfWork unitOfWork)
    {
        _db = db;
        _pages = pages;
        _unitOfWork = unitOfWork;
    }

    public Task<PageLayoutReplacement> ReplaceAsync(
        Page page,
        IReadOnlyList<Panel> newPanels,
        Guid userId,
        CancellationToken cancellationToken) =>
        _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var oldPanels = await _db.Panels
                .Where(p => p.PageId == page.Id)
                .OrderBy(p => p.ReadingOrder)
                .ThenBy(p => p.PanelNumber)
                .ToListAsync(ct);

            var replacements = oldPanels
                .Select((panel, index) => new
                {
                    panel.Id,
                    NewPanelId = index < newPanels.Count ? newPanels[index].Id : (Guid?)null
                })
                .ToDictionary(x => x.Id, x => x.NewPanelId);
            var oldPanelIds = replacements.Keys.ToList();

            foreach (var panel in oldPanels)
                panel.DeletedBy = userId.ToString();
            _db.Panels.RemoveRange(oldPanels);
            await _db.SaveChangesAsync(ct);

            _db.Panels.AddRange(newPanels);
            await _db.SaveChangesAsync(ct);

            var artworkReferences = await _db.AssetReferences
                .Where(r => r.PanelId.HasValue && oldPanelIds.Contains(r.PanelId.Value))
                .ToListAsync(ct);
            // Khung bị bỏ (bố cục mới ít khung hơn): giữ artwork ở khung cũ (đã xoá mềm), KHÔNG gán PanelId = null — null nghĩa
            // là "ảnh cả trang", ảnh một khung lẻ sẽ bị dựng thành cả trang. FinalArtwork bỏ qua khung đã xoá nên ảnh này
            // tự rơi khỏi bản cuối; snapshot / link cũ vẫn dựng đúng nhờ toạ độ đã ghim.
            var unassignedArtworkCount = 0;
            foreach (var reference in artworkReferences)
            {
                if (replacements[reference.PanelId!.Value] is { } newPanelId) reference.ReassignPanel(newPanelId);
                else unassignedArtworkCount++;
            }

            var variants = await _db.PanelBibleVariants
                .Where(v => v.PanelId.HasValue && oldPanelIds.Contains(v.PanelId.Value))
                .ToListAsync(ct);
            foreach (var variant in variants)
                variant.ReassignPanel(replacements[variant.PanelId!.Value]);

            var scriptLines = await _db.ScriptLines
                .Where(l => l.PanelId.HasValue && oldPanelIds.Contains(l.PanelId.Value))
                .ToListAsync(ct);
            foreach (var line in scriptLines)
                line.AssignToPanel(replacements[line.PanelId!.Value]);

            var stageProgresses = await _db.StageProgresses
                .Where(s => s.PanelId.HasValue && oldPanelIds.Contains(s.PanelId.Value))
                .ToListAsync(ct);
            foreach (var progress in stageProgresses)
                progress.ReassignPanel(replacements[progress.PanelId!.Value]);

            var changeRequests = await _db.ChangeRequests
                .Where(r => r.PanelId.HasValue && oldPanelIds.Contains(r.PanelId.Value))
                .ToListAsync(ct);
            foreach (var request in changeRequests)
                request.ReassignPanel(replacements[request.PanelId!.Value]);

            var previewFeedback = await _db.PreviewFeedback
                .Where(f => f.PanelId.HasValue && oldPanelIds.Contains(f.PanelId.Value))
                .ToListAsync(ct);
            foreach (var feedback in previewFeedback)
                feedback.ReassignPanel(replacements[feedback.PanelId!.Value]);

            foreach (var element in page.Elements.Values.Where(e => !e.IsRemoved && e.PanelId.HasValue && replacements.ContainsKey(e.PanelId.Value)).ToList())
                page.ReassignElementPanel(element.ElementId, replacements[element.PanelId!.Value], userId);

            await _db.SaveChangesAsync(ct);
            if (page.UncommittedEvents.Count > 0)
                await _pages.SaveAsync(page, ct);

            return new PageLayoutReplacement(
                newPanels,
                unassignedArtworkCount);
        }, cancellationToken);
}
