using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.AI.Contracts;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Commands.RestoreScriptRevision;

/// <summary>
/// UC-13: khôi phục script của một Scene về một revision cũ. Không xoá, không sửa revision nào (BR-01): ghi một revision MỚI
/// ở đầu lịch sử mang nội dung của bản được chọn (BR-08). Chương đã Approved/Locked phải mở lại trước (BR-12).
/// Dòng giữ nguyên Id: dòng đã xoá mềm được bật lại nên neo Panel và chữ lettering gắn dòng đó vẫn đúng.
/// </summary>
public sealed record RestoreScriptRevisionCommand(
    Guid ChapterId, Guid SceneId, Guid RevisionId, Guid UserId, long? ExpectedChapterVersion)
    : IRequest<Result<RestoreScriptRevisionResult>>;

public sealed class RestoreScriptRevisionCommandHandler
    : IRequestHandler<RestoreScriptRevisionCommand, Result<RestoreScriptRevisionResult>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;

    public RestoreScriptRevisionCommandHandler(IPanelForgeDbContext db, IWorkspaceAuthorizationService auth)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<Result<RestoreScriptRevisionResult>> Handle(RestoreScriptRevisionCommand cmd, CancellationToken ct)
    {
        var authorized = await ScriptLineRules.AuthorizeAsync(_db, _auth, cmd.UserId, cmd.ChapterId, cmd.ExpectedChapterVersion, ct);
        if (!authorized.IsSuccess)
            return Fail(authorized.ErrorMessage!, authorized.ErrorCode!);
        var chapter = authorized.Value.Chapter;

        // Chặn IDOR: Scene phải thuộc Chapter trên route, revision phải là revision Script của đúng Scene đó
        if (!await _db.Scenes.AsNoTracking().AnyAsync(s => s.Id == cmd.SceneId && s.ChapterId == chapter.Id, ct))
            return Fail($"Scene {cmd.SceneId} không tồn tại trong Chapter này.", ResultErrorCodes.NotFound);

        var target = await _db.ContentRevisions.AsNoTracking().FirstOrDefaultAsync(
            r => r.Id == cmd.RevisionId && r.SceneId == cmd.SceneId && r.Kind == ContentRevisionKind.Script, ct);
        if (target is null)
            return Fail($"Revision script {cmd.RevisionId} không tồn tại trong Scene này.", ResultErrorCodes.NotFound);

        var latestId = await _db.ContentRevisions.AsNoTracking()
            .Where(r => r.SceneId == cmd.SceneId && r.Kind == ContentRevisionKind.Script)
            .OrderByDescending(r => r.VersionNumber).Select(r => r.Id).FirstAsync(ct);
        if (latestId == target.Id)
            return Fail("Revision này đang là bản hiện tại, không có gì để khôi phục.", ResultErrorCodes.Validation);

        var wanted = ScriptLineRules.ParseRevisionLines(target.PayloadJson);
        if (wanted is null || wanted.Any(w => w.Order <= 0 || ScriptLineRules.ValidateContent(w.Dialogue, w.StageDirection) is not null))
            return Fail("Nội dung revision không đọc được nên không khôi phục được.", ResultErrorCodes.Validation);

        // Kể cả dòng đã xoá mềm: khôi phục bật lại đúng dòng cũ thay vì tạo dòng mới trùng Id
        var rows = await _db.ScriptLines.IgnoreQueryFilters().Where(l => l.SceneId == cmd.SceneId).ToListAsync(ct);
        var live = rows.Where(l => !l.IsDeleted).ToList();
        var before = ScriptLineRules.Snapshot(live);

        // Speaker / Panel mà bản cũ trỏ tới có thể đã bị xoá sau đó: bỏ tham chiếu thay vì trỏ vào bản ghi đã xoá
        var speakerIds = wanted.Where(w => w.SpeakerId is not null).Select(w => w.SpeakerId!.Value).Distinct().ToList();
        var liveSpeakers = await _db.BibleEntries.AsNoTracking().Where(e => speakerIds.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
        var panelIds = rows.Where(l => l.PanelId is not null).Select(l => l.PanelId!.Value).Distinct().ToList();
        var livePanels = await _db.Panels.AsNoTracking().Where(p => panelIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);

        var wantedIds = wanted.Select(w => w.LineId).ToHashSet();
        foreach (var line in live.Where(l => !wantedIds.Contains(l.Id)))
        {
            line.DeletedBy = cmd.UserId.ToString();
            _db.ScriptLines.Remove(line); // soft-delete; nội dung còn trong các revision trước
        }

        var after = new List<ScriptLine>();
        foreach (var w in wanted)
        {
            Guid? speaker = w.SpeakerId is { } sid && liveSpeakers.Contains(sid) ? sid : null;
            var row = rows.FirstOrDefault(l => l.Id == w.LineId);
            if (row is null)
            {
                row = ScriptLine.Create(cmd.SceneId, w.Order, w.Dialogue, w.StageDirection, speaker, w.LineId);
                _db.ScriptLines.Add(row);
            }
            else
            {
                if (row.IsDeleted)
                {
                    row.IsDeleted = false;
                    row.DeletedAt = null;
                    row.DeletedBy = null;
                    if (row.PanelId is { } pid && !livePanels.Contains(pid)) row.AssignToPanel(null);
                }
                row.UpdateContent(w.Dialogue, w.StageDirection, speaker);
                row.Reorder(w.Order);
            }
            after.Add(row);
        }

        var revision = await ScriptLineRules.RecordRevisionAsync(
            _db, chapter, cmd.SceneId, $"Khôi phục về phiên bản {target.VersionNumber}", before, ScriptLineRules.Snapshot(after), ct,
            AiJson.Serialize(new { restoredFromRevisionId = target.Id, restoredFromVersionNumber = target.VersionNumber }));

        await _db.SaveChangesAsync(ct);
        return Result<RestoreScriptRevisionResult>.Success(new RestoreScriptRevisionResult(
            chapter.VersionVector, revision.Id, revision.VersionNumber, target.Id, target.VersionNumber));
    }

    private static Result<RestoreScriptRevisionResult> Fail(string message, string code) =>
        Result<RestoreScriptRevisionResult>.Failure(message, code);
}
