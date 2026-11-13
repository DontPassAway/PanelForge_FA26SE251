using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Entities.Content;
using PanelForge.Domain.Enums;

namespace PanelForge.Application.Features.ScriptLines.Commands.BatchSaveScriptLines;

public sealed record BatchSaveScriptLinesCommand(
    Guid ChapterId,
    Guid SceneId,
    Guid UserId,
    IReadOnlyList<BatchScriptLineItem> Lines,
    long? ExpectedChapterVersion = null,
    string? Summary = null,
    string? RawText = null
) : IRequest<Result<BatchSaveScriptLinesResult>>;

public sealed class BatchSaveScriptLinesCommandHandler
    : IRequestHandler<BatchSaveScriptLinesCommand, Result<BatchSaveScriptLinesResult>>
{
    private readonly IPanelForgeDbContext _db;
    private readonly IWorkspaceAuthorizationService _auth;
    private readonly IPageRepository _pages;

    public BatchSaveScriptLinesCommandHandler(
        IPanelForgeDbContext db,
        IWorkspaceAuthorizationService auth,
        IPageRepository pages)
    {
        _db = db;
        _auth = auth;
        _pages = pages;
    }

    public async Task<Result<BatchSaveScriptLinesResult>> Handle(
        BatchSaveScriptLinesCommand cmd,
        CancellationToken ct)
    {
        // 1. Phân quyền và kiểm tra phiên bản Chapter
        var authorized = await ScriptLineRules.AuthorizeAsync(
            _db, _auth, cmd.UserId, cmd.ChapterId, cmd.ExpectedChapterVersion, ct);
        if (!authorized.IsSuccess)
            return Result<BatchSaveScriptLinesResult>.Failure(authorized.ErrorMessage!, authorized.ErrorCode);

        var chapter = authorized.Value.Chapter;

        // 2. Chặn IDOR: Scene phải thuộc Chapter trên route
        var scene = await _db.Scenes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == cmd.SceneId && s.ChapterId == chapter.Id, ct);
        if (scene is null)
            return Result<BatchSaveScriptLinesResult>.Failure($"Scene {cmd.SceneId} không tồn tại trong Chapter này.", ResultErrorCodes.NotFound);

        // 3. Kiểm tra tính hợp lệ của từng dòng trong danh sách
        foreach (var item in cmd.Lines)
        {
            if (ScriptLineRules.ValidateContent(item.DialogueText, item.StageDirection) is { } invalid)
                return Result<BatchSaveScriptLinesResult>.Failure($"Dòng order {item.LineOrder}: {invalid}", ResultErrorCodes.Validation);

            if (item.LineOrder < 1)
                return Result<BatchSaveScriptLinesResult>.Failure($"LineOrder phải từ 1 trở lên (dòng {item.LineOrder}).", ResultErrorCodes.Validation);

            if (await ScriptLineRules.ValidateSpeakerAsync(_db, chapter.SeriesId, item.SpeakerCharacterId, ct) is { } speakerError)
                return Result<BatchSaveScriptLinesResult>.Failure(speakerError, ResultErrorCodes.Validation);

            if (await ScriptLineRules.ValidateBibleEntriesAsync(_db, chapter.SeriesId, item.BibleEntryIds, ct) is { } linkError)
                return Result<BatchSaveScriptLinesResult>.Failure(linkError, ResultErrorCodes.Validation);
        }

        // 4. Bàn viết gửi cả kịch bản chương: lấy dòng của mọi Scene trong chương (chương cũ có thể còn nhiều Scene).
        // Chỉ lấy theo Scene đích sẽ coi dòng của Scene khác là dòng mới → INSERT trùng Id (PK_script_lines).
        var existingLines = await _db.ScriptLines.Where(l => l.Scene.ChapterId == chapter.Id).ToListAsync(ct);
        var beforeSnapshot = ScriptLineRules.Snapshot(existingLines);

        var existingMap = existingLines.ToDictionary(l => l.Id);
        var incomingTargetIds = new HashSet<Guid>();
        var finalLiveLines = new List<ScriptLine>();
        var bibleLinksByLine = new Dictionary<Guid, IReadOnlyList<Guid>>();

        var modifiedLinesForLettering = new List<(ScriptLine Line, string? TextBefore, Guid? SpeakerBefore)>();

        foreach (var item in cmd.Lines)
        {
            var targetId = item.Id ?? item.ClientLineId;
            ScriptLine? line = null;

            if (targetId.HasValue && existingMap.TryGetValue(targetId.Value, out var found))
            {
                line = found;
            }

            var text = string.IsNullOrWhiteSpace(item.DialogueText) ? null : item.DialogueText;
            var direction = string.IsNullOrWhiteSpace(item.StageDirection) ? null : item.StageDirection;
            var mode = item.NarrationMode ?? scene.DefaultNarrationMode;

            if (line is not null)
            {
                var textBefore = ScriptLineRules.LetteringText(line);
                var speakerBefore = line.SpeakerCharacterId;

                line.UpdateContent(text, direction, item.SpeakerCharacterId, mode, item.Kind, item.SpeakerLabel, item.Text);
                line.Reorder(item.LineOrder);
                if (line.SceneId != cmd.SceneId) line.MoveToScene(cmd.SceneId);
                if (item.PanelId != line.PanelId)
                {
                    line.AssignToPanel(item.PanelId);
                }

                if (ScriptLineRules.LetteringText(line) != textBefore || line.SpeakerCharacterId != speakerBefore)
                {
                    modifiedLinesForLettering.Add((line, textBefore, speakerBefore));
                }
            }
            else
            {
                line = ScriptLine.Create(
                    cmd.SceneId,
                    item.LineOrder,
                    text,
                    direction,
                    item.SpeakerCharacterId,
                    targetId,
                    mode,
                    item.Kind,
                    item.SpeakerLabel,
                    item.Text);
                if (item.PanelId.HasValue)
                {
                    line.AssignToPanel(item.PanelId.Value);
                }
                _db.ScriptLines.Add(line);
            }

            incomingTargetIds.Add(line.Id);
            finalLiveLines.Add(line);

            // Gắn Bible links cho dòng
            var links = await ScriptLineRules.SetBibleLinksAsync(_db, line.Id, item.BibleEntryIds, ct);
            bibleLinksByLine[line.Id] = links;
        }

        // 5. Xoá mềm các dòng không còn xuất hiện trong payload
        var deletedLines = existingLines.Where(l => !incomingTargetIds.Contains(l.Id)).ToList();
        foreach (var toDelete in deletedLines)
        {
            toDelete.DeletedBy = cmd.UserId.ToString();
            _db.ScriptLines.Remove(toDelete);

            if (await ScriptLineRules.ReworkApprovedLetteringAsync(_db, _pages, chapter.Id, toDelete,
                    "Writer đã xoá dòng thoại trong lần lưu đồng bộ; cần cập nhật bóng thoại.", ct) is { } reworkDelError)
            {
                return Result<BatchSaveScriptLinesResult>.Failure(reworkDelError, ResultErrorCodes.Conflict);
            }
        }

        // 6. Kiểm tra lettering rework đối với các dòng đã sửa
        foreach (var (modLine, _, _) in modifiedLinesForLettering)
        {
            if (await ScriptLineRules.ReworkApprovedLetteringAsync(_db, _pages, chapter.Id, modLine,
                    "Writer đã sửa thoại trong lần lưu đồng bộ; cần cập nhật bóng thoại theo script mới.", ct) is { } reworkModError)
            {
                return Result<BatchSaveScriptLinesResult>.Failure(reworkModError, ResultErrorCodes.Conflict);
            }
        }

        // 7. Ghi revision append-only duy nhất cho toàn bộ lượt lưu
        var afterSnapshot = ScriptLineRules.Snapshot(finalLiveLines);
        var summary = string.IsNullOrWhiteSpace(cmd.Summary) ? "Đồng bộ kịch bản hàng loạt (Batch Save)" : cmd.Summary;

        var revision = await ScriptLineRules.RecordRevisionAsync(
            _db, chapter, cmd.SceneId, summary, beforeSnapshot, afterSnapshot, ct);

        // 8. Tự động đồng bộ và lưu toàn văn kịch bản hoàn chỉnh (ChapterScript.RawText)
        if (!string.IsNullOrWhiteSpace(cmd.RawText))
        {
            var chapterScript = await _db.ChapterScripts.FirstOrDefaultAsync(s => s.ChapterId == chapter.Id, ct);
            if (chapterScript == null)
            {
                chapterScript = ChapterScript.Create(chapter.Id, cmd.RawText);
                _db.ChapterScripts.Add(chapterScript);
            }
            else if (!chapterScript.IsLocked)
            {
                chapterScript.UpdateRawText(cmd.RawText, cmd.UserId);
            }
        }

        await _db.SaveChangesAsync(ct);

        var lineDtos = finalLiveLines
            .OrderBy(l => l.LineOrder)
            .Select(l => ScriptLineDto.From(l, chapter.VersionVector, revision.Id, bibleLinksByLine.GetValueOrDefault(l.Id, [])))
            .ToList();

        return Result<BatchSaveScriptLinesResult>.Success(
            new BatchSaveScriptLinesResult(chapter.VersionVector, revision.Id, lineDtos));
    }
}

