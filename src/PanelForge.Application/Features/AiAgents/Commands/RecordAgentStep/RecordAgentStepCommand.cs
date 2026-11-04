using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.Consistency.Common;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Commands.RecordAgentStep;

/// <summary>
/// Callback nội bộ từ AI service sau MỖI step. Phạm vi (workspace/series/chapter/scene/user) luôn lấy từ run đã lưu,
/// không lấy từ request. Idempotent theo (RunId, StepIndex): retry không nhân đôi step hay usage.
/// Chuỗi step hợp lệ do <see cref="WorkflowRules"/> theo loại workflow của run (AI service chỉ là bên đề xuất, Backend kiểm lại).
/// BR-01/10 (step append-only), BR-02 (suggestion), BR-18 (audit qua SaveChanges).
/// </summary>
public sealed record RecordAgentStepCommand(Guid RunId, AgentStepReport Report) : IRequest<Result<AgentStepAck>>;

public sealed class RecordAgentStepCommandHandler : IRequestHandler<RecordAgentStepCommand, Result<AgentStepAck>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IAiUsageService _usage;
    private readonly IAiStreamPublisher? _streamPublisher;

    public RecordAgentStepCommandHandler(IPanelForgeDbContext db, IAiUsageService usage, IAiStreamPublisher? streamPublisher = null)
    {
        _db = db;
        _usage = usage;
        _streamPublisher = streamPublisher;
    }

    public async Task<Result<AgentStepAck>> Handle(RecordAgentStepCommand cmd, CancellationToken ct)
    {
        var run = await _db.AiWorkflowRuns.FirstOrDefaultAsync(r => r.Id == cmd.RunId, ct);
        if (run is null) return AiRunHelpers.Fail<AgentStepAck>($"Run {cmd.RunId} không tồn tại.", ResultErrorCodes.NotFound);

        var r = cmd.Report;
        var steps = await _db.AiAgentSteps.Where(s => s.RunId == run.Id).OrderBy(s => s.StepIndex).ToListAsync(ct);

        var duplicate = steps.FirstOrDefault(s => s.StepIndex == r.StepIndex);
        if (duplicate is not null)
            return Result<AgentStepAck>.Success(Ack(duplicate, run, duplicate: true));

        if (!run.IsActive)
            return AiRunHelpers.Fail<AgentStepAck>($"Run đã kết thúc ({run.Status}).", ResultErrorCodes.Conflict);
        if (r.StepIndex != steps.Count + 1)
            return AiRunHelpers.Fail<AgentStepAck>(
                $"stepIndex phải là {steps.Count + 1}.", ResultErrorCodes.Conflict);
        if (!Enum.TryParse<AiAgentType>(r.Agent, ignoreCase: false, out var agent))
            return AiRunHelpers.Fail<AgentStepAck>($"Agent '{r.Agent}' không hợp lệ.", ResultErrorCodes.Validation);
        if (r.Provider != run.Provider || r.Model != run.Model)
            return AiRunHelpers.Fail<AgentStepAck>("Provider/model của step không khớp run.", ResultErrorCodes.Validation);
        if (string.IsNullOrWhiteSpace(r.PromptVersion) || string.IsNullOrWhiteSpace(r.PromptRef)
            || !IsJson(r.ParametersJson) || !IsJson(r.InputJson))
            return AiRunHelpers.Fail<AgentStepAck>("Thiếu prompt version/ref, parameters hoặc input hợp lệ.", ResultErrorCodes.Validation);

        var parent = r.ParentStepIndex is { } pi ? steps.FirstOrDefault(s => s.StepIndex == pi) : null;
        if (r.ParentStepIndex is not null && parent is null)
            return AiRunHelpers.Fail<AgentStepAck>("parentStepIndex không tồn tại.", ResultErrorCodes.Validation);

        var snapshot = AiRunHelpers.ReadSnapshot(run);
        if (snapshot is null)
            return AiRunHelpers.Fail<AgentStepAck>("Snapshot của run bị hỏng.", ResultErrorCodes.Validation);

        string? outputJson = null;
        if (r.Succeeded)
        {
            var check = Validate(run, snapshot, steps, agent, r, parent);
            if (check is not null) return AiRunHelpers.Fail<AgentStepAck>(check, ResultErrorCodes.Validation);
            outputJson = r.OutputJson;
        }
        else if (string.IsNullOrWhiteSpace(r.ErrorCode))
        {
            return AiRunHelpers.Fail<AgentStepAck>("Step thất bại phải có errorCode.", ResultErrorCodes.Validation);
        }

        var isMock = run.Provider == AiProtocol.ProviderMock;
        var step = AiAgentStep.Create(
            run.Id, r.StepIndex, agent, r.Round, parent?.Id, r.Succeeded ? AiStepStatus.Succeeded : AiStepStatus.Failed,
            r.Provider, r.Model, r.PromptVersion, r.PromptRef, r.ParametersJson, r.InputJson, outputJson,
            r.StartedAtUtc, r.CompletedAtUtc,
            // Mock không tiêu thụ token provider: không ghi số token do client tự khai
            isMock ? null : r.PromptTokens, isMock ? null : r.CompletionTokens,
            r.RepairAttempts, r.ErrorCode, r.ErrorMessage);
        _db.AiAgentSteps.Add(step);
        run.Touch();

        try
        {
            if (agent == AiAgentType.Writer && r.Succeeded) run.RegisterWriterRound(r.Round);

            if (!r.Succeeded)
            {
                run.MarkFailed(r.ErrorCode!, r.ErrorMessage);
            }
            else if (agent == AiAgentType.Continuity && !HasWriter(run.WorkflowType))
            {
                await SyncContinuityFindingsAsync(run, ReadContinuity(step)!, ct);
            }
            else if (agent == AiAgentType.Supervisor)
            {
                // Kết thúc workflow chỉ xảy ra qua quyết định của Supervisor (đã được hàng rào WorkflowRules cho phép ở Validate).
                var action = AiContentValidator.TryParse<SupervisorDecision>(r.OutputJson, out _)!.Action;
                if (action == WorkflowRules.ActionStopForHuman)
                {
                    // Dừng cho người quyết định (còn finding chặn, hết lượt sửa, thiếu context hoặc Supervisor chủ động leo thang):
                    // chỉ workflow có Writer mới có bản script đang dở để người Reject/RequestChanges (BR-02).
                    var writer = LastSucceeded(steps, AiAgentType.Writer);
                    var continuityStep = LastSucceeded(steps, AiAgentType.Continuity);
                    var lastContinuity = continuityStep is null ? null : ReadContinuity(continuityStep);
                    if (writer is not null && lastContinuity is { HasBlockingFindings: true })
                        _db.AiSuggestions.Add(ScriptSuggestion(run, writer, hasBlocking: true,
                            $"Script lượt {writer.Round} còn {lastContinuity.Findings.Count(f => f.Severity == AiProtocol.SeverityBlocking)} finding chặn; Supervisor dừng sau {run.RevisionRounds} lượt sửa."));
                    run.MarkWaitingForHuman();
                }
                else if (action == WorkflowRules.ActionFinish)
                {
                    AddFinishSuggestions(run, steps);
                    run.MarkCompleted();
                }
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return AiRunHelpers.Fail<AgentStepAck>(ex.Message, ResultErrorCodes.Validation);
        }
        catch (DbUpdateException)
        {
            // Callback trùng chạy song song: (RunId, StepIndex) unique chặn bản thứ hai → trả bản đã lưu.
            var saved = await _db.AiAgentSteps.AsNoTracking()
                .FirstOrDefaultAsync(s => s.RunId == run.Id && s.StepIndex == r.StepIndex, ct);
            if (saved is null) throw;
            return Result<AgentStepAck>.Success(Ack(saved, run, duplicate: true));
        }

        await RecordUsageAsync(run, step, ct);
        if (_streamPublisher is not null)
        {
            await _streamPublisher.PublishStepRecordedAsync(run.Id, r, run.Status.ToString(), ct);
            if (!run.IsActive)
                await _streamPublisher.PublishRunStatusChangedAsync(run.Id, run.Status.ToString(), run.ErrorCode, run.ErrorMessage, ct);
        }
        return Result<AgentStepAck>.Success(Ack(step, run, duplicate: false));
    }

    /// <summary>
    /// Điều kiện Finish phụ thuộc loại workflow: đề xuất áp dụng được (Script/Layout/ImagePrompt/Lettering) hoặc không có đề xuất nào
    /// (báo cáo Editor chỉ nằm trong output của step để tham khảo, không ép thành content revision).
    /// </summary>
    private void AddFinishSuggestions(AiWorkflowRun run, List<AiAgentStep> steps)
    {
        switch (run.WorkflowType)
        {
            case AiWorkflowType.FullScene:
            {
                var writer = LastSucceeded(steps, AiAgentType.Writer)!;
                var storyboard = LastSucceeded(steps, AiAgentType.Storyboard)!;
                var scriptSuggestion = ScriptSuggestion(run, writer, hasBlocking: false,
                    $"Script cuối (lượt sửa {writer.Round}), Continuity không còn finding chặn.");
                _db.AiSuggestions.Add(scriptSuggestion);
                _db.AiSuggestions.Add(Suggestion(run, storyboard, AiSuggestionKind.Layout,
                    "Layout Page/Panel từ script cuối; Editor đã viết báo cáo review.", scriptSuggestion.Id));
                break;
            }
            case AiWorkflowType.Script:
            {
                var writer = LastSucceeded(steps, AiAgentType.Writer)!;
                _db.AiSuggestions.Add(ScriptSuggestion(run, writer, hasBlocking: false,
                    $"Script cuối (lượt sửa {writer.Round}), Continuity không còn finding chặn."));
                break;
            }
            case AiWorkflowType.Storyboard:
                _db.AiSuggestions.Add(Suggestion(run, LastSucceeded(steps, AiAgentType.Storyboard)!, AiSuggestionKind.Layout,
                    "Layout Page/Panel từ script có sẵn (đã qua Continuity); Editor đã viết báo cáo review.", null));
                break;
            case AiWorkflowType.Artist:
                _db.AiSuggestions.Add(Suggestion(run, LastSucceeded(steps, AiAgentType.Artist)!, AiSuggestionKind.ImagePrompt,
                    "Prompt ảnh cho các panel của layout đã duyệt (chưa sinh ảnh).", null));
                break;
            // Editor: báo cáo tham khảo nằm trong step output; không có gì để Accept/Reject
        }
    }

    /// <summary>Ghi usage đúng một lần cho Gemini (mỗi step đã có lưu → retry callback không tới được đây); Mock không ghi.</summary>
    private async Task RecordUsageAsync(AiWorkflowRun run, AiAgentStep step, CancellationToken ct)
    {
        if (run.Provider == AiProtocol.ProviderMock) return;
        if (step.PromptTokens is null && step.CompletionTokens is null) return;

        var status = step.Status == AiStepStatus.Succeeded ? AiUsageStatus.Succeeded
            : step.ErrorCode == ResultErrorCodes.AiUnavailable ? AiUsageStatus.ProviderUnavailable : AiUsageStatus.Failed;
        var record = await _usage.RecordAsync(new AiUsageEntry(
            run.WorkspaceId, $"AiAgents.{step.Agent}", status, step.Model,
            step.PromptTokens ?? 0, step.CompletionTokens ?? 0, null, step.ErrorMessage, step.DurationMs,
            run.RequestedByUserId), ct);
        step.LinkUsageRecord(record.Id);

        if (run.IsActive)
        {
            var availability = await _usage.CheckAvailabilityAsync(run.WorkspaceId, ct);
            if (!availability.IsAvailable)
                run.MarkFailed(ResultErrorCodes.AiQuotaExceeded, availability.Message);
        }
        await _db.SaveChangesAsync(ct);
    }

    private static AiSuggestion ScriptSuggestion(AiWorkflowRun run, AiAgentStep writer, bool hasBlocking, string summary) =>
        Suggestion(run, writer, AiSuggestionKind.Script, summary, null, hasBlocking);

    private static AiSuggestion Suggestion(
        AiWorkflowRun run, AiAgentStep step, AiSuggestionKind kind, string summary, Guid? parentSuggestionId, bool hasBlocking = false) =>
        AiSuggestion.Create(
            run.Id, step.Id, run.WorkspaceId, run.SeriesId, run.ChapterId, run.SceneId!.Value, kind,
            step.OutputJson!, AiContentValidator.Sha256(step.OutputJson!), summary,
            run.BaseChapterVersion, run.BaseScriptRevisionId, run.BibleRevisionIdsJson, parentSuggestionId, hasBlocking);

    private static AiAgentStep? LastSucceeded(IEnumerable<AiAgentStep> steps, AiAgentType agent) =>
        steps.LastOrDefault(s => s.Agent == agent && s.Status == AiStepStatus.Succeeded);

    private static AgentStepAck Ack(AiAgentStep step, AiWorkflowRun run, bool duplicate) =>
        new(step.Id, duplicate, run.IsActive, run.Status.ToString());

    private static bool IsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try { using var _ = JsonDocument.Parse(json); return true; } catch (JsonException) { return false; }
    }

    private static bool HasWriter(AiWorkflowType type) => type is AiWorkflowType.FullScene or AiWorkflowType.Script;

    /// <summary>Kiểm tra trình tự workflow + nội dung output. Trả thông điệp lỗi hoặc null nếu hợp lệ.</summary>
    private static string? Validate(
        AiWorkflowRun run, RunSnapshot snap, List<AiAgentStep> steps, AiAgentType agent, AgentStepReport r, AiAgentStep? parent)
    {
        var lastAny = steps.LastOrDefault();
        if (lastAny is not null && lastAny.Status != AiStepStatus.Succeeded) return "Step trước đó đã thất bại.";
        var workers = steps.Where(s => s.Agent != AiAgentType.Supervisor).ToList();
        var last = workers.LastOrDefault();
        var type = run.WorkflowType;

        if (agent == AiAgentType.Supervisor)
        {
            // Supervisor (Gemini) chọn bước kế tiếp, nhưng CHỈ trong danh sách hàng rào do code tính (WorkflowRules theo loại workflow)
            if (lastAny?.Agent == AiAgentType.Supervisor) return "Quyết định điều phối trước đó chưa được thực thi.";
            var decision = AiContentValidator.TryParse<SupervisorDecision>(r.OutputJson, out var de);
            if (decision is null) return de;
            if (decision.Round != r.Round) return "round của quyết định không khớp step.";
            var facts = workers.Select(w => new StepFact(
                w.Agent.ToString(), w.Round, w.Status == AiStepStatus.Succeeded,
                w.Agent == AiAgentType.Continuity && (ReadContinuity(w)?.HasBlockingFindings ?? false))).ToList();
            var allowed = WorkflowRules.AllowedActions(type.ToString(), snap.Inputs, facts, run.MaxRevisionRounds);
            var supervisorErrors = AiContentValidator.ValidateSupervisor(run, decision, allowed);
            return supervisorErrors.Count == 0 ? null : AiContentValidator.Join(supervisorErrors);
        }

        // Agent chuyên môn chỉ chạy khi quyết định điều phối liền trước chọn đúng agent đó
        if (lastAny?.Agent != AiAgentType.Supervisor) return "Agent chỉ được chạy sau một quyết định điều phối của Supervisor.";
        var authorized = AiContentValidator.TryParse<SupervisorDecision>(lastAny.OutputJson, out _);
        if (authorized?.Action != agent.ToString())
            return $"Supervisor đã chọn '{authorized?.Action}', không phải {agent}.";
        if (!WorkflowRules.AgentsOf(type.ToString()).Contains(agent.ToString()))
            return $"Agent {agent} không thuộc workflow {type}.";

        // Writer ← Continuity(có finding chặn) | Continuity ← Writer (hoặc đầu workflow không có Writer) | Storyboard ← Continuity(sạch)
        // | Editor ← Storyboard hoặc Continuity | Artist là agent duy nhất của workflow
        switch (agent)
        {
            case AiAgentType.Writer when r.Round == 0:
                if (last is not null || parent is not null) return "Writer lượt đầu phải là step đầu tiên.";
                break;
            case AiAgentType.Writer:
                if (last?.Agent != AiAgentType.Continuity || parent?.Id != last.Id)
                    return "Writer lượt sửa phải nối sau step Continuity liền trước.";
                if (!(ReadContinuity(last)?.HasBlockingFindings ?? false))
                    return "Không có finding chặn để sửa.";
                if (r.Round != LastSucceeded(steps, AiAgentType.Writer)!.Round + 1)
                    return "Số lượt sửa phải tăng đúng 1.";
                break;
            case AiAgentType.Continuity when HasWriter(type):
                if (last?.Agent != AiAgentType.Writer || parent?.Id != last.Id || r.Round != last.Round)
                    return "Continuity phải kiểm tra đúng lượt Writer liền trước.";
                break;
            case AiAgentType.Continuity:
                if (last is not null || parent is not null || r.Round != 0)
                    return "Continuity của workflow này phải là step đầu tiên (kiểm script có sẵn, lượt 0).";
                break;
            case AiAgentType.Storyboard:
                if (last?.Agent != AiAgentType.Continuity || parent?.Id != last.Id)
                    return "Storyboard phải nối sau Continuity.";
                if (ReadContinuity(last)?.HasBlockingFindings ?? true)
                    return "Còn finding chặn: không được chạy Storyboard.";
                break;
            case AiAgentType.Editor:
                if (last is null || parent?.Id != last.Id || last.Agent is not (AiAgentType.Storyboard or AiAgentType.Continuity))
                    return "Editor phải nối sau Storyboard (hoặc Continuity trong workflow review).";
                break;
            default:   // Artist
                if (last is not null || parent is not null || r.Round != 0)
                    return $"{agent} phải là step đầu tiên và duy nhất của workflow.";
                break;
        }

        // Script nền: bản Writer mới nhất, hoặc script có sẵn của Scene khi workflow không có Writer
        ScriptDraft? script = null;
        if (agent is AiAgentType.Continuity or AiAgentType.Storyboard or AiAgentType.Editor)
        {
            if (HasWriter(type))
            {
                var writer = LastSucceeded(steps, AiAgentType.Writer);
                script = AiContentValidator.TryParse<ScriptDraft>(writer?.OutputJson, out var se);
                if (script is null) return $"Script nền không đọc được: {se}";
            }
            else
            {
                script = AiContentValidator.BaselineDraft(run, snap);
                if (script.Lines.Count == 0) return "Scene không có script để kiểm tra.";
            }
        }

        IReadOnlyList<string> errors;
        switch (agent)
        {
            case AiAgentType.Writer:
            {
                var draft = AiContentValidator.TryParse<ScriptDraft>(r.OutputJson, out var e);
                if (draft is null) return e;
                if (draft.Round != r.Round) return "round của script không khớp step.";
                errors = AiContentValidator.ValidateScript(run, snap, draft);
                break;
            }
            case AiAgentType.Continuity:
            {
                var continuity = AiContentValidator.TryParse<ContinuityReport>(r.OutputJson, out var e);
                if (continuity is null) return e;
                if (continuity.Round != r.Round) return "round của report không khớp step.";
                errors = AiContentValidator.ValidateContinuity(run, snap, continuity, script!);
                break;
            }
            case AiAgentType.Storyboard:
            {
                var layout = AiContentValidator.TryParse<StoryboardLayout>(r.OutputJson, out var e);
                if (layout is null) return e;
                errors = AiContentValidator.ValidateLayout(run, snap, layout, script!);
                break;
            }
            case AiAgentType.Editor:
            {
                var review = AiContentValidator.TryParse<ReviewReport>(r.OutputJson, out var e);
                if (review is null) return e;
                errors = AiContentValidator.ValidateReview(run, review, script!);
                break;
            }
            case AiAgentType.Artist:
            {
                var set = AiContentValidator.TryParse<ArtistPromptSet>(r.OutputJson, out var e);
                if (set is null) return e;
                errors = AiContentValidator.ValidateArtist(run, snap, set);
                break;
            }
            default:
                return $"Agent {r.Agent} không còn được hỗ trợ.";
        }
        return errors.Count == 0 ? null : AiContentValidator.Join(errors);
    }

    private static ContinuityReport? ReadContinuity(AiAgentStep step) =>
        AiContentValidator.TryParse<ContinuityReport>(step.OutputJson, out _);

    private async Task SyncContinuityFindingsAsync(AiWorkflowRun run, ContinuityReport report, CancellationToken ct)
    {
        if (run.SceneId is not { } sceneId) return;
        var existing = await _db.ConsistencyFindings
            .Where(f => f.ChapterId == run.ChapterId && f.SceneId == sceneId
                && f.RuleCode.StartsWith(ConsistencyChecker.AiContinuityPrefix))
            .ToListAsync(ct);
        var byFp = existing.ToDictionary(f => f.Fingerprint);
        var now = DateTime.UtcNow;
        var active = new HashSet<string>();

        foreach (var f in report.Findings)
        {
            var sev = f.Severity switch
            {
                AiProtocol.SeverityBlocking => ConsistencyFindingSeverity.Blocking,
                "Warning" => ConsistencyFindingSeverity.Warning,
                _ => ConsistencyFindingSeverity.Info
            };
            var ruleCode = $"{ConsistencyChecker.AiContinuityPrefix}_{f.Category.ToUpperInvariant()}";
            var lineId = f.ScriptLineIds.FirstOrDefault() is var lid && lid != Guid.Empty ? (Guid?)lid : null;
            var bibleId = f.Evidence.FirstOrDefault()?.BibleEntryId;
            var ev = string.Join(" | ", f.Evidence.Select(e => e.Quote));
            var msg = string.IsNullOrWhiteSpace(f.SuggestedFix) ? f.Description : $"{f.Description} (Gợi ý: {f.SuggestedFix})";
            var fp = new DetectedFinding(ruleCode, sev, msg, ev, sceneId, lineId, bibleId, null).Fingerprint;
            if (!active.Add(fp)) continue;

            if (byFp.TryGetValue(fp, out var old)) old.Redetected(now);
            else
            {
                var created = ConsistencyFinding.Create(run.ChapterId, ruleCode, sev, msg, fp, ev, sceneId, lineId, bibleId);
                created.CreatedBy = run.RequestedByUserId.ToString();
                _db.ConsistencyFindings.Add(created);
                existing.Add(created);
                byFp[fp] = created;
            }
        }

        foreach (var f in existing.Where(x => !active.Contains(x.Fingerprint)))
            f.NoLongerDetected(now);
    }
}
