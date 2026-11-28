using PanelForge.Application.Features.AiConfig.Common;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Commands.RunWorkflow;

/// <summary>
/// BR-07 (quyền theo vai trò + assignment), BR-03 (scope Series→Chapter→Scene), BR-11 (version nguồn), BR-12 (chương khoá),
/// BR-15 (Bible tại chương), BR-02/10 (provenance). Loại workflow quyết định agent/chuỗi hợp lệ; quyền KHÔNG lấy từ client.
/// </summary>
public sealed record RunWorkflowCommand(
    Guid SeriesId,
    Guid UserId,
    Guid ChapterId,
    Guid? SceneId,
    string Brief,
    ReadingDirection ReadingDirection,
    long ChapterVersion,
    Guid? BaseScriptRevisionId,
    AiWorkflowType WorkflowType = AiWorkflowType.FullScene,
    IReadOnlyList<Guid>? PanelIds = null,
    Guid? RevisionOfSuggestionId = null,
    bool AppendScript = false) : IRequest<Result<RunWorkflowResponse>>;

public sealed class RunWorkflowCommandHandler : IRequestHandler<RunWorkflowCommand, Result<RunWorkflowResponse>>
{
    private const int MaxBriefLength = 4000;

    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IAiUsageService _usage;
    private readonly IAiKeyProtector _keyProtector;
    private readonly IAiClient _aiClient;
    private readonly AiAgentSettings _settings;
    private readonly IPageRepository _pages;

    public RunWorkflowCommandHandler(
        IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, IAiUsageService usage,
        IAiKeyProtector keyProtector, IAiClient aiClient, AiAgentSettings settings, IPageRepository pages)
    {
        _db = db;
        _auth = auth;
        _usage = usage;
        _keyProtector = keyProtector;
        _aiClient = aiClient;
        _settings = settings;
        _pages = pages;
    }

    public async Task<Result<RunWorkflowResponse>> Handle(RunWorkflowCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Brief) || cmd.Brief.Length > MaxBriefLength)
            return AiRunHelpers.Fail<RunWorkflowResponse>(
                $"Brief là bắt buộc và tối đa {MaxBriefLength} ký tự.", ResultErrorCodes.Validation);
        // Copilot đi qua POST /ai/copilot (sync), không phải workflow batch
        if (!Enum.IsDefined(cmd.WorkflowType) || cmd.WorkflowType is AiWorkflowType.Copilot or AiWorkflowType.Thumbnail or AiWorkflowType.Letterer or AiWorkflowType.Producer)
            return AiRunHelpers.Fail<RunWorkflowResponse>("workflowType không hợp lệ.", ResultErrorCodes.Validation);
        var panelFilter = cmd.PanelIds is { Count: > 0 } ? cmd.PanelIds.ToHashSet() : null;
        if (panelFilter is not null && cmd.WorkflowType != AiWorkflowType.Artist)
            return AiRunHelpers.Fail<RunWorkflowResponse>("panelIds chỉ dùng cho workflow Artist.", ResultErrorCodes.Validation);
        if (cmd.AppendScript && cmd.WorkflowType != AiWorkflowType.Script)
            return AiRunHelpers.Fail<RunWorkflowResponse>("appendScript chỉ dùng cho workflow Script.", ResultErrorCodes.Validation);
        if (!Enum.IsDefined(cmd.ReadingDirection))
            return AiRunHelpers.Fail<RunWorkflowResponse>("readingDirection không hợp lệ.", ResultErrorCodes.Validation);
        var type = cmd.WorkflowType;
        if (AiWorkflowPolicy.NeedsScene(type) && cmd.SceneId is null)
            return AiRunHelpers.Fail<RunWorkflowResponse>("sceneId là bắt buộc với workflow này.", ResultErrorCodes.Validation);

        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, cmd.SeriesId, ct);
        if (workspaceId is null)
            return AiRunHelpers.Fail<RunWorkflowResponse>($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        // Quyền theo vai trò: Backend tự tra từ membership, không tin role/agent do client gửi
        var role = await _auth.GetSeriesMemberRoleAsync(cmd.UserId, cmd.SeriesId, ct);
        var allowedRoles = AiWorkflowPolicy.RunRoles(type);
        if (role is null || !allowedRoles.Contains(role.Value))
            return AiRunHelpers.Fail<RunWorkflowResponse>(
                $"Chỉ {string.Join("/", allowedRoles)} của Workspace mới được chạy nhiệm vụ {AiWorkflowPolicy.Title(type)}.",
                ResultErrorCodes.Forbidden);

        // BR-03: Chapter thuộc Series trên route, Scene thuộc Chapter
        var chapter = await _db.Chapters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cmd.ChapterId && c.SeriesId == cmd.SeriesId, ct);
        if (chapter is null)
            return AiRunHelpers.Fail<RunWorkflowResponse>($"Chapter {cmd.ChapterId} không thuộc Series.", ResultErrorCodes.NotFound);
        Domain.Entities.Content.Scene? scene = null;
        if (AiWorkflowPolicy.NeedsScene(type))
        {
            scene = await _db.Scenes.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == cmd.SceneId && s.ChapterId == cmd.ChapterId, ct);
            if (scene is null)
                return AiRunHelpers.Fail<RunWorkflowResponse>($"Scene {cmd.SceneId} không thuộc Chapter.", ResultErrorCodes.NotFound);
        }

        var brief = cmd.Brief;
        if (cmd.RevisionOfSuggestionId is { } revisionOf)
        {
            var feedback = scene is null ? null : await AiRunHelpers.RevisionFeedbackAsync(_db, cmd.SeriesId, revisionOf, scene.Id, ct);
            if (feedback is null)
                return AiRunHelpers.Fail<RunWorkflowResponse>("Đề xuất cần làm lại không ở trạng thái Yêu cầu sửa trong Scene này.", ResultErrorCodes.Conflict);
            brief = AiRunHelpers.AppendFeedback(brief, feedback, MaxBriefLength);
        }

        // Assignment, stage/khoá chương, và context cần có — cùng bộ luật với API availability
        var facts = await AiWorkflowFacts.LoadAsync(_db, _auth, cmd.UserId, workspaceId.Value, chapter, scene, ct);
        // AI tắt xử lý ở nhánh provider bên dưới (Mock/Gemini có thông điệp + ghi usage riêng)
        var missing = facts.Evaluate(type, cmd.AppendScript).Where(m => m.Code != "AI_DISABLED").ToList();
        if (missing.Count > 0)
            return AiRunHelpers.Fail<RunWorkflowResponse>(
                string.Join(" ", missing.Select(m => m.Message)),
                missing.Any(m => m.Kind == "Permission") ? ResultErrorCodes.Forbidden : ResultErrorCodes.Conflict);

        // BR-11: version nguồn phải khớp trạng thái hiện tại (báo cáo cấp Chapter không dựa trên một revision nội dung)
        Guid? latestScriptRevisionId = null;
        if (scene is not null)
        {
            latestScriptRevisionId = await _db.ContentRevisions.AsNoTracking()
                .Where(r => r.SceneId == scene.Id && r.Kind == ContentRevisionKind.Script)
                .OrderByDescending(r => r.VersionNumber)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);
            if (chapter.VersionVector != cmd.ChapterVersion || latestScriptRevisionId != cmd.BaseScriptRevisionId)
                return AiRunHelpers.Fail<RunWorkflowResponse>(
                    $"Version nguồn đã cũ (chapterVersion hiện tại = {chapter.VersionVector}, scriptRevision hiện tại = {latestScriptRevisionId?.ToString() ?? "null"}). Tải lại rồi chạy lại.",
                    ResultErrorCodes.Conflict);
        }

        // Quyền + trạng thái AI của Workspace.
        var config = await _db.WorkspaceAiConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId, ct);
        if (config is null || !config.IsEnabled)
            return AiRunHelpers.Fail<RunWorkflowResponse>(
                "Tính năng AI chưa được bật cho Workspace này.", ResultErrorCodes.AiDisabled);
        var availability = await _usage.CheckAvailabilityAsync(workspaceId.Value, ct);
        if (!availability.IsAvailable)
        {
            await _usage.RecordAsync(new AiUsageEntry(
                workspaceId.Value, "AiAgents.RunWorkflow", availability.BlockedReason!.Value,
                ErrorMessage: availability.Message, UserId: cmd.UserId), ct);
            return AiRunHelpers.Fail<RunWorkflowResponse>(availability.Message ?? "AI không khả dụng.",
                availability.BlockedReason == AiUsageStatus.QuotaExceeded
                    ? ResultErrorCodes.AiQuotaExceeded : ResultErrorCodes.AiDisabled);
        }
        var cred = await AiCredentials.ResolveAsync(_db, _keyProtector, config!, _settings.GeminiModel, ct);
        var (provider, model) = (cred.Provider, cred.TextModel);
        var allowed = JsonSerializer.Deserialize<List<string>>(config!.AllowedModelsJson) ?? [];
        if (!cred.FromCredential && !allowed.Contains(model, StringComparer.OrdinalIgnoreCase))
            return AiRunHelpers.Fail<RunWorkflowResponse>(
                $"Model '{model}' chưa nằm trong danh sách model được phép của Workspace; Admin cần thêm qua PUT /api/Admin/workspaces/{{id}}/ai-config.",
                ResultErrorCodes.Validation);

        var snapshot = await AiSnapshotBuilder.BuildAsync(_db, _pages, chapter, scene, type, DateTime.UtcNow, ct, panelFilter);
        if (snapshot is not null && cmd.AppendScript) snapshot = snapshot with { AppendScript = true };
        if (snapshot is null)
            return AiRunHelpers.Fail<RunWorkflowResponse>("Series chưa có Series Bible.", ResultErrorCodes.NotFound);
        if (panelFilter is not null && snapshot.Artist?.Panels.Count != panelFilter.Count)
            return AiRunHelpers.Fail<RunWorkflowResponse>(
                "Có khung được chọn không thuộc layout đã duyệt của phân cảnh này.", ResultErrorCodes.Validation);
        if (!WorkflowRules.HasRequiredInput(type.ToString(), snapshot.Inputs))
            return AiRunHelpers.Fail<RunWorkflowResponse>(MissingInputMessage(type), ResultErrorCodes.Conflict);

        // OPS-03/OPS-24: giữ chỗ token và đếm lượt sau cùng, khi mọi cổng khác đã qua, để lượt bị từ chối không tốn gì.
        // Cả workflow nhiều agent tính là một lượt gọi AI của Studio.
        var reserved = _settings.WorkflowReservedTokens;
        var started = await _usage.TryStartRequestAsync(workspaceId.Value, reserved, ct);
        if (!started.IsAvailable)
        {
            await _usage.RecordAsync(new AiUsageEntry(workspaceId.Value, "AiAgents.RunWorkflow", started.BlockedReason!.Value,
                ErrorMessage: started.Message, UserId: cmd.UserId), ct);
            return AiRunHelpers.Fail<RunWorkflowResponse>(started.Message!, ResultErrorCodes.AiQuotaExceeded);
        }

        var run = AiWorkflowRun.Create(
            workspaceId.Value, cmd.SeriesId, cmd.ChapterId, scene?.Id, cmd.UserId, provider, model, brief,
            cmd.ReadingDirection, _settings.MaxRevisionRounds, chapter.VersionVector, latestScriptRevisionId,
            AiJson.Serialize(snapshot), AiJson.Serialize(snapshot.Bible.Select(b => b.RevisionId)), type);
        run.HoldReservation(reserved);
        _db.AiWorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        // Key do Backend giải mã và chỉ gửi qua localhost cho lần chạy này; không lưu, không log.
        var apiKey = cred.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return await FailRunAsync(run, workspaceId.Value, cmd.UserId,
                "Studio chưa có API key dùng được. Admin hãy nhập key ở mục Cấu hình AI.", ct);

        var ack = await _aiClient.StartRunAsync(
            new AiRunJob(run.Id, provider, model, apiKey, run.MaxRevisionRounds, AccountId: cred.AccountId), ct);
        if (!ack.Accepted)
            return await FailRunAsync(run, workspaceId.Value, cmd.UserId, ack.Message ?? "AI service không khả dụng.", ct);

        return Result<RunWorkflowResponse>.Success(new RunWorkflowResponse(run.Id, run.Status, provider, model));
    }

    private static string MissingInputMessage(AiWorkflowType type) => type switch
    {
        AiWorkflowType.Artist => "Layout đã Accept không có panel nào còn tồn tại.",
        _ => "Scene chưa có script để chạy nhiệm vụ này."
    };

    private async Task<Result<RunWorkflowResponse>> FailRunAsync(
        AiWorkflowRun run, Guid workspaceId, Guid userId, string message, CancellationToken ct)
    {
        run.MarkFailed(ResultErrorCodes.AiUnavailable, message);
        await _db.SaveChangesAsync(ct);
        await _usage.RecordAsync(new AiUsageEntry(
            workspaceId, "AiAgents.RunWorkflow", AiUsageStatus.ProviderUnavailable,
            Model: run.Model, ErrorMessage: message, UserId: userId, Provider: run.Provider,
            ReservedTokens: run.TakeReservation()), ct);
        return AiRunHelpers.Fail<RunWorkflowResponse>(message, ResultErrorCodes.AiUnavailable);
    }
}
