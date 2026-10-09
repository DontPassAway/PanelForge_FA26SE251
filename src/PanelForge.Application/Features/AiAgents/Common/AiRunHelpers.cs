using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Common;

internal static class AiRunHelpers
{
    public static Result<T> Fail<T>(string message, string? code = null) => Result<T>.Failure(message, code);

    public static RunSnapshot? ReadSnapshot(AiWorkflowRun run) =>
        JsonSerializer.Deserialize<RunSnapshot>(run.ContextSnapshotJson, AiJson.Options);

    public static IReadOnlyList<Guid> ReadGuidList(string json) =>
        JsonSerializer.Deserialize<List<Guid>>(json, AiJson.Options) ?? [];

    public static JsonElement? ToElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    public static AiStepDto ToDto(AiAgentStep s) => new(
        s.Id, s.StepIndex, s.Agent, s.Round, s.ParentStepId, s.Status, s.Provider, s.Model, s.PromptVersion, s.PromptRef,
        ToElement(s.ParametersJson), ToElement(s.InputJson), ToElement(s.OutputJson),
        s.StartedAtUtc, s.CompletedAtUtc, s.DurationMs, s.PromptTokens, s.CompletionTokens, s.RepairAttempts,
        s.ErrorCode, s.ErrorMessage);

    public static AiDecisionDto ToDto(AiSuggestionDecision d) => new(
        d.Id, d.Decision, d.DecidedByUserId, d.Reason, d.DecidedAtUtc, d.ChapterVersionAtDecision, d.ResultRevisionId,
        d.ModificationLevel);

    /// <param name="viewerRole">Vai trò của người gọi API trong Workspace (null = không phải thành viên) để tính <c>CanDecide</c>.</param>
    public static AiSuggestionDto ToDto(AiSuggestion s, IEnumerable<AiSuggestionDecision> decisions, WorkspaceRole? viewerRole)
    {
        var roles = AiWorkflowPolicy.ReviewRoles(s.Kind);
        return new AiSuggestionDto(
            s.Id, s.RunId, s.StepId, s.SceneId, s.Kind, s.Status, s.Summary, ToElement(s.PayloadJson), s.PayloadHash,
            s.BaseChapterVersion, s.BaseScriptRevisionId, ReadGuidList(s.BibleRevisionIdsJson), s.ParentSuggestionId,
            s.HasBlockingFindings, s.CreatedAt, decisions.OrderBy(d => d.DecidedAtUtc).Select(ToDto).ToList(),
            roles, s.Status == AiSuggestionStatus.Pending && viewerRole is { } r && roles.Contains(r));
    }

    /// <summary>Quyền đọc: thành viên Workspace chứa Series (BR-07, BR-23). Run/suggestion phải thuộc đúng Series trên route.</summary>
    public static async Task<Guid?> WorkspaceOfSeriesAsync(IPanelForgeDbContext db, Guid seriesId, CancellationToken ct) =>
        await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => (Guid?)s.WorkspaceId).FirstOrDefaultAsync(ct);
}