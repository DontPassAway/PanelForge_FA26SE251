using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;

namespace PanelForge.Application.Features.Chapters.Common;

public sealed record FinalPageArtwork(Guid PageId, int PageNumber, Guid AssetReferenceId);

/// <summary>
/// Bản artwork "đã duyệt" của chương dùng cho xem thử, xuất file (và ChapterSnapshot về sau): mỗi trang còn sống lấy
/// ảnh CẢ TRANG (PanelId null) ở stage có StageOrder cao nhất, phiên bản mới nhất. Trang chỉ có ảnh theo panel thì không có mặt.
/// </summary>
internal static class FinalArtwork
{
    public static async Task<List<FinalPageArtwork>> SelectAsync(IPanelForgeDbContext db, Guid chapterId, CancellationToken ct)
    {
        var refs = await (
            from r in db.AssetReferences.AsNoTracking()
            where r.ChapterId == chapterId && r.PanelId == null
            join s in db.PipelineStages.AsNoTracking() on r.PipelineStageId equals s.Id
            select new { r.Id, r.PageId, s.StageOrder, r.VersionNumber }).ToListAsync(ct);
        var pages = await db.Pages.AsNoTracking().Where(p => p.ChapterId == chapterId)
            .Select(p => new { p.Id, p.PageNumber }).ToListAsync(ct);

        return (
            from p in pages
            join g in refs.GroupBy(r => r.PageId) on p.Id equals g.Key
            orderby p.PageNumber
            select new FinalPageArtwork(p.Id, p.PageNumber,
                g.OrderByDescending(r => r.StageOrder).ThenByDescending(r => r.VersionNumber).First().Id)).ToList();
    }
}
