using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Commands.CreateScriptLine;

/// <summary>
/// UC-06: Writer thêm một dòng script bằng tay (thoại hoặc chỉ dẫn, speaker tuỳ chọn). Không phụ thuộc AI.
/// Ghi revision Script mới và tăng version chương cùng lần lưu.
/// </summary>
public sealed record CreateScriptLineCommand(
    Guid ChapterId, Guid SceneId, Guid UserId, string? DialogueText, string? StageDirection,
    Guid? SpeakerCharacterId, int? LineOrder, long? ExpectedChapterVersion,
    IReadOnlyList<Guid>? BibleEntryIds = null, Guid? ClientLineId = null,
    NarrationMode? NarrationMode = null) : IRequest<Result<ScriptLineDto>>;

public sealed class CreateScriptLineCommandHandler : IRequestHandler<CreateScriptLineCommand, Result<ScriptLineDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public CreateScriptLineCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<ScriptLineDto>> Handle(CreateScriptLineCommand cmd, CancellationToken ct)
    {
        if (ScriptLineRules.ValidateContent(cmd.DialogueText, cmd.StageDirection) is { } invalid)
            return Result<ScriptLineDto>.Failure(invalid, ResultErrorCodes.Validation);
        if (cmd.LineOrder is < 1)
            return Result<ScriptLineDto>.Failure("LineOrder phải từ 1 trở lên.", ResultErrorCodes.Validation);

        if (cmd.ClientLineId == Guid.Empty)
            return Result<ScriptLineDto>.Failure("clientLineId không hợp lệ.", ResultErrorCodes.Validation);

        // NFR-09: gửi lại sau mất mạng → trả dòng đã tạo từ lần trước (idempotent). Vẫn kiểm quyền, nhưng KHÔNG so
        // expectedChapterVersion vì lần gửi đầu đã làm tăng version.
        if (cmd.ClientLineId is { } clientId
            && await _db.ScriptLines.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Id == clientId, ct) is { } existing)
        {
            var replayAuth = await ScriptLineRules.AuthorizeAsync(_db, _auth, cmd.UserId, cmd.ChapterId, null, ct);
            if (!replayAuth.IsSuccess)
                return Result<ScriptLineDto>.Failure(replayAuth.ErrorMessage!, replayAuth.ErrorCode);
            if (existing.SceneId != cmd.SceneId)
                return Result<ScriptLineDto>.Failure($"clientLineId {clientId} đã dùng cho dòng của Scene khác.", ResultErrorCodes.Conflict);

            var revisionId = await _db.ContentRevisions.AsNoTracking()
                .Where(r => r.SceneId == cmd.SceneId && r.Kind == ContentRevisionKind.Script)
                .OrderByDescending(r => r.VersionNumber).Select(r => r.Id).FirstAsync(ct);
            var linked = await ScriptLineRules.SetBibleLinksAsync(_db, clientId, null, ct);
            return Result<ScriptLineDto>.Success(
                ScriptLineDto.From(existing, replayAuth.Value.Chapter.VersionVector, revisionId, linked) with { Replayed = true });
        }

        var authorized = await ScriptLineRules.AuthorizeAsync(_db, _auth, cmd.UserId, cmd.ChapterId, cmd.ExpectedChapterVersion, ct);
        if (!authorized.IsSuccess)
            return Result<ScriptLineDto>.Failure(authorized.ErrorMessage!, authorized.ErrorCode);
        var chapter = authorized.Value.Chapter;

        // Chặn IDOR: Scene phải thuộc Chapter trên route
        var scene = await _db.Scenes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == cmd.SceneId && s.ChapterId == chapter.Id, ct);
        if (scene is null)
            return Result<ScriptLineDto>.Failure($"Scene {cmd.SceneId} không tồn tại trong Chapter này.", ResultErrorCodes.NotFound);

        if (await ScriptLineRules.ValidateSpeakerAsync(_db, chapter.SeriesId, cmd.SpeakerCharacterId, ct) is { } speakerError)
            return Result<ScriptLineDto>.Failure(speakerError, ResultErrorCodes.Validation);
        if (await ScriptLineRules.ValidateBibleEntriesAsync(_db, chapter.SeriesId, cmd.BibleEntryIds, ct) is { } linkError)
            return Result<ScriptLineDto>.Failure(linkError, ResultErrorCodes.Validation);

        var lines = await _db.ScriptLines.Where(l => l.SceneId == cmd.SceneId).ToListAsync(ct);
        var before = ScriptLineRules.Snapshot(lines);

        // Chèn vào giữa thì dịch các dòng từ vị trí đó xuống một; vị trí quá cuối thì thêm vào cuối
        var end = (lines.Count == 0 ? 0 : lines.Max(l => l.LineOrder)) + 1;
        var order = Math.Min(cmd.LineOrder ?? end, end);
        foreach (var l in lines.Where(l => l.LineOrder >= order)) l.Reorder(l.LineOrder + 1);

        var mode = cmd.NarrationMode ?? scene.DefaultNarrationMode;
        var line = ScriptLine.Create(cmd.SceneId, order,
            string.IsNullOrWhiteSpace(cmd.DialogueText) ? null : cmd.DialogueText,
            string.IsNullOrWhiteSpace(cmd.StageDirection) ? null : cmd.StageDirection,
            cmd.SpeakerCharacterId,
            cmd.ClientLineId,
            mode);
        _db.ScriptLines.Add(line);
        lines.Add(line);
        var bibleEntryIds = await ScriptLineRules.SetBibleLinksAsync(_db, line.Id, cmd.BibleEntryIds ?? [], ct);

        var revision = await ScriptLineRules.RecordRevisionAsync(
            _db, chapter, cmd.SceneId, "Thêm dòng script thủ công", before, ScriptLineRules.Snapshot(lines), ct);

        await _db.SaveChangesAsync(ct);
        return Result<ScriptLineDto>.Success(ScriptLineDto.From(line, chapter.VersionVector, revision.Id, bibleEntryIds));
    }
}
