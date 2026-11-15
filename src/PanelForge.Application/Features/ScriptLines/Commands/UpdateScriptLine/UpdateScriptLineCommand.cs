using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;

namespace PanelForge.Application.Features.ScriptLines.Commands.UpdateScriptLine;

/// <summary>
/// UC-06: sửa thoại / chỉ dẫn / speaker của một dòng. Thay toàn bộ nội dung. Chữ lettering đã gắn dòng này sẽ tự bị đánh dấu
/// lỗi thời (BR-05) và đề xuất AI dựa trên bản cũ thành stale nhờ revision + version mới.
/// </summary>
public sealed record UpdateScriptLineCommand(
    Guid ChapterId, Guid LineId, Guid UserId, string? DialogueText, string? StageDirection,
    long? ExpectedChapterVersion,
    PanelForge.Domain.Enums.NarrationMode? NarrationMode = null,
    PanelForge.Domain.Enums.ScriptLineKind? Kind = null,
    string? SpeakerLabel = null,
    string? Text = null) : IRequest<Result<ScriptLineDto>>;

public sealed class UpdateScriptLineCommandHandler : IRequestHandler<UpdateScriptLineCommand, Result<ScriptLineDto>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IPageRepository _pages;

    public UpdateScriptLineCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth, IPageRepository pages)
    {
        _db = db;
        _auth = auth;
        _pages = pages;
    }

    public async Task<Result<ScriptLineDto>> Handle(UpdateScriptLineCommand cmd, CancellationToken ct)
    {
        if (ScriptLineRules.ValidateContent(cmd.DialogueText, cmd.StageDirection) is { } invalid)
            return Result<ScriptLineDto>.Failure(invalid, ResultErrorCodes.Validation);

        var authorized = await ScriptLineRules.AuthorizeAsync(_db, _auth, cmd.UserId, cmd.ChapterId, cmd.ExpectedChapterVersion, ct);
        if (!authorized.IsSuccess)
            return Result<ScriptLineDto>.Failure(authorized.ErrorMessage!, authorized.ErrorCode);
        var chapter = authorized.Value.Chapter;

        // Chặn IDOR: dòng phải thuộc một Scene của Chapter trên route
        var line = await _db.ScriptLines.FirstOrDefaultAsync(l => l.Id == cmd.LineId &&
            _db.Scenes.Any(s => s.Id == l.SceneId && s.ChapterId == chapter.Id), ct);
        if (line is null)
            return Result<ScriptLineDto>.Failure($"ScriptLine {cmd.LineId} không tồn tại trong Chapter này.", ResultErrorCodes.NotFound);

        var lines = await _db.ScriptLines.Where(l => l.SceneId == line.SceneId).ToListAsync(ct);
        var before = ScriptLineRules.Snapshot(lines);
        var textBefore = ScriptLineRules.LetteringText(line);

        line.UpdateContent(
            string.IsNullOrWhiteSpace(cmd.DialogueText) ? null : cmd.DialogueText,
            string.IsNullOrWhiteSpace(cmd.StageDirection) ? null : cmd.StageDirection,
            cmd.NarrationMode ?? line.NarrationMode,
            cmd.Kind,
            cmd.SpeakerLabel,
            cmd.Text);

        var revision = await ScriptLineRules.RecordRevisionAsync(
            _db, chapter, line.SceneId, "Sửa dòng script thủ công", before, ScriptLineRules.Snapshot(lines), ct);

        // CF4: thoại đổi thật → Lettering đã duyệt của bóng gắn dòng này phải làm lại
        var sourceChanged = ScriptLineRules.LetteringText(line) != textBefore;
        if (sourceChanged && await ScriptLineRules.ReworkApprovedLetteringAsync(_db, _pages, chapter.Id, line,
                "Writer đã sửa thoại đã được đặt chữ; cần cập nhật bóng thoại theo script mới.", ct) is { } reworkError)
            return Result<ScriptLineDto>.Failure(reworkError, ResultErrorCodes.Conflict);

        await _db.SaveChangesAsync(ct);
        return Result<ScriptLineDto>.Success(ScriptLineDto.From(line, chapter.VersionVector, revision.Id, []));
    }
}
