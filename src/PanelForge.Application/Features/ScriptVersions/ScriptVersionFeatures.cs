using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.ScriptVersions;

/// <summary>
/// Một hàng của kịch bản theo thứ tự đọc. <c>Type</c>: page · panel · Action · Dialogue · Thought · Narration · Sfx.
/// <c>Key</c> ổn định giữa các phiên bản (page:{n}, panel:{page}.{n}, line:{lineId}) để so sánh từng dòng.
/// </summary>
public sealed record ScriptVersionRowRef(Guid EntryId, string SurfaceText);
public sealed record ScriptVersionRowCast(Guid EntryId, Guid RevisionId);

public sealed record ScriptVersionRow(
    string Key,
    string Type,
    int Page,
    int Panel,
    string? Label,
    string Text,
    IReadOnlyList<ScriptVersionRowRef>? Refs = null,
    IReadOnlyList<ScriptVersionRowCast>? CastRevisions = null,
    Guid? EntityId = null,
    string? BoundingBox = null);

public sealed record ScriptCastChoiceItem(Guid BibleEntryId, Guid? RevisionId);

public sealed record ScriptVersionSnapshot(
    IReadOnlyList<ScriptVersionRow> Rows,
    IReadOnlyList<ScriptCastChoiceItem>? CastChoices = null);

public sealed record ScriptVersionSummaryDto(
    Guid Id,
    int VersionNumber,
    string Message,
    Guid AuthorUserId,
    string? AuthorName,
    DateTime CreatedAt,
    int LineCount,
    string ContentHash,
    /// <summary>Đã gửi duyệt (push); null = mới lưu (commit).</summary>
    DateTime? SubmittedAt);

public sealed record ScriptVersionDetailDto(ScriptVersionSummaryDto Version, IReadOnlyList<ScriptVersionRow> Rows);

/// <summary>Bản kịch bản hiện tại (chưa commit) — "working copy" để so với một phiên bản.</summary>
public sealed record ScriptWorkingCopyDto(string ContentHash, int LineCount, IReadOnlyList<ScriptVersionRow> Rows);

/// <summary>Dựng ảnh chụp kịch bản cấp chương: Trang → Panel (Direction) → dòng, không phụ thuộc Scene.</summary>
public static class ScriptSnapshotBuilder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<(IReadOnlyList<ScriptVersionRow> Rows, string Hash, int LineCount, IReadOnlyList<ScriptCastChoiceItem> CastChoices)> BuildWithChoicesAsync(
        IPanelForgeDbContext db, Guid chapterId, CancellationToken ct)
    {
        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.ChapterId == chapterId)
            .OrderBy(p => p.PageNumber)
            .Select(p => new { p.Id, p.PageNumber })
            .ToListAsync(ct);
        var pageIds = pages.Select(p => p.Id).ToList();
        var panels = await db.Panels.AsNoTracking()
            .Where(p => pageIds.Contains(p.PageId))
            .Select(p => new { p.Id, p.PageId, p.PanelNumber, p.Direction, p.BoundingBox })
            .ToListAsync(ct);

        // Dòng thuộc chương qua Panel → Page; dòng chưa neo Panel lấy qua Scene khi còn Scene
        var lines = await db.ScriptLines.AsNoTracking()
            .Where(l => (l.PanelId != null && l.Panel!.Page.ChapterId == chapterId) || l.Scene.ChapterId == chapterId)
            .Select(l => new
            {
                l.Id, l.PanelId, l.LineOrder, l.Kind, l.SpeakerLabel,
                Text = l.Text ?? string.Empty
            })
            .ToListAsync(ct);

        var lineIds = lines.Select(l => l.Id).ToList();
        var bibleLinks = await db.ScriptLineBibleLinks.AsNoTracking()
            .Where(k => lineIds.Contains(k.ScriptLineId))
            .ToListAsync(ct);
        var refsByLine = bibleLinks
            .GroupBy(k => k.ScriptLineId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ScriptVersionRowRef>)g
                    .OrderBy(x => x.BibleEntryId)
                    .Select(x => new ScriptVersionRowRef(x.BibleEntryId, x.SurfaceText))
                    .ToList());

        var panelIds = panels.Select(p => p.Id).ToList();
        var panelVariants = await db.PanelBibleVariants.AsNoTracking()
            .Where(v => v.PanelId.HasValue && panelIds.Contains(v.PanelId.Value))
            .ToListAsync(ct);
        var castsByPanel = panelVariants
            .GroupBy(v => v.PanelId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ScriptVersionRowCast>)g
                    .OrderBy(x => x.BibleEntryId)
                    .Select(x => new ScriptVersionRowCast(x.BibleEntryId, x.RevisionId))
                    .ToList());

        var writerChoices = await db.ChapterBibleBindings.AsNoTracking()
            .Where(b => b.ChapterId == chapterId && b.ChosenByWriter)
            .OrderBy(b => b.BibleEntryId)
            .Select(b => new ScriptCastChoiceItem(b.BibleEntryId, b.BoundRevisionId))
            .ToListAsync(ct);

        var rows = new List<ScriptVersionRow>();
        var placed = new HashSet<Guid>();
        foreach (var page in pages)
        {
            rows.Add(new ScriptVersionRow($"page:{page.PageNumber}", "page", page.PageNumber, 0, null, string.Empty,
                EntityId: page.Id));
            foreach (var panel in panels.Where(p => p.PageId == page.Id).OrderBy(p => p.PanelNumber))
            {
                var pCasts = castsByPanel.GetValueOrDefault(panel.Id);
                rows.Add(new ScriptVersionRow(
                    $"panel:{page.PageNumber}.{panel.PanelNumber}", "panel", page.PageNumber, panel.PanelNumber,
                    null, panel.Direction ?? string.Empty, null, pCasts is { Count: > 0 } ? pCasts : null,
                    panel.Id, panel.BoundingBox));
                foreach (var l in lines.Where(l => l.PanelId == panel.Id).OrderBy(l => l.LineOrder))
                {
                    placed.Add(l.Id);
                    var lRefs = refsByLine.GetValueOrDefault(l.Id);
                    rows.Add(new ScriptVersionRow($"line:{l.Id}", l.Kind.ToString(), page.PageNumber, panel.PanelNumber,
                        l.SpeakerLabel, l.Text.Trim(), lRefs is { Count: > 0 } ? lRefs : null, EntityId: l.Id));
                }
            }
        }
        foreach (var l in lines.Where(l => !placed.Contains(l.Id)).OrderBy(l => l.LineOrder))
        {
            var lRefs = refsByLine.GetValueOrDefault(l.Id);
            rows.Add(new ScriptVersionRow($"line:{l.Id}", l.Kind.ToString(), 0, 0, l.SpeakerLabel, l.Text.Trim(),
                lRefs is { Count: > 0 } ? lRefs : null, EntityId: l.Id));
        }

        var lineCount = rows.Count(r => r.Key.StartsWith("line:", StringComparison.Ordinal));

        // Băm nội dung: chuỗi Type|Page|Panel|Label|Text, chỉ nối thêm |refs:... / |cast:... khi có, và chapter-cast|... khi có Writer choices
        var rowLines = rows.Select(r =>
        {
            var baseLine = $"{r.Type}|{r.Page}|{r.Panel}|{r.Label}|{r.Text}";
            if (r.Refs is { Count: > 0 })
            {
                var sortedRefs = r.Refs.OrderBy(x => x.EntryId).Select(x => $"{x.EntryId}:{x.SurfaceText}");
                baseLine += $"|refs:{string.Join(',', sortedRefs)}";
            }
            else if (r.CastRevisions is { Count: > 0 })
            {
                var sortedCasts = r.CastRevisions.OrderBy(x => x.EntryId).Select(x => $"{x.EntryId}:{x.RevisionId}");
                baseLine += $"|cast:{string.Join(',', sortedCasts)}";
            }
            if (r.BoundingBox is not null) baseLine += $"|bbox:{r.BoundingBox}";
            return baseLine;
        }).ToList();

        if (writerChoices.Count > 0)
        {
            var choiceParts = writerChoices.OrderBy(b => b.BibleEntryId).Select(b => $"{b.BibleEntryId}:{b.RevisionId}");
            rowLines.Add($"chapter-cast|{string.Join(',', choiceParts)}");
        }

        var hash = AiContentValidator.Sha256(string.Join('\n', rowLines));
        return (rows, hash, lineCount, writerChoices);
    }

    public static async Task<(IReadOnlyList<ScriptVersionRow> Rows, string Hash, int LineCount)> BuildAsync(
        IPanelForgeDbContext db, Guid chapterId, CancellationToken ct)
    {
        var (rows, hash, lineCount, _) = await BuildWithChoicesAsync(db, chapterId, ct);
        return (rows, hash, lineCount);
    }

    public static string Serialize(IReadOnlyList<ScriptVersionRow> rows, IReadOnlyList<ScriptCastChoiceItem>? castChoices = null) =>
        JsonSerializer.Serialize(new ScriptVersionSnapshot(rows, castChoices), Json);

    public static IReadOnlyList<ScriptVersionRow> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : (JsonSerializer.Deserialize<ScriptVersionSnapshot>(json, Json)?.Rows ?? []);

    public static ScriptVersionSnapshot DeserializeSnapshot(string json) =>
        string.IsNullOrWhiteSpace(json) ? new ScriptVersionSnapshot([]) : (JsonSerializer.Deserialize<ScriptVersionSnapshot>(json, Json) ?? new ScriptVersionSnapshot([]));

    /// <summary>
    /// Commit kịch bản hiện tại thành phiên bản mới (số tăng dần). Nội dung trùng phiên bản gần nhất thì trả lại
    /// phiên bản đó, không tạo bản trùng. Chỉ thêm vào DbContext; người gọi SaveChanges.
    /// Chạy ChapterCastService.RecomputeAsync trước khi snapshot (BIB-12, 3A).
    /// </summary>
    public static async Task<ScriptVersion> CommitAsync(
        IPanelForgeDbContext db,
        Guid chapterId,
        Guid authorUserId,
        string? message,
        CancellationToken ct,
        Chapters.Services.IChapterCastService castService)
    {
        await castService.RecomputeAsync(chapterId, authorUserId, ct);

        var (rows, hash, lineCount, choices) = await BuildWithChoicesAsync(db, chapterId, ct);
        var latest = await db.ScriptVersions
            .Where(v => v.ChapterId == chapterId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (latest is not null && latest.ContentHash == hash) return latest;

        var version = ScriptVersion.Create(
            chapterId, (latest?.VersionNumber ?? 0) + 1, message, authorUserId, hash, Serialize(rows, choices), lineCount);
        db.ScriptVersions.Add(version);
        return version;
    }
}

public sealed record GetScriptVersionsQuery(Guid ChapterId, Guid UserId) : IRequest<Result<IReadOnlyList<ScriptVersionSummaryDto>>>;
public sealed record GetScriptVersionQuery(Guid ChapterId, Guid VersionId, Guid UserId) : IRequest<Result<ScriptVersionDetailDto>>;
public sealed record GetScriptWorkingCopyQuery(Guid ChapterId, Guid UserId) : IRequest<Result<ScriptWorkingCopyDto>>;

public sealed class ScriptVersionQueryHandlers :
    IRequestHandler<GetScriptVersionsQuery, Result<IReadOnlyList<ScriptVersionSummaryDto>>>,
    IRequestHandler<GetScriptVersionQuery, Result<ScriptVersionDetailDto>>,
    IRequestHandler<GetScriptWorkingCopyQuery, Result<ScriptWorkingCopyDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public ScriptVersionQueryHandlers(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<IReadOnlyList<ScriptVersionSummaryDto>>> Handle(GetScriptVersionsQuery q, CancellationToken ct)
    {
        if (await DenyAsync(q.ChapterId, q.UserId, ct) is { } deny)
            return Result<IReadOnlyList<ScriptVersionSummaryDto>>.Failure(deny.Error, deny.Code);

        var versions = await _db.ScriptVersions.AsNoTracking()
            .Where(v => v.ChapterId == q.ChapterId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);
        var names = await NamesAsync(versions.Select(v => v.AuthorUserId), ct);
        return Result<IReadOnlyList<ScriptVersionSummaryDto>>.Success(versions.Select(v => Summary(v, names)).ToList());
    }

    public async Task<Result<ScriptVersionDetailDto>> Handle(GetScriptVersionQuery q, CancellationToken ct)
    {
        if (await DenyAsync(q.ChapterId, q.UserId, ct) is { } deny)
            return Result<ScriptVersionDetailDto>.Failure(deny.Error, deny.Code);

        var v = await _db.ScriptVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == q.VersionId && x.ChapterId == q.ChapterId, ct);
        if (v is null) return Result<ScriptVersionDetailDto>.Failure("Phiên bản không tồn tại.", ResultErrorCodes.NotFound);

        var names = await NamesAsync([v.AuthorUserId], ct);
        return Result<ScriptVersionDetailDto>.Success(
            new ScriptVersionDetailDto(Summary(v, names), ScriptSnapshotBuilder.Deserialize(v.RowsJson)));
    }

    public async Task<Result<ScriptWorkingCopyDto>> Handle(GetScriptWorkingCopyQuery q, CancellationToken ct)
    {
        if (await DenyAsync(q.ChapterId, q.UserId, ct) is { } deny)
            return Result<ScriptWorkingCopyDto>.Failure(deny.Error, deny.Code);

        var (rows, hash, lineCount) = await ScriptSnapshotBuilder.BuildAsync(_db, q.ChapterId, ct);
        return Result<ScriptWorkingCopyDto>.Success(new ScriptWorkingCopyDto(hash, lineCount, rows));
    }

    private async Task<(string Error, string Code)?> DenyAsync(Guid chapterId, Guid userId, CancellationToken ct)
    {
        var workspaceId = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == chapterId)
            .Join(_db.Series, c => c.SeriesId, s => s.Id, (c, s) => (Guid?)s.WorkspaceId)
            .FirstOrDefaultAsync(ct);
        if (workspaceId is null) return ($"Chương {chapterId} không tồn tại.", ResultErrorCodes.NotFound);
        return await _auth.GetMemberRoleAsync(userId, workspaceId.Value, ct) is null
            ? ("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden)
            : null;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await _db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private static ScriptVersionSummaryDto Summary(ScriptVersion v, IReadOnlyDictionary<Guid, string> names) => new(
        v.Id, v.VersionNumber, v.Message, v.AuthorUserId, names.GetValueOrDefault(v.AuthorUserId),
        v.CreatedAt, v.LineCount, v.ContentHash, v.SubmittedAt);
}
