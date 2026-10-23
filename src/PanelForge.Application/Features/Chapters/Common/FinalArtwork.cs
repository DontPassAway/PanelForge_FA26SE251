using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Chapters.Common;

/// <summary>Một artwork tạo nên trang: ảnh cả trang (PanelId null) hoặc ảnh của một Panel.</summary>
public sealed record FinalArtworkPart(Guid AssetReferenceId, Guid? PanelId);

public sealed record FinalPageArtwork(Guid PageId, int PageNumber, IReadOnlyList<FinalArtworkPart> Parts)
{
    public IEnumerable<Guid> AssetReferenceIds => Parts.Select(p => p.AssetReferenceId);

    /// <summary>Ảnh cả trang (null nếu trang được ghép từ ảnh Panel).</summary>
    public Guid? WholePageReferenceId => Parts is [{ PanelId: null } only] ? only.AssetReferenceId : null;
}

/// <summary>
/// Bản artwork "đã duyệt" của chương — dùng chung cho trình đọc công khai, xem thử, xuất file và ChapterSnapshot để mọi nơi
/// thấy cùng một trang (cùng quy tắc FE `buildPageComposite`). Mỗi trang còn sống:
/// ảnh CẢ TRANG mới nhất nếu nó ở stage không thấp hơn ảnh Panel nào; ngược lại bản mới nhất (stage cao nhất, rồi version)
/// của từng Panel còn sống — trang sẽ được ghép (<see cref="PageImageBuilder"/>). Trang chưa có ảnh nào thì không có mặt.
/// </summary>
internal static class FinalArtwork
{
    public static async Task<List<FinalPageArtwork>> SelectAsync(
        IPanelForgeDbContext db, Guid chapterId, CancellationToken ct, Guid? pageId = null)
    {
        var refs = await (
            from r in db.AssetReferences.AsNoTracking()
            where r.ChapterId == chapterId && (pageId == null || r.PageId == pageId)
            join s in db.PipelineStages.AsNoTracking() on r.PipelineStageId equals s.Id
            select new { r.Id, r.PageId, r.PanelId, s.StageOrder, r.VersionNumber }).ToListAsync(ct);
        var pages = await db.Pages.AsNoTracking().Where(p => p.ChapterId == chapterId && (pageId == null || p.Id == pageId))
            .Select(p => new { p.Id, p.PageNumber }).ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToList();
        var livePanels = (await db.Panels.AsNoTracking().Where(p => pageIds.Contains(p.PageId))
            .Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        var result = new List<FinalPageArtwork>();
        foreach (var page in pages.OrderBy(p => p.PageNumber))
        {
            var mine = refs.Where(r => r.PageId == page.Id).ToList();
            var whole = mine.Where(r => r.PanelId == null)
                .OrderByDescending(r => r.StageOrder).ThenByDescending(r => r.VersionNumber).FirstOrDefault();
            var perPanel = mine.Where(r => r.PanelId is { } id && livePanels.Contains(id))
                .GroupBy(r => r.PanelId)
                .Select(g => g.OrderByDescending(r => r.StageOrder).ThenByDescending(r => r.VersionNumber).First())
                .ToList();

            if (whole is not null && (perPanel.Count == 0 || whole.StageOrder >= perPanel.Max(r => r.StageOrder)))
                result.Add(new FinalPageArtwork(page.Id, page.PageNumber, [new FinalArtworkPart(whole.Id, null)]));
            else if (perPanel.Count > 0)
                result.Add(new FinalPageArtwork(page.Id, page.PageNumber,
                    perPanel.Select(r => new FinalArtworkPart(r.Id, r.PanelId)).ToList()));
        }

        return result;
    }

    /// <summary>Số trang của từng snapshot artwork (đếm trang khác nhau, vì trang ghép có nhiều artwork) — nạp một lần cho cả danh sách.</summary>
    public static async Task<List<int>> PageCountsAsync(
        IPanelForgeDbContext db, IReadOnlyList<IReadOnlyCollection<Guid>> snapshots, CancellationToken ct)
    {
        var all = snapshots.SelectMany(s => s).Distinct().ToList();
        var pageOf = await db.AssetReferences.AsNoTracking().Where(r => all.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.PageId, ct);
        return snapshots.Select(s => s.Where(pageOf.ContainsKey).Select(id => pageOf[id]).Distinct().Count()).ToList();
    }
}
