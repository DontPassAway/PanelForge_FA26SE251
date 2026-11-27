using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.ScriptVersions;
using PanelForge.Application.Features.TaskGrid.Common;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Workflow;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Commands.RunImpactSuggestion;

/// <summary>
/// Smart Ripple bản hẹp: Producer xin gợi ý Keep/Rework cho ô mang cờ do sửa kịch bản. AI chỉ thấy các dòng đã đổi của đúng trang;
/// kết quả là đề xuất, Impact Review chỉ chạy khi Producer Accept (BR-02).
/// </summary>
public sealed record RunImpactSuggestionCommand(Guid ChapterId, Guid TaskId, Guid UserId) : IRequest<Result<AiSuggestionDto>>;

public sealed class RunImpactSuggestionCommandHandler : IRequestHandler<RunImpactSuggestionCommand, Result<AiSuggestionDto>>
{
    private const string Task = CopilotTasks.ImpactRipple;
    private const int MaxChanges = 40;

    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IAiUsageService _usage;
    private readonly IAiKeyProtector _keyProtector;
    private readonly IAiClient _aiClient;
    private readonly AiAgentSettings _settings;

    public RunImpactSuggestionCommandHandler(
        IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, IAiUsageService usage,
        IAiKeyProtector keyProtector, IAiClient aiClient, AiAgentSettings settings)
    {
        _db = db;
        _auth = auth;
        _usage = usage;
        _keyProtector = keyProtector;
        _aiClient = aiClient;
        _settings = settings;
    }

    private static Result<AiSuggestionDto> Fail(string message, string code) => AiRunHelpers.Fail<AiSuggestionDto>(message, code);

    public async Task<Result<AiSuggestionDto>> Handle(RunImpactSuggestionCommand cmd, CancellationToken ct)
    {
        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không tồn tại.", ResultErrorCodes.NotFound);
        var series = await _db.Series.AsNoTracking().FirstAsync(s => s.Id == chapter.SeriesId, ct);
        var role = await _auth.GetSeriesMemberRoleAsync(cmd.UserId, series.Id, ct);
        if (role != WorkspaceRole.Producer) return Fail("Chỉ Producer mới được xin gợi ý xử lý ảnh hưởng.", ResultErrorCodes.Forbidden);
        if (!chapter.IsContentEditable) return Fail($"Chapter đang ở trạng thái {chapter.Status} (chỉ đọc).", ResultErrorCodes.Conflict);

        var task = await _db.StageTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == cmd.TaskId && t.ChapterId == chapter.Id, ct);
        if (task is null) return Fail($"Ô {cmd.TaskId} không thuộc chương này.", ResultErrorCodes.NotFound);
        if (!task.IsInputOutdated || task.OutdatedReason != StageTask.ScriptChangedReason || task.PageId is null)
            return Fail("Ô này không mang cờ ảnh hưởng do sửa kịch bản.", ResultErrorCodes.Conflict);

        var pageNumber = await _db.Pages.AsNoTracking().Where(p => p.Id == task.PageId).Select(p => p.PageNumber).FirstAsync(ct);
        var changes = await PageChangesAsync(chapter.Id, pageNumber, ct);
        if (changes.Count == 0) return Fail("Không tìm thấy thay đổi kịch bản của trang này để so sánh.", ResultErrorCodes.Conflict);

        var stage = (await TaskGridRules.LoadStagesAsync(_db, chapter.SeriesId, ct)).First(s => s.Id == task.PipelineStageId);
        var gate = await CopilotGate.ResolveAsync(_db, _usage, _settings, _keyProtector, series.WorkspaceId, series.Id, cmd.UserId, $"AiAgents.Copilot.{Task}", ct);
        if (!gate.IsSuccess) return Fail(gate.ErrorMessage!, gate.ErrorCode!);
        var p = gate.Value!;

        var context = new ImpactRippleContext(stage.Name, stage.AllowedRole?.ToString() ?? "Any", pageNumber, changes);
        var run = AiWorkflowRun.Create(series.WorkspaceId, series.Id, chapter.Id, null, cmd.UserId, p.Provider, p.Model, Task,
            series.ReadingDirection, 0, chapter.VersionVector, null, AiJson.Serialize(context), "[]", AiWorkflowType.Copilot);
        _db.AiWorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var apiKey = CopilotGate.ApiKey(_keyProtector, p);
        if (apiKey is "")
        {
            run.MarkFailed(ResultErrorCodes.AiUnavailable, "Studio chưa có API key dùng được. Admin hãy nhập key ở mục Cấu hình AI.");
            await _db.SaveChangesAsync(ct);
            return Fail("Studio chưa có API key dùng được. Admin hãy nhập key ở mục Cấu hình AI.", ResultErrorCodes.AiUnavailable);
        }

        var response = await _aiClient.RunCopilotAsync(new CopilotRequest(Task, p.Provider, p.Model, apiKey, null, [], null, null, Ripple: context, AccountId: p.AccountId), ct);
        if (response.Succeeded && CopilotRules.ValidateImpact(response.ImpactDecision, response.Reason) is { Count: > 0 } errors)
            response = response with { Succeeded = false, ErrorCode = "INVALID_OUTPUT", ErrorMessage = string.Join(" ", errors) };

        var payload = response.Succeeded
            ? new ImpactRippleSuggestion(AiProtocol.SchemaVersion, run.Id, task.Id, response.ImpactDecision!, response.Reason!, changes)
            : null;
        var step = await CopilotGate.RecordStepAsync(_db, _usage, _settings, run, AiAgentType.Producer, p, Task, response,
            new { task = Task, timeoutSeconds = _settings.CopilotTimeoutSeconds }, new { taskId = task.Id, changes = changes.Count },
            payload is null ? null : AiJson.Serialize(payload), cmd.UserId, ct);
        if (payload is null)
        {
            run.MarkFailed(response.ErrorCode ?? ResultErrorCodes.AiUnavailable, response.ErrorMessage);
            await _db.SaveChangesAsync(ct);
            return Fail(response.ErrorMessage ?? "AI không trả được kết quả.", CopilotGate.FailureCode(response));
        }

        var json = AiJson.Serialize(payload);
        var suggestion = AiSuggestion.Create(run.Id, step.Id, series.WorkspaceId, series.Id, chapter.Id, Guid.Empty,
            AiSuggestionKind.ImpactRipple, json, AiContentValidator.Sha256(json), $"Gợi ý {payload.Decision} cho {stage.Name}, trang {pageNumber}",
            chapter.VersionVector, null, "[]", null, false);
        _db.AiSuggestions.Add(suggestion);
        run.MarkCompleted();
        await _db.SaveChangesAsync(ct);
        return Result<AiSuggestionDto>.Success(AiRunHelpers.ToDto(suggestion, [], role));
    }

    /// <summary>So bản hiện tại với phiên bản gần nhất có nội dung khác trên trang này.</summary>
    private async Task<IReadOnlyList<ImpactLineChange>> PageChangesAsync(Guid chapterId, int pageNumber, CancellationToken ct)
    {
        var current = (await ScriptSnapshotBuilder.BuildAsync(_db, chapterId, ct)).Rows.Where(r => r.Page == pageNumber).ToList();
        var versions = await _db.ScriptVersions.AsNoTracking().Where(v => v.ChapterId == chapterId)
            .OrderByDescending(v => v.VersionNumber).Select(v => v.RowsJson).ToListAsync(ct);
        foreach (var json in versions)
        {
            var before = ScriptSnapshotBuilder.DeserializeSnapshot(json).Rows.Where(r => r.Page == pageNumber).ToList();
            var changes = Diff(before, current);
            if (changes.Count > 0) return changes;
        }
        return [];
    }

    private static List<ImpactLineChange> Diff(IReadOnlyList<ScriptVersionRow> before, IReadOnlyList<ScriptVersionRow> after)
    {
        var old = before.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
        var now = after.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First());
        var result = new List<ImpactLineChange>();
        foreach (var row in after)
            if (!old.TryGetValue(row.Key, out var prev)) result.Add(new(row.Type, row.Label, null, row.Text));
            else if (prev.Text != row.Text || prev.Label != row.Label || prev.Type != row.Type) result.Add(new(row.Type, row.Label, prev.Text, row.Text));
        result.AddRange(before.Where(r => !now.ContainsKey(r.Key)).Select(r => new ImpactLineChange(r.Type, r.Label, r.Text, null)));
        return result.Take(MaxChanges).ToList();
    }
}
