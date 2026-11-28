using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Commands.RunNextLine;

/// <summary>
/// TASK-358: Copilot viết MỘT dòng kịch bản mới (Dialogue | Sfx | Action) chèn sau <see cref="AfterLineId"/> (null = cuối Scene).
/// Guard giống Copilot theo dòng: vai trò Writer/Producer (BR-07), Scene thuộc Series (BR-03), Chapter InProduction và chưa khoá
/// kịch bản (BR-12), assignment, AI bật + quota + giới hạn lượt/phút. Kết quả chỉ là AiSuggestion ScriptLineInsert chờ Accept (BR-02).
/// </summary>
public sealed record RunNextLineCommand(
    Guid SeriesId,
    Guid UserId,
    Guid SceneId,
    Guid? AfterLineId,
    string Kind,
    string? Speaker,
    string? Instruction,
    Guid? RevisionOfSuggestionId = null) : IRequest<Result<AiSuggestionDto>>;

public sealed class RunNextLineCommandHandler(
    IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, IAiUsageService usage,
    IAiKeyProtector keyProtector, IAiClient aiClient, AiAgentSettings settings)
    : IRequestHandler<RunNextLineCommand, Result<AiSuggestionDto>>
{
    private const int MaxBibleEntries = 15, LinesBefore = 8, LinesAfter = 2;
    private const string Task = CopilotTasks.WriterNextLine;

    private static Result<AiSuggestionDto> Fail(string message, string code) => AiRunHelpers.Fail<AiSuggestionDto>(message, code);

    public async Task<Result<AiSuggestionDto>> Handle(RunNextLineCommand cmd, CancellationToken ct)
    {
        if (NextLineRules.NormalizeKind(cmd.Kind) is not { } kind)
            return Fail($"kind phải là {string.Join(", ", NextLineRules.Kinds)}.", ResultErrorCodes.Validation);
        if (cmd.Instruction is { Length: > CopilotRules.MaxInstructionLength })
            return Fail($"instruction tối đa {CopilotRules.MaxInstructionLength} ký tự.", ResultErrorCodes.Validation);
        if (kind != NextLineRules.Dialogue && !string.IsNullOrWhiteSpace(cmd.Speaker))
            return Fail($"Dòng {kind} không có người nói.", ResultErrorCodes.Validation);

        var workspaceId = await AiRunHelpers.WorkspaceOfSeriesAsync(db, cmd.SeriesId, ct);
        if (workspaceId is null) return Fail($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        // BR-07: vai trò do Backend tra từ membership
        var role = await auth.GetSeriesMemberRoleAsync(cmd.UserId, cmd.SeriesId, ct);
        var allowed = AiWorkflowPolicy.CopilotRoles(Task);
        if (role is null || !allowed.Contains(role.Value))
            return Fail($"Chỉ {string.Join("/", allowed)} của Workspace mới được dùng {Task}.", ResultErrorCodes.Forbidden);

        // BR-03: Scene → Chapter thuộc Series trên route
        var scene = await db.Scenes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == cmd.SceneId, ct);
        var chapter = scene is null ? null
            : await db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scene.ChapterId && c.SeriesId == cmd.SeriesId, ct);
        if (scene is null || chapter is null) return Fail("Scene không thuộc Series này.", ResultErrorCodes.NotFound);

        var lines = await db.ScriptLines.AsNoTracking().Where(l => l.SceneId == scene.Id).OrderBy(l => l.LineOrder).ToListAsync(ct);
        var index = cmd.AfterLineId is { } afterId ? lines.FindIndex(l => l.Id == afterId) : lines.Count - 1;
        if (cmd.AfterLineId is not null && index < 0) return Fail("Dòng làm mốc không thuộc Scene này.", ResultErrorCodes.NotFound);

        // BR-12: không tốn token cho đề xuất chắc chắn bị từ chối khi Accept
        if (!chapter.IsContentEditable)
            return Fail($"Chapter đang ở trạng thái {chapter.Status} (chỉ đọc).", ResultErrorCodes.Conflict);
        if (chapter.ScriptLockReason is { } scriptLocked) return Fail(scriptLocked, ResultErrorCodes.Conflict);
        if (role != WorkspaceRole.Producer
            && (await AiWorkflowFacts.EvaluateAssignmentAsync(db, cmd.UserId, chapter, role.Value, ct)).BlockedReason is { } blocked)
            return Fail(blocked, ResultErrorCodes.Forbidden);

        // Người nói: chỉ nhân vật trong Bible hiệu lực tại chương (BR-15). Writer đã chọn thì AI không được đổi.
        var bible = await AiSnapshotBuilder.BuildBibleAsync(db, chapter, ct) ?? [];
        var characters = bible.Where(b => b.Category == "Character").Select(b => b.Name).Distinct().ToList();
        IReadOnlyList<string> speakers = characters;
        if (kind == NextLineRules.Dialogue)
        {
            if (characters.Count == 0)
                return Fail("Series Bible chưa có nhân vật nào để viết thoại.", ResultErrorCodes.Validation);
            if (!string.IsNullOrWhiteSpace(cmd.Speaker))
            {
                if (NextLineRules.MatchSpeaker(cmd.Speaker, characters) is not { } chosen)
                    return Fail($"Người nói phải là nhân vật trong Series Bible: {string.Join(", ", characters)}.", ResultErrorCodes.Validation);
                speakers = [chosen];
            }
        }
        else speakers = [];

        var instruction = cmd.Instruction;
        if (cmd.RevisionOfSuggestionId is { } revisionOf)
        {
            var feedback = await AiRunHelpers.RevisionFeedbackAsync(db, cmd.SeriesId, revisionOf, scene.Id, ct);
            if (feedback is null) return Fail("Đề xuất cần làm lại không ở trạng thái Yêu cầu sửa trong Scene này.", ResultErrorCodes.Conflict);
            instruction = AiRunHelpers.AppendFeedback(instruction, feedback, CopilotRules.MaxInstructionLength);
        }

        var gate = await CopilotGate.ResolveAsync(db, usage, settings, keyProtector, workspaceId.Value, cmd.SeriesId, cmd.UserId,
            $"AiAgents.Copilot.{Task}", ct);
        if (!gate.IsSuccess) return Fail(gate.ErrorMessage!, gate.ErrorCode!);
        var p = gate.Value!;

        // Context: nhân vật đứng trước, rồi các dòng quanh vị trí chèn (không gửi dữ liệu ngoài Scene/Bible của Series)
        bible = bible.OrderBy(b => !speakers.Contains(b.Name)).ThenBy(b => b.Category != "Character").Take(MaxBibleEntries).ToList();
        var before = lines.Take(index + 1).TakeLast(LinesBefore).Select(Format).ToList();
        var after = lines.Skip(index + 1).Take(LinesAfter).Select(Format).ToList();
        var heading = scene.Heading ?? $"Scene {scene.SceneNumber}";
        var context = new NextLineContext(kind, speakers, heading, scene.Summary, before, after);

        var latestScriptRevisionId = await db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == scene.Id && r.Kind == ContentRevisionKind.Script)
            .OrderByDescending(r => r.VersionNumber).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        var sceneLines = lines.Select(l => new BaselineScriptLine(l.Id, l.LineOrder, null, l.DialogueText, l.StageDirection)).ToList();
        var snapshot = new RunSnapshot(chapter.ChapterNumber, heading, scene.Summary, bible, sceneLines);
        var run = AiWorkflowRun.Create(
            workspaceId.Value, cmd.SeriesId, chapter.Id, scene.Id, cmd.UserId, p.Provider, p.Model,
            string.IsNullOrWhiteSpace(instruction) ? $"{Task}: {kind}" : $"{Task}: {kind}: {instruction}",
            ReadingDirection.LeftToRight, 0, chapter.VersionVector, latestScriptRevisionId,
            AiJson.Serialize(snapshot), AiJson.Serialize(bible.Select(b => b.RevisionId)), AiWorkflowType.Copilot);
        db.AiWorkflowRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var apiKey = CopilotGate.ApiKey(keyProtector, p);
        if (apiKey is "")
        {
            run.MarkFailed(ResultErrorCodes.AiUnavailable, "Studio chưa có API key dùng được. Admin hãy chọn API ở mục Cấu hình AI.");
            AiRunHelpers.NotifyRequesterOfFinish(db, run);
            await db.SaveChangesAsync(ct);
            return Fail("Studio chưa có API key dùng được. Admin hãy chọn API ở mục Cấu hình AI.", ResultErrorCodes.AiUnavailable);
        }

        var request = new CopilotRequest(Task, p.Provider, p.Model, apiKey, null, bible, null, instruction?.Trim(),
            AccountId: p.AccountId, NextLine: context);
        var response = await aiClient.RunCopilotAsync(request, ct);

        // Backend kiểm lại output bằng cùng luật tất định (không tin AI service tuyệt đối)
        var speaker = kind == NextLineRules.Dialogue ? NextLineRules.MatchSpeaker(response.Speaker, speakers) : null;
        if (response.Succeeded && NextLineRules.Validate(kind, speaker ?? response.Speaker, response.Text, speakers) is { Count: > 0 } errors)
            response = response with { Succeeded = false, ErrorCode = "INVALID_OUTPUT", ErrorMessage = string.Join(" ", errors) };

        var payload = !response.Succeeded ? null
            : new NextLineSuggestion(AiProtocol.SchemaVersion, run.Id, scene.Id, cmd.AfterLineId, kind, speaker,
                response.Text!.Trim(), response.Reason ?? "", speakers);
        var step = await CopilotGate.RecordStepAsync(db, usage, settings, run, AiAgentType.Writer, p, Task, response,
            new { task = Task, kind, timeoutSeconds = settings.CopilotTimeoutSeconds },
            new { sceneId = scene.Id, afterLineId = cmd.AfterLineId, kind, speakers, instruction, before = before.Count, after = after.Count },
            payload is null ? null : AiJson.Serialize(payload), cmd.UserId, ct);

        if (payload is null)
        {
            // Lỗi: không ghi đề xuất nào, run Failed giữ lại để truy vết
            run.MarkFailed(response.ErrorCode ?? ResultErrorCodes.AiUnavailable, response.ErrorMessage);
            AiRunHelpers.NotifyRequesterOfFinish(db, run);
            await db.SaveChangesAsync(ct);
            return Fail(response.ErrorMessage ?? "AI không trả được kết quả.", CopilotGate.FailureCode(response));
        }

        var json = AiJson.Serialize(payload);
        var title = kind switch
        {
            NextLineRules.Dialogue => $"Thoại tiếp theo cho {speaker}",
            NextLineRules.Sfx => "SFX tiếp theo",
            _ => "Hành động tiếp theo"
        };
        var suggestion = AiSuggestion.Create(run.Id, step.Id, workspaceId.Value, cmd.SeriesId, chapter.Id, scene.Id,
            AiSuggestionKind.ScriptLineInsert, json, AiContentValidator.Sha256(json), title,
            chapter.VersionVector, latestScriptRevisionId, run.BibleRevisionIdsJson, null, false);
        db.AiSuggestions.Add(suggestion);
        run.MarkCompleted();
        AiRunHelpers.NotifyRequesterOfFinish(db, run);
        await db.SaveChangesAsync(ct);

        return Result<AiSuggestionDto>.Success(AiRunHelpers.ToDto(suggestion, [], role));
    }

    /// <summary>Một dòng ngữ cảnh: "TÊN: thoại", "[SFX] ...", "(hành động) ...".</summary>
    private static string Format(ScriptLine l) => l.Kind switch
    {
        ScriptLineKind.Dialogue or ScriptLineKind.Thought => $"{l.SpeakerLabel ?? "?"}: {l.DialogueText ?? l.Text}",
        ScriptLineKind.Sfx => $"[SFX] {l.Text}",
        ScriptLineKind.Narration => $"[Dẫn truyện] {l.Text}",
        _ => $"(hành động) {l.Text ?? l.StageDirection}"
    };
}
