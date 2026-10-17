using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.Chapters.Common;

/// <param name="CanvasStreamVersion">Version event stream (Marten) của Page lúc chụp; -1 = Page chưa có event nào.</param>
/// <param name="ArtworkReferenceId">Artwork cuối của trang theo <see cref="FinalArtwork"/>; null = trang chưa có ảnh cả trang.</param>
public sealed record SnapshotPage(
    Guid PageId, int PageNumber, long CanvasStreamVersion, Guid? ArtworkReferenceId, string? ArtworkSha256);

public sealed record SnapshotManifest(
    Guid ChapterId, long ChapterVersion, IReadOnlyList<SnapshotPage> Pages,
    IReadOnlyList<Guid> ScriptRevisionIds, IReadOnlyList<Guid> BibleRevisionIds);

/// <summary>
/// CF4 / 4.1: dựng <see cref="ChapterSnapshot"/> cho chương. Handler gọi rồi Add vào DbContext để lưu cùng SaveChanges với thay đổi
/// trạng thái chương (BR-18). Artwork dùng chung định nghĩa <see cref="FinalArtwork"/> với xem thử và xuất file.
/// </summary>
internal static class ChapterSnapshotBuilder
{
    public static async Task<ChapterSnapshot> CreateAsync(
        IPanelForgeDbContext db, IPageRepository pages, Chapter chapter, Guid userId, CancellationToken ct)
    {
        // 1. Artwork cuối từng trang + SHA-256 của file (BR-04)
        var finals = await FinalArtwork.SelectAsync(db, chapter.Id, ct);
        var refIds = finals.Select(f => f.AssetReferenceId).ToList();
        var shas = await (
            from r in db.AssetReferences.AsNoTracking()
            where refIds.Contains(r.Id)
            join a in db.Assets.AsNoTracking() on r.AssetId equals a.Id
            select new { r.Id, a.Sha256 }).ToDictionaryAsync(x => x.Id, x => x.Sha256, ct);

        // 2. Mọi trang còn sống + version stream canvas
        var pageRows = await db.Pages.AsNoTracking().Where(p => p.ChapterId == chapter.Id)
            .OrderBy(p => p.PageNumber).Select(p => new { p.Id, p.PageNumber }).ToListAsync(ct);
        var snapshotPages = new List<SnapshotPage>();
        foreach (var p in pageRows)
        {
            var art = finals.FirstOrDefault(f => f.PageId == p.Id);
            var streamVersion = (await pages.GetAsync(p.Id, ct))?.Version ?? -1;
            snapshotPages.Add(new SnapshotPage(p.Id, p.PageNumber, streamVersion, art?.AssetReferenceId,
                art is null ? null : shas.GetValueOrDefault(art.AssetReferenceId)));
        }

        // 3. Revision script mới nhất của từng scene (group trong bộ nhớ để chạy được cả Npgsql lẫn InMemory)
        var scriptRows = await db.ContentRevisions.AsNoTracking()
            .Where(r => r.ChapterId == chapter.Id && r.Kind == ContentRevisionKind.Script)
            .Select(r => new { r.SceneId, r.Id, r.VersionNumber }).ToListAsync(ct);
        var scriptRevisionIds = scriptRows.GroupBy(r => r.SceneId)
            .Select(g => g.MaxBy(r => r.VersionNumber)!.Id).ToList();

        // 4. Revision Bible có hiệu lực tại chương: đã khoá, phạm vi [From, To] chứa số chương.
        //    Chồng lấn thì giai đoạn nối tiếp (From lớn hơn) thắng, rồi tới bản mới nhất (như BibleEntryRevision.CoversChapter)
        var bibleRows = await (
            from r in db.BibleEntryRevisions.AsNoTracking()
            join e in db.BibleEntries.AsNoTracking() on r.BibleEntryId equals e.Id
            join b in db.SeriesBibles.AsNoTracking() on e.SeriesBibleId equals b.Id
            where b.SeriesId == chapter.SeriesId && r.LockedAt != null
                  && r.EffectiveFromChapterNumber <= chapter.ChapterNumber
                  && (r.EffectiveToChapterNumber == null || chapter.ChapterNumber <= r.EffectiveToChapterNumber)
            select new { r.BibleEntryId, r.Id, r.EffectiveFromChapterNumber, r.VersionNumber }).ToListAsync(ct);
        var bibleRevisionIds = bibleRows.GroupBy(x => x.BibleEntryId)
            .Select(g => g.OrderByDescending(x => x.EffectiveFromChapterNumber)
                          .ThenByDescending(x => x.VersionNumber).First().Id)
            .ToList();

        // 5. Số snapshot tăng dần (unique index chặn trùng khi hai lần duyệt chạy song song) + hash manifest
        var next = (await db.ChapterSnapshots.Where(s => s.ChapterId == chapter.Id)
            .MaxAsync(s => (int?)s.SnapshotNumber, ct) ?? 0) + 1;
        var json = AiJson.Serialize(new SnapshotManifest(
            chapter.Id, chapter.VersionVector, snapshotPages, scriptRevisionIds, bibleRevisionIds));

        return ChapterSnapshot.Create(chapter.Id, next, chapter.VersionVector, userId, json, AiContentValidator.Sha256(json));
    }
}
