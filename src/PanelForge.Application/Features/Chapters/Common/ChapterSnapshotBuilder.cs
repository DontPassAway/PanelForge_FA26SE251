using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Common;

/// <param name="CanvasStreamVersion">Version event stream (Marten) của Page lúc chụp; -1 = Page chưa có event nào.</param>
/// <param name="ArtworkReferenceId">Ảnh cả trang theo <see cref="FinalArtwork"/>; null = trang ghép từ ảnh Panel hoặc chưa có ảnh.</param>
/// <param name="PanelArtworks">Ảnh Panel tạo nên trang khi không dùng ảnh cả trang (trang được ghép); null với snapshot cũ.</param>
/// <param name="WidthPx">Khổ trang lúc chụp, để dựng lại trang ghép đúng tỉ lệ; null với snapshot cũ.</param>
public sealed record SnapshotPage(
    Guid PageId, int PageNumber, long CanvasStreamVersion, Guid? ArtworkReferenceId, string? ArtworkSha256,
    IReadOnlyList<SnapshotPanelArtwork>? PanelArtworks = null, int? WidthPx = null, int? HeightPx = null);

/// <param name="BoundingBox">JSON toạ độ khung lúc chụp (như <c>Panel.BoundingBox</c>); null với snapshot cũ.</param>
public sealed record SnapshotPanelArtwork(Guid PanelId, Guid ArtworkReferenceId, string? ArtworkSha256, string? BoundingBox = null);

public sealed record SnapshotCastItem(
    Guid BibleEntryId, Guid BoundRevisionId, string? ModelSheetSha256);

public sealed record SnapshotPanelVariant(
    Guid PanelId, Guid BibleEntryId, Guid RevisionId, string? ModelSheetSha256);

public sealed record SnapshotManifest(
    Guid ChapterId,
    long ChapterVersion,
    IReadOnlyList<SnapshotPage> Pages,
    Guid? FrozenScriptVersionId,
    IReadOnlyList<SnapshotCastItem> Cast,
    IReadOnlyList<SnapshotPanelVariant> PanelVariants,
    IReadOnlyList<Guid>? ScriptRevisionIds = null,
    IReadOnlyList<Guid>? BibleRevisionIds = null);

/// <summary>
/// CF4 / 4.1: dựng <see cref="ChapterSnapshot"/> cho chương. Handler gọi rồi Add vào DbContext để lưu cùng SaveChanges với thay đổi
/// trạng thái chương (BR-18). Artwork dùng chung định nghĩa <see cref="FinalArtwork"/> với xem thử và xuất file.
/// </summary>
internal static class ChapterSnapshotBuilder
{
    public static async Task<ChapterSnapshot> CreateAsync(
        IPanelForgeDbContext db, IPageRepository pages, Chapter chapter, Guid userId, CancellationToken ct, string? languageCode = null)
    {
        // 1. Artwork cuối từng trang + SHA-256 của file (BR-04)
        var finals = await FinalArtwork.SelectAsync(db, chapter.Id, ct);
        var refIds = finals.SelectMany(f => f.AssetReferenceIds).ToList();
        var shas = await (
            from r in db.AssetReferences.AsNoTracking()
            where refIds.Contains(r.Id)
            join a in db.Assets.AsNoTracking() on r.AssetId equals a.Id
            select new { r.Id, a.Sha256 }).ToDictionaryAsync(x => x.Id, x => x.Sha256, ct);

        // 2. Mọi trang còn sống + version stream canvas (batch multi-page stream query, giữ -1 cho trang chưa có event)
        var pageRows = await db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id)
            .OrderBy(p => p.PageNumber).Select(p => new { p.Id, p.PageNumber, p.WidthPx, p.HeightPx }).ToListAsync(ct);
        var pageIds = pageRows.Select(p => p.Id).ToList();
        // Toạ độ khung lúc duyệt: trang ghép phải dựng lại được dù Panel bị sửa / thay bố cục sau khi mở lại chương
        var artPanelIds = finals.SelectMany(f => f.Parts).Where(x => x.PanelId != null).Select(x => x.PanelId!.Value).ToList();
        var panelRows = await db.Panels.AsNoTracking().Where(p => artPanelIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.BoundingBox, p.PanelNumber }, ct);

        IReadOnlyDictionary<Guid, long>? streamVersions = null;
        try
        {
            streamVersions = await pages.GetStreamVersionsAsync(pageIds, ct);
        }
        catch
        {
            // Fallback nếu mock chưa setup GetStreamVersionsAsync
        }

        var snapshotPages = new List<SnapshotPage>();
        foreach (var p in pageRows)
        {
            var art = finals.FirstOrDefault(f => f.PageId == p.Id);
            long streamVersion = -1;
            if (streamVersions is not null && streamVersions.TryGetValue(p.Id, out var v))
            {
                streamVersion = v;
            }
            else
            {
                try
                {
                    var singleV = await pages.GetStreamVersionAsync(p.Id, ct);
                    if (singleV >= 0)
                    {
                        streamVersion = singleV;
                    }
                    else
                    {
                        var singleP = await pages.GetAsync(p.Id, ct);
                        if (singleP is not null) streamVersion = singleP.Version;
                    }
                }
                catch
                {
                    streamVersion = -1;
                }
            }

            var whole = art?.WholePageReferenceId;
            var panelArtworks = art is null || whole is not null
                ? null
                // Thứ tự ghép = thứ tự khung (như PageImageBuilder dựng từ dữ liệu sống)
                : art.Parts.OrderBy(x => panelRows.GetValueOrDefault(x.PanelId!.Value)?.PanelNumber ?? int.MaxValue)
                    .Select(x => new SnapshotPanelArtwork(x.PanelId!.Value, x.AssetReferenceId,
                        shas.GetValueOrDefault(x.AssetReferenceId), panelRows.GetValueOrDefault(x.PanelId!.Value)?.BoundingBox))
                    .ToList();
            snapshotPages.Add(new SnapshotPage(p.Id, p.PageNumber, streamVersion, whole,
                whole is null ? null : shas.GetValueOrDefault(whole.Value), panelArtworks, p.WidthPx, p.HeightPx));
        }

        // 3. Phiên bản kịch bản chốt sản xuất (BIB-14, SCR-41)
        var frozenScriptVersionId = chapter.FrozenScriptVersionId ?? chapter.LastSubmittedVersionId;

        // 4. Bản đồ thực thể chương (BIB-14: ghi cast map, bỏ đọc Bible theo era)
        var bindings = await db.ChapterBibleBindings.AsNoTracking()
            .Include(b => b.BoundRevision)
                .ThenInclude(r => r!.Events)
            .Where(b => b.ChapterId == chapter.Id && b.BoundRevisionId != null)
            .ToListAsync(ct);

        var cast = bindings.Select(b =>
        {
            var lockedEvt = b.BoundRevision?.Events
                .Where(e => e.Type == BibleRevisionEventType.Locked)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefault();
            var sha = lockedEvt?.AssetSha256;
            return new SnapshotCastItem(b.BibleEntryId, b.BoundRevisionId!.Value, sha);
        }).ToList();

        // 5. Biến thể theo Panel
        var panelIds = await db.Panels.AsNoTracking()
            .Where(pan => pageIds.Contains(pan.PageId))
            .Select(pan => pan.Id)
            .ToListAsync(ct);

        var panelVariants = await db.PanelBibleVariants.AsNoTracking()
            .Include(v => v.Revision)
                .ThenInclude(r => r.Events)
            .Where(v => v.PanelId.HasValue && panelIds.Contains(v.PanelId.Value))
            .ToListAsync(ct);

        var variants = panelVariants.Select(v =>
        {
            var lockedEvt = v.Revision.Events
                .Where(e => e.Type == BibleRevisionEventType.Locked)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefault();
            var sha = lockedEvt?.AssetSha256;
            return new SnapshotPanelVariant(v.PanelId!.Value, v.BibleEntryId, v.RevisionId, sha);
        }).ToList();

        // Gồm cả bản Bible gắn riêng từng Panel (biến thể), không chỉ cast cấp chương
        var bibleRevisionIds = cast.Select(c => c.BoundRevisionId).Concat(variants.Select(v => v.RevisionId)).Distinct().ToList();

        // 6. Số snapshot tăng dần + hash manifest
        var next = (await db.ChapterSnapshots.Where(s => s.ChapterId == chapter.Id)
            .MaxAsync(s => (int?)s.SnapshotNumber, ct) ?? 0) + 1;
        var manifest = new SnapshotManifest(
            chapter.Id,
            chapter.VersionVector,
            snapshotPages,
            frozenScriptVersionId,
            cast,
            variants,
            ScriptRevisionIds: [],
            BibleRevisionIds: bibleRevisionIds);

        var json = AiJson.Serialize(manifest);

        return ChapterSnapshot.Create(chapter.Id, next, chapter.VersionVector, userId, json, AiContentValidator.Sha256(json), languageCode);
    }
}
