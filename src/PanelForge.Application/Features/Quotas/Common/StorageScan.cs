using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Domain.Entities.Workspaces;

namespace PanelForge.Application.Features.Quotas.Common;

/// <summary>
/// OPS-22: quét kho file một lần và đối chiếu với các bản ghi còn tham chiếu.
/// Dùng chung cho báo cáo/tính lại dung lượng và cho lệnh xoá file mồ côi.
/// </summary>
internal static partial class StorageScan
{
    internal sealed class Tally
    {
        public long Computed;
        public int OrphanFiles;
        public long OrphanBytes;
        public long Derived;
        public int Missing;
    }

    /// <summary>File không bản ghi nào trỏ tới. Owner = Studio còn hoạt động sở hữu nó; null = không xác định / Studio đã xoá.</summary>
    internal sealed record Orphan(string Key, long SizeBytes, DateTime LastModifiedUtc, Guid? Owner);

    internal sealed record Result(
        List<StudioWorkspace> Workspaces,
        Dictionary<Guid, Tally> Rows,
        List<Orphan> Orphans,
        int ScannedFiles,
        long ScannedBytes,
        int UnattributedFiles,
        long UnattributedBytes);

    private sealed record ReferenceMaps(
        Dictionary<string, Guid> Keys,
        HashSet<string> BibleShas,
        Dictionary<Guid, Guid> SeriesWorkspace,
        Dictionary<Guid, Guid> ChapterWorkspace,
        Dictionary<Guid, Guid> ExportWorkspace);

    private enum FileKind { Primary, Derived }

    /// <summary>Ảnh Bible được trỏ bằng URL hoặc SHA trong nhiều cột (cả lịch sử append-only): gom mọi SHA-256 xuất hiện.</summary>
    [GeneratedRegex("[0-9a-f]{64}")]
    private static partial Regex Sha256Pattern();

    public static async Task<Result> RunAsync(IPanelForgeDbContext db, IAssetStorage storage, CancellationToken ct)
    {
        // Tải Studio trước (đang track, khoá xmin): upload nào chen vào sau mốc này sẽ làm lần ghi dung lượng bị từ chối.
        var allWorkspaces = await db.StudioWorkspaces.IgnoreQueryFilters().OrderBy(w => w.Name).ToListAsync(ct);
        var workspaces = allWorkspaces.Where(w => !w.IsDeleted).ToList();
        var known = allWorkspaces.Select(w => w.Id).ToHashSet();
        var maps = await LoadReferencesAsync(db, allWorkspaces, ct);

        var rows = workspaces.ToDictionary(w => w.Id, _ => new Tally());
        var orphans = new List<Orphan>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int scannedFiles = 0, unattributedFiles = 0;
        long scannedBytes = 0, unattributedBytes = 0;

        await foreach (var file in storage.ListAsync("", ct))
        {
            scannedFiles++;
            scannedBytes += file.SizeBytes;
            seen.Add(file.Key);

            var (kind, owner) = Attribute(file.Key, known, maps);
            var tally = owner is { } id && rows.TryGetValue(id, out var t) ? t : null;

            if (kind == FileKind.Derived)
            {
                if (tally is not null) tally.Derived += file.SizeBytes;
                continue;
            }

            if (IsReferenced(file.Key, maps))
            {
                if (tally is not null) tally.Computed += file.SizeBytes;
                continue;
            }

            if (tally is not null)
            {
                tally.OrphanFiles++;
                tally.OrphanBytes += file.SizeBytes;
            }
            else
            {
                unattributedFiles++;
                unattributedBytes += file.SizeBytes;
            }
            orphans.Add(new Orphan(file.Key, file.SizeBytes, file.LastModifiedUtc, tally is null ? null : owner));
        }

        foreach (var (key, owner) in maps.Keys)
            if (!seen.Contains(key) && rows.TryGetValue(owner, out var t))
                t.Missing++;

        return new Result(workspaces, rows, orphans, scannedFiles, scannedBytes, unattributedFiles, unattributedBytes);
    }

    /// <summary>Đọc lại tham chiếu ngay trước khi xoá: file vừa được dùng lại (cùng SHA) trong lúc quét thì giữ.</summary>
    public static async Task<HashSet<string>> StillReferencedAsync(
        IPanelForgeDbContext db, IEnumerable<string> keys, CancellationToken ct)
    {
        var workspaces = await db.StudioWorkspaces.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var maps = await LoadReferencesAsync(db, workspaces, ct);
        return keys.Where(k => IsReferenced(k, maps)).ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsReferenced(string key, ReferenceMaps maps)
        => maps.Keys.ContainsKey(key)
           || (key.StartsWith("bible/", StringComparison.Ordinal)
               && maps.BibleShas.Contains(key[(key.LastIndexOf('/') + 1)..]));

    private static async Task<ReferenceMaps> LoadReferencesAsync(
        IPanelForgeDbContext db, IReadOnlyCollection<StudioWorkspace> workspaces, CancellationToken ct)
    {
        var series = await db.Series.IgnoreQueryFilters().AsNoTracking()
            .Select(s => new { s.Id, s.WorkspaceId, s.CoverImageSha256, s.AvatarImageSha256, s.BannerImageSha256 })
            .ToListAsync(ct);
        var seriesWorkspace = series.ToDictionary(s => s.Id, s => s.WorkspaceId);
        var chapters = await db.Chapters.IgnoreQueryFilters().AsNoTracking()
            .Select(c => new { c.Id, c.SeriesId, c.CoverImageSha256 })
            .ToListAsync(ct);
        var chapterWorkspace = chapters
            .Where(c => seriesWorkspace.ContainsKey(c.SeriesId))
            .ToDictionary(c => c.Id, c => seriesWorkspace[c.SeriesId]);
        var exportWorkspace = await db.ExportJobs.IgnoreQueryFilters().AsNoTracking()
            .ToDictionaryAsync(j => j.Id, j => j.WorkspaceId, ct);

        // Khoá được tham chiếu → Studio sở hữu. Asset kể cả bản ghi đã xoá mềm (lịch sử phiên bản còn trỏ tới).
        var keys = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var a in await db.Assets.IgnoreQueryFilters().AsNoTracking()
                     .Select(a => new { a.StorageKey, a.WorkspaceId }).ToListAsync(ct))
            keys.TryAdd(a.StorageKey, a.WorkspaceId);
        foreach (var w in workspaces.Where(w => w.CoverImageSha256 != null))
            keys.TryAdd($"covers/workspaces/{w.Id:N}/{w.CoverImageSha256}", w.Id);
        foreach (var s in series)
        {
            if (s.CoverImageSha256 != null) keys.TryAdd($"covers/{s.Id:N}/{s.CoverImageSha256}", s.WorkspaceId);
            if (s.AvatarImageSha256 != null) keys.TryAdd($"series-avatars/{s.Id:N}/{s.AvatarImageSha256}", s.WorkspaceId);
            if (s.BannerImageSha256 != null) keys.TryAdd($"series-banners/{s.Id:N}/{s.BannerImageSha256}", s.WorkspaceId);
        }
        foreach (var c in chapters.Where(c => c.CoverImageSha256 != null && chapterWorkspace.ContainsKey(c.Id)))
            keys.TryAdd($"chapter-covers/{c.Id:N}/{c.CoverImageSha256}", chapterWorkspace[c.Id]);

        return new ReferenceMaps(keys, await LoadBibleShasAsync(db, ct), seriesWorkspace, chapterWorkspace, exportWorkspace);
    }

    private static async Task<HashSet<string>> LoadBibleShasAsync(IPanelForgeDbContext db, CancellationToken ct)
    {
        var texts = new List<string?>();
        foreach (var e in await db.BibleEntries.IgnoreQueryFilters().AsNoTracking()
                     .Select(e => new { e.ReferenceImageUrl, e.DetailsJson }).ToListAsync(ct))
            texts.AddRange([e.ReferenceImageUrl, e.DetailsJson]);
        foreach (var r in await db.BibleEntryRevisions.IgnoreQueryFilters().AsNoTracking()
                     .Select(r => new { r.ConceptImageUrl, r.SnapshotJson }).ToListAsync(ct))
            texts.AddRange([r.ConceptImageUrl, r.SnapshotJson]);
        foreach (var e in await db.BibleRevisionEvents.IgnoreQueryFilters().AsNoTracking()
                     .Where(e => e.ImageUrl != null || e.AssetSha256 != null)
                     .Select(e => new { e.ImageUrl, e.AssetSha256 }).ToListAsync(ct))
            texts.AddRange([e.ImageUrl, e.AssetSha256]);

        var shas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (Match m in Sha256Pattern().Matches(text.ToLowerInvariant()))
                shas.Add(m.Value);
        }
        return shas;
    }

    /// <summary>Gán file cho Studio theo cấu trúc khoá CAS (xem các hàm StorageKey của từng lệnh lưu file).</summary>
    private static (FileKind Kind, Guid? Owner) Attribute(string key, HashSet<Guid> workspaceIds, ReferenceMaps maps)
    {
        var parts = key.Split('/');
        Guid? Lookup(Dictionary<Guid, Guid> map, string segment)
            => Guid.TryParseExact(segment, "N", out var id) && map.TryGetValue(id, out var ws) ? ws : null;
        Guid? Workspace(string segment)
            => Guid.TryParseExact(segment, "N", out var id) && workspaceIds.Contains(id) ? id : null;

        return parts[0] switch
        {
            // thumbs/{khoá asset}.w{N}.webp — ảnh thu nhỏ sinh lại được.
            "thumbs" when parts.Length > 1 => (FileKind.Derived, Workspace(parts[1])),
            // exports/{jobId}.{ext}
            "exports" when parts.Length > 1 =>
                (FileKind.Derived, Guid.TryParse(parts[1].Split('.')[0], out var job) && maps.ExportWorkspace.TryGetValue(job, out var ws) ? ws : null),
            "covers" when parts.Length > 2 && parts[1] == "workspaces" => (FileKind.Primary, Workspace(parts[2])),
            "covers" or "series-avatars" or "series-banners" or "bible" when parts.Length > 1 =>
                (FileKind.Primary, Lookup(maps.SeriesWorkspace, parts[1])),
            "chapter-covers" when parts.Length > 1 => (FileKind.Primary, Lookup(maps.ChapterWorkspace, parts[1])),
            _ => (FileKind.Primary, Workspace(parts[0])),
        };
    }
}
