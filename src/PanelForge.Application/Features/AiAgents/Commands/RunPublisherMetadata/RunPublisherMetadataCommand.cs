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

namespace PanelForge.Application.Features.AiAgents.Commands.RunPublisherMetadata;

/// <summary>
/// Copilot Publisher (kế hoạch AI đa vai trò, Tuần 4): gợi ý logline, synopsis, thể loại, teaser, age rating cho Series khi phát hành Chapter.
/// Quyền Producer/Publisher (BR-07); Chapter thuộc Series trên route (BR-03). Không cần InProduction: metadata là cấp Series, không sửa nội dung chương.
/// Kết quả là AiSuggestion SeriesMetadata; Accept chỉ ghi synopsis + genre vào Series (BR-02), phần còn lại để Publisher dùng khi đăng.
/// </summary>
public sealed record RunPublisherMetadataCommand(Guid SeriesId, Guid ChapterId, Guid UserId, string? Instruction)
    : IRequest<Result<AiSuggestionDto>>;

public sealed class RunPublisherMetadataCommandHandler : IRequestHandler<RunPublisherMetadataCommand, Result<AiSuggestionDto>>
{
    private const int MaxOutlines = 30, MaxOutlineChars = 500, MaxCharacters = 10;
    private const string Task = CopilotTasks.PublisherMetadata;

    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IAiUsageService _usage;
    private readonly IAiKeyProtector _keyProtector;
    private readonly IAiClient _aiClient;
    private readonly AiAgentSettings _settings;

    public RunPublisherMetadataCommandHandler(
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

    public async Task<Result<AiSuggestionDto>> Handle(RunPublisherMetadataCommand cmd, CancellationToken ct)
    {
        if (cmd.Instruction is { Length: > CopilotRules.MaxInstructionLength })
            return Fail($"instruction tối đa {CopilotRules.MaxInstructionLength} ký tự.", ResultErrorCodes.Validation);

        var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == cmd.SeriesId, ct);
        if (series is null) return Fail($"Series {cmd.SeriesId} không tồn tại.", ResultErrorCodes.NotFound);

        var role = await _auth.GetMemberRoleAsync(cmd.UserId, series.WorkspaceId, ct);
        var allowed = AiWorkflowPolicy.CopilotRoles(Task);
        if (role is null || !allowed.Contains(role.Value))
            return Fail($"Chỉ {string.Join("/", allowed)} của Workspace mới được dùng {Task}.", ResultErrorCodes.Forbidden);

        var chapter = await _db.Chapters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cmd.ChapterId && c.SeriesId == cmd.SeriesId, ct);
        if (chapter is null) return Fail($"Chapter {cmd.ChapterId} không thuộc Series.", ResultErrorCodes.NotFound);

        var gate = await CopilotGate.ResolveAsync(_db, _usage, _settings, series.WorkspaceId, series.Id, cmd.UserId,
            $"AiAgents.Copilot.{Task}", ct);
        if (!gate.IsSuccess) return Fail(gate.ErrorMessage!, gate.ErrorCode!);
        var p = gate.Value!;

        // Context gọn: dàn ý các chương tới chương đang phát hành (không gửi toàn bộ script) + nhân vật trong Bible hiệu lực
        var outlines = await _db.Chapters.AsNoTracking()
            .Where(c => c.SeriesId == series.Id && c.ChapterNumber <= chapter.ChapterNumber && c.Outline != null && c.Outline != "")
            .OrderByDescending(c => c.ChapterNumber).Take(MaxOutlines)
            .Select(c => new { c.ChapterNumber, c.Title, c.Outline }).ToListAsync(ct);
        var outlineTexts = outlines.OrderBy(o => o.ChapterNumber)
            .Select(o => $"Chương {o.ChapterNumber}{(string.IsNullOrWhiteSpace(o.Title) ? "" : $" ({o.Title})")}: " +
                         (o.Outline!.Length > MaxOutlineChars ? o.Outline[..MaxOutlineChars] : o.Outline))
            .ToList();
        var bible = (await AiSnapshotBuilder.BuildBibleAsync(_db, chapter, ct) ?? [])
            .Where(b => b.Category == "Character").Take(MaxCharacters).ToList();
        var context = new CopilotSeriesContext(series.Title, series.Synopsis, series.Genre, series.Format, series.StoryArc, outlineTexts);

        var run = AiWorkflowRun.Create(series.WorkspaceId, series.Id, chapter.Id, null, cmd.UserId, p.Provider, p.Model,
            string.IsNullOrWhiteSpace(cmd.Instruction) ? Task : $"{Task}: {cmd.Instruction}",
            series.ReadingDirection, 0, chapter.VersionVector, null,
            AiJson.Serialize(context), AiJson.Serialize(bible.Select(b => b.RevisionId)), AiWorkflowType.Copilot);
        _db.AiWorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var apiKey = CopilotGate.ApiKey(_settings, _keyProtector, p);
        if (apiKey is "")
        {
            run.MarkFailed(ResultErrorCodes.AiUnavailable, "Không giải mã được API key của Workspace.");
            await _db.SaveChangesAsync(ct);
            return Fail("Không giải mã được API key của Workspace (key ring đã đổi?). Admin cần nhập lại key.", ResultErrorCodes.AiUnavailable);
        }

        var response = await _aiClient.RunCopilotAsync(
            new CopilotRequest(Task, p.Provider, p.Model, apiKey, null, bible, null, cmd.Instruction?.Trim(), Series: context), ct);
        if (response.Succeeded && CopilotRules.ValidateMetadata(response.Metadata) is { Count: > 0 } errors)
            response = response with { Succeeded = false, ErrorCode = "INVALID_OUTPUT", ErrorMessage = string.Join(" ", errors) };

        var payload = response.Succeeded
            ? new SeriesMetadataSuggestion(AiProtocol.SchemaVersion, run.Id, series.Id, series.Synopsis, series.Genre,
                response.Metadata!, response.Reason ?? "")
            : null;
        var step = await CopilotGate.RecordStepAsync(_db, _usage, _settings, run, AiAgentType.Publisher, p, Task, response,
            new { task = Task, timeoutSeconds = _settings.CopilotTimeoutSeconds },
            new { chapterId = chapter.Id, outlines = outlineTexts.Count, characters = bible.Count, instruction = cmd.Instruction },
            payload is null ? null : AiJson.Serialize(payload), cmd.UserId, ct);

        if (payload is null)
        {
            run.MarkFailed(response.ErrorCode ?? ResultErrorCodes.AiUnavailable, response.ErrorMessage);
            await _db.SaveChangesAsync(ct);
            return Fail(response.ErrorMessage ?? "AI không trả được kết quả.", CopilotGate.FailureCode(response));
        }

        var json = AiJson.Serialize(payload);
        // SceneId = Empty: đề xuất cấp Series (cột không có khoá ngoại tới Scene)
        var suggestion = AiSuggestion.Create(run.Id, step.Id, series.WorkspaceId, series.Id, chapter.Id, Guid.Empty,
            AiSuggestionKind.SeriesMetadata, json, AiContentValidator.Sha256(json),
            Truncate($"Gợi ý phát hành: {payload.Metadata.Logline}", 1000), chapter.VersionVector, null, run.BibleRevisionIdsJson, null, false);
        _db.AiSuggestions.Add(suggestion);
        run.MarkCompleted();
        await _db.SaveChangesAsync(ct);

        return Result<AiSuggestionDto>.Success(AiRunHelpers.ToDto(suggestion, [], role));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
