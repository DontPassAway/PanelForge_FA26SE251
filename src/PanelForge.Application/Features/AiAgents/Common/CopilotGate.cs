using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Workspaces;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Common;

internal sealed record CopilotProvider(string Provider, string Model, WorkspaceAiConfig Config);

/// <summary>
/// Phần dùng chung của mọi task Copilot sync: AI bật + quota Workspace + model được phép, giới hạn lượt/phút theo Series,
/// giải mã key, và ghi step + usage sau khi gọi. Guard nghiệp vụ riêng (vai trò, đối tượng thuộc Series, BR-12) nằm ở từng handler.
/// </summary>
internal static class CopilotGate
{
    public static async Task<Result<CopilotProvider>> ResolveAsync(
        IPanelForgeDbContext db, IAiUsageService usage, AiAgentSettings settings,
        Guid workspaceId, Guid seriesId, Guid userId, string feature, CancellationToken ct)
    {
        var config = await db.WorkspaceAiConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId, ct);
        if (config is null || !config.IsEnabled)
            return Result<CopilotProvider>.Failure("Tính năng AI chưa được bật cho Workspace này.", ResultErrorCodes.AiDisabled);

        string provider, model;
        if (settings.IsMock) (provider, model) = (AiProtocol.ProviderMock, "mock-fixture-v1");
        else
        {
            var availability = await usage.CheckAvailabilityAsync(workspaceId, ct);
            if (!availability.IsAvailable)
            {
                await usage.RecordAsync(new AiUsageEntry(workspaceId, feature, availability.BlockedReason!.Value,
                    ErrorMessage: availability.Message, UserId: userId), ct);
                return Result<CopilotProvider>.Failure(availability.Message ?? "AI không khả dụng.",
                    availability.BlockedReason == AiUsageStatus.QuotaExceeded ? ResultErrorCodes.AiQuotaExceeded : ResultErrorCodes.AiDisabled);
            }
            (provider, model) = (AiProtocol.ProviderGemini, settings.GeminiModel);
            var models = JsonSerializer.Deserialize<List<string>>(config.AllowedModelsJson) ?? [];
            if (!models.Contains(model, StringComparer.OrdinalIgnoreCase))
                return Result<CopilotProvider>.Failure(
                    $"Model '{model}' chưa nằm trong danh sách model được phép của Workspace.", ResultErrorCodes.Validation);
        }

        // ponytail: đếm trên DB nên 2 request song song có thể cùng lọt ở ngưỡng; đủ để chặn lạm dụng.
        var since = DateTime.UtcNow.AddMinutes(-1);
        var recent = await db.AiWorkflowRuns.AsNoTracking().CountAsync(
            r => r.SeriesId == seriesId && r.WorkflowType == AiWorkflowType.Copilot && r.CreatedAt >= since, ct);
        if (recent >= settings.CopilotCallsPerMinutePerSeries)
            return Result<CopilotProvider>.Failure(
                $"Series đã dùng {recent} lượt Copilot trong 1 phút qua (giới hạn {settings.CopilotCallsPerMinutePerSeries}). Thử lại sau ít giây.",
                ResultErrorCodes.AiQuotaExceeded);

        return Result<CopilotProvider>.Success(new CopilotProvider(provider, model, config));
    }

    /// <summary>Key chỉ giải mã trong bộ nhớ cho lần gọi này. Null = Mock; rỗng = giải mã thất bại.</summary>
    public static string? ApiKey(AiAgentSettings settings, IAiKeyProtector protector, CopilotProvider p) =>
        settings.IsMock ? null : protector.Unprotect(p.Config.ApiKeyEncrypted ?? string.Empty) ?? "";

    /// <summary>Ghi step (provenance BR-10) + usage cho một lần gọi Copilot; chưa SaveChanges.</summary>
    public static async Task<AiAgentStep> RecordStepAsync(
        IPanelForgeDbContext db, IAiUsageService usage, AiAgentSettings settings, AiWorkflowRun run, AiAgentType agent,
        CopilotProvider p, string task, CopilotResponse response, object parameters, object input, string? outputJson,
        Guid userId, CancellationToken ct)
    {
        var step = AiAgentStep.Create(run.Id, 1, agent, 0, null,
            response.Succeeded ? AiStepStatus.Succeeded : AiStepStatus.Failed, p.Provider, p.Model,
            response.PromptVersion is { Length: > 0 } pv ? pv : task, response.PromptRef is { Length: > 0 } pr ? pr : task,
            AiJson.Serialize(parameters), AiJson.Serialize(input), outputJson,
            response.StartedAtUtc, response.CompletedAtUtc, response.PromptTokens, response.CompletionTokens,
            response.RepairAttempts, response.ErrorCode, response.ErrorMessage);
        db.AiAgentSteps.Add(step);

        if (!settings.IsMock)
        {
            var record = await usage.RecordAsync(new AiUsageEntry(run.WorkspaceId, $"AiAgents.Copilot.{task}",
                response.Succeeded ? AiUsageStatus.Succeeded
                    : response.ErrorCode == ResultErrorCodes.AiUnavailable ? AiUsageStatus.ProviderUnavailable : AiUsageStatus.Failed,
                Model: p.Model, PromptTokens: response.PromptTokens ?? 0, CompletionTokens: response.CompletionTokens ?? 0,
                ErrorMessage: response.ErrorMessage, DurationMs: step.DurationMs, UserId: userId), ct);
            step.LinkUsageRecord(record.Id);
        }
        return step;
    }

    public static string FailureCode(CopilotResponse r) =>
        r.ErrorCode == ResultErrorCodes.AiUnavailable ? ResultErrorCodes.AiUnavailable : ResultErrorCodes.Validation;
}
