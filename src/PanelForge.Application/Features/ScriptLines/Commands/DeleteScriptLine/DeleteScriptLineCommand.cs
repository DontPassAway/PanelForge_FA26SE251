using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;

namespace PanelForge.Application.Features.ScriptLines.Commands.DeleteScriptLine;

/// <summary>
/// UC-06: xoá mềm một dòng script. Nội dung cũ còn trong revision (BR-01). Chữ lettering đang gắn dòng này thành lỗi thời (BR-05);
/// Panel đang neo dòng này đơn giản không còn dòng đó. Không đánh số lại các dòng còn lại.
/// </summary>
public sealed record DeleteScriptLineCommand(Guid ChapterId, Guid LineId, Guid UserId, long? ExpectedChapterVersion)
    : IRequest<Result<DeleteScriptLineResult>>;

public sealed class DeleteScriptLineCommandHandler : IRequestHandler<DeleteScriptLineCommand, Result<DeleteScriptLineResult>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public DeleteScriptLineCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<DeleteScriptLineResult>> Handle(DeleteScriptLineCommand cmd, CancellationToken ct)
    {
        var authorized = await ScriptLineRules.AuthorizeAsync(_db, _auth, cmd.UserId, cmd.ChapterId, cmd.ExpectedChapterVersion, ct);
        if (!authorized.IsSuccess)
            return Result<DeleteScriptLineResult>.Failure(authorized.ErrorMessage!, authorized.ErrorCode);
        var chapter = authorized.Value.Chapter;

        var line = await _db.ScriptLines.FirstOrDefaultAsync(l => l.Id == cmd.LineId &&
            _db.Scenes.Any(s => s.Id == l.SceneId && s.ChapterId == chapter.Id), ct);
        if (line is null)
            return Result<DeleteScriptLineResult>.Failure($"ScriptLine {cmd.LineId} không tồn tại trong Chapter này.", ResultErrorCodes.NotFound);

        var lines = await _db.ScriptLines.Where(l => l.SceneId == line.SceneId).ToListAsync(ct);
        var before = ScriptLineRules.Snapshot(lines);
        var remaining = lines.Where(l => l.Id != line.Id).ToList();

        line.DeletedBy = cmd.UserId.ToString();
        _db.ScriptLines.Remove(line); // soft-delete

        var revision = await ScriptLineRules.RecordRevisionAsync(
            _db, chapter, line.SceneId, "Xoá dòng script thủ công", before, ScriptLineRules.Snapshot(remaining), ct);

        await _db.SaveChangesAsync(ct);
        return Result<DeleteScriptLineResult>.Success(new DeleteScriptLineResult(chapter.VersionVector, revision.Id));
    }
}
