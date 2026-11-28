using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.Quotas.Models;

namespace PanelForge.Application.Features.Quotas.Commands.ReconcileStorage;

/// <summary>
/// POST /api/admin/storage/reconcile — Admin đối soát kho file với DB (OPS-22, C6: tính lại + báo cáo, chưa xoá).
/// Quét toàn bộ kho một lần, gán từng file cho Studio theo khoá CAS, so với các bản ghi còn tham chiếu.
/// Apply = false chỉ báo cáo; Apply = true ghi lại UsedStorageBytes = dung lượng file còn được dùng (audit tự ghi, BR-18).
/// Không xoá file nào.
/// </summary>
public sealed record ReconcileStorageCommand(bool Apply) : IRequest<Result<StorageReconcileReportDto>>;

public sealed partial class ReconcileStorageCommandHandler(IPanelForgeDbContext db, IAssetStorage storage)
    : IRequestHandler<ReconcileStorageCommand, Result<StorageReconcileReportDto>>
{
    public const int MaxOrphanSamples = 200;
    public const string ConcurrentMessage = "Có Studio vừa tải file lên trong lúc đối soát. Chạy lại để lấy số mới.";

    private enum FileKind { Primary, Derived }

    /// <summary>Ảnh Bible được trỏ bằng URL hoặc SHA trong nhiều cột (cả lịch sử append-only): gom mọi SHA-256 xuất hiện.</summary>
    [GeneratedRegex("[0-9a-f]{64}")]
    private static partial Regex Sha256Pattern();

    public async Task<Result<StorageReconcileReportDto>> Handle(ReconcileStorageCommand command, CancellationToken ct)
    {
        // Tải Studio trước (đang track, khoá xmin): upload nào chen vào sau mốc này sẽ làm lần ghi Apply bị từ chối.
        var workspaces = await db.StudioWorkspaces.OrderBy(w => w.Name).ToListAsync(ct);
        var workspaceIds = workspaces.Select(w => w.Id).ToHashSet();

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
        var referenced = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var a in await db.Assets.IgnoreQueryFilters().AsNoTracking()
                     .Select(a => new { a.StorageKey, a.WorkspaceId }).ToListAsync(ct))
            referenced.TryAdd(a.StorageKey, a.WorkspaceId);
        foreach (var w in workspaces.Where(w => w.CoverImageSha256 != null))
            referenced.TryAdd($"covers/workspaces/{w.Id:N}/{w.CoverImageSha256}", w.Id);
        foreach (var s in series)
        {
            if (s.CoverImageSha256 != null) referenced.TryAdd($"covers/{s.Id:N}/{s.CoverImageSha256}", s.WorkspaceId);
            if (s.AvatarImageSha256 != null) referenced.TryAdd($"series-avatars/{s.Id:N}/{s.AvatarImageSha256}", s.WorkspaceId);
            if (s.BannerImageSha256 != null) referenced.TryAdd($"series-banners/{s.Id:N}/{s.BannerImageSha256}", s.WorkspaceId);
        }
        foreach (var c in chapters.Where(c => c.CoverImageSha256 != null && chapterWorkspace.ContainsKey(c.Id)))
            referenced.TryAdd($"chapter-covers/{c.Id:N}/{c.CoverImageSha256}", chapterWorkspace[c.Id]);

        var bibleShas = await LoadBibleShasAsync(ct);

        var rows = workspaces.ToDictionary(w => w.Id, _ => new Tally());
        var orphans = new List<StorageOrphanFileDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int scannedFiles = 0, unattributedFiles = 0, orphanTotal = 0;
        long scannedBytes = 0, unattributedBytes = 0;

        await foreach (var file in storage.ListAsync("", ct))
        {
            scannedFiles++;
            scannedBytes += file.SizeBytes;
            seen.Add(file.Key);

            var (kind, owner) = Attribute(file.Key, workspaceIds, seriesWorkspace, chapterWorkspace, exportWorkspace);
            var tally = owner is { } id && rows.TryGetValue(id, out var t) ? t : null;

            if (kind == FileKind.Derived)
            {
                if (tally is not null) tally.Derived += file.SizeBytes;
                continue;
            }

            var isReferenced = referenced.ContainsKey(file.Key)
                || (file.Key.StartsWith("bible/", StringComparison.Ordinal) && bibleShas.Contains(LastSegment(file.Key)));
            if (isReferenced && tally is not null)
            {
                tally.Computed += file.SizeBytes;
                continue;
            }

            // Mồ côi: không bản ghi nào trỏ tới, hoặc thuộc Studio không còn tồn tại.
            orphanTotal++;
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
            if (orphans.Count < MaxOrphanSamples)
                orphans.Add(new StorageOrphanFileDto(file.Key, file.SizeBytes, tally is null ? null : owner));
        }

        foreach (var (key, owner) in referenced)
            if (!seen.Contains(key) && rows.TryGetValue(owner, out var t))
                t.Missing++;

        var report = workspaces.Select(w =>
        {
            var t = rows[w.Id];
            return new StorageReconcileWorkspaceDto(
                w.Id, w.Name, w.UsedStorageBytes, t.Computed, t.OrphanFiles, t.OrphanBytes, t.Derived, t.Missing);
        }).ToList();

        if (command.Apply)
        {
            foreach (var w in workspaces.Where(w => w.UsedStorageBytes != rows[w.Id].Computed))
                w.UpdateUsedStorage(rows[w.Id].Computed);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<StorageReconcileReportDto>.Failure(ConcurrentMessage, ResultErrorCodes.Conflict);
            }
        }

        return Result<StorageReconcileReportDto>.Success(new StorageReconcileReportDto(
            DateTime.UtcNow, command.Apply, scannedFiles, scannedBytes, report,
            unattributedFiles, unattributedBytes, orphans, orphanTotal > orphans.Count));
    }

    private async Task<HashSet<string>> LoadBibleShasAsync(CancellationToken ct)
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
    private static (FileKind Kind, Guid? Owner) Attribute(
        string key,
        HashSet<Guid> workspaceIds,
        Dictionary<Guid, Guid> seriesWorkspace,
        Dictionary<Guid, Guid> chapterWorkspace,
        Dictionary<Guid, Guid> exportWorkspace)
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
                (FileKind.Derived, Guid.TryParse(parts[1].Split('.')[0], out var job) && exportWorkspace.TryGetValue(job, out var ws) ? ws : null),
            "covers" when parts.Length > 2 && parts[1] == "workspaces" => (FileKind.Primary, Workspace(parts[2])),
            "covers" or "series-avatars" or "series-banners" or "bible" when parts.Length > 1 =>
                (FileKind.Primary, Lookup(seriesWorkspace, parts[1])),
            "chapter-covers" when parts.Length > 1 => (FileKind.Primary, Lookup(chapterWorkspace, parts[1])),
            _ => (FileKind.Primary, Workspace(parts[0])),
        };
    }

    private static string LastSegment(string key) => key[(key.LastIndexOf('/') + 1)..];

    private sealed class Tally
    {
        public long Computed;
        public int OrphanFiles;
        public long OrphanBytes;
        public long Derived;
        public int Missing;
    }
}
