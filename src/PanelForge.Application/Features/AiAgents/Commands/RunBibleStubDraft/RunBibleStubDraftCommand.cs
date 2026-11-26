using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.AiAgents.Common;
using PanelForge.Application.Features.AiAgents.Models;
using PanelForge.Application.Features.Chapters.Services;
using PanelForge.Domain.Entities.Ai;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.AiAgents.Commands.RunBibleStubDraft;

/// <summary>
/// Bible Stub Harvester: Writer xin hồ sơ nháp cho mục Bible còn là stub, chỉ từ các dòng kịch bản gắn với mục đó.
/// Không có dòng nào thì không gọi AI. Mọi chi tiết phải có trích dẫn nguyên văn (BR-02: kết quả chỉ là đề xuất).
/// </summary>
public sealed record RunBibleStubDraftCommand(Guid SeriesId, Guid EntryId, Guid UserId, string? Instruction)
    : IRequest<Result<AiSuggestionDto>>;

public sealed class RunBibleStubDraftCommandHandler : IRequestHandler<RunBibleStubDraftCommand, Result<AiSuggestionDto>>
{
    private const int MaxEvidenceLines = 30;
    private const string Task = CopilotTasks.BibleStubDraft;

    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IAiUsageService _usage;
    private readonly IAiKeyProtector _keyProtector;
    private readonly IAiClient _aiClient;
    private readonly AiAgentSettings _settings;

    public RunBibleStubDraftCommandHandler(
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

    public async Task<Result<AiSuggestionDto>> Handle(RunBibleStubDraftCommand cmd, CancellationToken ct)
    {
        if (cmd.Instruction is { Length: > CopilotRules.MaxInstructionLength })
            return Fail($"instruction tối đa {CopilotRules.MaxInstructionLength} ký tự.", ResultErrorCodes.Validation);

        var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null) return Fail($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);
        var role = await _auth.GetMemberRoleAsync(cmd.UserId, series.WorkspaceId, ct);
        if (role is null || !AiWorkflowPolicy.CopilotRoles(Task).Contains(role.Value))
            return Fail("Chỉ Writer của Workspace mới được xin hồ sơ nháp.", ResultErrorCodes.Forbidden);

        var entry = await (from e in _db.BibleEntries.AsNoTracking()
                           join b in _db.SeriesBibles.AsNoTracking() on e.SeriesBibleId equals b.Id
                           where e.Id == cmd.EntryId && b.SeriesId == cmd.SeriesId
                           select e).FirstOrDefaultAsync(ct);
        if (entry is null) return Fail("Mục Bible không thuộc Series này.", ResultErrorCodes.NotFound);
        if (!entry.IsStub) return Fail("Mục này đã được định nghĩa.", ResultErrorCodes.Conflict);

        var mentions = await (from k in _db.ScriptLineBibleLinks.AsNoTracking()
                              join l in _db.ScriptLines.AsNoTracking() on k.ScriptLineId equals l.Id
                              join s in _db.Scenes.AsNoTracking() on l.SceneId equals s.Id
                              join c in _db.Chapters.AsNoTracking() on s.ChapterId equals c.Id
                              where k.BibleEntryId == entry.Id && c.SeriesId == cmd.SeriesId
                              orderby c.ChapterNumber, s.SceneNumber, l.LineOrder
                              select new MentionRow(l.Id, l.DialogueText, l.StageDirection, l.SpeakerLabel, s.Id, c.Id,
                                  c.ChapterNumber, s.SceneNumber, l.LineOrder))
            .Take(MaxEvidenceLines).ToListAsync(ct);

        // Stub Nhân vật thường được tạo tự động từ tên người nói (ChapterCastService, không có thẻ ScriptLineBibleLink):
        // các dòng nhân vật đó nói cũng là căn cứ, so khớp tên đã chuẩn hoá như lúc tạo stub.
        if (entry.Category == BibleEntryCategory.Character)
        {
            var spoken = await (from l in _db.ScriptLines.AsNoTracking()
                                join s in _db.Scenes.AsNoTracking() on l.SceneId equals s.Id
                                join c in _db.Chapters.AsNoTracking() on s.ChapterId equals c.Id
                                where c.SeriesId == cmd.SeriesId && l.SpeakerLabel != null
                                select new MentionRow(l.Id, l.DialogueText, l.StageDirection, l.SpeakerLabel, s.Id, c.Id,
                                    c.ChapterNumber, s.SceneNumber, l.LineOrder))
                .ToListAsync(ct);
            var known = mentions.Select(m => m.Id).ToHashSet();
            mentions = mentions
                .Concat(spoken.Where(m => !known.Contains(m.Id)
                    && SpeakerResolver.NormalizeSpeaker(m.SpeakerLabel) == entry.NormalizedName))
                .OrderBy(m => m.ChapterNumber).ThenBy(m => m.SceneNumber).ThenBy(m => m.LineOrder)
                .Take(MaxEvidenceLines)
                .ToList();
        }

        var evidence = mentions
            .Select(m => new BibleStubEvidence(m.Id, string.Join(" ", new[] { m.SpeakerLabel, m.DialogueText, m.StageDirection }
                .Where(t => !string.IsNullOrWhiteSpace(t)))))
            .Where(e => e.Text.Length > 0).ToList();
        if (evidence.Count == 0)
            return Fail("Chưa có dòng kịch bản nào nhắc tới mục này, nên không có căn cứ để soạn.", ResultErrorCodes.Conflict);

        var chapter = await _db.Chapters.AsNoTracking().FirstAsync(c => c.Id == mentions[0].ChapterId, ct);
        var gate = await CopilotGate.ResolveAsync(_db, _usage, _settings, _keyProtector, series.WorkspaceId, series.Id, cmd.UserId, $"AiAgents.Copilot.{Task}", ct);
        if (!gate.IsSuccess) return Fail(gate.ErrorMessage!, gate.ErrorCode!);
        var p = gate.Value!;

        var context = new BibleStubContext(entry.Name, entry.Category.ToString(), evidence);
        var run = AiWorkflowRun.Create(series.WorkspaceId, series.Id, chapter.Id, mentions[0].SceneId, cmd.UserId, p.Provider, p.Model,
            string.IsNullOrWhiteSpace(cmd.Instruction) ? Task : $"{Task}: {cmd.Instruction}", series.ReadingDirection, 0,
            chapter.VersionVector, null, AiJson.Serialize(context), "[]", AiWorkflowType.Copilot);
        _db.AiWorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var apiKey = CopilotGate.ApiKey(_keyProtector, p);
        if (apiKey is "")
        {
            run.MarkFailed(ResultErrorCodes.AiUnavailable, "Studio chưa có API key dùng được. Admin hãy nhập key ở mục Cấu hình AI.");
            await _db.SaveChangesAsync(ct);
            return Fail("Studio chưa có API key dùng được. Admin hãy nhập key ở mục Cấu hình AI.", ResultErrorCodes.AiUnavailable);
        }

        var response = await _aiClient.RunCopilotAsync(
            new CopilotRequest(Task, p.Provider, p.Model, apiKey, null, [], null, cmd.Instruction?.Trim(), Stub: context, AccountId: p.AccountId), ct);
        // Backend kiểm lại trích dẫn bằng cùng luật tất định, không tin AI service tuyệt đối
        if (response.Succeeded && CopilotRules.ValidateStubDraft(response.Stub, context) is { Count: > 0 } errors)
            response = response with { Succeeded = false, ErrorCode = "INVALID_OUTPUT", ErrorMessage = string.Join(" ", errors) };

        var payload = response.Succeeded
            ? new BibleStubSuggestion(AiProtocol.SchemaVersion, run.Id, entry.Id, entry.Name, response.Stub!, response.Reason ?? "")
            : null;
        var step = await CopilotGate.RecordStepAsync(_db, _usage, _settings, run, AiAgentType.Writer, p, Task, response,
            new { task = Task, timeoutSeconds = _settings.CopilotTimeoutSeconds },
            new { entryId = entry.Id, lines = evidence.Count, instruction = cmd.Instruction },
            payload is null ? null : AiJson.Serialize(payload), cmd.UserId, ct);
        if (payload is null)
        {
            run.MarkFailed(response.ErrorCode ?? ResultErrorCodes.AiUnavailable, response.ErrorMessage);
            await _db.SaveChangesAsync(ct);
            return Fail(response.ErrorMessage ?? "AI không trả được kết quả.", CopilotGate.FailureCode(response));
        }

        var json = AiJson.Serialize(payload);
        var suggestion = AiSuggestion.Create(run.Id, step.Id, series.WorkspaceId, series.Id, chapter.Id, mentions[0].SceneId,
            AiSuggestionKind.BibleStub, json, AiContentValidator.Sha256(json), $"Hồ sơ nháp cho {entry.Name}",
            chapter.VersionVector, null, "[]", null, false);
        _db.AiSuggestions.Add(suggestion);
        run.MarkCompleted();
        await _db.SaveChangesAsync(ct);
        return Result<AiSuggestionDto>.Success(AiRunHelpers.ToDto(suggestion, [], role));
    }
}

internal sealed record MentionRow(
    Guid Id, string? DialogueText, string? StageDirection, string? SpeakerLabel, Guid SceneId, Guid ChapterId,
    decimal ChapterNumber, int SceneNumber, int LineOrder);
