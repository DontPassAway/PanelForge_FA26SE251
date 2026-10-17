using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Queries.CompareRevisions;

/// <summary>
/// UC-18 / CF4 bước 5: so sánh hai revision cùng loại của một Scene. Script so theo LineId (Id dòng giữ nguyên qua sửa tay,
/// AI và khôi phục) nên chèn một dòng không làm mọi dòng sau thành "Modified"; loại khác so theo khoá/vị trí JSON.
/// Trả kèm payload hai bên để FE tự render diff sâu hơn nếu cần.
/// </summary>
public sealed record CompareRevisionsQuery(
    Guid SeriesId,
    Guid SceneId,
    Guid LeftRevisionId,
    Guid RightRevisionId,
    Guid UserId
) : IRequest<Result<RevisionDiffResponse>>;

public sealed record RevisionSideDto(
    Guid Id,
    int VersionNumber,
    ContentRevisionKind Kind,
    string Summary,
    string ContentHash,
    Guid? SourceSuggestionId,
    string? CreatedBy,
    DateTime CreatedAt);

/// <param name="Path">Script: <c>line:{lineId}</c>; loại khác: khoá JSON hoặc chỉ mục mảng (<c>[2]</c>), <c>$</c> = toàn bộ.</param>
/// <param name="ChangeType">Added, Removed, Modified, Moved (script: chỉ đổi thứ tự).</param>
public sealed record DiffEntry(string Path, string ChangeType, string? OldValue, string? NewValue);

public sealed record RevisionDiffResponse(
    RevisionSideDto Left,
    RevisionSideDto Right,
    IReadOnlyList<DiffEntry> Differences,
    JsonElement? LeftPayload,
    JsonElement? RightPayload);

public sealed class CompareRevisionsQueryHandler : IRequestHandler<CompareRevisionsQuery, Result<RevisionDiffResponse>>
{
    private const int MaxValueLength = 500;

    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public CompareRevisionsQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<RevisionDiffResponse>> Handle(CompareRevisionsQuery q, CancellationToken ct)
    {
        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, q.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<RevisionDiffResponse>("Series không tồn tại.", ResultErrorCodes.NotFound);
        if (!await _auth.IsMemberAsync(q.UserId, workspaceId.Value, ct))
            return AiRunHelpers.Fail<RevisionDiffResponse>("Bạn không phải thành viên của Workspace này.", ResultErrorCodes.Forbidden);

        // BR-03: Scene phải thuộc Series trên route (qua Chapter)
        var inSeries = await _db.Scenes.AsNoTracking().AnyAsync(
            s => s.Id == q.SceneId && _db.Chapters.Any(c => c.Id == s.ChapterId && c.SeriesId == q.SeriesId), ct);
        if (!inSeries)
            return AiRunHelpers.Fail<RevisionDiffResponse>("Scene không thuộc Series.", ResultErrorCodes.NotFound);

        var ids = new[] { q.LeftRevisionId, q.RightRevisionId };
        var revisions = await _db.ContentRevisions.AsNoTracking()
            .Where(r => ids.Contains(r.Id) && r.SceneId == q.SceneId)
            .ToListAsync(ct);
        var left = revisions.FirstOrDefault(r => r.Id == q.LeftRevisionId);
        var right = revisions.FirstOrDefault(r => r.Id == q.RightRevisionId);
        if (left is null || right is null)
            return AiRunHelpers.Fail<RevisionDiffResponse>("Revision không tồn tại hoặc không thuộc Scene.", ResultErrorCodes.NotFound);
        if (left.Kind != right.Kind)
            return AiRunHelpers.Fail<RevisionDiffResponse>(
                $"Chỉ so sánh được hai revision cùng loại ({left.Kind} khác {right.Kind}).", ResultErrorCodes.Validation);

        var diffs = left.ContentHash == right.ContentHash
            ? []
            : left.Kind == ContentRevisionKind.Script
                ? DiffScript(left.PayloadJson, right.PayloadJson)
                : DiffJson(left.PayloadJson, right.PayloadJson);

        return Result<RevisionDiffResponse>.Success(new RevisionDiffResponse(
            new RevisionSideDto(left.Id, left.VersionNumber, left.Kind, left.Summary, left.ContentHash,
                left.SourceSuggestionId, left.CreatedBy, left.CreatedAt),
            new RevisionSideDto(right.Id, right.VersionNumber, right.Kind, right.Summary, right.ContentHash,
                right.SourceSuggestionId, right.CreatedBy, right.CreatedAt),
            diffs,
            AiRunHelpers.ToElement(left.PayloadJson),
            AiRunHelpers.ToElement(right.PayloadJson)));
    }

    /// <summary>
    /// Script có hai dạng payload (mảng dòng khi sửa tay/baseline, object có <c>lines</c> khi từ AI):
    /// <see cref="ScriptLineRules.ParseRevisionLines"/> đọc được cả hai nên so được bản AI với bản sửa tay.
    /// </summary>
    internal static List<DiffEntry> DiffScript(string leftJson, string rightJson)
    {
        var left = ScriptLineRules.ParseRevisionLines(leftJson);
        var right = ScriptLineRules.ParseRevisionLines(rightJson);
        if (left is null || right is null) return DiffJson(leftJson, rightJson);

        var l = left.GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.First());
        var r = right.GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.First());
        var result = new List<DiffEntry>();

        foreach (var line in right.Where(x => !l.ContainsKey(x.LineId)))
            result.Add(new DiffEntry($"line:{line.LineId}", "Added", null, Describe(line)));
        foreach (var line in left.Where(x => !r.ContainsKey(x.LineId)))
            result.Add(new DiffEntry($"line:{line.LineId}", "Removed", Describe(line), null));
        foreach (var old in l.Values)
        {
            if (!r.TryGetValue(old.LineId, out var now)) continue;
            if (old.Dialogue != now.Dialogue || old.StageDirection != now.StageDirection || old.SpeakerId != now.SpeakerId)
                result.Add(new DiffEntry($"line:{old.LineId}", "Modified", Describe(old), Describe(now)));
            else if (old.Order != now.Order)
                result.Add(new DiffEntry($"line:{old.LineId}", "Moved", $"#{old.Order}", $"#{now.Order}"));
        }
        return result;
    }

    private static string Describe(BaselineScriptLine x) =>
        Truncate($"#{x.Order} {x.Dialogue}{(string.IsNullOrWhiteSpace(x.StageDirection) ? "" : $" [{x.StageDirection}]")}")!;

    /// <summary>Layout / ImagePrompt: object so theo khoá cấp cao nhất, mảng so theo chỉ mục. Đủ để biết phần nào đổi.</summary>
    private static List<DiffEntry> DiffJson(string leftJson, string rightJson)
    {
        var result = new List<DiffEntry>();
        try
        {
            using var leftDoc = JsonDocument.Parse(leftJson);
            using var rightDoc = JsonDocument.Parse(rightJson);
            var (lr, rr) = (leftDoc.RootElement, rightDoc.RootElement);

            if (lr.ValueKind == JsonValueKind.Object && rr.ValueKind == JsonValueKind.Object)
            {
                var lp = lr.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
                var rp = rr.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
                foreach (var key in rp.Keys.Except(lp.Keys)) result.Add(new DiffEntry(key, "Added", null, Truncate(rp[key])));
                foreach (var key in lp.Keys.Except(rp.Keys)) result.Add(new DiffEntry(key, "Removed", Truncate(lp[key]), null));
                foreach (var key in lp.Keys.Intersect(rp.Keys).Where(k => lp[k] != rp[k]))
                    result.Add(new DiffEntry(key, "Modified", Truncate(lp[key]), Truncate(rp[key])));
            }
            else if (lr.ValueKind == JsonValueKind.Array && rr.ValueKind == JsonValueKind.Array)
            {
                var la = lr.EnumerateArray().Select(e => e.GetRawText()).ToList();
                var ra = rr.EnumerateArray().Select(e => e.GetRawText()).ToList();
                for (var i = 0; i < Math.Max(la.Count, ra.Count); i++)
                {
                    var lv = i < la.Count ? la[i] : null;
                    var rv = i < ra.Count ? ra[i] : null;
                    if (lv == rv) continue;
                    result.Add(new DiffEntry($"[{i}]", lv is null ? "Added" : rv is null ? "Removed" : "Modified", Truncate(lv), Truncate(rv)));
                }
            }
            else
            {
                result.Add(new DiffEntry("$", "Modified", Truncate(leftJson), Truncate(rightJson)));
            }
        }
        catch (JsonException)
        {
            if (leftJson != rightJson) result.Add(new DiffEntry("$", "Modified", Truncate(leftJson), Truncate(rightJson)));
        }
        return result;
    }

    private static string? Truncate(string? s) =>
        s is null ? null : s.Length <= MaxValueLength ? s : s[..MaxValueLength] + "…";
}
