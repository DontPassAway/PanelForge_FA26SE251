using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.Chapters.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Queries.GetProvenanceReport;

/// <summary>
/// UC-19 / CF4 bước 3 và 8: Provenance Report của một Chapter (lọc thêm theo Scene nếu cần). Dựng chuỗi
/// Run → Step (agent, model, prompt version, tham số) → Suggestion (revision script/Bible đầu vào) → Decision (ai quyết, mức sửa)
/// → Content Revision → Snapshot đã duyệt, để trả lời "agent nào, từ revision nào, tạo ra nội dung này và ai duyệt" (BR-10).
/// Producer xem; Editor xem chỉ đọc. Chỉ đọc dữ liệu append-only, không ghi gì.
/// </summary>
public sealed record GetProvenanceReportQuery(Guid SeriesId, Guid ChapterId, Guid? SceneId, Guid UserId)
    : IRequest<Result<ProvenanceReportResponse>>;

/// <param name="NodeType">Run, Step, Suggestion, Decision, Revision, Snapshot.</param>
/// <param name="Details">Trường provenance theo loại node (model, promptRef, tham số, người quyết, mức sửa, hash...).</param>
public sealed record ProvenanceNode(string NodeType, Guid Id, DateTime Timestamp, string Summary, object? Details);

/// <param name="Relationship">
/// produced (Run→Step, Step→Suggestion), inputFor (Step→Step), dependsOn (Suggestion cha→con), basedOn (Revision→Suggestion),
/// decided (Suggestion→Decision), created (Decision→Revision), previousVersion, originatedFrom (Suggestion→Revision),
/// restoredFrom (Revision cũ→Revision khôi phục), includedIn (Revision→Snapshot).
/// </param>
public sealed record ProvenanceEdge(Guid FromId, Guid ToId, string Relationship);

public sealed record ProvenanceStats(
    int TotalRuns,
    int TotalSteps,
    int TotalSuggestions,
    int AcceptedSuggestions,
    int RejectedSuggestions,
    int PendingSuggestions,
    int ModifiedByHumanDecisions,
    int TotalRevisions,
    int AiOriginatedRevisions,
    int ManualRevisions,
    int TotalSnapshots,
    long TotalPromptTokens,
    long TotalCompletionTokens);

/// <param name="EventType">RunStarted, RunCompleted, RunFailed, StepCompleted, SuggestionCreated, DecisionMade, RevisionCreated, SnapshotCreated.</param>
public sealed record ProvenanceTimelineEntry(DateTime Timestamp, string EventType, string Summary, Guid EntityId, Guid? RelatedRunId);

/// <param name="Timeline">Mọi sự kiện sắp theo thời gian, cũ nhất trước — FE dùng để render dòng thời gian.</param>
public sealed record ProvenanceReportResponse(
    Guid ChapterId,
    Guid? SceneId,
    DateTime GeneratedAtUtc,
    ProvenanceStats Stats,
    IReadOnlyList<ProvenanceNode> Nodes,
    IReadOnlyList<ProvenanceEdge> Edges,
    IReadOnlyList<ProvenanceTimelineEntry> Timeline);

public sealed class GetProvenanceReportQueryHandler : IRequestHandler<GetProvenanceReportQuery, Result<ProvenanceReportResponse>>
{
    private static readonly WorkspaceRole[] ViewerRoles = [WorkspaceRole.Producer, WorkspaceRole.Editor];

    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public GetProvenanceReportQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<ProvenanceReportResponse>> Handle(GetProvenanceReportQuery q, CancellationToken ct)
    {
        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, q.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<ProvenanceReportResponse>("Series không tồn tại.", ResultErrorCodes.NotFound);
        if (!await _auth.HasWorkspaceRoleAsync(q.UserId, workspaceId.Value, ViewerRoles, ct))
            return AiRunHelpers.Fail<ProvenanceReportResponse>(
                "Chỉ Producer hoặc Editor của Workspace xem được Provenance Report.", ResultErrorCodes.Forbidden);

        // BR-03: Chapter thuộc Series, Scene (nếu lọc) thuộc Chapter
        if (!await _db.Chapters.AsNoTracking().AnyAsync(c => c.Id == q.ChapterId && c.SeriesId == q.SeriesId, ct))
            return AiRunHelpers.Fail<ProvenanceReportResponse>("Chapter không thuộc Series.", ResultErrorCodes.NotFound);
        if (q.SceneId is { } sceneId &&
            !await _db.Scenes.AsNoTracking().AnyAsync(s => s.Id == sceneId && s.ChapterId == q.ChapterId, ct))
            return AiRunHelpers.Fail<ProvenanceReportResponse>("Scene không thuộc Chapter.", ResultErrorCodes.NotFound);

        // ── Dữ liệu (append-only, chỉ đọc) ──
        var runsQuery = _db.AiWorkflowRuns.AsNoTracking().Where(r => r.ChapterId == q.ChapterId);
        var revisionsQuery = _db.ContentRevisions.AsNoTracking().Where(r => r.ChapterId == q.ChapterId);
        if (q.SceneId is { } sid)
        {
            runsQuery = runsQuery.Where(r => r.SceneId == sid);
            revisionsQuery = revisionsQuery.Where(r => r.SceneId == sid);
        }

        var runs = await runsQuery.OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var runIds = runs.Select(r => r.Id).ToList();
        var steps = await _db.AiAgentSteps.AsNoTracking()
            .Where(s => runIds.Contains(s.RunId)).OrderBy(s => s.StartedAtUtc).ToListAsync(ct);
        var suggestions = await _db.AiSuggestions.AsNoTracking()
            .Where(s => runIds.Contains(s.RunId)).OrderBy(s => s.CreatedAt).ToListAsync(ct);
        var suggestionIds = suggestions.Select(s => s.Id).ToList();
        var decisions = await _db.AiSuggestionDecisions.AsNoTracking()
            .Where(d => suggestionIds.Contains(d.SuggestionId)).OrderBy(d => d.DecidedAtUtc).ToListAsync(ct);
        var revisions = await revisionsQuery.OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var snapshots = await _db.ChapterSnapshots.AsNoTracking()
            .Where(s => s.ChapterId == q.ChapterId).OrderBy(s => s.SnapshotNumber).ToListAsync(ct);

        var userIds = runs.Select(r => r.RequestedByUserId)
            .Concat(decisions.Select(d => d.DecidedByUserId))
            .Concat(snapshots.Select(s => s.CreatedByUserId))
            .Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        string Name(Guid id) => names.GetValueOrDefault(id) ?? id.ToString();

        var nodes = new List<ProvenanceNode>();
        var edges = new List<ProvenanceEdge>();
        var timeline = new List<ProvenanceTimelineEntry>();

        foreach (var run in runs)
        {
            nodes.Add(new ProvenanceNode("Run", run.Id, run.CreatedAt, $"[{run.WorkflowType}] {run.Brief}", new
            {
                run.WorkflowType, run.Status, run.Provider, run.Model, run.SceneId,
                requestedBy = Name(run.RequestedByUserId),
                run.BaseChapterVersion, run.BaseScriptRevisionId,
                bibleRevisionIds = AiRunHelpers.ReadGuidList(run.BibleRevisionIdsJson),
                run.RevisionRounds, run.ErrorCode
            }));
            timeline.Add(new ProvenanceTimelineEntry(run.CreatedAt, "RunStarted",
                $"{Name(run.RequestedByUserId)} chạy workflow {run.WorkflowType}: {run.Brief}", run.Id, run.Id));
            if (run.CompletedAtUtc is { } done)
                timeline.Add(new ProvenanceTimelineEntry(done, run.Status == AiRunStatus.Completed ? "RunCompleted" : "RunFailed",
                    $"Workflow kết thúc: {run.Status}", run.Id, run.Id));
        }

        // Step: không trả Input/Output (nặng, đã có ở GET /ai/runs/{runId}); giữ đủ trường để truy agent/model/prompt
        foreach (var step in steps)
        {
            nodes.Add(new ProvenanceNode("Step", step.Id, step.CompletedAtUtc, $"{step.Agent} (vòng {step.Round}) — {step.Status}", new
            {
                step.Agent, step.Round, step.Status, step.Provider, step.Model, step.PromptVersion, step.PromptRef,
                parameters = AiRunHelpers.ToElement(step.ParametersJson),
                step.PromptTokens, step.CompletionTokens, step.RepairAttempts, step.DurationMs, step.ErrorCode
            }));
            edges.Add(new ProvenanceEdge(step.RunId, step.Id, "produced"));
            if (step.ParentStepId is { } parent) edges.Add(new ProvenanceEdge(parent, step.Id, "inputFor"));
            timeline.Add(new ProvenanceTimelineEntry(step.CompletedAtUtc, "StepCompleted",
                $"Agent {step.Agent} ({step.Model}, prompt {step.PromptVersion}) — {step.Status}", step.Id, step.RunId));
        }

        foreach (var sug in suggestions)
        {
            nodes.Add(new ProvenanceNode("Suggestion", sug.Id, sug.CreatedAt, $"[{sug.Kind}] {sug.Summary}", new
            {
                sug.Kind, sug.Status, sug.SceneId, sug.PayloadHash, sug.BaseChapterVersion, sug.BaseScriptRevisionId,
                bibleRevisionIds = AiRunHelpers.ReadGuidList(sug.BibleRevisionIdsJson), sug.HasBlockingFindings
            }));
            edges.Add(new ProvenanceEdge(sug.StepId, sug.Id, "produced"));
            if (sug.ParentSuggestionId is { } parent) edges.Add(new ProvenanceEdge(parent, sug.Id, "dependsOn"));
            if (sug.BaseScriptRevisionId is { } baseRevision) edges.Add(new ProvenanceEdge(baseRevision, sug.Id, "basedOn"));
            timeline.Add(new ProvenanceTimelineEntry(sug.CreatedAt, "SuggestionCreated",
                $"Đề xuất {sug.Kind}: {sug.Summary}", sug.Id, sug.RunId));
        }

        var runOfSuggestion = suggestions.ToDictionary(s => s.Id, s => s.RunId);
        foreach (var dec in decisions)
        {
            var reason = string.IsNullOrWhiteSpace(dec.Reason) ? "" : $": {dec.Reason}";
            nodes.Add(new ProvenanceNode("Decision", dec.Id, dec.DecidedAtUtc, $"{dec.Decision} bởi {Name(dec.DecidedByUserId)}{reason}", new
            {
                dec.Decision, decidedByUserId = dec.DecidedByUserId, decidedBy = Name(dec.DecidedByUserId), dec.Reason,
                dec.ModificationLevel, dec.ChapterVersionAtDecision, dec.ResultRevisionId
            }));
            edges.Add(new ProvenanceEdge(dec.SuggestionId, dec.Id, "decided"));
            if (dec.ResultRevisionId is { } created) edges.Add(new ProvenanceEdge(dec.Id, created, "created"));
            timeline.Add(new ProvenanceTimelineEntry(dec.DecidedAtUtc, "DecisionMade",
                $"{Name(dec.DecidedByUserId)} quyết định {dec.Decision}{reason}", dec.Id, runOfSuggestion.GetValueOrDefault(dec.SuggestionId)));
        }

        foreach (var rev in revisions)
        {
            var mapping = AiRunHelpers.ToElement(rev.MappingJson);
            nodes.Add(new ProvenanceNode("Revision", rev.Id, rev.CreatedAt, $"v{rev.VersionNumber} [{rev.Kind}] {rev.Summary}", new
            {
                rev.Kind, rev.VersionNumber, rev.SceneId, rev.ContentHash, rev.CreatedBy,
                origin = rev.SourceSuggestionId is null ? "Human" : "AI", rev.SourceSuggestionId, mapping
            }));
            if (rev.PreviousRevisionId is { } previous) edges.Add(new ProvenanceEdge(previous, rev.Id, "previousVersion"));
            if (rev.SourceSuggestionId is { } source) edges.Add(new ProvenanceEdge(source, rev.Id, "originatedFrom"));
            // RestoreScriptRevisionCommand ghi nguồn khôi phục vào mapping
            if (mapping is { ValueKind: JsonValueKind.Object } m &&
                m.TryGetProperty("restoredFromRevisionId", out var from) && from.TryGetGuid(out var fromId))
                edges.Add(new ProvenanceEdge(fromId, rev.Id, "restoredFrom"));
            timeline.Add(new ProvenanceTimelineEntry(rev.CreatedAt, "RevisionCreated",
                $"Revision v{rev.VersionNumber} [{rev.Kind}] ({(rev.SourceSuggestionId is null ? "người" : "từ AI")}): {rev.Summary}", rev.Id, null));
        }

        var revisionIds = revisions.Select(r => r.Id).ToHashSet();
        foreach (var snap in snapshots)
        {
            nodes.Add(new ProvenanceNode("Snapshot", snap.Id, snap.CreatedAt, $"Bản duyệt #{snap.SnapshotNumber}", new
            {
                snap.SnapshotNumber, snap.ChapterVersion, approvedBy = Name(snap.CreatedByUserId), snap.ManifestHash
            }));
            var manifest = AiJson.Deserialize<SnapshotManifest>(snap.ManifestJson);
            foreach (var scriptRevision in manifest?.ScriptRevisionIds ?? [])
                if (revisionIds.Contains(scriptRevision)) edges.Add(new ProvenanceEdge(scriptRevision, snap.Id, "includedIn"));
            timeline.Add(new ProvenanceTimelineEntry(snap.CreatedAt, "SnapshotCreated",
                $"{Name(snap.CreatedByUserId)} duyệt chương, tạo bản #{snap.SnapshotNumber}", snap.Id, null));
        }

        timeline.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        var stats = new ProvenanceStats(
            TotalRuns: runs.Count,
            TotalSteps: steps.Count,
            TotalSuggestions: suggestions.Count,
            AcceptedSuggestions: suggestions.Count(s => s.Status == AiSuggestionStatus.Accepted),
            RejectedSuggestions: suggestions.Count(s => s.Status == AiSuggestionStatus.Rejected),
            PendingSuggestions: suggestions.Count(s => s.Status == AiSuggestionStatus.Pending),
            ModifiedByHumanDecisions: decisions.Count(d => d.ModificationLevel != "None"),
            TotalRevisions: revisions.Count,
            AiOriginatedRevisions: revisions.Count(r => r.SourceSuggestionId.HasValue),
            ManualRevisions: revisions.Count(r => !r.SourceSuggestionId.HasValue),
            TotalSnapshots: snapshots.Count,
            TotalPromptTokens: steps.Sum(s => s.PromptTokens ?? 0),
            TotalCompletionTokens: steps.Sum(s => s.CompletionTokens ?? 0));

        return Result<ProvenanceReportResponse>.Success(new ProvenanceReportResponse(
            q.ChapterId, q.SceneId, DateTime.UtcNow, stats, nodes, edges, timeline));
    }
}
