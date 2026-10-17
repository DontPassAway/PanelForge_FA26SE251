using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Queries.GetProvenanceReport;

/// <summary>
/// Provenance Report cho một Scene: chuỗi đầy đủ AI Run → Steps → Suggestions → Decisions → Revisions.
/// Cho phép truy nguyên bất kỳ nội dung nào được tạo bởi AI hoặc con người.
/// </summary>
public sealed record GetProvenanceReportQuery(
    Guid SeriesId,
    Guid SceneId,
    Guid UserId
) : IRequest<Result<ProvenanceReportResponse>>;

// ── DTOs ───────────────────────────────────────────────────────────────

/// <summary>Một node trong đồ thị provenance.</summary>
public sealed record ProvenanceNode(
    /// <summary>"Run", "Step", "Suggestion", "Decision", "Revision".</summary>
    string NodeType,
    Guid Id,
    DateTime Timestamp,
    string Summary,
    /// <summary>Chi tiết tuỳ loại node (context snapshot, output, mapping...). Null nếu không có.</summary>
    JsonElement? Details);

/// <summary>Cạnh nối 2 node trong đồ thị provenance.</summary>
public sealed record ProvenanceEdge(
    Guid FromId,
    Guid ToId,
    /// <summary>"produced", "decided", "created", "inputFor", "previousVersion", "originatedFrom".</summary>
    string Relationship);

/// <summary>Thống kê tổng hợp provenance.</summary>
public sealed record ProvenanceStats(
    int TotalRuns,
    int TotalSteps,
    int TotalSuggestions,
    int AcceptedSuggestions,
    int RejectedSuggestions,
    int PendingSuggestions,
    int TotalRevisions,
    int AiOriginatedRevisions,
    int ManualRevisions,
    long TotalPromptTokens,
    long TotalCompletionTokens);

/// <summary>Sự kiện trên timeline.</summary>
public sealed record ProvenanceTimelineEntry(
    DateTime Timestamp,
    /// <summary>"RunStarted", "RunCompleted", "RunFailed", "StepCompleted", "SuggestionCreated", "DecisionMade", "RevisionCreated".</summary>
    string EventType,
    string Summary,
    Guid EntityId,
    Guid? RelatedRunId);

public sealed record ProvenanceReportResponse(
    Guid SceneId,
    ProvenanceStats Stats,
    IReadOnlyList<ProvenanceNode> Nodes,
    IReadOnlyList<ProvenanceEdge> Edges,
    /// <summary>Mọi sự kiện sắp theo thời gian — FE dùng để render timeline.</summary>
    IReadOnlyList<ProvenanceTimelineEntry> Timeline);

// ── Handler ────────────────────────────────────────────────────────────

public sealed class GetProvenanceReportQueryHandler
    : IRequestHandler<GetProvenanceReportQuery, Result<ProvenanceReportResponse>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public GetProvenanceReportQueryHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<ProvenanceReportResponse>> Handle(GetProvenanceReportQuery q, CancellationToken ct)
    {
        // ── 1. Kiểm tra quyền ──
        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, q.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<ProvenanceReportResponse>("Series không tồn tại.", ResultErrorCodes.NotFound);
        if (!await _auth.IsMemberAsync(q.UserId, workspaceId.Value, ct))
            return AiRunHelpers.Fail<ProvenanceReportResponse>("Bạn không phải thành viên.", ResultErrorCodes.Forbidden);

        // Scene phải thuộc Series
        var inSeries = await _db.Scenes.AsNoTracking().AnyAsync(
            s => s.Id == q.SceneId && _db.Chapters.Any(c => c.Id == s.ChapterId && c.SeriesId == q.SeriesId), ct);
        if (!inSeries)
            return AiRunHelpers.Fail<ProvenanceReportResponse>("Scene không thuộc Series.", ResultErrorCodes.NotFound);

        // ── 2. Lấy dữ liệu (5 bảng, đều AsNoTracking) ──

        var runs = await _db.AiWorkflowRuns.AsNoTracking()
            .Where(r => r.SeriesId == q.SeriesId && r.SceneId == q.SceneId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        var runIds = runs.Select(r => r.Id).ToList();

        var steps = await _db.AiAgentSteps.AsNoTracking()
            .Where(s => runIds.Contains(s.RunId))
            .OrderBy(s => s.StartedAtUtc)
            .ToListAsync(ct);

        var suggestions = await _db.AiSuggestions.AsNoTracking()
            .Where(s => s.SceneId == q.SceneId && runIds.Contains(s.RunId))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);
        var suggestionIds = suggestions.Select(s => s.Id).ToList();

        var decisions = await _db.AiSuggestionDecisions.AsNoTracking()
            .Where(d => suggestionIds.Contains(d.SuggestionId))
            .OrderBy(d => d.DecidedAtUtc)
            .ToListAsync(ct);

        var revisions = await _db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == q.SceneId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        // ── 3. Build Nodes & Edges ──

        var nodes = new List<ProvenanceNode>();
        var edges = new List<ProvenanceEdge>();
        var timeline = new List<ProvenanceTimelineEntry>();

        // Runs
        foreach (var run in runs)
        {
            nodes.Add(new ProvenanceNode("Run", run.Id, run.CreatedAt,
                $"[{run.WorkflowType}] {run.Brief} — {run.Provider}/{run.Model}",
                AiRunHelpers.ToElement(run.ContextSnapshotJson)));

            timeline.Add(new ProvenanceTimelineEntry(run.CreatedAt, "RunStarted",
                $"Workflow {run.WorkflowType} bắt đầu: {run.Brief}", run.Id, run.Id));

            if (run.CompletedAtUtc.HasValue)
            {
                var eventType = run.Status == AiRunStatus.Completed ? "RunCompleted" : "RunFailed";
                timeline.Add(new ProvenanceTimelineEntry(run.CompletedAtUtc.Value, eventType,
                    $"Workflow kết thúc: {run.Status}", run.Id, run.Id));
            }
        }

        // Steps
        foreach (var step in steps)
        {
            nodes.Add(new ProvenanceNode("Step", step.Id, step.CompletedAtUtc,
                $"{step.Agent} (round {step.Round}) — {step.Status} — {step.DurationMs}ms",
                AiRunHelpers.ToElement(step.OutputJson)));

            edges.Add(new ProvenanceEdge(step.RunId, step.Id, "produced"));
            if (step.ParentStepId.HasValue)
                edges.Add(new ProvenanceEdge(step.ParentStepId.Value, step.Id, "inputFor"));

            timeline.Add(new ProvenanceTimelineEntry(step.CompletedAtUtc, "StepCompleted",
                $"Agent {step.Agent} hoàn thành (round {step.Round}, {step.DurationMs}ms)", step.Id, step.RunId));
        }

        // Suggestions
        foreach (var sug in suggestions)
        {
            nodes.Add(new ProvenanceNode("Suggestion", sug.Id, sug.CreatedAt,
                $"[{sug.Kind}] {sug.Summary} — {sug.Status}", null));

            edges.Add(new ProvenanceEdge(sug.StepId, sug.Id, "produced"));
            if (sug.ParentSuggestionId.HasValue)
                edges.Add(new ProvenanceEdge(sug.ParentSuggestionId.Value, sug.Id, "dependsOn"));

            timeline.Add(new ProvenanceTimelineEntry(sug.CreatedAt, "SuggestionCreated",
                $"Đề xuất {sug.Kind}: {sug.Summary}", sug.Id, sug.RunId));
        }

        // Decisions
        foreach (var dec in decisions)
        {
            var reason = string.IsNullOrEmpty(dec.Reason) ? "" : $": {dec.Reason}";
            nodes.Add(new ProvenanceNode("Decision", dec.Id, dec.DecidedAtUtc,
                $"{dec.Decision} bởi user {dec.DecidedByUserId}{reason}", null));

            edges.Add(new ProvenanceEdge(dec.SuggestionId, dec.Id, "decided"));
            if (dec.ResultRevisionId.HasValue)
                edges.Add(new ProvenanceEdge(dec.Id, dec.ResultRevisionId.Value, "created"));

            timeline.Add(new ProvenanceTimelineEntry(dec.DecidedAtUtc, "DecisionMade",
                $"Quyết định: {dec.Decision}{reason}", dec.Id, null));
        }

        // Revisions
        foreach (var rev in revisions)
        {
            nodes.Add(new ProvenanceNode("Revision", rev.Id, rev.CreatedAt,
                $"v{rev.VersionNumber} [{rev.Kind}] {rev.Summary}",
                AiRunHelpers.ToElement(rev.MappingJson)));

            if (rev.PreviousRevisionId.HasValue)
                edges.Add(new ProvenanceEdge(rev.PreviousRevisionId.Value, rev.Id, "previousVersion"));
            if (rev.SourceSuggestionId.HasValue)
                edges.Add(new ProvenanceEdge(rev.SourceSuggestionId.Value, rev.Id, "originatedFrom"));

            var origin = rev.SourceSuggestionId.HasValue ? "từ AI" : "thủ công";
            timeline.Add(new ProvenanceTimelineEntry(rev.CreatedAt, "RevisionCreated",
                $"Revision v{rev.VersionNumber} [{rev.Kind}] ({origin}): {rev.Summary}", rev.Id, null));
        }

        // ── 4. Sắp xếp timeline theo thời gian ──
        timeline.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // ── 5. Thống kê ──
        var stats = new ProvenanceStats(
            TotalRuns: runs.Count,
            TotalSteps: steps.Count,
            TotalSuggestions: suggestions.Count,
            AcceptedSuggestions: suggestions.Count(s => s.Status == AiSuggestionStatus.Accepted),
            RejectedSuggestions: suggestions.Count(s => s.Status == AiSuggestionStatus.Rejected),
            PendingSuggestions: suggestions.Count(s => s.Status == AiSuggestionStatus.Pending),
            TotalRevisions: revisions.Count,
            AiOriginatedRevisions: revisions.Count(r => r.SourceSuggestionId.HasValue),
            ManualRevisions: revisions.Count(r => !r.SourceSuggestionId.HasValue),
            TotalPromptTokens: steps.Sum(s => s.PromptTokens ?? 0),
            TotalCompletionTokens: steps.Sum(s => s.CompletionTokens ?? 0));

        return Result<ProvenanceReportResponse>.Success(
            new ProvenanceReportResponse(q.SceneId, stats, nodes, edges, timeline));
    }
}
