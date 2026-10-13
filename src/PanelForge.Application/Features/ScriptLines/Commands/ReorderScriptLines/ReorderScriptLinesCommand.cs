using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;

namespace PanelForge.Application.Features.ScriptLines.Commands.ReorderScriptLines;

public sealed record ReorderScriptLineItem(Guid LineId, int NewOrder);

public sealed record ReorderScriptLinesRequest(
    IReadOnlyList<ReorderScriptLineItem> Lines,
    long? ExpectedChapterVersion = null);

public sealed record ReorderScriptLinesCommand(
    Guid ChapterId,
    Guid SceneId,
    Guid UserId,
    IReadOnlyList<ReorderScriptLineItem> Lines,
    long? ExpectedChapterVersion = null
) : IRequest<Result<bool>>;

public sealed class ReorderScriptLinesCommandHandler : IRequestHandler<ReorderScriptLinesCommand, Result<bool>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public ReorderScriptLinesCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<bool>> Handle(ReorderScriptLinesCommand cmd, CancellationToken ct)
    {
        var authorized = await ScriptLineRules.AuthorizeAsync(_db, _auth, cmd.UserId, cmd.ChapterId, cmd.ExpectedChapterVersion, ct);
        if (!authorized.IsSuccess)
            return Result<bool>.Failure(authorized.ErrorMessage!, authorized.ErrorCode);

        var chapter = authorized.Value.Chapter;

        // Scene phải thuộc Chapter
        if (!await _db.Scenes.AsNoTracking().AnyAsync(s => s.Id == cmd.SceneId && s.ChapterId == chapter.Id, ct))
            return Result<bool>.Failure($"Scene {cmd.SceneId} không tồn tại trong Chapter này.", ResultErrorCodes.NotFound);

        var lines = await _db.ScriptLines.Where(l => l.SceneId == cmd.SceneId).ToListAsync(ct);
        if (lines.Count == 0)
            return Result<bool>.Success(true);

        var lineMap = lines.ToDictionary(l => l.Id);
        var before = ScriptLineRules.Snapshot(lines);

        foreach (var item in cmd.Lines)
        {
            if (lineMap.TryGetValue(item.LineId, out var line))
            {
                line.Reorder(item.NewOrder);
            }
        }

        var after = ScriptLineRules.Snapshot(lines);
        await ScriptLineRules.RecordRevisionAsync(
            _db, chapter, cmd.SceneId, "Đổi thứ tự dòng kịch bản (Reorder)", before, after, ct);

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
