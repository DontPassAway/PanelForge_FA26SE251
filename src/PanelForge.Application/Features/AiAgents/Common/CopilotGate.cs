using PanelForge.Application.Features.AiConfig.Common;
using System.Text;
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

internal sealed record CopilotProvider(string Provider, string Model, WorkspaceAiConfig Config, string? ApiKey = null, string? AccountId = null,
    long ReservedTokens = 0)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Provider = {Provider}, Model = {Model}, ApiKey = ***, AccountId = {AccountId}");
        return true;
    }
}

/// <summary>
/// Phần dùng chung của mọi task Copilot sync: AI bật + quota Workspace + model được phép, giới hạn lượt/phút theo Series,
/// giải mã key, và ghi step + usage sau khi gọi. Guard nghiệp vụ riêng (vai trò, đối tượng thuộc Series, BR-12) nằm ở từng handler.
/// </summary>
internal static class CopilotGate
{
    public static async Task<Result<CopilotProvider>> ResolveAsync(
        IPanelForgeDbContext db, IAiUsageService usage, AiAgentSettings settings, IAiKeyProtector protector,
        Guid workspaceId, Guid seriesId, Guid userId, string feature, CancellationToken ct)
    {
        var config = await db.WorkspaceAiConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId, ct);
        if (config is null || !config.IsEnabled)
            return Result<CopilotProvider>.Failure("Tính năng AI chưa được bật cho Workspace này.", ResultErrorCodes.AiDisabled);

        var availability = await usage.CheckAvailabilityAsync(workspaceId, ct);
        if (!availability.IsAvailable)
        {
            await usage.RecordAsync(new AiUsageEntry(workspaceId, feature, availability.BlockedReason!.Value,
                ErrorMessage: availability.Message, UserId: userId), ct);
            return Result<CopilotProvider>.Failure(availability.Message ?? "AI không khả dụng.",
                availability.BlockedReason == AiUsageStatus.QuotaExceeded ? ResultErrorCodes.AiQuotaExceeded : ResultErrorCodes.AiDisabled);
        }
        var cred = await AiCredentials.ResolveAsync(db, protector, config, settings.GeminiModel, ct);
        var (provider, model) = (cred.Provider, cred.TextModel);
        var models = JsonSerializer.Deserialize<List<string>>(config.AllowedModelsJson) ?? [];
        // Credential Admin khai báo đã chọn model riêng: không cần nằm trong AllowedModels (danh sách cho key Gemini cũ)
        if (!cred.FromCredential && !models.Contains(model, StringComparer.OrdinalIgnoreCase))
            return Result<CopilotProvider>.Failure(
                $"Model '{model}' chưa nằm trong danh sách model được phép của Workspace.", ResultErrorCodes.Validation);

        // ponytail: đếm trên DB nên 2 request song song có thể cùng lọt ở ngưỡng; đủ để chặn lạm dụng.
        var since = DateTime.UtcNow.AddMinutes(-1);
        var recent = await db.AiWorkflowRuns.AsNoTracking().CountAsync(
            r => r.SeriesId == seriesId && r.WorkflowType == AiWorkflowType.Copilot && r.CreatedAt >= since, ct);
        if (recent >= settings.CopilotCallsPerMinutePerSeries)
            return Result<CopilotProvider>.Failure(
                $"Series đã dùng {recent} lượt Copilot trong 1 phút qua (giới hạn {settings.CopilotCallsPerMinutePerSeries}). Thử lại sau ít giây.",
                ResultErrorCodes.AiQuotaExceeded);

        // OPS-03/OPS-24: giữ chỗ token và đếm lượt sau cùng, khi mọi cổng khác đã qua, để lượt bị từ chối không tốn gì
        var reserved = settings.CopilotReservedTokens;
        var started = await usage.TryStartRequestAsync(workspaceId, reserved, ct);
        if (!started.IsAvailable)
        {
            await usage.RecordAsync(new AiUsageEntry(workspaceId, feature, started.BlockedReason!.Value,
                ErrorMessage: started.Message, UserId: userId), ct);
            return Result<CopilotProvider>.Failure(started.Message!, ResultErrorCodes.AiQuotaExceeded);
        }

        return Result<CopilotProvider>.Success(new CopilotProvider(provider, model, config, cred.ApiKey, cred.AccountId, reserved));
    }

    /// <summary>Key chỉ giải mã trong bộ nhớ cho lần gọi này; rỗng = Studio chưa có key dùng được.</summary>
    public static string ApiKey(IAiKeyProtector protector, CopilotProvider p) => p.ApiKey ?? "";

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

        var record = await usage.RecordAsync(new AiUsageEntry(run.WorkspaceId, $"AiAgents.Copilot.{task}",
            response.Succeeded ? AiUsageStatus.Succeeded
                : response.ErrorCode == ResultErrorCodes.AiUnavailable ? AiUsageStatus.ProviderUnavailable : AiUsageStatus.Failed,
            Model: p.Model, PromptTokens: response.PromptTokens ?? 0, CompletionTokens: response.CompletionTokens ?? 0,
            ErrorMessage: response.ErrorMessage, DurationMs: step.DurationMs, UserId: userId, Provider: p.Provider,
            ReservedTokens: p.ReservedTokens), ct);
        step.LinkUsageRecord(record.Id);
        return step;
    }

    public static string FailureCode(CopilotResponse r) =>
        r.ErrorCode switch
        {
            ResultErrorCodes.AiUnavailable or "PROVIDER_UNAVAILABLE" or "PROVIDER_ERROR" or "RUN_TIMEOUT" => ResultErrorCodes.AiUnavailable,
            "INVALID_OUTPUT" => ResultErrorCodes.AiInvalidOutput, // lỗi của model, không phải dữ liệu người dùng nhập
            _ => ResultErrorCodes.Validation
        };
}
