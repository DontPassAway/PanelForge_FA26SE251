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

namespace PanelForge.Application.Features.AiAgents.Commands.RunCopilot;

/// <summary>
/// Copilot sync trên MỘT dòng thoại (writer.rewrite, letterer.shorten). Guard TRƯỚC khi gọi AI: vai trò theo task (BR-07), assignment,
/// dòng thoại thuộc đúng Series (BR-03), Chapter còn InProduction (BR-12), AI bật + quota Workspace + giới hạn lượt/phút của Series.
/// Kết quả chỉ là AiSuggestion (LineRewrite) chờ người Accept/Reject qua ReviewSuggestionCommand (BR-02); run + step lưu provenance (BR-10).
/// </summary>
public sealed record RunCopilotCommand(
    Guid SeriesId,
    Guid UserId,
    string Task,
    Guid LineId,
    string? Instruction,
    int? MaxChars) : IRequest<Result<AiSuggestionDto>>;

public sealed class RunCopilotCommandHandler : IRequestHandler<RunCopilotCommand, Result<AiSuggestionDto>>
{
    private const int MaxBibleEntries = 15;

    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IAiUsageService _usage;
    private readonly IAiKeyProtector _keyProtector;
    private readonly IAiClient _aiClient;
    private readonly AiAgentSettings _settings;

    public RunCopilotCommandHandler(
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

    public async Task<Result<AiSuggestionDto>> Handle(RunCopilotCommand cmd, CancellationToken ct)
    {
        if (!CopilotTasks.IsLineTask(cmd.Task))
            return Fail($"task phải là {CopilotTasks.WriterRewrite} hoặc {CopilotTasks.LettererShorten}.", ResultErrorCodes.Validation);
        if (cmd.Instruction is { Length: > CopilotRules.MaxInstructionLength })
            return Fail($"instruction tối đa {CopilotRules.MaxInstructionLength} ký tự.", ResultErrorCodes.Validation);
        var shorten = cmd.Task == CopilotTasks.LettererShorten;
        if (shorten && cmd.MaxChars is not (> 0 and <= CopilotRules.MaxCharsLimit))
            return Fail($"maxChars là bắt buộc với {cmd.Task} (1..{CopilotRules.MaxCharsLimit}).", ResultErrorCodes.Validation);
        var maxChars = shorten ? cmd.MaxChars : null;

        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(_db, cmd.SeriesId, ct);
        if (workspaceId is null) return Fail($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        // BR-07: vai trò theo task do Backend tra từ membership
        var role = await _auth.GetMemberRoleAsync(cmd.UserId, workspaceId.Value, ct);
        var allowed = AiWorkflowPolicy.CopilotRoles(cmd.Task);
        if (role is null || !allowed.Contains(role.Value))
            return Fail($"Chỉ {string.Join("/", allowed)} của Workspace mới được dùng {cmd.Task}.", ResultErrorCodes.Forbidden);

        // BR-03: dòng thoại → Scene → Chapter thuộc Series trên route (chặn đổi id sang Series khác)
        var line = await _db.ScriptLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == cmd.LineId, ct);
        var scene = line is null ? null : await _db.Scenes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == line.SceneId, ct);
        var chapter = scene is null ? null
            : await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scene.ChapterId && c.SeriesId == cmd.SeriesId, ct);
        if (line is null || scene is null || chapter is null)
            return Fail("Dòng thoại không thuộc Series này.", ResultErrorCodes.NotFound);
        if (string.IsNullOrWhiteSpace(line.DialogueText))
            return Fail("Dòng này không có thoại để xử lý.", ResultErrorCodes.Validation);

        // BR-12: không tốn token cho đề xuất chắc chắn bị từ chối khi Accept
        if (chapter.Status != ChapterStatus.InProduction)
            return Fail($"Chapter đang ở trạng thái {chapter.Status} (chỉ đọc).", ResultErrorCodes.Conflict);

        if (role != WorkspaceRole.Producer
            && (await AiWorkflowFacts.EvaluateAssignmentAsync(_db, cmd.UserId, chapter, role.Value, ct)).BlockedReason is { } blocked)
            return Fail(blocked, ResultErrorCodes.Forbidden);

        var gate = await CopilotGate.ResolveAsync(_db, _usage, _settings, workspaceId.Value, cmd.SeriesId, cmd.UserId,
            $"AiAgents.Copilot.{cmd.Task}", ct);
        if (!gate.IsSuccess) return Fail(gate.ErrorMessage!, gate.ErrorCode!);
        var p = gate.Value!;

        // Context: Bible hiệu lực tại chương (BR-15), nhân vật nói đứng đầu; vài dòng xung quanh để giữ mạch
        var bible = await AiSnapshotBuilder.BuildBibleAsync(_db, chapter, ct) ?? [];
        bible = bible.OrderByDescending(b => b.EntryId == line.SpeakerCharacterId).ThenBy(b => b.Category != "Character")
            .Take(MaxBibleEntries).ToList();
        var sceneLines = await _db.ScriptLines.AsNoTracking().Where(l => l.SceneId == scene.Id).OrderBy(l => l.LineOrder)
            .Select(l => new BaselineScriptLine(l.Id, l.LineOrder, l.SpeakerCharacterId, l.DialogueText, l.StageDirection))
            .ToListAsync(ct);
        var index = sceneLines.FindIndex(l => l.LineId == line.Id);
        var neighbors = sceneLines.Where((_, i) => i != index && Math.Abs(i - index) <= 2)
            .Select(l => l.Dialogue ?? l.StageDirection ?? "").Where(t => t.Length > 0).ToList();
        var latestScriptRevisionId = await _db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == scene.Id && r.Kind == ContentRevisionKind.Script)
            .OrderByDescending(r => r.VersionNumber).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);

        var original = line.DialogueText.Trim();
        var snapshot = new RunSnapshot(chapter.ChapterNumber, scene.Heading ?? $"Scene {scene.SceneNumber}", scene.Summary, bible, sceneLines);
        var run = AiWorkflowRun.Create(
            workspaceId.Value, cmd.SeriesId, chapter.Id, scene.Id, cmd.UserId, p.Provider, p.Model,
            string.IsNullOrWhiteSpace(cmd.Instruction) ? cmd.Task : $"{cmd.Task}: {cmd.Instruction}",
            ReadingDirection.LeftToRight, 0, chapter.VersionVector, latestScriptRevisionId,
            AiJson.Serialize(snapshot), AiJson.Serialize(bible.Select(b => b.RevisionId)), AiWorkflowType.Copilot);
        _db.AiWorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var apiKey = CopilotGate.ApiKey(_settings, _keyProtector, p);
        if (apiKey is "")
        {
            run.MarkFailed(ResultErrorCodes.AiUnavailable, "Không giải mã được API key của Workspace.");
            await _db.SaveChangesAsync(ct);
            return Fail("Không giải mã được API key của Workspace (key ring đã đổi?). Admin cần nhập lại key.", ResultErrorCodes.AiUnavailable);
        }

        var request = new CopilotRequest(cmd.Task, p.Provider, p.Model, apiKey,
            new CopilotLine(line.Id, line.SpeakerCharacterId, original, line.StageDirection, neighbors), bible, maxChars, cmd.Instruction?.Trim());
        var response = await _aiClient.RunCopilotAsync(request, ct);

        // Backend kiểm lại output bằng cùng luật tất định (không tin AI service tuyệt đối)
        if (response.Succeeded && CopilotRules.Validate(cmd.Task, original, response.Text, maxChars) is { Count: > 0 } errors)
            response = response with { Succeeded = false, ErrorCode = "INVALID_OUTPUT", ErrorMessage = string.Join(" ", errors) };

        var payload = response.Succeeded
            ? new LineRewriteSuggestion(AiProtocol.SchemaVersion, run.Id, scene.Id, cmd.Task, line.Id, original,
                response.Text!.Trim(), response.Reason ?? "", maxChars)
            : null;
        var step = await CopilotGate.RecordStepAsync(_db, _usage, _settings, run, shorten ? AiAgentType.Letterer : AiAgentType.Writer,
            p, cmd.Task, response,
            new { task = cmd.Task, maxChars, timeoutSeconds = _settings.CopilotTimeoutSeconds },
            new { lineId = line.Id, original, maxChars, instruction = cmd.Instruction, neighbors = neighbors.Count },
            payload is null ? null : AiJson.Serialize(payload), cmd.UserId, ct);

        if (payload is null)
        {
            // Lỗi: không ghi đề xuất nào, run Failed giữ lại để truy vết
            run.MarkFailed(response.ErrorCode ?? ResultErrorCodes.AiUnavailable, response.ErrorMessage);
            await _db.SaveChangesAsync(ct);
            return Fail(response.ErrorMessage ?? "AI không trả được kết quả.", CopilotGate.FailureCode(response));
        }

        var json = AiJson.Serialize(payload);
        var suggestion = AiSuggestion.Create(run.Id, step.Id, workspaceId.Value, cmd.SeriesId, chapter.Id, scene.Id,
            AiSuggestionKind.LineRewrite, json, AiContentValidator.Sha256(json),
            shorten ? $"Rút gọn thoại còn {payload.SuggestedText.Length}/{maxChars} ký tự" : "Viết lại thoại theo giọng nhân vật",
            chapter.VersionVector, latestScriptRevisionId, run.BibleRevisionIdsJson, null, false);
        _db.AiSuggestions.Add(suggestion);
        run.MarkCompleted();
        await _db.SaveChangesAsync(ct);

        return Result<AiSuggestionDto>.Success(AiRunHelpers.ToDto(suggestion, [], role));
    }
}
