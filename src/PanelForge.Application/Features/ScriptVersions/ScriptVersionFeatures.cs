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
public sealed record ScriptVersionRow(string Key, string Type, int Page, int Panel, string? Label, string Text);

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

    public static async Task<(IReadOnlyList<ScriptVersionRow> Rows, string Hash, int LineCount)> BuildAsync(
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
            .Select(p => new { p.Id, p.PageId, p.PanelNumber, p.Direction })
            .ToListAsync(ct);

        // Dòng thuộc chương qua Panel → Page; dòng chưa neo Panel lấy qua Scene khi còn Scene
        var lines = await db.ScriptLines.AsNoTracking()
            .Where(l => (l.PanelId != null && l.Panel!.Page.ChapterId == chapterId) || l.Scene.ChapterId == chapterId)
            .Select(l => new
            {
                l.Id, l.PanelId, l.LineOrder, l.Kind, l.SpeakerLabel,
                Text = l.Text ?? l.DialogueText ?? l.StageDirection ?? string.Empty
            })
            .ToListAsync(ct);

        var rows = new List<ScriptVersionRow>();
        var placed = new HashSet<Guid>();
        foreach (var page in pages)
        {
            rows.Add(new ScriptVersionRow($"page:{page.PageNumber}", "page", page.PageNumber, 0, null, string.Empty));
            foreach (var panel in panels.Where(p => p.PageId == page.Id).OrderBy(p => p.PanelNumber))
            {
                rows.Add(new ScriptVersionRow(
                    $"panel:{page.PageNumber}.{panel.PanelNumber}", "panel", page.PageNumber, panel.PanelNumber,
                    null, panel.Direction ?? string.Empty));
                foreach (var l in lines.Where(l => l.PanelId == panel.Id).OrderBy(l => l.LineOrder))
                {
                    placed.Add(l.Id);
                    rows.Add(new ScriptVersionRow($"line:{l.Id}", l.Kind.ToString(), page.PageNumber, panel.PanelNumber,
                        l.SpeakerLabel, l.Text.Trim()));
                }
            }
        }
        foreach (var l in lines.Where(l => !placed.Contains(l.Id)).OrderBy(l => l.LineOrder))
            rows.Add(new ScriptVersionRow($"line:{l.Id}", l.Kind.ToString(), 0, 0, l.SpeakerLabel, l.Text.Trim()));

        var lineCount = rows.Count(r => r.Key.StartsWith("line:", StringComparison.Ordinal));
        // Băm nội dung (không băm Id dòng) để lưu lại cùng chữ thì nhận ra là không đổi
        var hash = AiContentValidator.Sha256(string.Join('\n',
            rows.Select(r => $"{r.Type}|{r.Page}|{r.Panel}|{r.Label}|{r.Text}")));
        return (rows, hash, lineCount);
    }

    public static string Serialize(IReadOnlyList<ScriptVersionRow> rows) => JsonSerializer.Serialize(rows, Json);

    public static IReadOnlyList<ScriptVersionRow> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<ScriptVersionRow>>(json, Json) ?? [];

    /// <summary>
    /// Commit kịch bản hiện tại thành phiên bản mới (số tăng dần). Nội dung trùng phiên bản gần nhất thì trả lại
    /// phiên bản đó, không tạo bản trùng. Chỉ thêm vào DbContext; người gọi SaveChanges.
    /// </summary>
    public static async Task<ScriptVersion> CommitAsync(
        IPanelForgeDbContext db, Guid chapterId, Guid authorUserId, string? message, CancellationToken ct)
    {
        var (rows, hash, lineCount) = await BuildAsync(db, chapterId, ct);
        var latest = await db.ScriptVersions
            .Where(v => v.ChapterId == chapterId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (latest is not null && latest.ContentHash == hash) return latest;

        var version = ScriptVersion.Create(
            chapterId, (latest?.VersionNumber ?? 0) + 1, message, authorUserId, hash, Serialize(rows), lineCount);
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

/// <summary>Lưu = commit: Writer lưu kịch bản thì chụp lại thành phiên bản (trùng nội dung thì không tạo mới).</summary>
public sealed record CommitScriptVersionCommand(Guid ChapterId, Guid UserId, string? Message)
    : IRequest<Result<ScriptVersionSummaryDto>>;

public sealed class CommitScriptVersionCommandHandler
    : IRequestHandler<CommitScriptVersionCommand, Result<ScriptVersionSummaryDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public CommitScriptVersionCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<ScriptVersionSummaryDto>> Handle(CommitScriptVersionCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null)
            return Result<ScriptVersionSummaryDto>.Failure($"Chương {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        var workspaceId = await _db.Series.AsNoTracking().Where(s => s.Id == chapter.SeriesId).Select(s => s.WorkspaceId).FirstAsync(ct);
        var role = await _auth.GetMemberRoleAsync(cmd.UserId, workspaceId, ct);
        if (role is not (Domain.Enums.WorkspaceRole.Writer or Domain.Enums.WorkspaceRole.Producer))
            return Result<ScriptVersionSummaryDto>.Failure("Chỉ Writer lưu phiên bản kịch bản.", ResultErrorCodes.Forbidden);
        if (chapter.ScriptLockReason is { } locked)
            return Result<ScriptVersionSummaryDto>.Failure(locked, ResultErrorCodes.Conflict);

        var version = await ScriptSnapshotBuilder.CommitAsync(_db, chapter.Id, cmd.UserId, cmd.Message, ct);
        await _db.SaveChangesAsync(ct);

        var name = await _db.Users.AsNoTracking().Where(u => u.Id == version.AuthorUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return Result<ScriptVersionSummaryDto>.Success(new ScriptVersionSummaryDto(
            version.Id, version.VersionNumber, version.Message, version.AuthorUserId, name,
            version.CreatedAt, version.LineCount, version.ContentHash, version.SubmittedAt));
    }
}
