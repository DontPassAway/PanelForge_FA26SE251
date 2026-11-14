using MediatR;
using Microsoft.EntityFrameworkCore;
using PanelForge.Application.Common;
using PanelForge.Application.Common.Interfaces.Persistence;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Application.Features.ScriptLines.Common;
using PanelForge.Application.Features.ScriptLines.Models;
using PanelForge.Domain.Common;
using PanelForge.Domain.Entities.Bible;
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
        var existingLines = await _db.ScriptLines.Where(l => l.Scene.ChapterId == chapter.Id).ToListAsync(ct);
        var existingMap = existingLines.ToDictionary(l => l.Id);
        var warnings = new List<string>();

        foreach (var item in cmd.Lines)
        {
            if (ScriptLineRules.ValidateContent(item.DialogueText, item.StageDirection) is { } invalid)
                return Result<BatchSaveScriptLinesResult>.Failure($"Dòng order {item.LineOrder}: {invalid}", ResultErrorCodes.Validation);

            if (item.LineOrder < 1)
                return Result<BatchSaveScriptLinesResult>.Failure($"LineOrder phải từ 1 trở lên (dòng {item.LineOrder}).", ResultErrorCodes.Validation);

            // BIB-12, 1A: Chỉ chấp nhận refs trên dòng Action, Narration (Caption), Sfx; dòng khác có refs -> 400
            var targetIdCheck = item.Id ?? item.ClientLineId;
            var existingLineCheck = targetIdCheck.HasValue && existingMap.TryGetValue(targetIdCheck.Value, out var foundCheck) ? foundCheck : null;
            var effectiveKind = item.Kind ?? existingLineCheck?.Kind ?? ScriptLineKind.Action;

            if (item.Refs is { Count: > 0 } && effectiveKind is not (ScriptLineKind.Action or ScriptLineKind.Narration or ScriptLineKind.Sfx))
            {
                return Result<BatchSaveScriptLinesResult>.Failure(
                    $"Dòng {item.LineOrder}: Chỉ cho phép gắn tham chiếu Series Bible trên dòng Action, Narration hoặc Sfx.",
                    ResultErrorCodes.Validation);
            }

            if (await ScriptLineRules.ValidateSpeakerAsync(_db, chapter.SeriesId, item.SpeakerCharacterId, ct) is { } speakerError)
                return Result<BatchSaveScriptLinesResult>.Failure(speakerError, ResultErrorCodes.Validation);

            if (await ScriptLineRules.ValidateBibleEntriesAsync(_db, chapter.SeriesId, item.BibleEntryIds, ct) is { } linkError)
                return Result<BatchSaveScriptLinesResult>.Failure(linkError, ResultErrorCodes.Validation);
        }

        // 4. SeriesBible của Series
        var seriesBible = await _db.SeriesBibles.FirstOrDefaultAsync(b => b.SeriesId == chapter.SeriesId, ct);
        if (seriesBible is null)
        {
            var series = await _db.Series.AsNoTracking().FirstOrDefaultAsync(s => s.Id == chapter.SeriesId, ct);
            var workspaceId = series?.WorkspaceId ?? Guid.Empty;
            seriesBible = SeriesBible.Create(chapter.SeriesId, workspaceId, "Series Bible");
            _db.SeriesBibles.Add(seriesBible);
            await _db.SaveChangesAsync(ct);
        }

        var beforeSnapshot = ScriptLineRules.Snapshot(existingLines);

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

            // Xử lý tham chiếu Series Bible (BIB-12, 1A, 2A, 5A)
            var lineFullText = line.Text ?? line.DialogueText ?? line.StageDirection ?? string.Empty;
            var normLineText = BibleNameNormalizer.Normalize(lineFullText);

            if (item.Refs is not null)
            {
                var validRefs = new List<(Guid BibleEntryId, string SurfaceText)>();
                var seenEntries = new HashSet<Guid>();

                foreach (var r in item.Refs)
                {
                    if (string.IsNullOrWhiteSpace(r.SurfaceText))
                        continue;

                    var normSurface = BibleNameNormalizer.Normalize(r.SurfaceText);
                    if (string.IsNullOrEmpty(normSurface) || !normLineText.Contains(normSurface))
                    {
                        warnings.Add($"Dòng {item.LineOrder}: tham chiếu '{r.SurfaceText}' không có trong nội dung dòng.");
                        continue;
                    }

                    Guid? targetEntryId = null;
                    if (r.BibleEntryId.HasValue)
                    {
                        var entry = await _db.BibleEntries.AsNoTracking()
                            .FirstOrDefaultAsync(e => e.Id == r.BibleEntryId.Value && e.SeriesBibleId == seriesBible.Id, ct);
                        if (entry is null || !entry.IsActive)
                        {
                            return Result<BatchSaveScriptLinesResult>.Failure(
                                $"Dòng {item.LineOrder}: Mục Bible '{r.BibleEntryId}' không tồn tại hoặc đã bị vô hiệu hóa.",
                                ResultErrorCodes.Validation);
                        }
                        if (entry.Category is not (BibleEntryCategory.Character or BibleEntryCategory.Location or BibleEntryCategory.Prop))
                        {
                            return Result<BatchSaveScriptLinesResult>.Failure(
                                $"Dòng {item.LineOrder}: Chỉ cho phép tham chiếu mục Nhân vật, Địa điểm hoặc Đạo cụ.",
                                ResultErrorCodes.Validation);
                        }
                        targetEntryId = entry.Id;
                    }
                    else if (r.NewDraft is not null)
                    {
                        var draftName = r.NewDraft.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(draftName))
                        {
                            warnings.Add($"Dòng {item.LineOrder}: tên mục nháp không được để trống.");
                            continue;
                        }
                        if (r.NewDraft.Category is not (BibleEntryCategory.Character or BibleEntryCategory.Location or BibleEntryCategory.Prop))
                        {
                            return Result<BatchSaveScriptLinesResult>.Failure(
                                $"Dòng {item.LineOrder}: Mục nháp phải thuộc danh mục Nhân vật, Địa điểm hoặc Đạo cụ.",
                                ResultErrorCodes.Validation);
                        }

                        var normDraftName = BibleNameNormalizer.Normalize(draftName);
                        var localName = _db.BibleEntryNames.Local.FirstOrDefault(
                            n => n.SeriesBibleId == seriesBible.Id && n.NormalizedName == normDraftName);
                        if (localName is not null)
                        {
                            targetEntryId = localName.BibleEntryId;
                        }
                        else
                        {
                            var existingName = await _db.BibleEntryNames.FirstOrDefaultAsync(
                                n => n.SeriesBibleId == seriesBible.Id && n.NormalizedName == normDraftName, ct);
                            if (existingName is not null)
                            {
                                targetEntryId = existingName.BibleEntryId;
                            }
                            else
                            {
                                var stub = BibleEntry.CreateStub(seriesBible.Id, r.NewDraft.Category, draftName, description: "");
                                _db.BibleEntries.Add(stub);
                                var entryName = BibleEntryName.Create(seriesBible.Id, draftName, stub.Id, isPrimary: true);
                                _db.BibleEntryNames.Add(entryName);
                                targetEntryId = stub.Id;
                            }
                        }
                    }

                    if (targetEntryId.HasValue && seenEntries.Add(targetEntryId.Value))
                    {
                        validRefs.Add((targetEntryId.Value, r.SurfaceText.Trim()));
                    }
                }

                // Cập nhật script_line_bible_links cho dòng
                var currentLinks = await _db.ScriptLineBibleLinks.Where(k => k.ScriptLineId == line.Id).ToListAsync(ct);
                var wantedEntryIds = validRefs.Select(x => x.BibleEntryId).ToHashSet();
                foreach (var oldLink in currentLinks.Where(k => !wantedEntryIds.Contains(k.BibleEntryId)))
                {
                    _db.ScriptLineBibleLinks.Remove(oldLink);
                }

                var existingEntryIds = currentLinks.Select(k => k.BibleEntryId).ToHashSet();
                foreach (var (entryId, surface) in validRefs)
                {
                    if (!existingEntryIds.Contains(entryId))
                    {
                        _db.ScriptLineBibleLinks.Add(ScriptLineBibleLink.Create(line.Id, entryId, surfaceText: surface));
                    }
                }
                bibleLinksByLine[line.Id] = validRefs.Select(x => x.BibleEntryId).ToList();
            }
            else
            {
                // Chữ của dòng đổi mà không gửi lại refs: giữ tham chiếu cũ nếu surface_text còn trong chữ mới, không còn -> gỡ và cảnh báo
                var currentLinks = await _db.ScriptLineBibleLinks.Where(k => k.ScriptLineId == line.Id).ToListAsync(ct);
                if (currentLinks.Count > 0)
                {
                    var keptEntryIds = new List<Guid>();
                    foreach (var link in currentLinks)
                    {
                        var normSurface = BibleNameNormalizer.Normalize(link.SurfaceText);
                        if (!string.IsNullOrEmpty(normSurface) && normLineText.Contains(normSurface))
                        {
                            keptEntryIds.Add(link.BibleEntryId);
                        }
                        else
                        {
                            _db.ScriptLineBibleLinks.Remove(link);
                            warnings.Add($"Dòng {item.LineOrder}: gỡ tham chiếu '{link.SurfaceText}' vì nội dung đã thay đổi.");
                        }
                    }
                    bibleLinksByLine[line.Id] = keptEntryIds;
                }
                else if (item.BibleEntryIds != null)
                {
                    var links = await ScriptLineRules.SetBibleLinksAsync(_db, line.Id, item.BibleEntryIds, ct);
                    bibleLinksByLine[line.Id] = links;
                }
                else
                {
                    bibleLinksByLine[line.Id] = [];
                }
            }
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
            new BatchSaveScriptLinesResult(chapter.VersionVector, revision.Id, lineDtos, warnings.Count > 0 ? warnings : null));
    }
}

