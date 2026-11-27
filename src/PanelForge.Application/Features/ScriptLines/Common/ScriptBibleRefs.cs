using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Common;

/// <summary>Xử lý tham chiếu Series Bible bôi đen trên dòng kịch bản (BIB-12, 1A, 2A, 5A), dùng chung cho lưu theo dòng và lưu cả chương.</summary>
internal static class ScriptBibleRefs
{
    /// <summary>Chỉ dòng Action, Narration, Sfx mới gắn được tham chiếu. Trả lỗi hoặc null.</summary>
    public static string? ValidateKind(IReadOnlyList<ScriptLineRefInput>? refs, ScriptLineKind kind, string lineLabel) =>
        refs is { Count: > 0 } && kind is not (ScriptLineKind.Action or ScriptLineKind.Narration or ScriptLineKind.Sfx)
            ? $"{lineLabel}: Chỉ cho phép gắn tham chiếu Series Bible trên dòng Action, Narration hoặc Sfx."
            : null;

    public static async Task<SeriesBible> GetOrAddBibleAsync(IPanelForgeDbContext db, Guid seriesId, CancellationToken ct)
    {
        var bible = await db.SeriesBibles.FirstOrDefaultAsync(b => b.SeriesId == seriesId, ct);
        if (bible is not null) return bible;

        var workspaceId = await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => s.WorkspaceId).FirstOrDefaultAsync(ct);
        bible = SeriesBible.Create(seriesId, workspaceId, "Series Bible");
        db.SeriesBibles.Add(bible);
        return bible;
    }

    /// <summary>
    /// Đặt tập tham chiếu của dòng. <paramref name="refs"/> null: giữ tham chiếu cũ còn khớp chữ, gỡ cái mất chữ (kèm cảnh báo);
    /// có giá trị: thay bằng tập mới (mục nháp tạo qua CreateStub). Trả lỗi nghiệp vụ hoặc null.
    /// </summary>
    public static async Task<string?> ApplyAsync(
        IPanelForgeDbContext db,
        SeriesBible seriesBible,
        ScriptLine line,
        string lineLabel,
        IReadOnlyList<ScriptLineRefInput>? refs,
        List<string> warnings,
        Dictionary<Guid, IReadOnlyList<Guid>> linksByLine,
        CancellationToken ct)
    {
        var lineFullText = line.Text ?? line.DialogueText ?? line.StageDirection ?? string.Empty;
        var normLineText = BibleNameNormalizer.Normalize(lineFullText);

        if (refs is not null)
        {
            var validRefs = new List<(Guid BibleEntryId, string SurfaceText)>();
            var seenEntries = new HashSet<Guid>();

            foreach (var r in refs)
            {
                if (string.IsNullOrWhiteSpace(r.SurfaceText))
                    continue;

                var normSurface = BibleNameNormalizer.Normalize(r.SurfaceText);
                if (string.IsNullOrEmpty(normSurface) || !normLineText.Contains(normSurface))
                {
                    warnings.Add($"{lineLabel}: tham chiếu '{r.SurfaceText}' không có trong nội dung dòng.");
                    continue;
                }

                Guid? targetEntryId = null;
                if (r.BibleEntryId.HasValue)
                {
                    var entry = await db.BibleEntries.AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == r.BibleEntryId.Value && e.SeriesBibleId == seriesBible.Id, ct);
                    if (entry is null)
                        return $"{lineLabel}: Mục Bible '{r.BibleEntryId}' không tồn tại.";
                    if (entry.Category is not (BibleEntryCategory.Character or BibleEntryCategory.Location or BibleEntryCategory.Prop))
                        return $"{lineLabel}: Chỉ cho phép tham chiếu mục Nhân vật, Địa điểm hoặc Đạo cụ.";
                    targetEntryId = entry.Id;
                }
                else if (r.NewDraft is not null)
                {
                    var draftName = r.NewDraft.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(draftName))
                    {
                        warnings.Add($"{lineLabel}: tên mục nháp không được để trống.");
                        continue;
                    }
                    if (r.NewDraft.Category is not (BibleEntryCategory.Character or BibleEntryCategory.Location or BibleEntryCategory.Prop))
                        return $"{lineLabel}: Mục nháp phải thuộc danh mục Nhân vật, Địa điểm hoặc Đạo cụ.";

                    var normDraftName = BibleNameNormalizer.Normalize(draftName);
                    var localEntry = db.BibleEntries.Local.FirstOrDefault(e =>
                        e.SeriesBibleId == seriesBible.Id && e.NormalizedName == normDraftName);
                    if (localEntry is not null)
                    {
                        targetEntryId = localEntry.Id;
                    }
                    else
                    {
                        var existingEntry = await db.BibleEntries.FirstOrDefaultAsync(
                            e => e.SeriesBibleId == seriesBible.Id && e.NormalizedName == normDraftName, ct);
                        if (existingEntry is not null)
                        {
                            targetEntryId = existingEntry.Id;
                        }
                        else
                        {
                            var stub = BibleEntry.CreateStub(seriesBible.Id, r.NewDraft.Category, draftName, description: "");
                            db.BibleEntries.Add(stub);
                            targetEntryId = stub.Id;
                        }
                    }
                }

                if (targetEntryId.HasValue && seenEntries.Add(targetEntryId.Value))
                    validRefs.Add((targetEntryId.Value, r.SurfaceText.Trim()));
            }

            var currentLinks = await db.ScriptLineBibleLinks.Where(k => k.ScriptLineId == line.Id).ToListAsync(ct);
            var wantedEntryIds = validRefs.Select(x => x.BibleEntryId).ToHashSet();
            foreach (var oldLink in currentLinks.Where(k => !wantedEntryIds.Contains(k.BibleEntryId)))
                db.ScriptLineBibleLinks.Remove(oldLink);

            var existingEntryIds = currentLinks.Select(k => k.BibleEntryId).ToHashSet();
            foreach (var (entryId, surface) in validRefs)
            {
                if (!existingEntryIds.Contains(entryId))
                    db.ScriptLineBibleLinks.Add(ScriptLineBibleLink.Create(line.Id, entryId, surfaceText: surface));
            }
            linksByLine[line.Id] = validRefs.Select(x => x.BibleEntryId).ToList();
            return null;
        }

        var kept = await db.ScriptLineBibleLinks.Where(k => k.ScriptLineId == line.Id).ToListAsync(ct);
        if (kept.Count > 0)
        {
            var keptEntryIds = new List<Guid>();
            foreach (var link in kept)
            {
                var normSurface = BibleNameNormalizer.Normalize(link.SurfaceText);
                if (!string.IsNullOrEmpty(normSurface) && normLineText.Contains(normSurface))
                {
                    keptEntryIds.Add(link.BibleEntryId);
                }
                else
                {
                    db.ScriptLineBibleLinks.Remove(link);
                    warnings.Add($"{lineLabel}: gỡ tham chiếu '{link.SurfaceText}' vì nội dung đã thay đổi.");
                }
            }
            linksByLine[line.Id] = keptEntryIds;
        }
        else
        {
            linksByLine[line.Id] = [];
        }
        return null;
    }
}
