using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Entities.Content;

namespace PanelForge.Application.Features.ScriptLines.Commands.CreateScriptLine;

/// <summary>
/// UC-06: Writer thêm một dòng script bằng tay (thoại hoặc chỉ dẫn, speaker tuỳ chọn). Không phụ thuộc AI.
/// Ghi revision Script mới và tăng version chương cùng lần lưu.
/// </summary>
public sealed record CreateScriptLineCommand(
    Guid ChapterId, Guid SceneId, Guid UserId, string? DialogueText, string? StageDirection,
    Guid? SpeakerCharacterId, int? LineOrder, long? ExpectedChapterVersion) : IRequest<Result<ScriptLineDto>>;

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

        var authorized = await ScriptLineRules.AuthorizeAsync(_db, _auth, cmd.UserId, cmd.ChapterId, cmd.ExpectedChapterVersion, ct);
        if (!authorized.IsSuccess)
            return Result<ScriptLineDto>.Failure(authorized.ErrorMessage!, authorized.ErrorCode);
        var chapter = authorized.Value.Chapter;

        // Chặn IDOR: Scene phải thuộc Chapter trên route
        if (!await _db.Scenes.AsNoTracking().AnyAsync(s => s.Id == cmd.SceneId && s.ChapterId == chapter.Id, ct))
            return Result<ScriptLineDto>.Failure($"Scene {cmd.SceneId} không tồn tại trong Chapter này.", ResultErrorCodes.NotFound);

        if (await ScriptLineRules.ValidateSpeakerAsync(_db, chapter.SeriesId, cmd.SpeakerCharacterId, ct) is { } speakerError)
            return Result<ScriptLineDto>.Failure(speakerError, ResultErrorCodes.Validation);

        var lines = await _db.ScriptLines.Where(l => l.SceneId == cmd.SceneId).ToListAsync(ct);
        var before = ScriptLineRules.Snapshot(lines);

        // Chèn vào giữa thì dịch các dòng từ vị trí đó xuống một; vị trí quá cuối thì thêm vào cuối
        var end = (lines.Count == 0 ? 0 : lines.Max(l => l.LineOrder)) + 1;
        var order = Math.Min(cmd.LineOrder ?? end, end);
        foreach (var l in lines.Where(l => l.LineOrder >= order)) l.Reorder(l.LineOrder + 1);

        var line = ScriptLine.Create(cmd.SceneId, order,
            string.IsNullOrWhiteSpace(cmd.DialogueText) ? null : cmd.DialogueText,
            string.IsNullOrWhiteSpace(cmd.StageDirection) ? null : cmd.StageDirection,
            cmd.SpeakerCharacterId);
        _db.ScriptLines.Add(line);
        lines.Add(line);

        var revision = await ScriptLineRules.RecordRevisionAsync(
            _db, chapter, cmd.SceneId, "Thêm dòng script thủ công", before, ScriptLineRules.Snapshot(lines), ct);

        await _db.SaveChangesAsync(ct);
        return Result<ScriptLineDto>.Success(ScriptLineDto.From(line, chapter.VersionVector, revision.Id));
    }
}
