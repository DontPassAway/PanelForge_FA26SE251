using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;

namespace PanelForge.Application.Features.AiAgents.Queries.CompareRevisions;

/// <summary>
/// So sánh payload giữa 2 revision của cùng Scene. Trả payload 2 bên
/// kèm danh sách thay đổi (added / removed / modified) ở top-level.
/// </summary>
public sealed record CompareRevisionsQuery(
    Guid SeriesId,
    Guid SceneId,
    Guid LeftRevisionId,
    Guid RightRevisionId,
    Guid UserId
) : IRequest<Result<RevisionDiffResponse>>;

// ── DTOs ───────────────────────────────────────────────────────────────

public sealed record RevisionSideDto(
    Guid Id,
    int VersionNumber,
    string Kind,
    string Summary,
    string ContentHash,
    Guid? SourceSuggestionId,
    DateTime CreatedAt);

public sealed record DiffEntry(
    /// <summary>JSON path hoặc chỉ mục mảng, ví dụ "lines", "[2]", "panels".</summary>
    string Path,
    /// <summary>"Added", "Removed", "Modified".</summary>
    string ChangeType,
    string? OldValue,
    string? NewValue);

public sealed record RevisionDiffResponse(
    RevisionSideDto Left,
    RevisionSideDto Right,
    IReadOnlyList<DiffEntry> Differences,
    /// <summary>Payload JSON đầy đủ 2 bên để FE tự render diff nếu cần.</summary>
    JsonElement LeftPayload,
    JsonElement RightPayload);

// ── Handler ────────────────────────────────────────────────────────────

public sealed class CompareRevisionsQueryHandler
    : IRequestHandler<CompareRevisionsQuery, Result<RevisionDiffResponse>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public CompareRevisionsQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<RevisionDiffResponse>> Handle(CompareRevisionsQuery q, CancellationToken ct)
    {
        // ── 1. Kiểm tra quyền ──
        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, q.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<RevisionDiffResponse>("Series không tồn tại.", ResultErrorCodes.NotFound);
        if (!await _auth.IsMemberAsync(q.UserId, workspaceId.Value, ct))
            return AiRunHelpers.Fail<RevisionDiffResponse>("Bạn không phải thành viên.", ResultErrorCodes.Forbidden);

        // ── 2. Lấy 2 revision cùng lúc ──
        var ids = new[] { q.LeftRevisionId, q.RightRevisionId };
        var revisions = await _db.ContentRevisions.AsNoTracking()
            .Where(r => ids.Contains(r.Id) && r.SceneId == q.SceneId)
            .ToListAsync(ct);

        var left = revisions.FirstOrDefault(r => r.Id == q.LeftRevisionId);
        var right = revisions.FirstOrDefault(r => r.Id == q.RightRevisionId);

        if (left is null)
            return AiRunHelpers.Fail<RevisionDiffResponse>("Revision trái không tồn tại hoặc không thuộc Scene.", ResultErrorCodes.NotFound);
        if (right is null)
            return AiRunHelpers.Fail<RevisionDiffResponse>("Revision phải không tồn tại hoặc không thuộc Scene.", ResultErrorCodes.NotFound);

        // ── 3. Nếu hash giống nhau → nội dung giống, trả về diff trống ──
        var diffs = left.ContentHash == right.ContentHash
            ? new List<DiffEntry>()
            : ComputeJsonDiff(left.PayloadJson, right.PayloadJson);

        var leftPayload = AiRunHelpers.ToElement(left.PayloadJson) ?? default;
        var rightPayload = AiRunHelpers.ToElement(right.PayloadJson) ?? default;

        return Result<RevisionDiffResponse>.Success(new RevisionDiffResponse(
            new RevisionSideDto(left.Id, left.VersionNumber, left.Kind.ToString(),
                left.Summary, left.ContentHash, left.SourceSuggestionId, left.CreatedAt),
            new RevisionSideDto(right.Id, right.VersionNumber, right.Kind.ToString(),
                right.Summary, right.ContentHash, right.SourceSuggestionId, right.CreatedAt),
            diffs,
            leftPayload,
            rightPayload));
    }

    // ── JSON Diff ──────────────────────────────────────────────────────

    /// <summary>
    /// Diff đơn giản: phân biệt Object vs Array.
    /// - Object: so sánh top-level keys.
    /// - Array (script lines / layout panels): so sánh từng phần tử theo chỉ mục.
    /// Đủ dùng cho payload script lines và layout, FE có thể render diff phía client
    /// nếu cần sâu hơn (leftPayload + rightPayload luôn được trả).
    /// </summary>
    private static List<DiffEntry> ComputeJsonDiff(string leftJson, string rightJson)
    {
        var result = new List<DiffEntry>();
        try
        {
            using var leftDoc = JsonDocument.Parse(leftJson);
            using var rightDoc = JsonDocument.Parse(rightJson);

            if (leftDoc.RootElement.ValueKind == JsonValueKind.Object
                && rightDoc.RootElement.ValueKind == JsonValueKind.Object)
            {
                DiffObjects(leftDoc.RootElement, rightDoc.RootElement, result);
            }
            else if (leftDoc.RootElement.ValueKind == JsonValueKind.Array
                     && rightDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                DiffArrays(leftDoc.RootElement, rightDoc.RootElement, result);
            }
            else
            {
                // Kiểu gốc khác nhau (object vs array) → toàn bộ khác
                result.Add(new DiffEntry("$", "Modified", Truncate(leftJson), Truncate(rightJson)));
            }
        }
        catch (JsonException)
        {
            // Fallback: raw string compare
            if (leftJson != rightJson)
                result.Add(new DiffEntry("$", "Modified", Truncate(leftJson), Truncate(rightJson)));
        }
        return result;
    }

    private static void DiffObjects(JsonElement left, JsonElement right, List<DiffEntry> result)
    {
        var leftProps = new Dictionary<string, string>();
        foreach (var prop in left.EnumerateObject())
            leftProps[prop.Name] = prop.Value.GetRawText();

        var rightProps = new Dictionary<string, string>();
        foreach (var prop in right.EnumerateObject())
            rightProps[prop.Name] = prop.Value.GetRawText();

        foreach (var key in rightProps.Keys.Except(leftProps.Keys))
            result.Add(new DiffEntry(key, "Added", null, Truncate(rightProps[key])));

        foreach (var key in leftProps.Keys.Except(rightProps.Keys))
            result.Add(new DiffEntry(key, "Removed", Truncate(leftProps[key]), null));

        foreach (var key in leftProps.Keys.Intersect(rightProps.Keys))
        {
            if (leftProps[key] != rightProps[key])
                result.Add(new DiffEntry(key, "Modified", Truncate(leftProps[key]), Truncate(rightProps[key])));
        }
    }

    private static void DiffArrays(JsonElement left, JsonElement right, List<DiffEntry> result)
    {
        var leftArr = left.EnumerateArray().ToList();
        var rightArr = right.EnumerateArray().ToList();
        var max = Math.Max(leftArr.Count, rightArr.Count);

        for (var i = 0; i < max; i++)
        {
            var lVal = i < leftArr.Count ? leftArr[i].GetRawText() : null;
            var rVal = i < rightArr.Count ? rightArr[i].GetRawText() : null;
            if (lVal == rVal) continue;

            var change = lVal is null ? "Added" : rVal is null ? "Removed" : "Modified";
            result.Add(new DiffEntry($"[{i}]", change, Truncate(lVal), Truncate(rVal)));
        }
    }

    private static string? Truncate(string? s, int max = 500)
        => s is null ? null : s.Length <= max ? s : s[..max] + "…";
}
